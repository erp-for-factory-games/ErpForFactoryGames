using System.Net.Http.Json;
using Satisfactory.Presentation.Web.Auth;

namespace Satisfactory.Presentation.Web;

/// <summary>
/// Typed HTTP client for the Auth API (ADR-0026 §Presentation/Api/Auth).
/// Owns the player + agent-token endpoints that moved out of the Sat API
/// in phase 5c2. Registered with base URL <c>https+http://auth-api</c> via
/// Aspire service-discovery.
///
/// <para>Under <c>Auth:Backend=keycloak</c> (#292) it forwards the signed-in
/// user's Keycloak access token (resolved by <see cref="UserAccessTokenAccessor"/>)
/// so the Auth API resolves + JIT-provisions the right player. Under the dev
/// backend the accessor yields no token and behaviour is unchanged.</para>
/// </summary>
public sealed class AuthApiClient(HttpClient httpClient, UserAccessTokenAccessor tokens)
{
    public async Task<CurrentPlayerView?> GetCurrentPlayerAsync(CancellationToken ct = default)
    {
        await tokens.ApplyAsync(httpClient);
        return await httpClient.GetFromJsonAsync<CurrentPlayerView>("/players/current", ct);
    }

    public async Task<MintAgentTokenResponse?> MintAgentTokenAsync(Guid playerId, string? label, CancellationToken ct = default)
    {
        await tokens.ApplyAsync(httpClient);
        var response = await httpClient.PostAsJsonAsync($"/players/{playerId}/agent-tokens", new { label }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MintAgentTokenResponse>(ct);
    }

    public async Task<AgentTokenView[]> ListAgentTokensAsync(Guid playerId, CancellationToken ct = default)
    {
        await tokens.ApplyAsync(httpClient);
        return await httpClient.GetFromJsonAsync<AgentTokenView[]>($"/players/{playerId}/agent-tokens", ct) ?? [];
    }

    public async Task<bool> RevokeAgentTokenAsync(Guid playerId, Guid tokenId, CancellationToken ct = default)
    {
        await tokens.ApplyAsync(httpClient);
        var response = await httpClient.DeleteAsync($"/players/{playerId}/agent-tokens/{tokenId}", ct);
        return response.IsSuccessStatusCode;
    }
}
