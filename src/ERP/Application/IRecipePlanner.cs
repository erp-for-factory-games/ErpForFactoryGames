using ERP.Application.Queries.PlanProduction;
using ERP.Domain;

namespace ERP.Application;

/// <summary>
/// Port for planning production: take a set of <see cref="ProductionTarget"/>s
/// plus <see cref="ResourceAvailability"/> caps and return a
/// <see cref="ProductionPlan"/> that hits the targets. Multiple implementations
/// exist (recursive expansion today; LP-backed optimisation per #88) and are
/// selected via configuration.
/// </summary>
public interface IRecipePlanner
{
    ProductionPlan Plan(PlanProductionQuery query);
}
