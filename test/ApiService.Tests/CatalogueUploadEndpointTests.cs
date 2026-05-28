using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiService.Tests;

/// <summary>
/// Integration tests for <c>POST /api/agent/catalogue/satisfactory</c>
/// (#238, ADR-0025 §4-§5). Validates auth, content-type rejection, hash
/// computation + dedup, and upsert semantics.
/// </summary>
public sealed class CatalogueUploadEndpointTests : IClassFixture<AgentEndpointsTests.AgentApiFactory>
{
    private readonly AgentEndpointsTests.AgentApiFactory _factory;

    public CatalogueUploadEndpointTests(AgentEndpointsTests.AgentApiFactory factory) => _factory = factory;

    private const string Endpoint = "/api/agent/catalogue/satisfactory";

    [Fact]
    public async Task Upload_without_token_returns_401()
    {
        var client = _factory.CreateClient();
        var body = new ByteArrayContent(SampleDocsJson());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync(Endpoint, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_wrong_content_type_returns_415()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();
        var body = new StringContent("not docs", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = body };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task First_upload_persists_row_with_correct_hash()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();
        // Unique bytes per test invocation so we don't accidentally hit the
        // 304-cached path when CatalogueUploadEndpointTests share a fixture
        // with the rest of the API test classes.
        var bytes = UniqueDocsJson();

        var response = await PostAsync(client, token, bytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UploadResponseView>();
        Assert.NotNull(payload);
        Assert.True(payload!.Changed);
        Assert.Equal(bytes.Length, payload.SizeBytes);

        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, payload.DocsHash);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var row = await db.PlayerCatalogues.AsNoTracking()
            .SingleAsync(c => c.PlayerId == new PlayerId(_factory.DevPlayerId) && c.Game == PlayerCatalogue.SatisfactoryGame);
        Assert.Equal(expectedHash, row.DocsHash);
        Assert.Equal(bytes.Length, row.SizeBytes);
    }

    [Fact]
    public async Task Re_upload_of_identical_bytes_returns_304()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();
        var bytes = UniqueDocsJson();

        var first = await PostAsync(client, token, bytes);
        Assert.True(first.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotModified,
            $"First upload should be 200 OK or 304 (if state survived from a sibling test); got {first.StatusCode}");

        var second = await PostAsync(client, token, bytes);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Re_upload_of_different_bytes_overwrites_row()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();

        var v1 = UniqueDocsJson("v1");
        var v2 = UniqueDocsJson("v2");

        var first = await PostAsync(client, token, v1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostAsync(client, token, v2);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var rows = await db.PlayerCatalogues.AsNoTracking()
            .Where(c => c.PlayerId == new PlayerId(_factory.DevPlayerId))
            .ToListAsync();
        // Composite PK on (PlayerId, Game) — only ever one row per player+game.
        Assert.Single(rows);
        var expected = Convert.ToHexString(SHA256.HashData(v2)).ToLowerInvariant();
        Assert.Equal(expected, rows[0].DocsHash);
    }

    [Fact]
    public async Task Empty_body_returns_400()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();
        var body = new ByteArrayContent(Array.Empty<byte>());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = body };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, byte[] bytes)
    {
        var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = body };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        return await client.SendAsync(request);
    }

    private static byte[] SampleDocsJson() =>
        Encoding.UTF8.GetBytes("""{"version":"test-fixture-1.0","items":[],"buildings":[],"recipes":[]}""");

    private static byte[] UniqueDocsJson(string? marker = null)
    {
        var tag = marker ?? Guid.NewGuid().ToString("N");
        return Encoding.UTF8.GetBytes($$"""{"version":"test","stamp":"{{Guid.NewGuid():N}}","marker":"{{tag}}"}""");
    }

    private sealed record UploadResponseView(string DocsHash, long SizeBytes, DateTime UploadedUtc, bool Changed);
}
