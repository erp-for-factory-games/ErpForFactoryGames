namespace ERP.Domain;

public sealed record ProductionPlan(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available,
    IReadOnlyList<ProductionStep> Steps,
    IReadOnlyList<ItemAmount> RawInputsConsumed,
    IReadOnlyList<ItemAmount> MissingInputs)
{
    public bool IsFeasible => MissingInputs.Count == 0;
}
