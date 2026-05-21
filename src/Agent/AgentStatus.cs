namespace Agent;

/// <summary>
/// In-process snapshot of what the agent is doing. The future Web UI
/// component <c>AgentStatusCard</c> (#200) reads a server-side projection of
/// this via <c>GET /agent/status</c>; the agent itself uses it to publish
/// its own state to the API on each upload.
/// </summary>
public interface IAgentStatus
{
    /// <summary>Resolved save folder the watcher is using, or null if none.</summary>
    string? SaveFolderInUse { get; }

    /// <summary>True iff <see cref="SaveFolderInUse"/> exists and is readable.</summary>
    bool SaveFolderAccessible { get; }

    /// <summary>Most recent save file the watcher noticed, by full path. Null if no event yet.</summary>
    string? LastDetectedSavePath { get; }
    DateTimeOffset? LastDetectedAt { get; }

    /// <summary>Last upload attempt (success or failure). Null if no attempt yet.</summary>
    UploadAttempt? LastUpload { get; }

    void RecordSaveFolder(string? path, bool accessible);
    void RecordDetected(string path);
    void RecordUpload(UploadAttempt attempt);
}

/// <summary>One row in the agent's recent-uploads ledger. Surfaces in the UI card.</summary>
public sealed record UploadAttempt(
    string FileName,
    DateTimeOffset AttemptedAt,
    bool Succeeded,
    int? StatusCode,
    string? Detail);

internal sealed class AgentStatus : IAgentStatus
{
    // No locking — these properties are written by the BackgroundService thread
    // and read by anything that injects IAgentStatus. Reference assignment is
    // atomic in .NET; the worst case is a slightly-stale read.
    public string? SaveFolderInUse { get; private set; }
    public bool SaveFolderAccessible { get; private set; }
    public string? LastDetectedSavePath { get; private set; }
    public DateTimeOffset? LastDetectedAt { get; private set; }
    public UploadAttempt? LastUpload { get; private set; }

    public void RecordSaveFolder(string? path, bool accessible)
    {
        SaveFolderInUse = path;
        SaveFolderAccessible = accessible;
    }

    public void RecordDetected(string path)
    {
        LastDetectedSavePath = path;
        LastDetectedAt = DateTimeOffset.UtcNow;
    }

    public void RecordUpload(UploadAttempt attempt) => LastUpload = attempt;
}
