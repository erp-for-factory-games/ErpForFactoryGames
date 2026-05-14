using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// SQLite-specific subclass of <see cref="PlanDbContext"/>.
///
/// <para>
/// Exists solely so EF migrations can be scoped per provider: the model snapshot
/// is keyed by context type, so two providers must each have their own derived
/// context type to keep their migrations and snapshots from clashing.
/// </para>
///
/// <para>The schema lives in <c>Migrations/Sqlite/</c> (namespace
/// <c>ERP.Infrastructure.Persistence.Migrations.Sqlite</c>).</para>
/// </summary>
public sealed class SqlitePlanDbContext : PlanDbContext
{
    public SqlitePlanDbContext(DbContextOptions<SqlitePlanDbContext> options)
        : base(options)
    {
    }
}
