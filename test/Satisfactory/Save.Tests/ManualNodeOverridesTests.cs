using Erp.Domain.Common;

namespace Satisfactory.Save.Tests;

public class ManualNodeOverridesTests
{
    [Fact]
    public void LoadOrCreate_Returns_Empty_Instance_When_File_Missing()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "nope.json");

        var overrides = ManualNodeOverrides.LoadOrCreate(path);

        Assert.Equal(0, overrides.Count);
        Assert.Equal(path, overrides.FilePath);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadOrCreate_Returns_Empty_Instance_When_File_Is_Malformed()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "broken.json");
        File.WriteAllText(path, "{ this is not valid json");

        var overrides = ManualNodeOverrides.LoadOrCreate(path);

        Assert.Equal(0, overrides.Count);
    }

    [Fact]
    public void Upsert_Persists_New_Entry_And_Roundtrips_From_Disk()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "overrides.json");
        var overrides = ManualNodeOverrides.LoadOrCreate(path);

        overrides.Upsert(new Position(1000, 2000, 50), "Desc_OreIron_C", NodePurity.Pure);

        Assert.True(File.Exists(path));
        var reloaded = ManualNodeOverrides.LoadOrCreate(path);
        Assert.Equal(1, reloaded.Count);
        var hit = reloaded.Lookup(new Position(1000, 2000, 50));
        Assert.NotNull(hit);
        Assert.Equal("Desc_OreIron_C", hit!.Resource);
        Assert.Equal(NodePurity.Pure, hit.Purity);
    }

    [Fact]
    public void Upsert_Replaces_Existing_Entry_Within_Tolerance()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "overrides.json");
        var overrides = ManualNodeOverrides.LoadOrCreate(path);

        overrides.Upsert(new Position(0, 0, 0), "Desc_OreIron_C", NodePurity.Normal);
        // A small movement (well under default 500 unit tolerance) — same node, updated.
        overrides.Upsert(new Position(100, 100, 0), "Desc_OreCopper_C", NodePurity.Pure);

        Assert.Equal(1, overrides.Count);
        var hit = overrides.Lookup(new Position(50, 50, 0));
        Assert.Equal("Desc_OreCopper_C", hit!.Resource);
        Assert.Equal(NodePurity.Pure, hit.Purity);
    }

    [Fact]
    public void Upsert_Adds_New_Entry_When_Outside_Tolerance()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "overrides.json");
        var overrides = ManualNodeOverrides.LoadOrCreate(path);

        overrides.Upsert(new Position(0, 0, 0), "Desc_OreIron_C", NodePurity.Normal);
        overrides.Upsert(new Position(10000, 0, 0), "Desc_OreCopper_C", NodePurity.Pure);

        Assert.Equal(2, overrides.Count);
    }

    [Fact]
    public void Delete_Removes_Entry_And_Persists()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "overrides.json");
        var overrides = ManualNodeOverrides.LoadOrCreate(path);
        overrides.Upsert(new Position(1000, 1000, 0), "Desc_Coal_C", NodePurity.Pure);

        var removed = overrides.Delete(new Position(1100, 1100, 0));

        Assert.True(removed);
        Assert.Equal(0, overrides.Count);
        var reloaded = ManualNodeOverrides.LoadOrCreate(path);
        Assert.Equal(0, reloaded.Count);
    }

    [Fact]
    public void Delete_Is_Noop_When_No_Match_In_Tolerance()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "overrides.json");
        var overrides = ManualNodeOverrides.LoadOrCreate(path);
        overrides.Upsert(new Position(0, 0, 0), "Desc_Coal_C", NodePurity.Pure);

        var removed = overrides.Delete(new Position(50000, 50000, 0));

        Assert.False(removed);
        Assert.Equal(1, overrides.Count);
    }

    [Fact]
    public void Empty_Instance_Throws_On_Upsert()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ManualNodeOverrides.Empty.Upsert(new Position(0, 0, 0), "Desc_OreIron_C", NodePurity.Normal));
    }

    [Fact]
    public void Path_Resolver_Prefers_Env_Var_Over_Config_And_Default()
    {
        var prior = Environment.GetEnvironmentVariable(ManualNodeOverridesPath.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ManualNodeOverridesPath.EnvVar, @"C:\from\env.json");

            Assert.Equal(@"C:\from\env.json",
                ManualNodeOverridesPath.Resolve(configuredPath: @"C:\from\config.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ManualNodeOverridesPath.EnvVar, prior);
        }
    }

    [Fact]
    public void Path_Resolver_Falls_Through_To_Configured_Then_LocalAppData()
    {
        var prior = Environment.GetEnvironmentVariable(ManualNodeOverridesPath.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ManualNodeOverridesPath.EnvVar, null);

            Assert.Equal(@"C:\from\config.json",
                ManualNodeOverridesPath.Resolve(configuredPath: @"C:\from\config.json"));

            var defaultPath = ManualNodeOverridesPath.Resolve(configuredPath: null);
            Assert.EndsWith(ManualNodeOverridesPath.DefaultRelativePath, defaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ManualNodeOverridesPath.EnvVar, prior);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("erp-overrides-test-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
