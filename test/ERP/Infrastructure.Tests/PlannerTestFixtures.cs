using ERP.Application;
using ERP.Domain;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Shared recipe/building/item ids and an in-memory <see cref="ICatalogProvider"/>
/// stand-in. The same fixture set powers both the LP-specific tests and the
/// parity tests against <c>RecursiveRecipePlanner</c>.
/// </summary>
internal static class PlannerTestFixtures
{
    public static readonly BuildingId SmelterId = new("Build_SmelterMk1_C");
    public static readonly BuildingId ConstructorId = new("Build_ConstructorMk1_C");
    public static readonly BuildingId FoundryId = new("Build_FoundryMk1_C");
    public static readonly BuildingId RefineryId = new("Build_OilRefinery_C");

    public static readonly ItemId IronOre = new("Desc_OreIron_C");
    public static readonly ItemId IronIngot = new("Desc_IronIngot_C");
    public static readonly ItemId IronPlate = new("Desc_IronPlate_C");
    public static readonly ItemId Coal = new("Desc_Coal_C");
    public static readonly ItemId SteelIngot = new("Desc_SteelIngot_C");
    public static readonly ItemId CopperOre = new("Desc_OreCopper_C");
    public static readonly ItemId CopperIngot = new("Desc_CopperIngot_C");
    public static readonly ItemId HeavyOilResidue = new("Desc_HeavyOilResidue_C");
    public static readonly ItemId Plastic = new("Desc_Plastic_C");

    public static readonly Recipe IronIngotRecipe = new(
        new RecipeId("Recipe_IngotIron_C"),
        "Iron Ingot",
        SmelterId,
        Inputs: [new ItemAmount(IronOre, 1)],
        Outputs: [new ItemAmount(IronIngot, 1)],
        Duration: TimeSpan.FromSeconds(2));

    public static readonly Recipe IronPlateRecipe = new(
        new RecipeId("Recipe_IronPlate_C"),
        "Iron Plate",
        ConstructorId,
        Inputs: [new ItemAmount(IronIngot, 3)],
        Outputs: [new ItemAmount(IronPlate, 2)],
        Duration: TimeSpan.FromSeconds(6));

    /// <summary>
    /// In-memory catalogue stand-in. Planners only call
    /// <see cref="FindDefaultProducerOf"/>, <see cref="FindBuilding"/>, and
    /// iterate <see cref="Recipes"/>; everything else throws so unexpected
    /// usage is caught in tests.
    /// </summary>
    public sealed class FakeCatalog : ICatalogProvider
    {
        private readonly Dictionary<BuildingId, Building> _buildings;
        private readonly List<Recipe> _recipes;
        private readonly Dictionary<ItemId, Item> _items;

        public FakeCatalog(
            IEnumerable<Building> buildings,
            IEnumerable<Recipe> recipes,
            IEnumerable<Item>? items = null)
        {
            _buildings = buildings.ToDictionary(b => b.Id);
            _recipes = recipes.ToList();
            _items = (items ?? []).ToDictionary(i => i.Id);
        }

        public bool IsLoaded => true;
        public string? Source => "fake";
        public IReadOnlyList<Item> Items => _items.Values.ToList();
        public IReadOnlyList<Building> Buildings => _buildings.Values.ToList();
        public IReadOnlyList<Recipe> Recipes => _recipes;

        public Item? FindItem(ItemId id) => _items.TryGetValue(id, out var item) ? item : null;
        public Building? FindBuilding(BuildingId id) =>
            _buildings.TryGetValue(id, out var b) ? b : null;
        public Recipe? FindRecipe(RecipeId id) =>
            _recipes.FirstOrDefault(r => r.Id == id);

        public Recipe? FindDefaultProducerOf(ItemId item) =>
            _recipes.FirstOrDefault(r => r.Outputs.Any(o => o.Item == item));

        public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) =>
            _recipes.Where(r => r.Outputs.Any(o => o.Item == item)).ToList();

        public CatalogueStatus GetStatus() => throw new NotSupportedException();
        public CatalogueStatus LoadFromPath(string docsJsonPath) => throw new NotSupportedException();
    }
}
