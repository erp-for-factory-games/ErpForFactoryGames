using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent;

/// <summary>
/// Ships a Satisfactory <c>.sav</c> file to the hosted API. Returns the
/// outcome rather than throwing so the watcher can record it in the status
/// ledger and surface the failure to the user without restarting the host.
/// </summary>
public interface ISaveUploader
{
    Task<UploadAttempt> UploadAsync(string filePath, CancellationToken ct);
}

/// <summary>
/// HTTP implementation. Wire shape per ADR-0024 §4 — raw bytes,
/// <c>application/octet-stream</c>, three custom headers.
/// </summary>
internal sealed class HttpSaveUploader : ISaveUploader
{
    private const string UploadPath = "/agent/savegames/satisfactory";

    private readonly HttpClient _http;
    private readonly AgentOptions _options;
    private readonly ILogger<HttpSaveUploader> _logger;

    public HttpSaveUploader(
        HttpClient http,
        IOptions<AgentOptions> options,
        ILogger<HttpSaveUploader> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadAttempt> UploadAsync(string filePath, CancellationToken ct)
    {
        var fileName = Path.GetFileName(filePath);
        var attemptedAt = DateTimeOffset.UtcNow;

        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var request = new HttpRequestMessage(HttpMethod.Post, UploadPath) { Content = content };
            request.Headers.TryAddWithoutValidation("X-Agent-Token", _options.AgentToken);
            request.Headers.TryAddWithoutValidation("X-Agent-FileName", Uri.EscapeDataString(fileName));
            request.Headers.TryAddWithoutValidation(
                "X-Agent-Version",
                typeof(HttpSaveUploader).Assembly.GetName().Version?.ToString() ?? "0.0.0");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Uploaded {File} -> {Status}", fileName, (int)response.StatusCode);
                return new UploadAttempt(fileName, attemptedAt, Succeeded: true,
                    StatusCode: (int)response.StatusCode, Detail: null);
            }

            var detail = await SafeReadFirstLineAsync(response, ct).ConfigureAwait(false);
            _logger.LogWarning("Upload of {File} failed: {Status} {Detail}",
                fileName, (int)response.StatusCode, detail);
            return new UploadAttempt(fileName, attemptedAt, Succeeded: false,
                StatusCode: (int)response.StatusCode, Detail: detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload of {File} threw", fileName);
            return new UploadAttempt(fileName, attemptedAt, Succeeded: false,
                StatusCode: null, Detail: ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<string?> SafeReadFirstLineAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var line = body.AsSpan().IndexOf('\n') is var idx and >= 0 ? body[..idx] : body;
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }
}
