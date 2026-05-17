using ERP.Domain;

namespace ERP.Application.Queries.PlanProduction;

/// <summary>
/// Detects plan-wide power-variance caveats and produces user-facing warnings
/// (issue #91, v1). Miners and fluid extractors have variable in-game draw —
/// the catalogue only exposes <c>BasePowerMw</c>, so <see cref="ProductionStep.PowerMw"/>
/// and <see cref="ProductionPlan"/>'s aggregate are base × count, which
/// systematically under-reports peak load. When a plan touches any of these
/// buildings, we attach a single advisory string so the user knows to over-size
/// their generator budget rather than treat the figure as ground truth.
/// <para>
/// Detection is intentionally a hard-coded building-ID set, not a name regex —
/// names vary by locale / catalogue update, but the BPID is stable across
/// Satisfactory versions. Add to the set if new variable-draw buildings ship.
/// </para>
/// <para>
/// Out of scope here: actually <em>modelling</em> peak power, generator-aware
/// planning, fuse limits — those are the larger #91 follow-up.
/// </para>
/// </summary>
public static class PowerVarianceWarning
{
    /// <summary>
    /// Buildings whose in-game power draw can exceed the documented base
    /// figure under normal operation — miners follow a dig-cycle that spikes
    /// to ~150% of base, fluid extractors have similar pump-cycle variance.
    /// Anything in this set causes <see cref="Build"/> to emit the variance
    /// advisory.
    /// </summary>
    public static readonly IReadOnlySet<BuildingId> VariablePowerBuildings = new HashSet<BuildingId>
    {
        new("Build_MinerMk1_C"),
        new("Build_MinerMk2_C"),
        new("Build_MinerMk3_C"),
        new("Build_OilPump_C"),
        new("Build_FrackingExtractor_C"),
        new("Build_WaterPump_C"),
    };

    /// <summary>
    /// The single advisory string emitted when a plan touches any
    /// <see cref="VariablePowerBuildings"/> entry. Kept as a constant so tests
    /// can assert the wording and so it appears verbatim wherever the plan is
    /// rendered.
    /// </summary>
    public const string MinerVarianceAdvisory =
        "Power draw shown is base × count; miners and fluid extractors draw up to ~150% during dig/pump cycles. " +
        "Over-size your generator budget by ~20% to be safe.";

    /// <summary>
    /// Inspect the steps and return any warnings that apply. Returns an empty
    /// list (never null) when nothing to flag, so call-sites can hand the
    /// result straight to <see cref="ProductionPlan.Warnings"/>.
    /// </summary>
    public static IReadOnlyList<string> Build(IEnumerable<ProductionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var step in steps)
        {
            if (VariablePowerBuildings.Contains(step.Recipe.Building))
                return new[] { MinerVarianceAdvisory };
        }

        return Array.Empty<string>();
    }
}
