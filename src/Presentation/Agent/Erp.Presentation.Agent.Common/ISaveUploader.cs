namespace Erp.Presentation.Agent.Common;

/// <summary>
/// Ships a save file to the hosted API. Returns the outcome rather than
/// throwing so the watcher can record it in the status ledger and surface
/// the failure to the user without restarting the host.
/// </summary>
public interface ISaveUploader
{
    Task<UploadAttempt> UploadAsync(string filePath, CancellationToken ct);
}
