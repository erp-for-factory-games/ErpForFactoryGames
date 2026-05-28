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
        var response = await client.PostAsJsonAsync("/api/agent/logs", new { lines = new[] { "x" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_wrong_content_type_returns_415()
    {
        var client = _factory.CreateClient();
        var token = await _factory.MintTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/logs")
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_then_Get_round_trips_lines_in_chronological_order()
    {
        await using var fresh = new LogsApiFactory();
        var client = fresh.CreateClient();
        var token = await fresh.MintTokenAsync();

        async Task Post(params string[] lines)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/logs")
            {
                Content = JsonContent.Create(new { lines, agentVersion = "0.0.0-test" }),
            };
            request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
            request.Headers.TryAddWithoutValidation("X-Agent-Version", "0.0.0-test");
            var resp = await client.SendAsync(request);
            Assert.True(resp.IsSuccessStatusCode, $"POST failed: {(int)resp.StatusCode}");
        }

        await Post("first batch line 1", "first batch line 2");
        await Post("second batch line 1");

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/api/agent/logs?limit=10");

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
        var token = await fresh.MintTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/logs")
        {
            Content = JsonContent.Create(new { lines = new[] { "1", "2", "3", "4", "5" } }),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        await client.SendAsync(request);

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/api/agent/logs?limit=2");
        Assert.NotNull(logs);
        Assert.Equal(5, logs!.TotalReceived);
        Assert.Equal(new[] { "4", "5" }, logs.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Empty_buffer_returns_empty_with_zero_total()
    {
        await using var fresh = new LogsApiFactory();
        var client = fresh.CreateClient();

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/api/agent/logs");
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
        var token = await fresh.MintTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent/logs")
        {
            Content = JsonContent.Create(new { lines = new[] { "a", "b", "c", "d", "e" } }),
        };
        request.Headers.TryAddWithoutValidation("X-Agent-Token", token);
        await client.SendAsync(request);

        var logs = await client.GetFromJsonAsync<LogsEnvelope>("/api/agent/logs?limit=100");
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

        public Guid DevPlayerId { get; } = Guid.NewGuid();

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
                    ["Auth:DevPlayerId"] = DevPlayerId.ToString(),
                });
            });
        }

        /// <summary>
        /// Mint a fresh agent token for the dev player and return the
        /// plaintext header value. Mirrors <c>AgentApiFactory.MintTokenAsync</c>
        /// — ADR-0025 §3 validates by hash so tests need a real row in the DB.
        /// </summary>
        public async Task<string> MintTokenAsync(string? label = null)
        {
            // After ADR-0026 phase 5c2 the mint endpoint lives on Auth API,
            // not on the Sat API binary this fixture spins up. Bypass HTTP
            // and mint via DI so this test fixture keeps targeting Sat.
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<Erp.Application.Common.IAgentTokenRepository>();
            var hasher = scope.ServiceProvider.GetRequiredService<Erp.Application.Common.IAgentTokenHasher>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

            var plaintext = hasher.MintPlaintext();
            var hash = hasher.Hash(plaintext);
            var token = new Erp.Domain.Common.AgentToken(
                Erp.Domain.Common.AgentTokenId.New(),
                new Erp.Domain.Common.PlayerId(DevPlayerId),
                label ?? $"test-{Guid.NewGuid():N}",
                hash,
                clock.GetUtcNow().UtcDateTime);
            await tokens.AddAsync(token, default);
            await tokens.SaveChangesAsync(default);
            return plaintext;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
            try { if (Directory.Exists(_uploadDir)) Directory.Delete(_uploadDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
