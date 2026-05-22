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

    /// <summary>
    /// Log-tail shipping: periodically POST recently-written log lines from
    /// the local Serilog file sink to the hosted API so the Web UI can
    /// surface them. See ADR-0024 §9 + issue #210.
    /// </summary>
    public LogTailOptions LogTail { get; set; } = new();
}

/// <summary>
/// Bound from <c>Agent:LogTail</c>. Controls the
/// <c>LogTailBackgroundService</c>'s read-and-ship loop.
/// </summary>
public sealed class LogTailOptions
{
    /// <summary>Master switch. Defaults to on; flip to false to opt out.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the agent reads new lines from its log file
    /// and POSTs them to <c>/agent/logs</c>. Default 60 seconds.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Cap on lines shipped per interval. New lines beyond this
    /// will be picked up on the next tick.</summary>
    public int MaxLinesPerUpload { get; set; } = 500;
}
