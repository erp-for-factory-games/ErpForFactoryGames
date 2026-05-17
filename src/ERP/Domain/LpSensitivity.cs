namespace ERP.Domain;

/// <summary>
/// LP sensitivity analysis attached to a <see cref="ProductionPlan"/> when
/// the planner is the OR-Tools / GLOP engine (#129). The recursive engine
/// can't produce these numbers; <see cref="ProductionPlan.Sensitivity"/>
/// is <c>null</c> on plans it produced.
///
/// <para>
/// Two surfaces here, both straight off the LP solve:
/// </para>
/// <list type="bullet">
///   <item><see cref="SupplyConstraints"/> — per-item dual values + slack.</item>
///   <item><see cref="ProductionRecipes"/> — per-recipe reduced costs for the
///     recipes the LP did NOT activate (helps explain "why didn't the LP
///     pick this alt?").</item>
/// </list>
/// </summary>
public sealed record LpSensitivity(
    IReadOnlyList<ItemShadowPrice> SupplyConstraints,
    IReadOnlyList<RecipeReducedCost> ProductionRecipes);

/// <summary>
/// Per-item dual analysis on the LP's <c>supply ≥ demand</c> constraint.
/// </summary>
/// <param name="Item">The item the constraint is for.</param>
/// <param name="ShadowPrice">
/// Marginal objective improvement per additional unit of supply for this
/// item (units: MW per items/min, since the objective is power). Zero on
/// non-binding constraints; positive on binding ones — the higher the
/// number, the more leveraged the bottleneck.
/// </param>
/// <param name="Slack">
/// How much the constraint's LHS exceeds the RHS at the optimum (units:
/// items/min). Zero on binding constraints; positive on non-binding ones.
/// Slack > 0 ⇒ headroom you could spend on additional downstream demand.
/// </param>
public sealed record ItemShadowPrice(
    ItemId Item,
    decimal ShadowPrice,
    decimal Slack);

/// <summary>
/// Per-recipe reduced cost: how much the objective coefficient (power)
/// would have to *drop* for this recipe to be worth activating at the
/// optimum. Always ≥ 0 in this LP (minimisation). Recipes that are
/// already active have reduced cost ≈ 0. Surfaces the "this alt is just
/// barely not worth it" recipes most clearly.
/// </summary>
public sealed record RecipeReducedCost(
    RecipeId Recipe,
    decimal ReducedCost);
