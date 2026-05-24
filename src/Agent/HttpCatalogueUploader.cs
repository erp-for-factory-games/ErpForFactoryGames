using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent;

/// <summary>
/// HTTP implementation of <see cref="ICatalogueUploader"/>. Posts the
/// raw <c>Docs.json</c> bytes to <c>/api/agent/catalogue/satisfactory</c>;
/// the server hashes + dedups + persists.
/// </summary>
internal sealed class HttpCatalogueUploader : ICatalogueUploader
{
    private const string UploadPath = "/api/agent/catalogue/satisfactory";

    private readonly HttpClient _http;
    private readonly AgentOptions _agentOptions;
    private readonly CatalogueUploadOptions _catalogueOptions;
    private readonly ILogger<HttpCatalogueUploader> _logger;

    public HttpCatalogueUploader(
        HttpClient http,
        IOptions<AgentOptions> agentOptions,
        IOptions<CatalogueUploadOptions> catalogueOptions,
        ILogger<HttpCatalogueUploader> logger)
    {
        _http = http;
        _agentOptions = agentOptions.Value;
        _catalogueOptions = catalogueOptions.Value;
        _logger = logger;
    }

    public async Task<CatalogueUploadAttempt> UploadAsync(CancellationToken ct)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        var path = ResolveDocsPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogInformation(
                "Catalogue:Satisfactory:DocsPath is not configured — skipping catalogue upload. "
                + "Set the path in agent.json to enable.");
            return new CatalogueUploadAttempt(null, attemptedAt, Skipped: true,
                Succeeded: false, StatusCode: null, DocsHash: null, SizeBytes: null,
                Detail: "DocsPath not configured.");
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Catalogue Docs.json not found at {Path}.", path);
            return new CatalogueUploadAttempt(path, attemptedAt, Skipped: true,
                Succeeded: false, StatusCode: null, DocsHash: null, SizeBytes: null,
                Detail: $"Not found: {path}");
        }

        try
        {
            byte[] bytes;
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                             bufferSize: 64 * 1024, useAsync: true))
            {
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, UploadPath) { Content = content };
            request.Headers.TryAddWithoutValidation("X-Agent-Token", _agentOptions.AgentToken);
            request.Headers.TryAddWithoutValidation(
                "X-Agent-Version",
                typeof(HttpCatalogueUploader).Assembly.GetName().Version?.ToString() ?? "0.0.0");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogInformation("Catalogue at {Path} matches server's stored hash (304).", path);
                return new CatalogueUploadAttempt(path, attemptedAt, Skipped: false,
                    Succeeded: true, StatusCode: 304, DocsHash: null, SizeBytes: bytes.Length,
                    Detail: "No change since last upload.");
            }

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<UploadResponse>(ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Catalogue uploaded ({Bytes} bytes, hash {Hash}).",
                    bytes.Length, payload?.DocsHash ?? "?");
                return new CatalogueUploadAttempt(path, attemptedAt, Skipped: false,
                    Succeeded: true, StatusCode: (int)response.StatusCode,
                    DocsHash: payload?.DocsHash, SizeBytes: bytes.Length, Detail: null);
            }

            var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Catalogue upload failed: {Status} {Detail}", (int)response.StatusCode, detail);
            return new CatalogueUploadAttempt(path, attemptedAt, Skipped: false,
                Succeeded: false, StatusCode: (int)response.StatusCode,
                DocsHash: null, SizeBytes: bytes.Length, Detail: detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalogue upload threw");
            return new CatalogueUploadAttempt(path, attemptedAt, Skipped: false,
                Succeeded: false, StatusCode: null, DocsHash: null, SizeBytes: null,
                Detail: ex.GetType().Name + ": " + ex.Message);
        }
    }

    private string? ResolveDocsPath()
    {
        // Env var wins, matches the server-side resolution from ADR-0011.
        var fromEnv = Environment.GetEnvironmentVariable(CatalogueUploadOptions.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        return string.IsNullOrWhiteSpace(_catalogueOptions.DocsPath) ? null : _catalogueOptions.DocsPath;
    }

    private sealed record UploadResponse(string DocsHash, long SizeBytes, DateTime UploadedUtc, bool Changed);
}
