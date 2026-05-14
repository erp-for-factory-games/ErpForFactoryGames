using ERP.Application;
using ERP.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Composition-root wiring for the EF Core persistence layer.
///
/// <para>
/// PROVIDER NOT YET CHOSEN (see issue #12). The current implementation registers the
/// <see cref="PlanDbContext"/> with no provider — callers MUST supply a configure
/// delegate that calls <c>UseSqlite</c> / <c>UseNpgsql</c> / etc. The host project
/// (<c>ApiService</c>) is responsible for that one-line decision so it stays out of
/// the infrastructure project.
/// </para>
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="PlanDbContext"/> and the <see cref="IPlanRepository"/>
    /// implementation. The <paramref name="configureDbContext"/> delegate is where
    /// the chosen provider gets wired in (e.g. <c>opts.UseSqlite(...)</c>).
    /// </summary>
    /// <remarks>
    /// While no provider is chosen, callers may pass a no-op delegate; the host can
    /// then opt-in to <c>UseInMemoryDatabase</c> for local dev once that package is
    /// referenced, OR (preferred) the whole persistence registration can be skipped
    /// until a provider lands. Either way, <c>SaveChangesAsync</c> on an
    /// un-configured context will throw, which is intentional — better to fail fast
    /// than silently lose user plans.
    /// </remarks>
    public static IServiceCollection AddErpPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(configureDbContext);

        services.AddDbContext<PlanDbContext>(configureDbContext);
        services.AddScoped<IPlanRepository, PlanRepository>();
        return services;
    }
}
