using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

/// <summary>
/// Postgres-specific subclass of <see cref="PlanDbContext"/>.
///
/// <para>
/// Exists solely so EF migrations can be scoped per provider: the model snapshot
/// is keyed by context type, so two providers must each have their own derived
/// context type to keep their migrations and snapshots from clashing.
/// </para>
///
/// <para>The schema lives in <c>Migrations/Postgres/</c> (namespace
/// <c>Erp.Infrastructure.Persistence.Migrations.Postgres</c>).</para>
/// </summary>
public sealed class PostgresPlanDbContext : PlanDbContext
{
    public PostgresPlanDbContext(DbContextOptions<PostgresPlanDbContext> options)
        : base(options)
    {
    }
}
