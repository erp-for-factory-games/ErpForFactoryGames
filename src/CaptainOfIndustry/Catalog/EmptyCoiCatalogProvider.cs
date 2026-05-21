using ERP.Application;
using ERP.Domain;

namespace CaptainOfIndustry.Catalog;

/// <summary>
/// Placeholder Captain of Industry catalogue adapter. Returns an empty catalogue
/// until the extractor pipeline lands (see issue #177, ADR-0022). Exists so the
/// CoI module can be wired into a host without a real data source.
/// </summary>
public sealed class EmptyCoiCatalogProvider : ICatalogProvider
{
    public bool IsLoaded => false;
    public string? Source => null;

    public IReadOnlyList<Item> Items { get; } = Array.Empty<Item>();
    public IReadOnlyList<Building> Buildings { get; } = Array.Empty<Building>();
    public IReadOnlyList<Recipe> Recipes { get; } = Array.Empty<Recipe>();

    public Item? FindItem(ItemId id) => null;
    public Building? FindBuilding(BuildingId id) => null;
    public Recipe? FindRecipe(RecipeId id) => null;

    public Recipe? FindDefaultProducerOf(ItemId item) => null;
    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => Array.Empty<Recipe>();

    public CatalogueStatus GetStatus() => new(
        IsLoaded: false,
        Source: null,
        ItemCount: 0,
        BuildingCount: 0,
        RecipeCount: 0,
        AlternateRecipeCount: 0,
        Warnings: new[] { "Captain of Industry catalogue not yet implemented. Tracked under milestone #16." });

    public CatalogueStatus LoadFromPath(string docsJsonPath) => GetStatus();
}
