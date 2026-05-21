namespace CaptainOfIndustry.Catalog.Tests;

public class CoiCataloguePathResolverTests : IDisposable
{
    private readonly string? _originalEnv;

    public CoiCataloguePathResolverTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable);
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, _originalEnv);
    }

    [Fact]
    public void Env_Var_Wins_Over_Configured_Path()
    {
        Environment.SetEnvironmentVariable(CoiCataloguePathResolver.EnvironmentVariable, @"C:\from-env.json");

        var resolved = CoiCataloguePathResolver.Resolve(new CoiCatalogueOptions { CataloguePath = @"C:\from-config.json" });

        Assert.Equal(@"C:\from-env.json", resolved);
    }

    [Fact]
    public void Configured_Path_Wins_Over_Default()
    {
        var resolved = CoiCataloguePathResolver.Resolve(new CoiCatalogueOptions { CataloguePath = @"C:\from-config.json" });

        Assert.Equal(@"C:\from-config.json", resolved);
    }

    [Fact]
    public void Falls_Back_To_Default_Path_When_Nothing_Configured()
    {
        var resolved = CoiCataloguePathResolver.Resolve(new CoiCatalogueOptions());

        Assert.Equal(CoiCataloguePathResolver.DefaultPath, resolved);
    }

    [Fact]
    public void Default_Path_Lives_Under_LocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, CoiCataloguePathResolver.DefaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("coi-catalogue.json", CoiCataloguePathResolver.DefaultPath);
    }

    [Fact]
    public void Whitespace_Configured_Path_Is_Ignored()
    {
        var resolved = CoiCataloguePathResolver.Resolve(new CoiCatalogueOptions { CataloguePath = "   " });

        Assert.Equal(CoiCataloguePathResolver.DefaultPath, resolved);
    }

    [Fact]
    public void ResolveExisting_Returns_Null_When_File_Missing()
    {
        var result = CoiCataloguePathResolver.ResolveExisting(new CoiCatalogueOptions
        {
            CataloguePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"),
        });

        Assert.Null(result);
    }

    [Fact]
    public void ResolveExisting_Returns_Path_When_File_Exists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(tempFile, "{}");
        try
        {
            var result = CoiCataloguePathResolver.ResolveExisting(new CoiCatalogueOptions { CataloguePath = tempFile });

            Assert.Equal(tempFile, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
