using Erp.Application.Common;
using Erp.Domain.Common;

namespace ERP.Infrastructure;

/// <summary>
/// Immutable in-memory snapshot of a parsed catalogue. Used by
/// <see cref="DocsCatalogProvider"/> (server-local file) and
/// <see cref="PlayerScopedCatalogProvider"/> (per-player upload) so
/// they share one implementation of the read/find surface.
/// </summary>
public sealed class InMemoryCatalogue : ICatalogProvider
{
    public bool IsLoaded { get; }
    public string? Source { get; }
    public IReadOnlyList<Item> Items { get; }
    public IReadOnlyList<Building> Buildings { get; }
    public IReadOnlyList<Recipe> Recipes { get; }
    public IReadOnlyList<string> Warnings { get; }

    private readonly IReadOnlyDictionary<ItemId, Item> _itemsById;
    private readonly IReadOnlyDictionary<BuildingId, Building> _buildingsById;
    private readonly IReadOnlyDictionary<RecipeId, Recipe> _recipesById;
    private readonly IReadOnlyDictionary<ItemId, IReadOnlyList<Recipe>> _producersByItem;

    private InMemoryCatalogue(
        bool isLoaded,
        string? source,
        IReadOnlyList<Item> items,
        IReadOnlyList<Building> buildings,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<string> warnings)
    {
        IsLoaded = isLoaded;
        Source = source;
        Items = items;
        Buildings = buildings;
        Recipes = recipes;
        Warnings = warnings;

        _itemsById = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        _buildingsById = buildings.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
        _recipesById = recipes.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        _producersByItem = recipes
            .SelectMany(r => r.Outputs.Select(o => (Output: o.Item, Recipe: r)))
            .GroupBy(x => x.Output)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Recipe>)g.Select(x => x.Recipe).ToList());
    }

    public static InMemoryCatalogue Empty(string? source = null) =>
        new(isLoaded: false, source: source ?? "(empty)", [], [], [], []);

    public static InMemoryCatalogue Loaded(
        string source,
        IReadOnlyList<Item> items,
        IReadOnlyList<Building> buildings,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<string> warnings) =>
        new(isLoaded: true, source: source, items, buildings, recipes, warnings);

    public Item? FindItem(ItemId id) => _itemsById.GetValueOrDefault(id);
    public Building? FindBuilding(BuildingId id) => _buildingsById.GetValueOrDefault(id);
    public Recipe? FindRecipe(RecipeId id) => _recipesById.GetValueOrDefault(id);

    public Recipe? FindDefaultProducerOf(ItemId item)
    {
        if (!_producersByItem.TryGetValue(item, out var producers)) return null;

        var noFeedback = producers
            .Where(r => r.Inputs.All(i => i.Item != item))
            .ToList();

        return noFeedback.FirstOrDefault(r => !r.IsAlternate)
            ?? noFeedback.FirstOrDefault()
            ?? producers.FirstOrDefault(r => !r.IsAlternate)
            ?? producers.FirstOrDefault();
    }

    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) =>
        _producersByItem.TryGetValue(item, out var producers) ? producers : [];

    public CatalogueStatus GetStatus() => new(
        IsLoaded: IsLoaded,
        Source: Source,
        ItemCount: Items.Count,
        BuildingCount: Buildings.Count,
        RecipeCount: Recipes.Count,
        AlternateRecipeCount: Recipes.Count(r => r.IsAlternate),
        Warnings: Warnings);

    /// <summary>
    /// Throws — an immutable snapshot can't be reloaded in place. Callers
    /// holding an <see cref="ICatalogProvider"/> reference that may be an
    /// <see cref="InMemoryCatalogue"/> shouldn't be calling LoadFromPath
    /// directly; the settings-page reload path goes through
    /// <see cref="DocsCatalogProvider"/>.
    /// </summary>
    public CatalogueStatus LoadFromPath(string docsJsonPath) =>
        throw new NotSupportedException(
            "InMemoryCatalogue is immutable. Reload via the owning provider " +
            "(DocsCatalogProvider for server-local files, agent upload for player catalogues).");
}
