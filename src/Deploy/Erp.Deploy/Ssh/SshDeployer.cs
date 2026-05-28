using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;

namespace Erp.Deploy.Ssh;

public sealed record RemoteCommandResult(string Command, int ExitStatus, string StdOut, string StdErr);

// SFTP + remote-exec wrapper over Renci.SshNet. Two operations matter:
//
//   1. UploadFiles — pushes byte arrays to remote paths and chmods them.
//      This is the bug fix the whole CLI was started for: writes are
//      transport-layer SFTP, never re-parsed by a remote shell, so a
//      stack.env line like `FOO="ab $c\nd"` lands byte-for-byte intact.
//
//   2. Run — runs one shell-compound command. The remote shell still parses
//      the command body itself, so callers must build commands carefully;
//      we don't accept user input here.
public sealed class SshDeployer : IDisposable
{
    private readonly SshClient _ssh;
    private readonly SftpClient _sftp;
    private readonly ResolvedSshConnection _conn;
    private readonly ILogger<SshDeployer> _log;

    private SshDeployer(SshClient ssh, SftpClient sftp, ResolvedSshConnection conn, ILogger<SshDeployer> log)
    {
        _ssh = ssh;
        _sftp = sftp;
        _conn = conn;
        _log = log;
    }

    public static SshDeployer Connect(ResolvedSshConnection conn, ILogger<SshDeployer>? log = null)
    {
        log ??= NullLogger<SshDeployer>.Instance;

        if (conn.IdentityFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "No SSH identity files available — SshConnectionResolver should have caught this.");
        }

        // Load every surviving key and feed them all to one auth method.
        // Renci.SshNet tries them in order at handshake time, same as OpenSSH.
        var keyFiles = conn.IdentityFiles.Select(p => new PrivateKeyFile(p)).ToArray();
        var auth = new PrivateKeyAuthenticationMethod(conn.User, keyFiles);
        var info = new ConnectionInfo(conn.Host, conn.Port, conn.User, auth);

        var ssh = new SshClient(info);
        ssh.HostKeyReceived += (_, e) =>
        {
            // TODO Phase 3: verify against ~/.ssh/known_hosts. For v1 accept
            // any key and surface the fingerprint so the operator can sanity-
            // check it against what `ssh-keyscan` reports.
            var fp = string.Join(":", e.FingerPrintSHA256);
            log.LogInformation("SSH host key SHA256: {Fingerprint}", fp);
            e.CanTrust = true;
        };

        ssh.Connect();
        log.LogInformation("SSH connected: {Display}", conn.Display);

        var sftp = new SftpClient(info);
        sftp.Connect();

        return new SshDeployer(ssh, sftp, conn, log);
    }

    public void UploadFiles(IReadOnlyList<RemoteFileUpload> files, string stackDir)
    {
        EnsureDir(stackDir);
        foreach (var f in files)
        {
            _log.LogInformation("SFTP write {Path} ({Bytes} bytes, mode {Mode:o})", f.RemotePath, f.Size, f.Mode);

            // UploadFile(canOverride:true) still EACCES's if the existing file
            // lacks owner-write. Pre-chmod 0644 so a stuck remote mode can't
            // wedge subsequent deploys.
            try { _sftp.ChangePermissions(f.RemotePath, 644); }
            catch (Renci.SshNet.Common.SftpPathNotFoundException) { /* new file */ }

            using var stream = new MemoryStream(f.Body);
            _sftp.UploadFile(stream, f.RemotePath, canOverride: true);
            _sftp.ChangePermissions(f.RemotePath, f.Mode);
        }
    }

    public RemoteCommandResult Run(string command, TimeSpan? timeout = null)
    {
        using var cmd = _ssh.CreateCommand(command);
        if (timeout is not null) cmd.CommandTimeout = timeout.Value;
        _log.LogInformation("ssh exec: {Command}", command);

        var stdout = cmd.Execute();
        // ExitStatus is int? — null means SSH didn't report one. Treat as -1.
        var result = new RemoteCommandResult(command, cmd.ExitStatus ?? -1, stdout, cmd.Error);
        if (result.ExitStatus != 0)
        {
            _log.LogWarning("ssh exit={ExitStatus}: {Stderr}", result.ExitStatus, result.StdErr.TrimEnd());
        }
        return result;
    }

    private void EnsureDir(string remotePath)
    {
        // mkdir -p, but issued over SSH exec so we don't need to walk the path
        // tree component-by-component over SFTP.
        var r = Run($"mkdir -p {Quote(remotePath)}");
        if (r.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"mkdir -p {remotePath} failed ({r.ExitStatus}): {r.StdErr.TrimEnd()}");
        }
    }

    // Single-quote-wrap, escaping any embedded single quote with '\'' (POSIX safe).
    // Used for paths we construct ourselves — not for user input.
    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public void Dispose()
    {
        try { _sftp.Disconnect(); _sftp.Dispose(); } catch { /* best effort */ }
        try { _ssh.Disconnect(); _ssh.Dispose(); } catch { /* best effort */ }
    }
}
