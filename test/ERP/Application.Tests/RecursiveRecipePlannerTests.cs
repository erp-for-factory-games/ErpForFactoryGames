using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using ERP.Domain;

namespace ERP.Application.Tests;

public class RecursiveRecipePlannerTests
{
    private static readonly BuildingId SmelterId = new("Build_SmelterMk1_C");
    private static readonly BuildingId ConstructorId = new("Build_ConstructorMk1_C");

    private static readonly ItemId IronOre = new("Desc_OreIron_C");
    private static readonly ItemId IronIngot = new("Desc_IronIngot_C");
    private static readonly ItemId IronPlate = new("Desc_IronPlate_C");

    // Update 10-ish values: smelter 4 MW, constructor 4 MW. Iron ingot recipe
    // produces 1 ingot per 2 s = 30/min; iron plate produces 2 plates per 6 s = 20/min.
    private static readonly Recipe IronIngotRecipe = new(
        new RecipeId("Recipe_IngotIron_C"),
        "Iron Ingot",
        SmelterId,
        Inputs: [new ItemAmount(IronOre, 1)],
        Outputs: [new ItemAmount(IronIngot, 1)],
        Duration: TimeSpan.FromSeconds(2));

    private static readonly Recipe IronPlateRecipe = new(
        new RecipeId("Recipe_IronPlate_C"),
        "Iron Plate",
        ConstructorId,
        Inputs: [new ItemAmount(IronIngot, 3)],
        Outputs: [new ItemAmount(IronPlate, 2)],
        Duration: TimeSpan.FromSeconds(6));

    [Fact]
    public void Computes_Power_For_Single_Step_Plan()
    {
        // 30 ingots/min → exactly one smelter (1.0 building × 4 MW = 4 MW).
        var catalog = new FakeCatalog(
            buildings: [
                new Building(SmelterId, "Smelter", BasePowerMw: 4),
            ],
            recipes: [IronIngotRecipe]);

        var planner = new RecursiveRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 30)]));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(1m, step.BuildingCount);
        Assert.Equal(4m, step.PowerMw);
    }

    [Fact]
    public void Computes_Power_Across_Multiple_Steps_With_Scaling()
    {
        // 60 plates/min → 3 constructors (60/20 = 3.0) at 4 MW each = 12 MW.
        // Needs 90 ingots/min → 3 smelters at 4 MW each = 12 MW.
        // Total = 24 MW.
        var catalog = new FakeCatalog(
            buildings: [
                new Building(SmelterId, "Smelter", BasePowerMw: 4),
                new Building(ConstructorId, "Constructor", BasePowerMw: 4),
            ],
            recipes: [IronIngotRecipe, IronPlateRecipe]);

        var planner = new RecursiveRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronPlate, 60)],
            Available: [new ResourceAvailability(IronOre, 999)]));

        Assert.Equal(2, plan.Steps.Count);
        var plateStep = plan.Steps.Single(s => s.Recipe.Id == IronPlateRecipe.Id);
        var ingotStep = plan.Steps.Single(s => s.Recipe.Id == IronIngotRecipe.Id);
        Assert.Equal(3m, plateStep.BuildingCount);
        Assert.Equal(12m, plateStep.PowerMw);
        Assert.Equal(3m, ingotStep.BuildingCount);
        Assert.Equal(12m, ingotStep.PowerMw);
        Assert.Equal(24m, plan.Steps.Sum(s => s.PowerMw));
    }

    [Fact]
    public void Power_Is_Zero_When_Building_Missing_From_Catalogue()
    {
        // Missing building entry → defensive zero rather than throwing.
        var catalog = new FakeCatalog(
            buildings: [], // smelter intentionally absent
            recipes: [IronIngotRecipe]);

        var planner = new RecursiveRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 30)]));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(0m, step.PowerMw);
    }

    /// <summary>
    /// Minimal in-memory catalogue stand-in. The planner only calls
    /// <see cref="FindDefaultProducerOf"/> and <see cref="FindBuilding"/>, so
    /// everything else throws to surface unexpected usage in tests.
    /// </summary>
    private sealed class FakeCatalog : ICatalogProvider
    {
        private readonly Dictionary<BuildingId, Building> _buildings;
        private readonly List<Recipe> _recipes;

        public FakeCatalog(IEnumerable<Building> buildings, IEnumerable<Recipe> recipes)
        {
            _buildings = buildings.ToDictionary(b => b.Id);
            _recipes = recipes.ToList();
        }

        public bool IsLoaded => true;
        public string? Source => "fake";
        public IReadOnlyList<Item> Items => [];
        public IReadOnlyList<Building> Buildings => _buildings.Values.ToList();
        public IReadOnlyList<Recipe> Recipes => _recipes;

        public Item? FindItem(ItemId id) => null;
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
