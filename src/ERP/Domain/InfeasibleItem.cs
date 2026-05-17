namespace ERP.Domain;

/// <summary>
/// Diagnostic entry for an item the planner couldn't fully supply (#8).
/// Replaces the bare <see cref="ItemAmount"/> entries that
/// <see cref="ProductionPlan.MissingInputs"/> used to hold — same item +
/// quantity, plus enrichment so the UI / ADA can surface actionable info:
/// what recipes could have produced this item (alternates worth considering),
/// what recipes are currently consuming it (so the user can scale or remove
/// downstream demand), and a one-line natural-language reason for the
/// shortfall.
/// </summary>
public sealed record InfeasibleItem(
    ItemId Item,
    decimal QuantityShort,
    string Reason,
    IReadOnlyList<RecipeId> CouldBeProducedBy,
    IReadOnlyList<RecipeId> TopConsumers);
