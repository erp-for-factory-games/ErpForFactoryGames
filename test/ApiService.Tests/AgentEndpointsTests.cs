using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ApiService.Tests;

/// <summary>
/// Integration tests for the agent upload + status endpoints (#199).
/// Token validation, content-type rejection, and the status snapshot
/// surface are covered; round-tripping a real <c>.sav</c> through the
/// parser is left to manual smoke (no committed fixture, and the parser
/// is exercised by the existing /factory/ingest tests).
/// </summary>
public sealed class AgentEndpointsTests : IClassFixture<AgentEndpointsTests.AgentApiFactory>
{
    private readonly AgentApiFactory _factory;

    public AgentEndpointsTests(AgentApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Upload_without_token_returns_401()
    {
        var client = _factory.CreateClient();
        var content = new ByteArrayContent(new byte[] { 0x01, 0x02 });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PostAsync("/agent/savegames/satisfactory", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_wrong_content_type_returns_415()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("not a save", System.Text.Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, "/agent/savegames/satisfactory")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", "any-non-empty");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_garbage_body_returns_422_and_records_failure()
    {
        var client = _factory.CreateClient();
        var content = new ByteArrayContent(new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x00, 0x00, 0x00, 0x00 });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/agent/savegames/satisfactory")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", "any-non-empty");
        request.Headers.TryAddWithoutValidation("X-Agent-FileName", "garbage.sav");
        request.Headers.TryAddWithoutValidation("X-Agent-Version", "0.0.0-test");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The failure should be visible on /agent/status too.
        var status = await client.GetFromJsonAsync<StatusEnvelope>("/agent/status");
        Assert.NotNull(status);
        Assert.NotNull(status!.LastUpload);
        Assert.False(status.LastUpload!.Succeeded);
        Assert.Equal(422, status.LastUpload.StatusCode);
        Assert.Equal("garbage.sav", status.LastUpload.FileName);
        Assert.Equal("0.0.0-test", status.LastUpload.AgentVersion);
    }

    [Fact]
    public async Task Status_returns_isStale_true_with_no_upload()
    {
        // Fresh factory — never received an upload. /agent/status should
        // surface that as isStale=true with null lastUpload.
        await using var fresh = new AgentApiFactory();
        var client = fresh.CreateClient();

        var status = await client.GetFromJsonAsync<StatusEnvelope>("/agent/status");

        Assert.NotNull(status);
        Assert.True(status!.IsStale);
        Assert.Null(status.LastUpload);
        Assert.Null(status.AgentSeen);
    }

    // Wire-equivalent of the JSON the endpoint emits. Matches the
    // anonymous-object shape in Program.cs.
    private sealed record StatusEnvelope(UploadSnapshotView? LastUpload, DateTimeOffset? AgentSeen, bool IsStale);
    private sealed record UploadSnapshotView(
        string FileName,
        DateTimeOffset ParsedAt,
        int? SaveVersion,
        int? BuildVersion,
        bool Succeeded,
        int StatusCode,
        string? Detail,
        string? AgentVersion);

    public sealed class AgentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"erp-agent-tests-{Guid.NewGuid():N}.db");
        private readonly string _uploadDir = Path.Combine(Path.GetTempPath(), $"erp-agent-uploads-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["ConnectionStrings:Plans"] = $"Data Source={_dbPath}",
                    ["AgentUploads:UploadDirectory"] = _uploadDir,
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
            try { if (Directory.Exists(_uploadDir)) Directory.Delete(_uploadDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
