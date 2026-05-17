using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Satisfactory.Save;

namespace ERP.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddErpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogueOptions>(configuration.GetSection("Catalogue:Satisfactory"));
        services.Configure<FactoryStateOptions>(configuration.GetSection("FactoryState:Satisfactory"));
        services.Configure<AutoIngestOptions>(configuration.GetSection("FactoryState:Satisfactory:AutoIngest"));
        services.Configure<PlannerOptions>(configuration.GetSection("Planner"));
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
        // Recipe planner port (#88). Engine is selected by Planner:Engine
        // config — defaults to Recursive. The LP engine uses OR-Tools GLOP
        // (Google.OrTools native deps verified for macOS dev + self-hosted
        // Linux CI). Both impls share the IRecipePlanner contract.
        services.AddSingleton<IRecipePlanner>(sp =>
        {
            var engine = sp.GetRequiredService<IOptions<PlannerOptions>>().Value.Engine;
            var catalog = sp.GetRequiredService<ICatalogProvider>();
            return engine switch
            {
                PlannerEngine.Lp => new OrToolsRecipePlanner(
                    catalog,
                    sp.GetService<ILogger<OrToolsRecipePlanner>>()),
                _ => new RecursiveRecipePlanner(catalog),
            };
        });
        // Post-ingest bottleneck analysis (#116, phase B). Scoped because it
        // depends on the scoped IFactoryAlertRepository (EF DbContext).
        services.AddScoped<FactoryAlertAnalysisService>();
        // TickerQ AutoIngestJob (#115). Singleton because TickerQ resolves
        // the function host directly; the job's per-tick scope is opened by
        // the framework around InvokeAsync calls inside RunAsync.
        services.AddSingleton<AutoIngestJob>();
        return services;
    }
}
