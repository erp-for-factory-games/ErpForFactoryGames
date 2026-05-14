using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Design-time factories used by <c>dotnet ef migrations</c> / <c>dotnet ef database</c>
/// tooling. The runtime composition root wires up its own context via DI and does
/// NOT go through these factories.
///
/// <para>
/// One factory per derived context. EF tooling picks the factory that matches the
/// <c>--context</c> argument:
/// </para>
///
/// <code>
/// # SQLite migrations (default DB file)
/// dotnet ef migrations add InitialCreate `
///     --project ../ERP/Infrastructure/Persistence `
///     --startup-project . `
///     --output-dir Migrations/Sqlite `
///     --context SqlitePlanDbContext
///
/// # Postgres migrations (set ERP_PERSISTENCE_CONNECTION first)
/// $env:ERP_PERSISTENCE_CONNECTION = 'Host=localhost;Database=plans;Username=postgres;Password=placeholder'
/// dotnet ef migrations add InitialCreate `
///     --project ../ERP/Infrastructure/Persistence `
///     --startup-project . `
///     --output-dir Migrations/Postgres `
///     --context PostgresPlanDbContext
/// </code>
/// </summary>
internal static class DesignTimeEnv
{
    public const string ConnectionStringEnvVar = "ERP_PERSISTENCE_CONNECTION";

    public static string? GetConnection() => Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
}

public sealed class SqlitePlanDbContextFactory : IDesignTimeDbContextFactory<SqlitePlanDbContext>
{
    public SqlitePlanDbContext CreateDbContext(string[] args)
    {
        var conn = DesignTimeEnv.GetConnection();
        if (string.IsNullOrWhiteSpace(conn))
            conn = PersistenceServiceCollectionExtensions.DefaultSqliteConnectionString;

        var options = new DbContextOptionsBuilder<SqlitePlanDbContext>()
            .UseSqlite(conn, b => b.MigrationsHistoryTable(
                PersistenceServiceCollectionExtensions.MigrationsHistoryTable))
            .Options;
        return new SqlitePlanDbContext(options);
    }
}

public sealed class PostgresPlanDbContextFactory : IDesignTimeDbContextFactory<PostgresPlanDbContext>
{
    public PostgresPlanDbContext CreateDbContext(string[] args)
    {
        var conn = DesignTimeEnv.GetConnection();
        if (string.IsNullOrWhiteSpace(conn))
        {
            throw new InvalidOperationException(
                $"Postgres design-time context requires a connection string in the " +
                $"'{DesignTimeEnv.ConnectionStringEnvVar}' environment variable. " +
                "A placeholder value is fine — the connection isn't actually opened during 'migrations add'.");
        }

        var options = new DbContextOptionsBuilder<PostgresPlanDbContext>()
            .UseNpgsql(conn, b => b.MigrationsHistoryTable(
                PersistenceServiceCollectionExtensions.MigrationsHistoryTable))
            .Options;
        return new PostgresPlanDbContext(options);
    }
}
