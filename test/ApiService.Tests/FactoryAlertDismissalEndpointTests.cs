using System.Net;
using ERP.Application;
using ERP.Domain;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiService.Tests;

/// <summary>
/// Integration tests for the dismissal endpoint (#116, phase C). Boots the
/// real <see cref="Program"/> via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// against a per-test SQLite file. Seeds an alert directly through the
/// repository (no create endpoint exists by design — alerts are written
/// by the analysis service), then POSTs the dismiss endpoint and verifies
/// state.
/// </summary>
public sealed class FactoryAlertDismissalEndpointTests : IClassFixture<FactoryAlertDismissalEndpointTests.AlertsApiFactory>
{
    private readonly AlertsApiFactory _factory;

    public FactoryAlertDismissalEndpointTests(AlertsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Dismiss_persists_DismissedUtc_and_removes_from_active_list()
    {
        var alertId = Guid.NewGuid();
        await SeedAlertAsync(new FactoryAlert(
            id: alertId,
            key: "blocker:Desc_OreIron_C",
            severity: AlertSeverity.Blocker,
            source: "save:test",
            title: "Iron Ore supply shortfall",
            detail: "Demand 450/min, supply 360/min — 90/min short.",
            fix: "Add 3 MK1 miners.",
            createdUtc: DateTime.UtcNow));

        var client = _factory.CreateClient();
        var response = await client.PostAsync($"/factory/alerts/{alertId}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Subsequent GET /factory/alerts must not include this row.
        var listResponse = await client.GetAsync("/factory/alerts");
        var body = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.DoesNotContain(alertId.ToString(), body);

        // Direct DB check — DismissedUtc set.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var stored = await db.FactoryAlerts.AsNoTracking().FirstAsync(a => a.Id == alertId);
        Assert.NotNull(stored.DismissedUtc);
    }

    [Fact]
    public async Task Dismiss_unknown_id_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync($"/factory/alerts/{Guid.NewGuid()}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dismiss_is_idempotent()
    {
        // Dismissing the same alert twice is a 204 both times — the user
        // double-clicking the dismiss button shouldn't surface an error.
        var alertId = Guid.NewGuid();
        await SeedAlertAsync(new FactoryAlert(
            id: alertId,
            key: "risk:Desc_Coal_C",
            severity: AlertSeverity.Risk,
            source: "save:test",
            title: "Coal at capacity",
            detail: "Demand 30/min, supply 30/min — 0/min headroom.",
            fix: "Scale coal extraction.",
            createdUtc: DateTime.UtcNow));

        var client = _factory.CreateClient();
        var first = await client.PostAsync($"/factory/alerts/{alertId}/dismiss", content: null);
        var second = await client.PostAsync($"/factory/alerts/{alertId}/dismiss", content: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    private async Task SeedAlertAsync(FactoryAlert alert)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFactoryAlertRepository>();
        await repo.AddAsync(alert);
        await repo.SaveChangesAsync();
    }

    public sealed class AlertsApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"erp-alerts-tests-{Guid.NewGuid():N}.db");

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
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        }
    }
}
