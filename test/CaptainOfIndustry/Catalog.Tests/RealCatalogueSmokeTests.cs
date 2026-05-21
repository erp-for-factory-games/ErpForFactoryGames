using ERP.Application;
using ERP.Domain;

namespace CaptainOfIndustry.Catalog.Tests;

/// <summary>
/// Smoke test that runs only when a real extracted CoI catalogue is present
/// under <c>%LocalAppData%\ErpForFactoryGames\coi-catalogue.json</c>. Guards
/// against extractor↔ingester schema drift on a dev machine; CI skips since
/// CoI isn't installed there.
/// </summary>
public class RealCatalogueSmokeTests
{
    private static string DefaultCataloguePath => CoiCataloguePathResolver.DefaultPath;

    private static bool Available => File.Exists(DefaultCataloguePath);

    [Fact]
    public void Real_Catalogue_Loads_And_Resolves_Known_Recipe()
    {
        if (!Available)
        {
            // CI / fresh dev machines won't have CoI installed. Test is a
            // local-only safety net, not a CI gate.
            return;
        }

        var provider = new JsonCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = DefaultCataloguePath });

        Assert.True(provider.IsLoaded);
        Assert.NotEmpty(provider.Items);
        Assert.NotEmpty(provider.Recipes);
        Assert.NotEmpty(provider.Buildings);

        // WheatMilling is a stable vanilla CoI recipe. If this ever flips,
        // the schema or the extractor changed; investigate before "fixing" the test.
        var wheatMilling = provider.FindRecipe(new RecipeId("WheatMilling"));
        Assert.NotNull(wheatMilling);
        Assert.Equal(new BuildingId("FoodMill"), wheatMilling!.Building);
    }
}
