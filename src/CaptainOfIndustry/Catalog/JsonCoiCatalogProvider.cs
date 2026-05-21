using System.Text.Json;
using ERP.Application;
using ERP.Domain;

namespace CaptainOfIndustry.Catalog;

/// <summary>
/// <see cref="ICatalogProvider"/> backed by the JSON catalogue produced by
/// <c>tools/CaptainOfIndustryExtractor</c>. The resolver
/// (<see cref="CoiCataloguePathResolver"/>) decides where the JSON lives; this
/// provider parses it at construction and exposes the result.
/// </summary>
/// <remarks>
/// Quantities in CoI's JSON are in the game's internal unit (50 = 1 belt slot
/// / 1 visual item). The mapper passes the raw integer through to
/// <see cref="ItemAmount.Quantity"/> for now — converting to "items per
/// minute" or similar is a planner UI concern, not a catalogue concern.
/// CoI sim ticks run at 40/s; <see cref="Recipe.Duration"/> is derived from
/// <c>durationTicks</c> on that basis.
/// </remarks>
public sealed class JsonCoiCatalogProvider : ICatalogProvider
{
    private const double TicksPerSecond = 40.0;

    private readonly CoiCatalogueOptions _options;
    private LoadedState _state;

    public JsonCoiCatalogProvider() : this(new CoiCatalogueOptions()) { }

    public JsonCoiCatalogProvider(CoiCatalogueOptions options)
    {
        _options = options;
        _state = LoadAtStartup();
    }

    public bool IsLoaded => _state.IsLoaded;
    public string? Source => _state.Source;
    public IReadOnlyList<Item> Items => _state.Items;
    public IReadOnlyList<Building> Buildings => _state.Buildings;
    public IReadOnlyList<Recipe> Recipes => _state.Recipes;

    public Item? FindItem(ItemId id) => _state.ItemsById.GetValueOrDefault(id);
    public Building? FindBuilding(BuildingId id) => _state.BuildingsById.GetValueOrDefault(id);
    public Recipe? FindRecipe(RecipeId id) => _state.RecipesById.GetValueOrDefault(id);

    public Recipe? FindDefaultProducerOf(ItemId item) =>
        _state.ProducersByItem.TryGetValue(item, out var producers) ? producers.FirstOrDefault() : null;

    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) =>
        _state.ProducersByItem.TryGetValue(item, out var producers) ? producers : Array.Empty<Recipe>();

    public CatalogueStatus GetStatus() => BuildStatus(_state);

    public CatalogueStatus LoadFromPath(string cataloguePath)
    {
        var newState = LoadedState.FromFile(cataloguePath);
        Interlocked.Exchange(ref _state, newState);
        return BuildStatus(newState);
    }

    private LoadedState LoadAtStartup()
    {
        var existing = CoiCataloguePathResolver.ResolveExisting(_options);
        if (existing is null)
        {
            var expected = CoiCataloguePathResolver.Resolve(_options);
            return LoadedState.NotConfigured(
                $"No catalogue JSON at '{expected}'. Run the extractor (tools/CaptainOfIndustryExtractor) to generate one.");
        }
        try
        {
            return LoadedState.FromFile(existing);
        }
        catch (Exception ex)
        {
            return LoadedState.NotConfigured($"Failed to parse catalogue JSON at '{existing}': {ex.Message}");
        }
    }

    private static CatalogueStatus BuildStatus(LoadedState s) => new(
        IsLoaded: s.IsLoaded,
        Source: s.Source,
        ItemCount: s.Items.Count,
        BuildingCount: s.Buildings.Count,
        RecipeCount: s.Recipes.Count,
        AlternateRecipeCount: 0,
        Warnings: s.Warnings);

    private sealed record LoadedState(
        bool IsLoaded,
        string? Source,
        IReadOnlyList<Item> Items,
        IReadOnlyList<Building> Buildings,
        IReadOnlyList<Recipe> Recipes,
        IReadOnlyDictionary<ItemId, Item> ItemsById,
        IReadOnlyDictionary<BuildingId, Building> BuildingsById,
        IReadOnlyDictionary<RecipeId, Recipe> RecipesById,
        IReadOnlyDictionary<ItemId, IReadOnlyList<Recipe>> ProducersByItem,
        IReadOnlyList<string> Warnings)
    {
        public static LoadedState NotConfigured(string warning) => new(
            IsLoaded: false,
            Source: null,
            Items: Array.Empty<Item>(),
            Buildings: Array.Empty<Building>(),
            Recipes: Array.Empty<Recipe>(),
            ItemsById: new Dictionary<ItemId, Item>(),
            BuildingsById: new Dictionary<BuildingId, Building>(),
            RecipesById: new Dictionary<RecipeId, Recipe>(),
            ProducersByItem: new Dictionary<ItemId, IReadOnlyList<Recipe>>(),
            Warnings: new[] { warning });

        public static LoadedState FromFile(string path)
        {
            using var fs = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<CoiCatalogueJson>(fs)
                ?? throw new InvalidDataException($"Catalogue JSON at '{path}' deserialised to null.");

            var items = dto.Items.Select(p => new Item(new ItemId(p.Id), p.Name)).ToList();
            var buildings = dto.Buildings
                .Select(b => new Building(new BuildingId(b.Id), b.Name, b.ElectricityKw / 1000.0))
                .ToList();

            var warnings = new List<string>(dto.Warnings);
            var recipes = new List<Recipe>(dto.Recipes.Count);
            foreach (var r in dto.Recipes)
            {
                if (string.IsNullOrEmpty(r.Building))
                {
                    // Recipes without an owning machine can't be planned for —
                    // they're player-crafted or special-cased in-game.
                    continue;
                }
                recipes.Add(new Recipe(
                    Id: new RecipeId(r.Id),
                    Name: r.Name,
                    Building: new BuildingId(r.Building),
                    Inputs: r.Inputs.Select(i => new ItemAmount(new ItemId(i.ProductId), i.Quantity)).ToList(),
                    Outputs: r.Outputs.Select(o => new ItemAmount(new ItemId(o.ProductId), o.Quantity)).ToList(),
                    Duration: TimeSpan.FromSeconds(r.DurationTicks / TicksPerSecond)));
            }
            var droppedRecipes = dto.Recipes.Count - recipes.Count;
            if (droppedRecipes > 0)
                warnings.Add($"Dropped {droppedRecipes} recipes with no owning building (player-crafted or special-cased).");

            var itemsById = items.ToDictionary(i => i.Id);
            var buildingsById = buildings.ToDictionary(b => b.Id);
            var recipesById = recipes.ToDictionary(r => r.Id);
            var producersByItem = recipes
                .SelectMany(r => r.Outputs.Select(o => (Item: o.Item, Recipe: r)))
                .GroupBy(t => t.Item)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<Recipe>)g.Select(t => t.Recipe).ToList());

            return new LoadedState(
                IsLoaded: true,
                Source: path,
                Items: items,
                Buildings: buildings,
                Recipes: recipes,
                ItemsById: itemsById,
                BuildingsById: buildingsById,
                RecipesById: recipesById,
                ProducersByItem: producersByItem,
                Warnings: warnings);
        }
    }
}
