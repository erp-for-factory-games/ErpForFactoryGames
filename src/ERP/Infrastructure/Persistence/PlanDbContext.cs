using ERP.Domain;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// EF Core context for persisting <see cref="SavedPlan"/> aggregates (ADR-0018).
///
/// <para>
/// <b>Single context, dual provider</b>. Provider-specific behaviour stays out of
/// the model — selection happens in
/// <see cref="PersistenceServiceCollectionExtensions.AddErpPersistence"/> via
/// <c>UseSqlite</c> / <c>UseNpgsql</c>. Each provider's migrations live in a
/// dedicated folder (<c>Migrations/Sqlite</c>, <c>Migrations/Postgres</c>) and the
/// runtime <c>MigrationsAssembly</c> hint plus filtered namespace ensures EF only
/// sees the migrations matching the active provider.
/// </para>
///
/// <para>
/// Entity configurations live in <c>Configurations/</c>, one file per aggregate,
/// picked up via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
/// </para>
/// </summary>
public class PlanDbContext : DbContext
{
    public PlanDbContext(DbContextOptions options) : base(options) { }

    public DbSet<SavedPlan> Plans => Set<SavedPlan>();

    public DbSet<PlanShareToken> PlanShareTokens => Set<PlanShareToken>();

    public DbSet<FactoryAlert> FactoryAlerts => Set<FactoryAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDbContext).Assembly);

        // TickerQ (#115, ADR-0019). Apply the operational-store entity
        // configurations directly here rather than via TickerQ's
        // IModelCustomizer — the customizer relies on the host's DI being
        // active during model build, which the design-time DbContext factory
        // (PlanDbContextFactory.cs) bypasses. Direct ApplyConfiguration is
        // identical at runtime AND at `dotnet ef migrations add` time, so
        // the snapshot stays consistent.
        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TimeTickerEntity>(Constants.DefaultSchema));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<CronTickerEntity>(Constants.DefaultSchema));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<CronTickerEntity>(Constants.DefaultSchema));

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignOwnedOrdinals();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignOwnedOrdinals();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// The two owned collections on <see cref="SavedPlan"/> (Targets, Available)
    /// key on (PlanId, Ordinal) where Ordinal is a shadow property. EF leaves
    /// it NULL on insert across both SQLite and Postgres, and SQLite refuses
    /// the row (no auto-increment for composite keys). Populating it here —
    /// per-owner, in list order — keeps the writes deterministic across
    /// providers without forcing the domain to expose the ordinal.
    /// </summary>
    private void AssignOwnedOrdinals()
    {
        ChangeTracker.DetectChanges();

        // Walk every tracked entry once and assign ordinals to anything that
        // owns a shadow `Ordinal` property — this covers both Targets and
        // Available without naming them explicitly. Counters are keyed by
        // (owner-plan-id, entity-clr-type) so the two collections under one
        // plan number independently.
        var counters = new Dictionary<(Guid PlanId, Type Type), int>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;
            if (entry.Metadata.FindProperty("Ordinal") is null) continue;

            var planIdProp = entry.Property("PlanId");
            if (planIdProp.CurrentValue is not Guid planId) continue;

            var key = (planId, entry.Entity.GetType());
            counters.TryGetValue(key, out var next);
            var ordinalProperty = entry.Property("Ordinal");
            ordinalProperty.CurrentValue = next;
            ordinalProperty.IsTemporary = false;
            counters[key] = next + 1;
        }
    }
}
