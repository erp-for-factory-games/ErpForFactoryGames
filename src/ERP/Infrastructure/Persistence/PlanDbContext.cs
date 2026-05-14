using ERP.Domain;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
