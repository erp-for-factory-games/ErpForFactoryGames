namespace Erp.Application.Common;

public sealed record CatalogueStatus(
    bool IsLoaded,
    string? Source,
    int ItemCount,
    int BuildingCount,
    int RecipeCount,
    int AlternateRecipeCount,
    IReadOnlyList<string> Warnings);
