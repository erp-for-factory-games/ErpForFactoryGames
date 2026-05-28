namespace Erp.Presentation.Web.Common;

/// <summary>
/// Wire shape for <c>GET /api/agent/status</c>. Mirrors the anonymous JSON
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
