using ERP.Application;
using ERP.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Composition-root wiring for the EF Core persistence layer (ADR-0018).
///
/// <para>
/// Dual provider: <c>sqlite</c> (default, single-user / OSS / dev) or <c>postgres</c>
/// (multi-user / hosted). Selection is driven by configuration:
/// </para>
///
/// <code>
/// {
///   "Persistence": { "Provider": "sqlite" | "postgres" },
///   "ConnectionStrings": { "Plans": "..." }
/// }
/// </code>
///
/// <para>
/// Migrations live in two provider-specific folders inside this assembly:
/// </para>
/// <list type="bullet">
///   <item><c>Migrations/Sqlite</c> (default namespace <c>ERP.Infrastructure.Persistence.Migrations.Sqlite</c>)</item>
///   <item><c>Migrations/Postgres</c> (default namespace <c>ERP.Infrastructure.Persistence.Migrations.Postgres</c>)</item>
/// </list>
/// <para>
/// At runtime, the chosen provider scans only its own migrations folder via
/// <c>MigrationsAssembly</c> + <c>MigrationsHistoryTable</c> overrides; EF Core
/// otherwise discovers all <c>Migration</c>-derived types in the assembly and
/// chokes when both provider's snapshots coexist.
/// </para>
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public const string ProviderConfigKey = "Persistence:Provider";
    public const string ConnectionStringName = "Plans";
    public const string SqliteProvider = "sqlite";
    public const string PostgresProvider = "postgres";
    public const string DefaultSqliteConnectionString = "Data Source=plans.db";

    /// <summary>
    /// Migrations history table name shared by both providers — the per-provider
    /// migration types are kept apart by namespace, so a single history table is
    /// fine (and removes ambiguity if a connection ever gets aimed at the wrong DB).
    /// </summary>
    internal const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Register <see cref="PlanDbContext"/> and the <see cref="IPlanRepository"/>
    /// implementation, applying the provider chosen via configuration.
    /// </summary>
    public static IServiceCollection AddErpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = (configuration[ProviderConfigKey] ?? SqliteProvider).Trim().ToLowerInvariant();
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        switch (provider)
        {
            case SqliteProvider:
                var sqliteConn = string.IsNullOrWhiteSpace(connectionString)
                    ? DefaultSqliteConnectionString
                    : connectionString;
                services.AddDbContext<SqlitePlanDbContext>(options => options.UseSqlite(sqliteConn,
                    b => b.MigrationsHistoryTable(MigrationsHistoryTable)));
                services.AddScoped<PlanDbContext>(sp => sp.GetRequiredService<SqlitePlanDbContext>());
                break;

            case PostgresProvider:
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "Persistence:Provider is 'postgres' but ConnectionStrings:Plans is not set. " +
                        "Supply a Npgsql connection string (e.g. 'Host=localhost;Database=plans;Username=...;Password=...').");
                }
                services.AddDbContext<PostgresPlanDbContext>(options => options.UseNpgsql(connectionString,
                    b => b.MigrationsHistoryTable(MigrationsHistoryTable)));
                services.AddScoped<PlanDbContext>(sp => sp.GetRequiredService<PostgresPlanDbContext>());
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Persistence:Provider value '{provider}'. " +
                    $"Expected '{SqliteProvider}' or '{PostgresProvider}'.");
        }

        services.AddScoped<IPlanRepository, PlanRepository>();
        return services;
    }
}
