using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using Erp.Domain.Common;

namespace CaptainOfIndustry.Catalog.Tests;

/// <summary>
/// The e2e proof for the milestone (#182): wire the JsonCoiCatalogProvider
/// (CoI module) into the planner core (ERP.Application) and confirm a real
/// production target resolves to a sensible recipe graph. If this test ever
/// flips red, the "planner is genuinely game-agnostic" claim from ADR-0022
/// has regressed — investigate the catalogue mapping, not the planner.
/// </summary>
[Collection(nameof(CoiCatalogueEnvCollection))]
public class PlannerSmokeTests : IDisposable
{
    private readonly string? _originalEnv;
    private readonly string _tempDir;

    public PlannerSmokeTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable);
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, null);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, _originalEnv);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // CoI internal-unit reminder: quantity 50 = 1 visual item / 1 belt slot in
    // game. The planner doesn't care about absolute units — it works in
    // "items/min" purely as ratios — so the smoke uses CoI's raw quantities
    // verbatim. Production UI is the layer that picks user-friendly units.
    private const string MillingChain = """
        {
          "extractorVersion": "0.4.0.0",
          "coiVersion": "0.8.4.0",
          "extractedAt": "2026-05-21T00:00:00+00:00",
          "items": [
            { "id": "Product_Wheat", "name": "Wheat", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 },
            { "id": "Product_Flour", "name": "Flour", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 },
            { "id": "Product_Bread", "name": "Bread", "kind": "Countable", "isStorable": true, "isWaste": false, "radioactivity": 0 },
            { "id": "Product_AnimalFeed", "name": "Animal feed", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 }
          ],
          "buildings": [
            { "id": "FoodMill", "name": "Food mill", "electricityKw": 200, "recipes": ["WheatMilling"] },
            { "id": "Bakery",   "name": "Bakery",    "electricityKw": 100, "recipes": ["BreadProduction"] }
          ],
          "recipes": [
            {
              "id": "WheatMilling",
              "name": "Wheat milling",
              "building": "FoodMill",
              "durationTicks": 300,
              "inputs":  [{ "productId": "Product_Wheat", "quantity": 8 }],
              "outputs": [
                { "productId": "Product_Flour",      "quantity": 8 },
                { "productId": "Product_AnimalFeed", "quantity": 1 }
              ]
            },
            {
              "id": "BreadProduction",
              "name": "Bread making",
              "building": "Bakery",
              "durationTicks": 300,
              "inputs":  [{ "productId": "Product_Flour", "quantity": 4 }],
              "outputs": [{ "productId": "Product_Bread", "quantity": 2 }]
            }
          ],
          "warnings": []
        }
        """;

    private (JsonCoiCatalogProvider catalog, RecursiveRecipePlanner planner) BuildPlanner(string json)
    {
        var path = Path.Combine(_tempDir, "cat.json");
        File.WriteAllText(path, json);
        var catalog = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });
        var planner = new RecursiveRecipePlanner(catalog);
        return (catalog, planner);
    }

    [Fact]
    public void Plans_Bread_Production_With_Both_Steps()
    {
        var (_, planner) = BuildPlanner(MillingChain);

        // Each cycle: 300 ticks @ 40/s = 7.5s -> 8 cycles/min -> 16 bread/min.
        // Target 16 bread/min should call for exactly 1 Bakery + 1 FoodMill.
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(new ItemId("Product_Bread"), 16)],
            Available: [new ResourceAvailability(new ItemId("Product_Wheat"), 9999)]));

        Assert.True(plan.IsFeasible);
        Assert.Equal(2, plan.Steps.Count);

        var bread = plan.Steps.Single(s => s.Recipe.Id == new RecipeId("BreadProduction"));
        Assert.Equal(new BuildingId("Bakery"), bread.Recipe.Building);
        Assert.Equal(1m, bread.BuildingCount);
        Assert.Equal(0.1m, bread.PowerMw); // 100 kW -> 0.1 MW

        var milling = plan.Steps.Single(s => s.Recipe.Id == new RecipeId("WheatMilling"));
        Assert.Equal(new BuildingId("FoodMill"), milling.Recipe.Building);
        Assert.Equal(0.5m, milling.BuildingCount); // 32 flour/min produced @ full speed; we need 16, so half a mill.
        Assert.Equal(0.1m, milling.PowerMw); // 200 kW * 0.5 = 0.1 MW
    }

    [Fact]
    public void Surfaces_Missing_Raw_Inputs()
    {
        var (_, planner) = BuildPlanner(MillingChain);

        // No wheat available — should flag Product_Wheat as missing.
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(new ItemId("Product_Bread"), 16)],
            Available: []));

        Assert.False(plan.IsFeasible);
        Assert.Contains(plan.MissingInputs, m => m.Item == new ItemId("Product_Wheat"));
    }

    [Fact]
    public void Multi_Output_Recipe_Powers_Both_Sinks()
    {
        var (_, planner) = BuildPlanner(MillingChain);

        // Wheat milling produces flour AND animal feed. Asking for Animal feed
        // should land on the same recipe — proving multi-output linkage works.
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(new ItemId("Product_AnimalFeed"), 8)],
            Available: [new ResourceAvailability(new ItemId("Product_Wheat"), 9999)]));

        Assert.True(plan.IsFeasible);
        var milling = Assert.Single(plan.Steps);
        Assert.Equal(new RecipeId("WheatMilling"), milling.Recipe.Id);
        // WheatMilling: 7.5s/cycle = 8 cycles/min; 1 animal feed/cycle = 8/min/mill.
        // Asking for 8 feed/min lands on exactly 1.0 mill (flour byproduct is excess).
        Assert.Equal(1m, milling.BuildingCount);
    }

    [Fact]
    public void Smoke_Against_Real_Extracted_Catalogue_If_Present()
    {
        var defaultPath = CoiCataloguePathResolver.DefaultPath;
        if (!File.Exists(defaultPath))
        {
            // No real catalogue on this machine — silent no-op. Mirrors
            // RealCatalogueSmokeTests' pattern: local-only safety net, not a CI gate.
            return;
        }

        var catalog = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = defaultPath });
        var planner = new RecursiveRecipePlanner(catalog);

        // WheatMilling is a stable vanilla CoI recipe with a well-known output
        // (Flour). If this ever flips red, either the extractor's mapping
        // changed or Mafi renamed it — investigate before mutating the test.
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(new ItemId("Product_Flour"), 100)],
            Available: [new ResourceAvailability(new ItemId("Product_Wheat"), 99999)]));

        Assert.NotEmpty(plan.Steps);
        Assert.Contains(plan.Steps, s => s.Recipe.Id == new RecipeId("WheatMilling"));
    }
}
