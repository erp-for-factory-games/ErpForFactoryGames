using ERP.Application;

namespace CaptainOfIndustry.Catalog.Tests;

public class EmptyCoiCatalogProviderTests : IDisposable
{
    private readonly string? _originalEnv;
    private readonly CoiCatalogueOptions _missingFileOptions;

    public EmptyCoiCatalogProviderTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable);
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, null);
        _missingFileOptions = new CoiCatalogueOptions
        {
            CataloguePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"),
        };
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, _originalEnv);
    }

    [Fact]
    public void Provider_Starts_Unloaded_With_No_Source()
    {
        ICatalogProvider provider = new EmptyCoiCatalogProvider(_missingFileOptions);

        Assert.False(provider.IsLoaded);
        Assert.Null(provider.Source);
        Assert.Empty(provider.Items);
        Assert.Empty(provider.Buildings);
        Assert.Empty(provider.Recipes);
    }

    [Fact]
    public void Lookups_Return_Null_Or_Empty()
    {
        var provider = new EmptyCoiCatalogProvider(_missingFileOptions);

        Assert.Null(provider.FindItem(new("iron")));
        Assert.Null(provider.FindBuilding(new("smelter")));
        Assert.Null(provider.FindRecipe(new("iron-smelting")));
        Assert.Null(provider.FindDefaultProducerOf(new("iron")));
        Assert.Empty(provider.FindAllProducersOf(new("iron")));
    }

    [Fact]
    public void Status_Surfaces_Missing_File_Warning()
    {
        var provider = new EmptyCoiCatalogProvider(_missingFileOptions);

        var status = provider.GetStatus();

        Assert.False(status.IsLoaded);
        Assert.Equal(2, status.Warnings.Count);
        Assert.Contains(status.Warnings, w => w.Contains("not yet implemented"));
        Assert.Contains(status.Warnings, w => w.Contains(_missingFileOptions.CataloguePath!));
    }

    [Fact]
    public void Status_Drops_Missing_File_Warning_When_File_Exists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(tempFile, "{}");
        try
        {
            var provider = new EmptyCoiCatalogProvider(new CoiCatalogueOptions { CataloguePath = tempFile });

            var status = provider.GetStatus();

            Assert.Equal(tempFile, status.Source);
            Assert.Single(status.Warnings);
            Assert.Contains("not yet implemented", status.Warnings[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
