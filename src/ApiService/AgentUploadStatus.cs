namespace ApiService;

/// <summary>
/// Server-side snapshot of what the agent last uploaded, for the
/// <c>GET /api/agent/status</c> endpoint and the Web UI status card (#200).
/// Singleton; replaced atomically on each upload.
/// </summary>
public interface IAgentUploadStatus
{
    UploadSnapshot? Latest { get; }

    void Record(UploadSnapshot snapshot);
}

/// <summary>
/// One row in the recent-uploads ledger. Wire-equivalent of the agent's own
/// <c>UploadAttempt</c>, projected for the UI.
/// </summary>
public sealed record UploadSnapshot(
    string FileName,
    DateTimeOffset ParsedAt,
    int? SaveVersion,
    int? BuildVersion,
    bool Succeeded,
    int StatusCode,
    string? Detail,
    string? AgentVersion);

internal sealed class AgentUploadStatus : IAgentUploadStatus
{
    // Reference assignment is atomic in .NET — no locking. Worst case is a
    // slightly-stale read in another thread, fine for a status snapshot.
    public UploadSnapshot? Latest { get; private set; }

    public void Record(UploadSnapshot snapshot) => Latest = snapshot;
}
