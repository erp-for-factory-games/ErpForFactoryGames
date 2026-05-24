namespace Agent;

/// <summary>
/// Uploads the player's local <c>Docs.json</c> to the hosted planner
/// (ADR-0025 §4-§5). One call per intended push; the server handles
/// dedup via the hash on its side, so the uploader doesn't need to
/// remember previous uploads.
/// </summary>
public interface ICatalogueUploader
{
    Task<CatalogueUploadAttempt> UploadAsync(CancellationToken ct);
}

/// <summary>
/// Result of one upload attempt. <see cref="StatusCode"/> is 304 when
/// the server's stored hash already matched — that's a healthy outcome,
/// not a failure.
/// </summary>
public sealed record CatalogueUploadAttempt(
    string? FilePath,
    DateTimeOffset AttemptedAt,
    bool Skipped,
    bool Succeeded,
    int? StatusCode,
    string? DocsHash,
    long? SizeBytes,
    string? Detail);
