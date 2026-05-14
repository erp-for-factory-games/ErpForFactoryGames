using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> and <c>dotnet ef database</c>
/// tooling. The runtime composition root wires up its own DbContext via DI and does
/// NOT go through this factory.
///
/// <para>
/// PROVIDER NOT YET CHOSEN (see issue #12). This factory deliberately throws until a
/// persistence provider is picked, so <c>dotnet ef</c> fails fast with a clear
/// message rather than silently using an unintended default.
/// </para>
///
/// <para>When a provider is picked, replace the body of <see cref="CreateDbContext"/> with e.g.:</para>
/// <code>
/// var connectionString = Environment.GetEnvironmentVariable("ERP_DB")
///     ?? "Data Source=erp.db"; // sqlite example
/// var options = new DbContextOptionsBuilder&lt;PlanDbContext&gt;()
///     .UseSqlite(connectionString)
///     .Options;
/// return new PlanDbContext(options);
/// </code>
/// </summary>
public sealed class PlanDbContextFactory : IDesignTimeDbContextFactory<PlanDbContext>
{
    // Env var the chosen provider's connection string should be read from at design time.
    // Keeping it stable now means migrations tooling won't need editing once the provider lands.
    public const string ConnectionStringEnvVar = "ERP_PERSISTENCE_CONNECTION";

    public PlanDbContext CreateDbContext(string[] args)
    {
        throw new InvalidOperationException(
            "No persistence provider has been chosen yet (see issue #12). " +
            $"Once a provider is selected, update {nameof(PlanDbContextFactory)} to call " +
            "the appropriate .UseXxx(...) extension and read the connection string from " +
            $"the '{ConnectionStringEnvVar}' environment variable.");
    }
}
