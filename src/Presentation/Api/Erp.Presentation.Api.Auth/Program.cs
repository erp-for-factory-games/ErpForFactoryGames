using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Hosting.ServiceDefaults;
using Erp.Infrastructure;
using Erp.Infrastructure.Persistence;
using Erp.Presentation.Api.Auth;
using Erp.Presentation.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Auth API owns the Player + AgentToken aggregate (ADR-0025 §1-§3,
// ADR-0026 §Presentation/Api/Auth). Same EF Core registration as the
// Sat API for now — phase 5c3 splits the storage so this binary is the
// only writer.
builder.Services.AddErpInfrastructure(builder.Configuration);
builder.Services.AddErpPersistence(builder.Configuration);

// Agent-token auth pipeline (ADR-0027 / 5c3): hybrid JWT-or-legacy
// authenticator + the AgentTokenJwt signer the mint endpoint uses.
builder.Services.AddAgentTokenAuth(builder.Configuration);
builder.Services.AddHostedService<DevPlayerBootstrap>();

// ICurrentPlayer + ICatalogueStorage are needed transitively by
// AddErpInfrastructure (PlayerScopedCatalogProvider). Auth API never
// actually serves catalog requests so these registrations are satisfying
// the DI validator more than anything. Phase 5c3 splits the service
// registration so this dead-weight goes away.
builder.Services.AddScoped<ICurrentPlayer, CurrentPlayerFromAuthOptions>();
builder.Services.Configure<CatalogueStorageOptions>(
    builder.Configuration.GetSection(CatalogueStorageOptions.SectionName));
builder.Services.AddSingleton<ICatalogueStorage, FileSystemCatalogueStorage>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Same migration-on-startup pattern as the Sat binary — both ride the
// same PlanDbContext while 5c3 hasn't split storage.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGet("/", () => "Auth API. Player + agent-token endpoints under /players/* and /api/me.");

// ---- Player + agent-token management (ADR-0025 §2, §9) -------------------
//
//   GET    /players/current                     — resolves the dev player
//                                                 (Auth:DevPlayerId) for the
//                                                 Web UI's "scope" context
//   POST   /players/{id}/agent-tokens           — mint, plaintext shown once
//   GET    /players/{id}/agent-tokens           — list (no plaintext)
//   DELETE /players/{id}/agent-tokens/{tokenId} — revoke
//   GET    /api/me                              — who-am-I for the agent's
//                                                 pairing validation call
//
// Note: the mint/list/revoke endpoints have no caller-auth in v2 — they're
// intended to be reached only by the Web UI, which itself runs the
// "single-user-shaped on purpose" dev-player flow until login lands.
// Production deployment must hide them behind the homelab's Web UI gate.
// ---------------------------------------------------------------------------

app.MapGet("/players/current", async (
    IOptions<AuthOptions> authOptions,
    IPlayerRepository players,
    CancellationToken ct) =>
{
    var playerId = new PlayerId(authOptions.Value.DevPlayerId);
    var player = await players.GetAsync(playerId, ct).ConfigureAwait(false);
    if (player is null)
    {
        return Results.Json(
            new { error = "Current player not yet provisioned. Check Auth:DevPlayerId and startup logs." },
            statusCode: 503);
    }

    return Results.Ok(new
    {
        playerId = player.Id.Value,
        displayName = player.DisplayName,
        createdUtc = player.CreatedUtc,
    });
});

app.MapPost("/players/{id:guid}/agent-tokens", async (
    Guid id,
    MintAgentTokenRequest request,
    IPlayerRepository players,
    IAgentTokenRepository tokens,
    IAgentTokenHasher hasher,
    AgentTokenJwt jwtSigner,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var playerId = new PlayerId(id);
    var player = await players.GetAsync(playerId, ct).ConfigureAwait(false);
    if (player is null) return Results.NotFound(new { error = $"Player {id} not found." });

    var label = string.IsNullOrWhiteSpace(request?.Label)
        ? $"Agent {DateTime.UtcNow:yyyy-MM-dd}"
        : request!.Label!.Trim();

    // ADR-0027: mint a JWT carrying sub=playerId, jti=tokenId. The AgentToken
    // row still exists for revocation + audit (and the legacy hybrid path);
    // its hash column holds the hash of the JWT so the row stays consistent,
    // but game APIs verify the JWT by signature, not by that hash.
    var now = clock.GetUtcNow().UtcDateTime;
    var tokenId = AgentTokenId.New();
    var jwt = jwtSigner.Sign(playerId, tokenId, now);
    var token = new AgentToken(
        tokenId,
        playerId,
        label,
        hasher.Hash(jwt),
        now);
    await tokens.AddAsync(token, ct).ConfigureAwait(false);
    await tokens.SaveChangesAsync(ct).ConfigureAwait(false);

    return Results.Json(new
    {
        id = token.Id.Value,
        plaintext = jwt,
        label = token.Label,
        createdUtc = token.CreatedUtc,
    }, statusCode: 201);
});

app.MapGet("/players/{id:guid}/agent-tokens", async (
    Guid id,
    IPlayerRepository players,
    IAgentTokenRepository tokens,
    CancellationToken ct) =>
{
    var playerId = new PlayerId(id);
    var player = await players.GetAsync(playerId, ct).ConfigureAwait(false);
    if (player is null) return Results.NotFound(new { error = $"Player {id} not found." });

    var list = await tokens.ListForPlayerAsync(playerId, ct).ConfigureAwait(false);
    return Results.Ok(list.Select(t => new AgentTokenView(
        Id: t.Id.Value,
        Label: t.Label,
        CreatedUtc: t.CreatedUtc,
        LastSeenUtc: t.LastSeenUtc,
        RevokedUtc: t.RevokedUtc)));
});

app.MapDelete("/players/{id:guid}/agent-tokens/{tokenId:guid}", async (
    Guid id,
    Guid tokenId,
    IAgentTokenRepository tokens,
    IAgentTokenAuthenticator authenticator,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var token = await tokens.GetAsync(new AgentTokenId(tokenId), ct).ConfigureAwait(false);
    if (token is null || token.PlayerId != new PlayerId(id))
    {
        return Results.NotFound(new { error = $"Token {tokenId} not found for player {id}." });
    }

    token.Revoke(clock.GetUtcNow().UtcDateTime);
    await tokens.SaveChangesAsync(ct).ConfigureAwait(false);
    authenticator.Invalidate(token.Id);
    return Results.NoContent();
});

// /api/me prefix per #245's routing rule — the agent reaches this through
// the Cloudflare tunnel for pair validation, so it has to sit under /api/*.
app.MapGet("/api/me", async (
    HttpRequest http,
    IAgentTokenAuthenticator authenticator,
    IPlayerRepository players,
    CancellationToken ct) =>
{
    var auth = await authenticator.AuthenticateAsync(http.Headers["X-Agent-Token"].ToString(), ct).ConfigureAwait(false);
    if (!auth.IsAuthenticated)
    {
        return Results.Json(new { error = "X-Agent-Token is missing or invalid." }, statusCode: 401);
    }

    var player = await players.GetAsync(auth.PlayerId, ct).ConfigureAwait(false);
    if (player is null)
    {
        return Results.Json(new { error = "Player no longer exists." }, statusCode: 401);
    }

    return Results.Ok(new
    {
        playerId = player.Id.Value,
        displayName = player.DisplayName,
        tokenId = auth.TokenId.Value,
    });
});

app.Run();

namespace Erp.Presentation.Api.Auth
{
    /// <summary>POST /players/{id}/agent-tokens body (ADR-0025 §2).</summary>
    public sealed record MintAgentTokenRequest(string? Label);

    /// <summary>GET /players/{id}/agent-tokens row shape (no plaintext).</summary>
    public sealed record AgentTokenView(
        Guid Id,
        string Label,
        DateTime CreatedUtc,
        DateTime? LastSeenUtc,
        DateTime? RevokedUtc);
}
