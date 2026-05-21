namespace Web.Shared;

/// <summary>
/// Wire shape for <c>GET /agent/status</c>. Mirrors the anonymous JSON
/// the ApiService endpoint emits (camelCase, nullable-friendly).
/// </summary>
public sealed record AgentStatusDto(
    UploadSnapshotDto? LastUpload,
    DateTimeOffset? AgentSeen,
    bool IsStale);

public sealed record UploadSnapshotDto(
    string FileName,
    DateTimeOffset ParsedAt,
    int? SaveVersion,
    int? BuildVersion,
    bool Succeeded,
    int StatusCode,
    string? Detail,
    string? AgentVersion);
