using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Satisfactory.Presentation.Api.Tests;

/// <summary>
/// End-to-end tests for the catalogue re-ingest trigger (#239, ADR-0025 §7).
/// Sets the flag via POST, asserts it appears in the log-tail response,
/// and that a subsequent catalogue upload clears it.
/// </summary>
public sealed class ReIngestCatalogueTests : IClassFixture<AgentEndpointsTests.AgentApiFactory>
{
    private readonly AgentEndpointsTests.AgentApiFactory _factory;

    public ReIngestCatalogueTests(AgentEndpointsTests.AgentApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Re_ingest_endpoint_sets_flag_and_logs_response_carries_it()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync(label: "reingest probe");

        // Set the flag.
        var trigger = await client.PostAsync(
            $"/players/{_factory.DevPlayerId}/re-ingest-catalogue",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, trigger.StatusCode);

        // The flag rides the log-tail poll response.
        var poll = await PostLogLineAsync(client, token, "first poll after trigger");
        var payload = await poll.Content.ReadFromJsonAsync<LogTailResponseView>();
        Assert.NotNull(payload);
        Assert.True(payload!.ReIngestRequested);

        // The catalogue status endpoint reflects the flag too.
        var status = await client.GetFromJsonAsync<CatalogueStatusEnvelope>(
            $"/players/{_factory.DevPlayerId}/catalogue/satisfactory");
        Assert.NotNull(status);
        Assert.True(status!.ReIngestRequested);
    }

    [Fact]
    public async Task Catalogue_upload_clears_re_ingest_flag()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync(label: "clear-flag probe");

        await client.PostAsync($"/players/{_factory.DevPlayerId}/re-ingest-catalogue", content: null);

        // Sanity: flag is set.
        var preStatus = await client.GetFromJsonAsync<CatalogueStatusEnvelope>(
            $"/players/{_factory.DevPlayerId}/catalogue/satisfactory");
        Assert.True(preStatus!.ReIngestRequested);

        // Upload a fresh catalogue.
        var bytes = Encoding.UTF8.GetBytes($$"""{"version":"clear-flag","stamp":"{{Guid.NewGuid():N}}"}""");
        var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/agent/catalogue/satisfactory") { Content = body };
        upload.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        var uploadResponse = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        // Flag is cleared.
        var postStatus = await client.GetFromJsonAsync<CatalogueStatusEnvelope>(
            $"/players/{_factory.DevPlayerId}/catalogue/satisfactory");
        Assert.False(postStatus!.ReIngestRequested);

        // And the row's hash matches what we just uploaded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var row = await db.PlayerCatalogues.AsNoTracking()
            .SingleAsync(c => c.PlayerId == new PlayerId(_factory.DevPlayerId));
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, row.DocsHash);
    }

    [Fact]
    public async Task Re_ingest_for_unknown_player_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/players/{Guid.NewGuid()}/re-ingest-catalogue",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Catalogue_status_without_upload_returns_null_catalogue_block()
    {
        // Fresh isolated factory so no previous test's upload bleeds in.
        await using var fresh = new AgentEndpointsTests.AgentApiFactory();
        var client = fresh.CreateClient();

        // The dev player must exist — /players/{id}/catalogue/* 404s for unknown
        // players (see Re_ingest_for_unknown_player_returns_404), and the Sat API
        // no longer seeds it (DevPlayerBootstrap moved to the Auth API in 5c2).
        await fresh.EnsureDevPlayerAsync();

        var status = await client.GetFromJsonAsync<CatalogueStatusEnvelope>(
            $"/players/{fresh.DevPlayerId}/catalogue/satisfactory");

        Assert.NotNull(status);
        Assert.False(status!.ReIngestRequested);
        Assert.Null(status.Catalogue);
    }

    private static async Task<HttpResponseMessage> PostLogLineAsync(HttpClient client, string token, string line)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/logs")
        {
            Content = JsonContent.Create(new { lines = new[] { line } }),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        return await client.SendAsync(request);
    }

    private sealed record LogTailResponseView(int Received, long? Retained, bool ReIngestRequested);
    private sealed record CatalogueStatusEnvelope(Guid PlayerId, bool ReIngestRequested, DateTime? ReIngestRequestedUtc, CatalogueBlock? Catalogue);
    private sealed record CatalogueBlock(string DocsHash, string? GameVersion, long SizeBytes, DateTime UploadedUtc);
}
