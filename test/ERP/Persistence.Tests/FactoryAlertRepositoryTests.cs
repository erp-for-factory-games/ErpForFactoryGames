using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence.Tests;

/// <summary>
/// Integration tests for <see cref="FactoryAlertRepository"/> against a real
/// SQLite file (#116, phase A). Same pattern as <see cref="PlanRepositoryTests"/>:
/// one DB file per test, migrations applied in the ctor, file cleaned up on
/// dispose.
/// </summary>
public class FactoryAlertRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public FactoryAlertRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"erp-alerts-test-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var ctx = CreateContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* test teardown — ignore */ }
        }
        GC.SuppressFinalize(this);
    }

    private SqlitePlanDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlitePlanDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new SqlitePlanDbContext(options);
    }

    [Fact]
    public async Task Alert_Survives_Context_Recreation()
    {
        var alert = new FactoryAlert(
            id: Guid.NewGuid(),
            key: "blocker:Desc_OreIron_C",
            severity: AlertSeverity.Blocker,
            source: "save:Beta Game_autosave_1.sav",
            title: "Iron ore extraction shortfall",
            detail: "Demand 450/min, supply 360/min — 90/min short.",
            fix: "Add 3 MK1 miners or upgrade to MK2.",
            createdUtc: DateTime.UtcNow);

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            await repo.AddAsync(alert);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var loaded = await repo.GetAsync(alert.Id);
            Assert.NotNull(loaded);
            Assert.Equal("blocker:Desc_OreIron_C", loaded!.Key);
            Assert.Equal(AlertSeverity.Blocker, loaded.Severity);
            Assert.Equal("Iron ore extraction shortfall", loaded.Title);
            Assert.True(loaded.IsActive);
        }
    }

    [Fact]
    public async Task ListActive_Excludes_Resolved_And_Dismissed()
    {
        var now = DateTime.UtcNow;
        var active = NewAlert("blocker:active", AlertSeverity.Blocker, now);
        var resolved = NewAlert("risk:resolved", AlertSeverity.Risk, now);
        resolved.Resolve(now.AddMinutes(1));
        var dismissed = NewAlert("degraded:dismissed", AlertSeverity.Degraded, now);
        dismissed.Dismiss(now.AddMinutes(1));

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            await repo.AddAsync(active);
            await repo.AddAsync(resolved);
            await repo.AddAsync(dismissed);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var list = await repo.ListActiveAsync();
            var listKeys = list.Select(a => a.Key).ToList();
            Assert.Single(listKeys);
            Assert.Equal("blocker:active", listKeys[0]);
        }
    }

    [Fact]
    public async Task ListActive_Orders_By_Severity_Descending()
    {
        var now = DateTime.UtcNow;
        var risk = NewAlert("risk:a", AlertSeverity.Risk, now);
        var blocker = NewAlert("blocker:b", AlertSeverity.Blocker, now.AddMinutes(1));
        var degraded = NewAlert("degraded:c", AlertSeverity.Degraded, now.AddMinutes(2));

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            // Insert in non-severity order to make sure the result isn't an
            // artefact of insertion order.
            await repo.AddAsync(risk);
            await repo.AddAsync(blocker);
            await repo.AddAsync(degraded);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var list = await repo.ListActiveAsync();
            Assert.Equal(
                new[] { AlertSeverity.Blocker, AlertSeverity.Degraded, AlertSeverity.Risk },
                list.Select(a => a.Severity).ToArray());
        }
    }

    [Fact]
    public async Task FindActiveByKey_Ignores_Dismissed_Same_Key()
    {
        var now = DateTime.UtcNow;
        const string key = "blocker:Desc_Coal_C";
        var oldDismissed = NewAlert(key, AlertSeverity.Blocker, now);
        oldDismissed.Dismiss(now.AddMinutes(1));
        var newActive = NewAlert(key, AlertSeverity.Blocker, now.AddMinutes(10));

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            await repo.AddAsync(oldDismissed);
            await repo.AddAsync(newActive);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var found = await repo.FindActiveByKeyAsync(key);
            Assert.NotNull(found);
            Assert.Equal(newActive.Id, found!.Id);
            Assert.True(found.IsActive);
        }
    }

    [Fact]
    public async Task Refresh_Updates_Fields_Without_Touching_Dismissal()
    {
        var now = DateTime.UtcNow;
        var alert = NewAlert("risk:test", AlertSeverity.Risk, now);
        alert.Dismiss(now.AddMinutes(1));

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            await repo.AddAsync(alert);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var loaded = await repo.GetAsync(alert.Id);
            Assert.NotNull(loaded);
            loaded!.Refresh("New title", "New detail", "New fix");
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new FactoryAlertRepository(ctx);
            var reloaded = await repo.GetAsync(alert.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("New title", reloaded!.Title);
            Assert.NotNull(reloaded.DismissedUtc); // refresh must NOT clear dismissal
            Assert.False(reloaded.IsActive);
        }
    }

    private static FactoryAlert NewAlert(string key, AlertSeverity severity, DateTime createdUtc) =>
        new(
            id: Guid.NewGuid(),
            key: key,
            severity: severity,
            source: "test",
            title: $"Alert {key}",
            detail: "details",
            fix: "fix",
            createdUtc: createdUtc);
}
