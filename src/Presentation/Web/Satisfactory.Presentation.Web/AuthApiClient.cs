using System.Net.Http.Json;

namespace Satisfactory.Presentation.Web;

/// <summary>
/// Typed HTTP client for the Auth API (ADR-0026 §Presentation/Api/Auth).
/// Owns the player + agent-token endpoints that moved out of the Sat API
/// in phase 5c2. Registered with base URL <c>https+http://auth-api</c> via
/// Aspire service-discovery.
/// </summary>
public sealed class AuthApiClient(HttpClient httpClient)
{
    public Task<CurrentPlayerView?> GetCurrentPlayerAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<CurrentPlayerView>("/players/current", ct);

    public async Task<MintAgentTokenResponse?> MintAgentTokenAsync(Guid playerId, string? label, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/players/{playerId}/agent-tokens", new { label }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MintAgentTokenResponse>(ct);
    }

    public async Task<AgentTokenView[]> ListAgentTokensAsync(Guid playerId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<AgentTokenView[]>($"/players/{playerId}/agent-tokens", ct) ?? [];

    public async Task<bool> RevokeAgentTokenAsync(Guid playerId, Guid tokenId, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"/players/{playerId}/agent-tokens/{tokenId}", ct);
        return response.IsSuccessStatusCode;
    }
}
