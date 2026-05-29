using System.Net;
using System.Net.Http.Headers;
using Erp.Domain.Common;
using Erp.Presentation.Api.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Satisfactory.Presentation.Api.Tests;

/// <summary>
/// ADR-0027 / phase 5c3 — JWT/HMAC agent-token auth.
///
/// Unit coverage for <see cref="AgentTokenJwt"/> sign/verify, plus an
/// integration check that a JWT presented in <c>X-Agent-Token</c> authenticates
/// against the Satisfactory API <b>without</b> any DB row — the whole point of
/// moving game-API verification off the Auth DB.
/// </summary>
public sealed class JwtAuthTests : IClassFixture<AgentEndpointsTests.AgentApiFactory>
{
    private readonly AgentEndpointsTests.AgentApiFactory _factory;

    public JwtAuthTests(AgentEndpointsTests.AgentApiFactory factory) => _factory = factory;

    // A signer over default AuthOptions → uses the dev fallback key, which is
    // exactly what the API-under-test uses when no Auth:JwtSigningKey is set.
    private static AgentTokenJwt DevSigner(AuthOptions? options = null) =>
        new(Options.Create(options ?? new AuthOptions()), NullLogger<AgentTokenJwt>.Instance);

    [Fact]
    public async Task Sign_then_validate_round_trips_player_and_token_ids()
    {
        var signer = DevSigner();
        var playerId = new PlayerId(Guid.NewGuid());
        var tokenId = AgentTokenId.New();

        var jwt = signer.Sign(playerId, tokenId, DateTime.UtcNow);
        var result = await signer.ValidateAsync(jwt);

        Assert.True(result.IsValid);
        Assert.Equal(playerId, result.PlayerId);
        Assert.Equal(tokenId, result.TokenId);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var signer = DevSigner();
        var jwt = signer.Sign(new PlayerId(Guid.NewGuid()), AgentTokenId.New(), DateTime.UtcNow);

        // Flip a character in the signature segment.
        var tampered = jwt[..^1] + (jwt[^1] == 'A' ? 'B' : 'A');

        var result = await signer.ValidateAsync(tampered);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var signer = DevSigner(new AuthOptions { JwtLifetimeDays = 1 });
        // Issue it ~400 days ago so exp (iat + 1 day) is well in the past.
        var jwt = signer.Sign(new PlayerId(Guid.NewGuid()), AgentTokenId.New(), DateTime.UtcNow.AddDays(-400));

        var result = await signer.ValidateAsync(jwt);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        var minter = DevSigner(new AuthOptions { JwtSigningKey = "a-completely-different-256-bit-key-aaaaaaaaaaaaaaaaaaaa" });
        var verifier = DevSigner(new AuthOptions { JwtSigningKey = "the-other-256-bit-key-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" });

        var jwt = minter.Sign(new PlayerId(Guid.NewGuid()), AgentTokenId.New(), DateTime.UtcNow);

        var result = await verifier.ValidateAsync(jwt);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Garbage_and_empty_tokens_are_rejected()
    {
        var signer = DevSigner();
        Assert.False((await signer.ValidateAsync("")).IsValid);
        Assert.False((await signer.ValidateAsync("not-a-jwt")).IsValid);
        Assert.False((await signer.ValidateAsync("eafg_legacy-style-token")).IsValid);
    }

    [Fact]
    public async Task Jwt_in_X_Agent_Token_authenticates_without_a_DB_row()
    {
        // Fresh factory — no Player, no AgentToken row anywhere. A valid JWT
        // must still authenticate (the hybrid authenticator's JWT path never
        // touches the DB). We assert 415 (content-type) rather than 401, which
        // proves auth passed.
        await using var fresh = new AgentEndpointsTests.AgentApiFactory();
        var client = fresh.CreateClient();

        var jwt = DevSigner().Sign(new PlayerId(fresh.DevPlayerId), AgentTokenId.New(), DateTime.UtcNow);

        var content = new StringContent("not a save", System.Text.Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/savegames/satisfactory")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", jwt);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Forged_jwt_in_X_Agent_Token_is_rejected_401()
    {
        await using var fresh = new AgentEndpointsTests.AgentApiFactory();
        var client = fresh.CreateClient();

        // Signed with a key the API doesn't know → must 401, not 415.
        var forged = DevSigner(new AuthOptions { JwtSigningKey = "attacker-controlled-key-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz" })
            .Sign(new PlayerId(fresh.DevPlayerId), AgentTokenId.New(), DateTime.UtcNow);

        var content = new ByteArrayContent(new byte[] { 0x01 });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/savegames/satisfactory") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", forged);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
