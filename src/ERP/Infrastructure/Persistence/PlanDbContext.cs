using ERP.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// EF Core context for persisting <see cref="SavedPlan"/> aggregates.
///
/// <para>
/// Provider-agnostic: this class never calls <c>UseSqlite</c> / <c>UseNpgsql</c> /
/// etc. — provider selection happens once in <see cref="PlanDbContextOptionsBuilder"/>
/// (or the AppHost composition root) so a single switch flips the storage backend.
/// </para>
///
/// <para>
/// Entity configurations live in <c>Configurations/</c>, one file per aggregate, picked
/// up via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
/// </para>
/// </summary>
public sealed class PlanDbContext : DbContext
{
    public PlanDbContext(DbContextOptions<PlanDbContext> options) : base(options) { }

    public DbSet<SavedPlan> Plans => Set<SavedPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
