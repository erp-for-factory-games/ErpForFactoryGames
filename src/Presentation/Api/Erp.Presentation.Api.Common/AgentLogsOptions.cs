namespace Erp.Presentation.Api.Common;

/// <summary>
/// Bound from <c>AgentLogs</c> in configuration. Controls the in-memory
/// ring buffer that backs <c>POST /api/agent/logs</c> + <c>GET /api/agent/logs</c>.
///
/// Persisted observability (across process restarts) is deliberately out
/// of scope for v1 — see ADR-0024 §9. The follow-up SigNoz / OTel issue
/// (#212) covers durable, multi-source observability.
/// </summary>
public sealed class AgentLogsOptions
{
    /// <summary>
    /// Maximum number of log lines retained in the ring buffer. Older lines
    /// are evicted when this is exceeded. 2000 lines at ~200 chars each ≈
    /// 400 KB — fine for an always-on process.
    /// </summary>
    public int MaxBufferLines { get; set; } = 2000;

    /// <summary>
    /// Maximum lines accepted in a single <c>POST /api/agent/logs</c> request.
    /// Anything beyond this is silently truncated server-side. Cap is for
    /// DoS hardening, not user-facing.
    /// </summary>
    public int MaxLinesPerRequest { get; set; } = 1000;
}
