namespace ERP.Domain;

/// <summary>
/// The output of a planning run. <see cref="Steps"/> covers the recipes /
/// building counts; <see cref="RawInputsConsumed"/> shows what was drawn from
/// the user-declared <see cref="ResourceAvailability"/>; <see cref="MissingInputs"/>
/// flags any unsatisfied demand.
/// <para>
/// <see cref="ExtractorAllocations"/> reports per-node miner placement (#92).
/// <see cref="Warnings"/> is a list of advisory, plan-wide caveats — strings
/// that the UI / API surface as informational text alongside the plan. Today
/// the only producer is the variable-power-buildings notice (#91 v1): when a
/// plan contains miners / extractors, the <c>PowerMw</c> figures are base
/// draw, not the higher peak draw those buildings hit in-game. The field
/// defaults to empty so older call-sites and serialised JSON keep working
/// unchanged.
/// </para>
/// </summary>
public sealed record ProductionPlan(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available,
    IReadOnlyList<ProductionStep> Steps,
    IReadOnlyList<ItemAmount> RawInputsConsumed,
    IReadOnlyList<InfeasibleItem> MissingInputs,
    IReadOnlyList<ExtractorAllocation>? ExtractorAllocations = null,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> WarningsOrEmpty => Warnings ?? Array.Empty<string>();

    public bool IsFeasible => MissingInputs.Count == 0;

    /// <summary>
    /// Non-null shorthand for the optional <see cref="ExtractorAllocations"/>.
    /// Most planner outputs (no node binding) leave it as the empty list.
    /// </summary>
    public IReadOnlyList<ExtractorAllocation> Allocations =>
        ExtractorAllocations ?? Array.Empty<ExtractorAllocation>();
}
