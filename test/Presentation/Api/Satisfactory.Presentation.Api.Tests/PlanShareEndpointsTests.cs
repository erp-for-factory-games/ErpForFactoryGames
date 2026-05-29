using System.Net;
using System.Net.Http.Json;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Satisfactory.Presentation.Api.Tests;

/// <summary>
/// Integration tests for the share-link API (#80). Boots the real
/// <see cref="Program"/> via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// with a per-test SQLite file so each run starts clean.
///
/// <para>
/// We exercise the full loop: <c>POST /plans</c> → <c>POST /plans/{id}/share</c>
/// → <c>GET /plans/shared/{token}</c> → <c>DELETE /plans/{id}/share/{token}</c>.
/// Revocation must turn subsequent fetches into 404s — that's the acceptance
/// criterion from the issue.
/// </para>
/// </summary>
public sealed class PlanShareEndpointsTests : IClassFixture<PlanShareEndpointsTests.SharedPlanApiFactory>
{
    private readonly SharedPlanApiFactory _factory;

    public PlanShareEndpointsTests(SharedPlanApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Share_link_round_trip_returns_plan_then_404_after_revoke()
    {
        var client = _factory.CreateClient();

        // 1. Save a plan.
        var save = await client.PostAsJsonAsync("/plans", new
        {
            name = "Test plan",
            targets = new[] { new { itemId = "Desc_IronPlate_C", itemsPerMinute = 30m } },
            available = new[] { new { itemId = "Desc_OreIron_C", itemsPerMinute = 60m } }
        });
        if (save.StatusCode != HttpStatusCode.Created)
        {
            var body = await save.Content.ReadAsStringAsync();
            var errors = string.Join("\n----\n", _factory.CapturedErrors);
            throw new Xunit.Sdk.XunitException($"POST /plans returned {(int)save.StatusCode}: {body}\n\nLogged errors:\n{errors}");
        }
        var saved = await save.Content.ReadFromJsonAsync<SavedPlanDto>();
        Assert.NotNull(saved);
        Assert.NotEqual(Guid.Empty, saved!.Id);

        // 2. Mint a share token.
        var shareResp = await client.PostAsync($"/plans/{saved.Id}/share", content: null);
        shareResp.EnsureSuccessStatusCode();
        var share = await shareResp.Content.ReadFromJsonAsync<ShareTokenDto>();
        Assert.NotNull(share);
        Assert.False(string.IsNullOrWhiteSpace(share!.Token));
        Assert.Contains(share.Token, share.Url);

        // 3. Public read-only endpoint returns the plan.
        var fetched = await client.GetFromJsonAsync<SavedPlanDto>($"/plans/shared/{share.Token}");
        Assert.NotNull(fetched);
        Assert.Equal(saved.Id, fetched!.Id);
        Assert.Equal("Test plan", fetched.Name);
        Assert.Single(fetched.Targets);
        Assert.Single(fetched.Available);
        Assert.Equal("Desc_IronPlate_C", fetched.Targets[0].ItemId);

        // 4. Revoke.
        var revoke = await client.DeleteAsync($"/plans/{saved.Id}/share/{share.Token}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // 5. Subsequent fetch is 404.
        var afterRevoke = await client.GetAsync($"/plans/shared/{share.Token}");
        Assert.Equal(HttpStatusCode.NotFound, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Unknown_token_returns_404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/plans/shared/this-token-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    public sealed record SavedPlanDto(
        Guid Id,
        string Name,
        IReadOnlyList<TargetDto> Targets,
        IReadOnlyList<AvailabilityDto> Available,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);
    public sealed record TargetDto(string ItemId, decimal ItemsPerMinute);
    public sealed record AvailabilityDto(string ItemId, decimal ItemsPerMinute);
    public sealed record ShareTokenDto(string Token, string Url, DateTime CreatedUtc, DateTime? ExpiresUtc);

    /// <summary>
    /// Spins up the ApiService with a unique SQLite file per fixture instance.
    /// SQLite (file-backed) keeps tests isolated without needing a server, and
    /// migrations run automatically because the host bootstraps in Development.
    /// </summary>
    public sealed class SharedPlanApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"erp-share-tests-{Guid.NewGuid():N}.db");

        public List<string> CapturedErrors { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["ConnectionStrings:Plans"] = $"Data Source={_dbPath}",
                });
            });
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CaptureLoggerProvider(CapturedErrors));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // Drop the per-fixture DB file. EF/SQLite holds the file open for
            // the lifetime of the host; once the factory disposes we can delete.
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _sink;
        public CaptureLoggerProvider(List<string> sink) => _sink = sink;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _sink);
        public void Dispose() { }

        private sealed class CaptureLogger : ILogger
        {
            private readonly string _category;
            private readonly List<string> _sink;
            public CaptureLogger(string category, List<string> sink) { _category = category; _sink = sink; }
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                lock (_sink)
                {
                    _sink.Add($"[{_category}] {formatter(state, exception)} {exception}");
                }
            }
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
