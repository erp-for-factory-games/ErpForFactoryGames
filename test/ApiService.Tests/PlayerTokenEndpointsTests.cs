using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiService.Tests;

/// <summary>
/// Integration tests for the agent-token management surface (ADR-0025 §2).
/// Mint → plaintext shown once, list never returns plaintext, revoke
/// idempotent, hash-at-rest (SHA-256 of plaintext, never the plaintext).
/// </summary>
public sealed class PlayerTokenEndpointsTests : IClassFixture<AgentEndpointsTests.AgentApiFactory>
{
    private readonly AgentEndpointsTests.AgentApiFactory _factory;

    public PlayerTokenEndpointsTests(AgentEndpointsTests.AgentApiFactory factory) => _factory = factory;

    [Fact(Skip = "Endpoints moved to Auth API in ADR-0026 phase 5c2; AuthApiFactory rebuild lands in a follow-up.")]
    public async Task Mint_returns_plaintext_once_and_persists_only_the_hash()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/players/{_factory.DevPlayerId}/agent-tokens",
            new { label = "Test rig" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MintResponseView>();
        Assert.NotNull(payload);
        Assert.StartsWith("eafg_", payload!.Plaintext);
        Assert.Equal("Test rig", payload.Label);

        // Hash-at-rest: the persisted bytes must equal SHA-256(plaintext)
        // — never the plaintext itself, and no other transform.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var row = await db.AgentTokens.AsNoTracking().SingleAsync(t => t.Id == new AgentTokenId(payload.Id));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(payload.Plaintext));
        Assert.Equal(expected, row.TokenHash);
        // Plaintext must NOT appear anywhere on the row.
        Assert.DoesNotContain(payload.Plaintext, row.Label);
    }

    [Fact(Skip = "Endpoints moved to Auth API in ADR-0026 phase 5c2; AuthApiFactory rebuild lands in a follow-up.")]
    public async Task List_does_not_return_plaintext()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync($"/players/{_factory.DevPlayerId}/agent-tokens", new { label = "L" });

        var list = await client.GetFromJsonAsync<TokenListItem[]>(
            $"/players/{_factory.DevPlayerId}/agent-tokens");

        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        // No property on the response shape carries plaintext; serialising
        // the list and grepping for the prefix is a paranoia check.
        var serialised = System.Text.Json.JsonSerializer.Serialize(list);
        Assert.DoesNotContain("eafg_", serialised);
    }

    [Fact(Skip = "Endpoints moved to Auth API in ADR-0026 phase 5c2; AuthApiFactory rebuild lands in a follow-up.")]
    public async Task Revoke_returns_204_and_subsequent_auth_is_401()
    {
        var client = _factory.CreateClient();
        var mint = await client.PostAsJsonAsync(
            $"/players/{_factory.DevPlayerId}/agent-tokens",
            new { label = "ephemeral" });
        var payload = await mint.Content.ReadFromJsonAsync<MintResponseView>();
        Assert.NotNull(payload);

        // The token works.
        var meOk = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meOk.Headers.TryAddWithoutValidation("X-Agent-Token", payload!.Plaintext);
        var meOkResponse = await client.SendAsync(meOk);
        Assert.Equal(HttpStatusCode.OK, meOkResponse.StatusCode);

        var revoke = await client.DeleteAsync(
            $"/players/{_factory.DevPlayerId}/agent-tokens/{payload.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // Now the token is dead — auth pipeline must reject (the cache was
        // invalidated by the revoke endpoint).
        var meDead = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meDead.Headers.TryAddWithoutValidation("X-Agent-Token", payload.Plaintext);
        var meDeadResponse = await client.SendAsync(meDead);
        Assert.Equal(HttpStatusCode.Unauthorized, meDeadResponse.StatusCode);
    }

    [Fact(Skip = "Endpoints moved to Auth API in ADR-0026 phase 5c2; AuthApiFactory rebuild lands in a follow-up.")]
    public async Task Me_returns_player_when_token_is_valid()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync(label: "/me probe");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal(_factory.DevPlayerId, me!.PlayerId);
    }

    [Fact(Skip = "Endpoints moved to Auth API in ADR-0026 phase 5c2; AuthApiFactory rebuild lands in a follow-up.")]
    public async Task Mint_for_unknown_player_returns_404()
    {
        var client = _factory.CreateClient();
        var unknown = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/players/{unknown}/agent-tokens",
            new { label = "noop" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record MintResponseView(Guid Id, string Plaintext, string Label, DateTime CreatedUtc);
    private sealed record TokenListItem(Guid Id, string Label, DateTime CreatedUtc, DateTime? LastSeenUtc, DateTime? RevokedUtc);
    private sealed record MeResponse(Guid PlayerId, string DisplayName, Guid TokenId);
}
