namespace Agent;

/// <summary>
/// Bound from the <c>Agent</c> configuration section. Env vars win
/// (<c>ERP_AGENT_*</c>), then <c>appsettings.json</c> / <c>agent.json</c>,
/// then defaults.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>
    /// Base URL of the ApiService that hosts <c>/agent/savegames/satisfactory</c>
    /// and <c>/agent/status</c>. No default — must be configured.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "";

    /// <summary>
    /// Opaque token sent as <c>X-Agent-Token</c> on every API call. v1 server
    /// accepts any non-empty value (auth seam — see ADR-0024 §5). User
    /// generates locally for now; a future ADR-0025 picks a real issuance
    /// mechanism.
    /// </summary>
    public string AgentToken { get; set; } = "";

    /// <summary>
    /// Override the auto-detected Satisfactory save folder.
    /// <see cref="SaveFolderResolver"/> falls back to OS defaults when this
    /// is null/empty.
    /// </summary>
    public string? SaveFolderPath { get; set; }

    /// <summary>
    /// Debounce after a <see cref="System.IO.FileSystemEventArgs"/> before
    /// reading the file. Satisfactory writes saves in chunks — uploading
    /// mid-write produces a truncated file.
    /// </summary>
    public TimeSpan WriteDebounce { get; set; } = TimeSpan.FromMilliseconds(500);
}
