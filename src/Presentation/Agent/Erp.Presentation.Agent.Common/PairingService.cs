using System.Net.Http.Json;

namespace Erp.Presentation.Agent.Common;

/// <summary>
/// Validates a token against the planner API and writes the agent's
/// on-disk config so the long-running service picks the values up on
/// next start (ADR-0025 §8). Single seam shared by the
/// <c>erp-agent --pair</c> deep-link path and the <c>erp-agent --setup</c>
/// CLI wizard.
/// </summary>
public sealed class PairingService
{
    private readonly AgentConfigWriter _configWriter;
    private readonly HttpClient _http;

    public PairingService(AgentConfigWriter configWriter, HttpClient http)
    {
        _configWriter = configWriter;
        _http = http;
    }

    /// <summary>
    /// Validate <paramref name="token"/> against <paramref name="apiBaseUrl"/> by
    /// calling <c>GET /api/me</c> with the token. On 200 the config is written.
    /// </summary>
    public async Task<PairingResult> PairAsync(
        string apiBaseUrl,
        string token,
        string? saveFolderOverride = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl)) return PairingResult.Error("API base URL is required.");
        if (string.IsNullOrWhiteSpace(token)) return PairingResult.Error("Token is required.");

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return PairingResult.Error($"API base URL is not a well-formed absolute URI: {apiBaseUrl}");
        }

        var meUri = new Uri(baseUri, "/api/me");

        MeResponse? me;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, meUri);
            request.Headers.TryAddWithoutValidation("X-Agent-Token", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return PairingResult.Error("Server rejected the token (401). Mint a fresh token from the web UI and try again.");
            }
            if (!response.IsSuccessStatusCode)
            {
                var detail = await SafeReadFirstLineAsync(response, ct).ConfigureAwait(false);
                return PairingResult.Error($"Validation call to {meUri} returned {(int)response.StatusCode}: {detail ?? "(no body)"}");
            }

            me = await response.Content.ReadFromJsonAsync<MeResponse>(ct).ConfigureAwait(false);
            if (me is null || me.PlayerId == Guid.Empty)
            {
                return PairingResult.Error("Server accepted the token but returned no player metadata.");
            }
        }
        catch (HttpRequestException ex)
        {
            return PairingResult.Error($"Could not reach {meUri}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return PairingResult.Error($"Timed out reaching {meUri}.");
        }

        var configPath = await _configWriter
            .WritePairingAsync(apiBaseUrl, token, saveFolderOverride, ct)
            .ConfigureAwait(false);

        return PairingResult.Success(new PairedAgent(me.PlayerId, me.DisplayName, me.TokenId, configPath));
    }

    private static async Task<string?> SafeReadFirstLineAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var idx = body.IndexOf('\n');
            return idx >= 0 ? body[..idx].Trim() : body.Trim();
        }
        catch { return null; }
    }

    private sealed record MeResponse(Guid PlayerId, string DisplayName, Guid TokenId);
}

public readonly record struct PairingResult(bool IsSuccess, PairedAgent? Paired, string? ErrorMessage)
{
    public static PairingResult Success(PairedAgent paired) => new(true, paired, null);
    public static PairingResult Error(string message) => new(false, null, message);
}

public sealed record PairedAgent(Guid PlayerId, string DisplayName, Guid TokenId, string ConfigPath);
