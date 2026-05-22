namespace Web.Shared;

/// <summary>
/// Wire shape for <c>GET /agent/logs</c>. Mirrors the JSON the
/// ApiService endpoint emits (camelCase).
/// </summary>
public sealed record AgentLogsDto(
    IReadOnlyList<AgentLogLineDto> Lines,
    int TotalReceived,
    DateTimeOffset? AgentLastSeen);

public sealed record AgentLogLineDto(
    string Text,
    DateTimeOffset UploadedAt,
    string? AgentVersion);
