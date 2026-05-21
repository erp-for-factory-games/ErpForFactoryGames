namespace ApiService;

/// <summary>
/// Bound from <c>AgentUploads</c> in configuration. Controls where uploaded
/// <c>.sav</c> files land on the server.
/// </summary>
public sealed class AgentUploadOptions
{
    /// <summary>
    /// Directory the server writes uploaded saves into. The agent's last
    /// upload is also persisted at <c>{UploadDirectory}/satisfactory-latest.sav</c>
    /// so a process restart can pick up where we left off via the existing
    /// <see cref="ERP.Application.IFactoryStateProvider.LoadFromPath"/>.
    /// Defaults to <c>%LocalAppData%/ErpForFactoryGames/uploads/</c> on
    /// Windows or <c>$XDG_STATE_HOME/ErpForFactoryGames/uploads/</c> on
    /// Linux. Override for containerised deployments to point at a mounted
    /// volume.
    /// </summary>
    public string? UploadDirectory { get; set; }

    /// <summary>
    /// How big a single upload can be. Satisfactory saves run 50–200 MB on
    /// real worlds; cap at 512 MB to leave headroom without being a DoS
    /// gift.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 512L * 1024 * 1024;

    public string ResolveUploadDirectory()
    {
        if (!string.IsNullOrWhiteSpace(UploadDirectory)) return UploadDirectory;

        var baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_STATE_HOME")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(baseDir, "ErpForFactoryGames", "uploads");
    }
}
