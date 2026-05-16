using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Satisfactory.Save;

namespace ERP.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddErpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogueOptions>(configuration.GetSection("Catalogue:Satisfactory"));
        services.Configure<FactoryStateOptions>(configuration.GetSection("FactoryState:Satisfactory"));
        services.AddSingleton<UserCatalogueConfig>();
        services.AddSingleton<ICatalogProvider, DocsCatalogProvider>();
        // Manual node overrides — user-local JSON loaded once at startup and
        // mutated through the /factory/node-override API. Same singleton is
        // shared with the SaveFileReader so re-parses pick up new entries.
        services.AddSingleton<ManualNodeOverrides>(_ => ManualNodeOverrides.LoadOrCreate(
            ManualNodeOverridesPath.Resolve(configuration["FactoryState:Satisfactory:OverridesPath"])));
        // Static flora dataset for the /factory/map "Flora" layer (issue #62).
        // Flora are not save-actors — they're foliage; vanilla positions are
        // world-fixed and we surface them straight from the bundled JSON.
        services.AddSingleton(_ => KnownFlora.LoadEmbedded());
        services.AddSingleton<IFactoryStateProvider, SatisfactorySaveNetFactoryStateProvider>();
        // Recipe planner port (#88). Recursive impl today; LP-backed adapter
        // will land alongside this one behind a `Planner:Engine` switch.
        services.AddSingleton<IRecipePlanner, RecursiveRecipePlanner>();
        return services;
    }
}
