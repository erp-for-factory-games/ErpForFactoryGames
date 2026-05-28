using Microsoft.Extensions.Options;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Server-side ring buffer of agent log lines, populated by
/// <c>POST /api/agent/logs</c> and read by <c>GET /api/agent/logs</c>. Singleton,
/// in-memory only — see ADR-0024 §9. Lost on process restart by design.
/// </summary>
public interface IAgentLogsStore
{
    /// <summary>Total lines ever received (not just retained).</summary>
    long TotalReceived { get; }

    /// <summary>Last time an agent POSTed lines. Null if no upload yet.</summary>
    DateTimeOffset? AgentLastSeen { get; }

    /// <summary>Append a batch of lines. Older lines fall off the ring.</summary>
    void Append(IEnumerable<string> lines, string? agentVersion, DateTimeOffset uploadedAt);

    /// <summary>Read up to <paramref name="limit"/> most-recent lines.</summary>
    IReadOnlyList<AgentLogLine> ReadLatest(int limit);
}

public sealed record AgentLogLine(string Text, DateTimeOffset UploadedAt, string? AgentVersion);

internal sealed class AgentLogsStore : IAgentLogsStore
{
    private readonly int _maxBufferLines;
    private readonly object _gate = new();
    // LinkedList for O(1) eviction at the head while retaining order.
    private readonly LinkedList<AgentLogLine> _buffer = new();

    public AgentLogsStore(IOptions<AgentLogsOptions> options)
    {
        _maxBufferLines = Math.Max(1, options.Value.MaxBufferLines);
    }

    public long TotalReceived { get; private set; }
    public DateTimeOffset? AgentLastSeen { get; private set; }

    public void Append(IEnumerable<string> lines, string? agentVersion, DateTimeOffset uploadedAt)
    {
        lock (_gate)
        {
            foreach (var text in lines)
            {
                _buffer.AddLast(new AgentLogLine(text, uploadedAt, agentVersion));
                TotalReceived++;
                while (_buffer.Count > _maxBufferLines)
                {
                    _buffer.RemoveFirst();
                }
            }
            AgentLastSeen = uploadedAt;
        }
    }

    public IReadOnlyList<AgentLogLine> ReadLatest(int limit)
    {
        if (limit <= 0) return Array.Empty<AgentLogLine>();
        lock (_gate)
        {
            if (_buffer.Count == 0) return Array.Empty<AgentLogLine>();
            var take = Math.Min(limit, _buffer.Count);
            var result = new AgentLogLine[take];
            // Walk backwards from the tail to grab the most-recent `take` lines,
            // then reverse so callers see chronological order.
            var node = _buffer.Last;
            for (var i = take - 1; i >= 0 && node is not null; i--, node = node.Previous)
            {
                result[i] = node.Value;
            }
            return result;
        }
    }
}
