using ERP.Application;
using ERP.Domain;

namespace CaptainOfIndustry.Catalog.Tests;

[Collection(nameof(CoiCatalogueEnvCollection))]
public class JsonCoiCatalogProviderTests : IDisposable
{
    private readonly string? _originalEnv;
    private readonly string _tempDir;

    public JsonCoiCatalogProviderTests()
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

    private string WriteFixture(string json)
    {
        var path = Path.Combine(_tempDir, "coi-catalogue.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string SampleCatalogue = """
        {
          "extractorVersion": "0.4.0.0",
          "coiVersion": "0.8.4.0",
          "extractedAt": "2026-05-21T00:00:00+00:00",
          "items": [
            { "id": "Product_Wheat", "name": "Wheat", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 },
            { "id": "Product_Flour", "name": "Flour", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 },
            { "id": "Product_AnimalFeed", "name": "Animal feed", "kind": "Loose", "isStorable": true, "isWaste": false, "radioactivity": 0 }
          ],
          "recipes": [
            {
              "id": "WheatMilling",
              "name": "Wheat milling",
              "building": "FoodMill",
              "durationTicks": 300,
              "inputs":  [{ "productId": "Product_Wheat", "quantity": 8 }],
              "outputs": [
                { "productId": "Product_Flour", "quantity": 8 },
                { "productId": "Product_AnimalFeed", "quantity": 1 }
              ]
            },
            {
              "id": "OrphanedRecipe",
              "name": "Player-crafted thing",
              "building": null,
              "durationTicks": 100,
              "inputs": [],
              "outputs": []
            }
          ],
          "buildings": [
            { "id": "FoodMill", "name": "Food mill", "electricityKw": 200, "recipes": ["WheatMilling"] }
          ],
          "warnings": ["test-warning-from-extractor"]
        }
        """;

    [Fact]
    public void Empty_State_When_Path_Missing()
    {
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions
        {
            CataloguePath = Path.Combine(_tempDir, "does-not-exist.json"),
        });

        Assert.False(provider.IsLoaded);
        Assert.Null(provider.Source);
        Assert.Empty(provider.Items);
        Assert.Empty(provider.Recipes);
        var status = provider.GetStatus();
        Assert.Single(status.Warnings);
        Assert.Contains("does-not-exist.json", status.Warnings[0]);
    }

    [Fact]
    public void Loads_Sample_Catalogue_Counts()
    {
        var path = WriteFixture(SampleCatalogue);
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        Assert.True(provider.IsLoaded);
        Assert.Equal(path, provider.Source);
        Assert.Equal(3, provider.Items.Count);
        Assert.Single(provider.Buildings);
        Assert.Single(provider.Recipes); // orphan dropped
    }

    [Fact]
    public void Maps_Recipe_Inputs_Outputs_And_Building()
    {
        var path = WriteFixture(SampleCatalogue);
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        var recipe = provider.FindRecipe(new RecipeId("WheatMilling"));
        Assert.NotNull(recipe);
        Assert.Equal("Wheat milling", recipe!.Name);
        Assert.Equal(new BuildingId("FoodMill"), recipe.Building);
        Assert.Equal(TimeSpan.FromSeconds(300 / 40.0), recipe.Duration);

        Assert.Single(recipe.Inputs);
        Assert.Equal(new ItemId("Product_Wheat"), recipe.Inputs[0].Item);
        Assert.Equal(8, recipe.Inputs[0].Quantity);

        Assert.Equal(2, recipe.Outputs.Count);
        Assert.Contains(recipe.Outputs, o => o.Item == new ItemId("Product_Flour") && o.Quantity == 8);
        Assert.Contains(recipe.Outputs, o => o.Item == new ItemId("Product_AnimalFeed") && o.Quantity == 1);
    }

    [Fact]
    public void Building_Power_Is_Converted_KW_To_MW()
    {
        var path = WriteFixture(SampleCatalogue);
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        var building = provider.FindBuilding(new BuildingId("FoodMill"));
        Assert.NotNull(building);
        Assert.Equal(0.2, building!.BasePowerMw);
    }

    [Fact]
    public void Producers_Index_Resolves_Multi_Output_Recipes()
    {
        var path = WriteFixture(SampleCatalogue);
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        var flourProducers = provider.FindAllProducersOf(new ItemId("Product_Flour"));
        var feedProducers = provider.FindAllProducersOf(new ItemId("Product_AnimalFeed"));
        Assert.Single(flourProducers);
        Assert.Single(feedProducers);
        Assert.Equal(flourProducers[0], feedProducers[0]);
    }

    [Fact]
    public void Orphaned_Recipe_Drop_Surfaces_As_Warning()
    {
        var path = WriteFixture(SampleCatalogue);
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        var status = provider.GetStatus();
        // 1 extractor warning passed through + 1 from-our-side about dropped orphan.
        Assert.Contains(status.Warnings, w => w.Contains("test-warning-from-extractor"));
        Assert.Contains(status.Warnings, w => w.Contains("Dropped 1 recipes"));
    }

    [Fact]
    public void LoadFromPath_Replaces_State()
    {
        var path = WriteFixture(SampleCatalogue);
        // Point at a missing path so the provider starts unloaded regardless
        // of whether the dev machine has a real catalogue at the default location.
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions
        {
            CataloguePath = Path.Combine(_tempDir, "starts-missing.json"),
        });

        Assert.False(provider.IsLoaded);
        provider.LoadFromPath(path);
        Assert.True(provider.IsLoaded);
        Assert.Equal(3, provider.Items.Count);
    }

    [Fact]
    public void Malformed_Json_Returns_Empty_State_With_Warning()
    {
        var path = WriteFixture("{ not json");
        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = path });

        Assert.False(provider.IsLoaded);
        var status = provider.GetStatus();
        Assert.Contains(status.Warnings, w => w.Contains("Failed to parse"));
    }
}
