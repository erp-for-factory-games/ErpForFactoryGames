using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent;

/// <summary>
/// Ships a batch of log lines to the hosted API. Returns the upload
/// outcome including any server-side flags piggybacked on the response
/// (ADR-0025 §7 — the re-ingest trigger rides this same poll).
/// </summary>
public interface ILogTailUploader
{
    Task<LogTailUploadResult> UploadAsync(IReadOnlyList<string> lines, CancellationToken ct);
}

/// <summary>
/// Outcome of one log-tail upload tick. <see cref="ReIngestRequested"/>
/// is the server's "please re-upload the catalogue" signal, set by the
/// Web UI's re-ingest button (ADR-0025 §7).
/// </summary>
public readonly record struct LogTailUploadResult(bool Succeeded, bool ReIngestRequested)
{
    public static LogTailUploadResult Failure { get; } = new(false, false);
}

/// <summary>
/// HTTP implementation. Wire shape per ADR-0024 §9 — JSON body,
/// <c>X-Agent-Token</c> header (same auth seam as the save uploader).
/// </summary>
internal sealed class HttpLogTailUploader : ILogTailUploader
{
    private const string UploadPath = "/api/agent/logs";

    private readonly HttpClient _http;
    private readonly AgentOptions _options;
    private readonly ILogger<HttpLogTailUploader> _logger;

    public HttpLogTailUploader(
        HttpClient http,
        IOptions<AgentOptions> options,
        ILogger<HttpLogTailUploader> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LogTailUploadResult> UploadAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        if (lines.Count == 0) return new LogTailUploadResult(true, false);

        var agentVersion = typeof(HttpLogTailUploader).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var payload = new { lines, agentVersion };
        using var content = JsonContent.Create(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadPath) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", _options.AgentToken);
        request.Headers.TryAddWithoutValidation("X-Agent-Version", agentVersion);

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Shipped {Count} log line(s) -> {Status}", lines.Count, (int)response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<LogTailResponse>(ct).ConfigureAwait(false);
                return new LogTailUploadResult(true, body?.ReIngestRequested ?? false);
            }
            _logger.LogWarning("Log-tail upload failed: {Status}", (int)response.StatusCode);
            return LogTailUploadResult.Failure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Don't log at error level — log shipping is best-effort and a
            // server outage shouldn't spam the local log with errors that
            // would then ship themselves once the server returns. Debug is
            // enough; the operator sees the missing lines in the UI.
            _logger.LogDebug(ex, "Log-tail upload threw");
            return LogTailUploadResult.Failure;
        }
    }

    private sealed record LogTailResponse(int Received, long? Retained, bool ReIngestRequested);
}
