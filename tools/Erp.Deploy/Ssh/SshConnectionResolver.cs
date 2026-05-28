using System.Diagnostics;
using Erp.Deploy.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Deploy.Ssh;

// Resolves an SSH config alias (or raw hostname) into a concrete connection
// tuple by shelling out to `ssh -G <host>`. Honours ~/.ssh/config so the
// existing alias-driven UX (`ssh erp-lxc`) keeps working.
//
// IdentityFile handling: ssh -G emits every candidate identityfile OpenSSH
// would try — explicit `IdentityFile` directives first, then the default
// ~/.ssh/id_* set. We keep all that exist on disk and hand them all to
// Renci.SshNet, which tries them in order at auth time. Same behaviour as
// OpenSSH itself: first key that authenticates wins.
//
// Explicit values on RemoteOptions take precedence; missing ones fall through
// to whatever `ssh -G` resolves.
public sealed class SshConnectionResolver
{
    private static readonly string[] DefaultIdentityCandidates =
    {
        "~/.ssh/id_ed25519",
        "~/.ssh/id_ecdsa",
        "~/.ssh/id_rsa",
    };

    private readonly ILogger<SshConnectionResolver> _log;

    public SshConnectionResolver(ILogger<SshConnectionResolver>? log = null)
    {
        _log = log ?? NullLogger<SshConnectionResolver>.Instance;
    }

    public ResolvedSshConnection Resolve(RemoteOptions opts)
    {
        var fromSshG = TrySshDashG(opts.Host);

        var host = fromSshG?.Host ?? opts.Host;
        var port = opts.Port ?? fromSshG?.Port ?? 22;
        var user = opts.User ?? fromSshG?.User ?? Environment.UserName;

        // If the user explicitly named a key, use just that one (and fail loud
        // if it doesn't exist — they asked for it specifically). Otherwise
        // take everything ssh -G suggested, fall back to the default set,
        // dedupe, expand ~/, keep only those that exist on disk.
        IReadOnlyList<string> keys;
        if (opts.IdentityFile is { Length: > 0 } explicitKey)
        {
            var expanded = ExpandHome(explicitKey);
            if (!File.Exists(expanded))
            {
                throw new FileNotFoundException(
                    $"Remote.IdentityFile points to a file that doesn't exist: {expanded}", expanded);
            }
            keys = new[] { expanded };
        }
        else
        {
            var candidates = (fromSshG?.IdentityFiles ?? Array.Empty<string>())
                .Concat(DefaultIdentityCandidates)
                .Select(ExpandHome)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            keys = candidates.Where(File.Exists).ToList();

            if (keys.Count == 0)
            {
                throw new FileNotFoundException(
                    "No usable SSH identity file found. Tried: " + string.Join(", ", candidates) + ". " +
                    "Set Remote.IdentityFile in deploy/erp-deploy.json, or generate a key with `ssh-keygen -t ed25519`.");
            }
        }

        return new ResolvedSshConnection(host, port, user, keys);
    }

    private record SshGOutput(string Host, int Port, string User, IReadOnlyList<string> IdentityFiles);

    private SshGOutput? TrySshDashG(string aliasOrHost)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-G");
        psi.ArgumentList.Add(aliasOrHost);

        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5_000);
            if (p.ExitCode != 0)
            {
                _log.LogDebug("ssh -G {Alias} exited {ExitCode}; falling back.", aliasOrHost, p.ExitCode);
                return null;
            }

            string? host = null, user = null;
            int port = 22;
            var identityFiles = new List<string>();
            foreach (var line in stdout.Split('\n'))
            {
                var parts = line.TrimEnd('\r').Split(' ', 2);
                if (parts.Length != 2) continue;
                switch (parts[0])
                {
                    case "hostname": host = parts[1]; break;
                    case "user": user = parts[1]; break;
                    case "port": int.TryParse(parts[1], out port); break;
                    case "identityfile": identityFiles.Add(parts[1]); break;
                }
            }
            if (host is null || user is null) return null;
            return new SshGOutput(host, port, user, identityFiles);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "ssh -G probe failed; falling back to literal host.");
            return null;
        }
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }
        return path;
    }
}
