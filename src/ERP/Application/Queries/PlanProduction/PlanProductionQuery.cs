using ERP.Application;
using ERP.Domain;

namespace ERP.Application.Queries.PlanProduction;

public sealed record PlanProductionQuery(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available);

/// <summary>
/// Wolverine handler entry point. The planning logic itself lives behind
/// <see cref="IRecipePlanner"/> — this shim just delegates so the engine can
/// be swapped (recursive today; LP-backed per #88) via DI registration in
/// <c>AddErpInfrastructure</c>.
/// </summary>
public static class PlanProductionHandler
{
    public static ProductionPlan Handle(PlanProductionQuery query, IRecipePlanner planner) =>
        planner.Plan(query);
}
