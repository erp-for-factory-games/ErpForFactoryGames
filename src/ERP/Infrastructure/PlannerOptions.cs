namespace ERP.Infrastructure;

/// <summary>
/// Bound from configuration section <c>Planner</c>. Selects which
/// <see cref="Erp.Application.Common.IRecipePlanner"/> implementation gets wired in
/// <c>AddErpInfrastructure</c>.
/// </summary>
public sealed class PlannerOptions
{
    public PlannerEngine Engine { get; set; } = PlannerEngine.Recursive;
}

public enum PlannerEngine
{
    /// <summary>Recursive recipe expansion (default; pre-#88 behaviour).</summary>
    Recursive,

    /// <summary>Linear-programming solver via Google OR-Tools (#88).</summary>
    Lp,
}
