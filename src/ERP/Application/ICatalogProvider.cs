using ERP.Domain;

namespace ERP.Application;

public interface ICatalogProvider
{
    bool IsLoaded { get; }
    string? Source { get; }

    IReadOnlyList<Item> Items { get; }
    IReadOnlyList<Building> Buildings { get; }
    IReadOnlyList<Recipe> Recipes { get; }

    Item? FindItem(ItemId id);
    Building? FindBuilding(BuildingId id);
    Recipe? FindRecipe(RecipeId id);

    Recipe? FindDefaultProducerOf(ItemId item);
    IReadOnlyList<Recipe> FindAllProducersOf(ItemId item);

    CatalogueStatus GetStatus();

    /// <summary>
    /// Loads the catalogue from the given Docs.json path, replacing the current state.
    /// Returns the resulting status (with warnings on partial parse) or throws on hard failure.
    /// </summary>
    CatalogueStatus LoadFromPath(string docsJsonPath);
}
