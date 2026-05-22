using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ApiService.Tests;

/// <summary>
/// Integration tests for the agent log-tail endpoints (#210).
/// </summary>
public sealed class AgentLogsEndpointsTests : IClassFixture<AgentLogsEndpointsTests.LogsApiFactory>
{
    private readonly LogsApiFactory _factory;

    public AgentLogsEndpointsTests(LogsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_without_token_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/agent/logs", new { lines = new[] { "x" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_wrong_content_type_returns_415()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/agent/logs")
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", "any");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_then_Get_round_trips_lines_in_chronological_order()
    {
        await using var fresh = new LogsApiFactory();
        var client = fresh.CreateClient();

        async Task Post(params string[] lines)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/logs")
            {
                Content = JsonContent.Create(new { lines, agentVersion = "0.0.0-test" }),
            };
            request.Headers.TryAddWithoutValidation("X-Agent-Token", "any-non-empty");
            request.Headers.TryAddWithoutValidation("X-Agent-Version", "0.0.0-test");
            var resp = await client.SendAsync(request);
            Assert.True(resp.IsSuccessStatusCode, $"POST failed: {(int)resp.StatusCode}");
        }

        await Post("first batch line 1", "first batch line 2");
        await Post("second batch line 1");

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/agent/logs?limit=10");

        Assert.NotNull(logs);
        Assert.Equal(3, logs!.TotalReceived);
        Assert.NotNull(logs.AgentLastSeen);
        Assert.Equal(
            new[] { "first batch line 1", "first batch line 2", "second batch line 1" },
            logs.Lines.Select(l => l.Text));
        Assert.All(logs.Lines, l => Assert.Equal("0.0.0-test", l.AgentVersion));
    }

    [Fact]
    public async Task Get_with_limit_returns_only_latest_N()
    {
        await using var fresh = new LogsApiFactory();
        var client = fresh.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/agent/logs")
        {
            Content = JsonContent.Create(new { lines = new[] { "1", "2", "3", "4", "5" } }),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", "any");
        await client.SendAsync(request);

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/agent/logs?limit=2");
        Assert.NotNull(logs);
        Assert.Equal(5, logs!.TotalReceived);
        Assert.Equal(new[] { "4", "5" }, logs.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Empty_buffer_returns_empty_with_zero_total()
    {
        await using var fresh = new LogsApiFactory();
        var client = fresh.CreateClient();

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/agent/logs");
        Assert.NotNull(logs);
        Assert.Empty(logs!.Lines);
        Assert.Equal(0, logs.TotalReceived);
        Assert.Null(logs.AgentLastSeen);
    }

    [Fact]
    public async Task Ring_buffer_evicts_oldest_when_capacity_exceeded()
    {
        await using var fresh = new LogsApiFactory(maxBufferLines: 3);
        var client = fresh.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/agent/logs")
        {
            Content = JsonContent.Create(new { lines = new[] { "a", "b", "c", "d", "e" } }),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", "any");
        await client.SendAsync(request);

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/agent/logs?limit=100");
        Assert.NotNull(logs);
        Assert.Equal(5, logs!.TotalReceived);
        // Only the last 3 should remain.
        Assert.Equal(new[] { "c", "d", "e" }, logs.Lines.Select(l => l.Text));
    }

    private sealed record LogsEnvelope(
        IReadOnlyList<LogLineView> Lines,
        long TotalReceived,
        DateTimeOffset? AgentLastSeen);

    private sealed record LogLineView(string Text, DateTimeOffset UploadedAt, string? AgentVersion);

    public sealed class LogsApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"erp-agent-logs-tests-{Guid.NewGuid():N}.db");
        private readonly string _uploadDir = Path.Combine(Path.GetTempPath(), $"erp-agent-logs-uploads-{Guid.NewGuid():N}");
        private readonly int _maxBufferLines;

        // Parameterless ctor is the one xUnit uses for the IClassFixture
        // wiring; the int-overload is internal so we can spin up
        // factories with custom buffer sizes from individual tests.
        public LogsApiFactory() : this(maxBufferLines: 2000) { }

        internal LogsApiFactory(int maxBufferLines) => _maxBufferLines = maxBufferLines;

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
                    ["AgentLogs:MaxBufferLines"] = _maxBufferLines.ToString(),
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
