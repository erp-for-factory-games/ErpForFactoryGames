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
    private readonly CoiCatalogueOptions _options;

    public EmptyCoiCatalogProvider() : this(new CoiCatalogueOptions()) { }

    public EmptyCoiCatalogProvider(CoiCatalogueOptions options)
    {
        _options = options;
    }

    public bool IsLoaded => false;
    public string? Source => CoiCataloguePathResolver.ResolveExisting(_options);

    public IReadOnlyList<Item> Items { get; } = Array.Empty<Item>();
    public IReadOnlyList<Building> Buildings { get; } = Array.Empty<Building>();
    public IReadOnlyList<Recipe> Recipes { get; } = Array.Empty<Recipe>();

    public Item? FindItem(ItemId id) => null;
    public Building? FindBuilding(BuildingId id) => null;
    public Recipe? FindRecipe(RecipeId id) => null;

    public Recipe? FindDefaultProducerOf(ItemId item) => null;
    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => Array.Empty<Recipe>();

    public CatalogueStatus GetStatus()
    {
        var existing = CoiCataloguePathResolver.ResolveExisting(_options);
        var expected = CoiCataloguePathResolver.Resolve(_options);
        var warnings = new List<string>
        {
            "Captain of Industry catalogue ingestion not yet implemented. Tracked under milestone #16.",
        };
        if (existing is null)
            warnings.Add($"No catalogue JSON at '{expected}'. Run the extractor (tools/CaptainOfIndustryExtractor, #177) to generate one.");

        return new CatalogueStatus(
            IsLoaded: false,
            Source: existing,
            ItemCount: 0,
            BuildingCount: 0,
            RecipeCount: 0,
            AlternateRecipeCount: 0,
            Warnings: warnings);
    }

    public CatalogueStatus LoadFromPath(string docsJsonPath)
    {
        _options.CataloguePath = docsJsonPath;
        return GetStatus();
    }
}
