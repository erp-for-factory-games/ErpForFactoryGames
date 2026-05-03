using ERP.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddErpInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IRecipeCatalog, SatisfactoryRecipeCatalog>();
        return services;
    }
}
