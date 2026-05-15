using ERP.Application;
using ERP.Domain;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Tests;

/// <summary>
/// Integration tests for the EF Core <see cref="PlanRepository"/> exercised
/// against a real SQLite file. Issue #77 — proves saved plans survive a
/// process restart, which is the whole point of swapping out the in-memory
/// foundation from PR #71.
///
/// <para>
/// SQLite file (not in-memory) so that closing and reopening the context
/// genuinely simulates a process restart: the schema and data must persist
/// across <see cref="DbContext"/> lifetimes.
/// </para>
/// </summary>
public class PlanRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public PlanRepositoryTests()
    {
        // One DB file per test instance, in the temp dir. xUnit creates a new
        // fixture per test method, so no cross-test contamination.
        _dbPath = Path.Combine(Path.GetTempPath(), $"erp-plans-test-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var ctx = CreateContext();
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        // Best-effort cleanup. SQLite holds a file handle until the connection
        // is disposed, so the using-block in each test releases it for us.
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
    public async Task SavedPlan_Survives_Context_Recreation()
    {
        // Acts as a "restart": save through one context, reload through another.
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var plan = new SavedPlan(
            id: id,
            name: "Iron plates 60/min",
            targets: [new ProductionTarget(new ItemId("Desc_IronPlate_C"), 60m)],
            available: [new ResourceAvailability(new ItemId("Desc_OreIron_C"), 240m)],
            createdUtc: now,
            updatedUtc: now);

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            await repo.AddAsync(plan);
            await repo.SaveChangesAsync();
        }

        // Fresh context = simulated process restart.
        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            var reloaded = await repo.GetAsync(id);

            Assert.NotNull(reloaded);
            Assert.Equal("Iron plates 60/min", reloaded!.Name);
            Assert.Single(reloaded.Targets);
            Assert.Equal("Desc_IronPlate_C", reloaded.Targets[0].Item.Value);
            Assert.Equal(60m, reloaded.Targets[0].ItemsPerMinute);
            Assert.Single(reloaded.Available);
            Assert.Equal("Desc_OreIron_C", reloaded.Available[0].Item.Value);
            Assert.Equal(240m, reloaded.Available[0].ItemsPerMinute);
        }
    }

    [Fact]
    public async Task Update_Replaces_Targets_And_Available_And_Persists()
    {
        var id = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            await repo.AddAsync(new SavedPlan(
                id, "v1",
                [new ProductionTarget(new ItemId("Desc_IronIngot_C"), 30m)],
                [new ResourceAvailability(new ItemId("Desc_OreIron_C"), 30m)],
                t0, t0));
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            var loaded = await repo.GetAsync(id);
            Assert.NotNull(loaded);
            var t1 = t0.AddMinutes(5);
            loaded!.Rename("v2", t1);
            loaded.Replace(
                [new ProductionTarget(new ItemId("Desc_IronPlate_C"), 20m),
                 new ProductionTarget(new ItemId("Desc_IronRod_C"),   15m)],
                [new ResourceAvailability(new ItemId("Desc_OreIron_C"), 999m)],
                t1);
            await repo.UpdateAsync(loaded);
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            var reloaded = await repo.GetAsync(id);

            Assert.NotNull(reloaded);
            Assert.Equal("v2", reloaded!.Name);
            Assert.Equal(2, reloaded.Targets.Count);
            Assert.Contains(reloaded.Targets, t => t.Item.Value == "Desc_IronPlate_C" && t.ItemsPerMinute == 20m);
            Assert.Contains(reloaded.Targets, t => t.Item.Value == "Desc_IronRod_C" && t.ItemsPerMinute == 15m);
            Assert.Single(reloaded.Available);
            Assert.Equal(999m, reloaded.Available[0].ItemsPerMinute);
        }
    }

    [Fact]
    public async Task List_Returns_All_Saved_Plans_Ordered_By_UpdatedUtc_Desc()
    {
        var now = DateTime.UtcNow;
        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            await repo.AddAsync(new SavedPlan(Guid.NewGuid(), "old",
                [new ProductionTarget(new ItemId("A"), 1m)], [],
                now.AddDays(-2), now.AddDays(-2)));
            await repo.AddAsync(new SavedPlan(Guid.NewGuid(), "newest",
                [new ProductionTarget(new ItemId("B"), 1m)], [],
                now, now));
            await repo.AddAsync(new SavedPlan(Guid.NewGuid(), "middle",
                [new ProductionTarget(new ItemId("C"), 1m)], [],
                now.AddDays(-1), now.AddDays(-1)));
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            var all = await repo.ListAsync();
            Assert.Equal(3, all.Count);
            Assert.Equal(["newest", "middle", "old"], all.Select(p => p.Name).ToArray());
        }
    }

    [Fact]
    public async Task Delete_Removes_Plan_And_Returns_True()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            await repo.AddAsync(new SavedPlan(id, "doomed",
                [new ProductionTarget(new ItemId("X"), 1m)], [], now, now));
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            Assert.True(await repo.DeleteAsync(id));
            await repo.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new PlanRepository(ctx);
            Assert.Null(await repo.GetAsync(id));
            Assert.False(await repo.DeleteAsync(id));
        }
    }
}
