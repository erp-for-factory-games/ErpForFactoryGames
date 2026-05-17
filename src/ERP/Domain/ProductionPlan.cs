namespace ERP.Domain;

public sealed record ProductionPlan(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available,
    IReadOnlyList<ProductionStep> Steps,
    IReadOnlyList<ItemAmount> RawInputsConsumed,
    IReadOnlyList<InfeasibleItem> MissingInputs)
{
    public bool IsFeasible => MissingInputs.Count == 0;
}
