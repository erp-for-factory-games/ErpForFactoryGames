namespace ERP.Domain;

public sealed record Recipe(
    RecipeId Id,
    string Name,
    BuildingId Building,
    IReadOnlyList<ItemAmount> Inputs,
    IReadOnlyList<ItemAmount> Outputs,
    TimeSpan Duration,
    bool IsAlternate = false);
