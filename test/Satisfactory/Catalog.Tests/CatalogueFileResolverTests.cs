namespace Satisfactory.Infrastructure.Tests;

public class CatalogueFileResolverTests
{
    [Fact]
    public void Returns_File_Path_Unchanged_When_Path_Is_File()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "en-US.json");
        File.WriteAllText(file, "[]");

        Assert.Equal(file, CatalogueFileResolver.Resolve(file));
    }

    [Fact]
    public void Prefers_EnUS_Locale_File_When_Path_Is_Directory()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "de-DE.json"), "[]");
        File.WriteAllText(Path.Combine(temp.Path, "en-US.json"), "[]");
        File.WriteAllText(Path.Combine(temp.Path, "ja-JP.json"), "[]");

        var resolved = CatalogueFileResolver.Resolve(temp.Path);
        Assert.NotNull(resolved);
        Assert.Equal("en-US.json", Path.GetFileName(resolved));
    }

    [Fact]
    public void Falls_Back_To_Legacy_Docs_Json_When_No_Locale_Files()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "Docs.json"), "[]");

        var resolved = CatalogueFileResolver.Resolve(temp.Path);
        Assert.NotNull(resolved);
        Assert.Equal("Docs.json", Path.GetFileName(resolved));
    }

    [Fact]
    public void Falls_Back_To_Any_Locale_File_When_EnUS_Missing()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "de-DE.json"), "[]");
        File.WriteAllText(Path.Combine(temp.Path, "ja-JP.json"), "[]");

        var resolved = CatalogueFileResolver.Resolve(temp.Path);
        Assert.NotNull(resolved);
        // Sorted alphabetically — de-DE wins over ja-JP.
        Assert.Equal("de-DE.json", Path.GetFileName(resolved));
    }

    [Fact]
    public void Returns_Null_For_Missing_Path()
    {
        Assert.Null(CatalogueFileResolver.Resolve(null));
        Assert.Null(CatalogueFileResolver.Resolve(""));
        Assert.Null(CatalogueFileResolver.Resolve(@"Z:\does\not\exist"));
    }

    [Fact]
    public void Returns_Null_For_Empty_Directory()
    {
        using var temp = new TempDir();
        Assert.Null(CatalogueFileResolver.Resolve(temp.Path));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("erp-resolver-test-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
