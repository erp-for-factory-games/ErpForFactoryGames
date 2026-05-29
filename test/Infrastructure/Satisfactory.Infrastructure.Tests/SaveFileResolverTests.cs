namespace Satisfactory.Infrastructure.Tests;

public class SaveFileResolverTests
{
    [Fact]
    public void Returns_File_Path_Unchanged_When_Path_Is_File()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "Beta Game_autosave_0.sav");
        File.WriteAllBytes(file, [0x00]);

        Assert.Equal(file, SaveFileResolver.Resolve(file));
    }

    [Fact]
    public void Picks_Most_Recent_Sav_In_Directory()
    {
        using var temp = new TempDir();
        var older = Path.Combine(temp.Path, "Older.sav");
        var newer = Path.Combine(temp.Path, "Newer.sav");
        File.WriteAllBytes(older, [0x00]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        File.WriteAllBytes(newer, [0x00]);
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var resolved = SaveFileResolver.Resolve(temp.Path);

        Assert.Equal(newer, resolved);
    }

    [Fact]
    public void Returns_Null_For_Missing_Or_Empty_Input()
    {
        Assert.Null(SaveFileResolver.Resolve(null));
        Assert.Null(SaveFileResolver.Resolve(""));
        Assert.Null(SaveFileResolver.Resolve(@"Z:\does\not\exist"));
    }

    [Fact]
    public void Returns_Null_For_Directory_With_No_Sav_Files()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "ignore me");

        Assert.Null(SaveFileResolver.Resolve(temp.Path));
    }

    [Fact]
    public void EnumerateDetectedSaves_Walks_Per_SteamId_Folders_Sorted_Most_Recent_First()
    {
        using var temp = new TempDir();
        var steamA = Directory.CreateDirectory(Path.Combine(temp.Path, "76561198000000001"));
        var steamB = Directory.CreateDirectory(Path.Combine(temp.Path, "76561198000000002"));

        var older = Path.Combine(steamA.FullName, "Old.sav");
        var middle = Path.Combine(steamB.FullName, "Middle.sav");
        var newest = Path.Combine(steamA.FullName, "Newest.sav");

        File.WriteAllBytes(older, [0x00]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        File.WriteAllBytes(middle, [0x00]);
        File.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddHours(-1));
        File.WriteAllBytes(newest, [0x00]);
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);

        var detected = SaveFileResolver.EnumerateDetectedSaves(temp.Path);

        Assert.Collection(detected,
            f => Assert.Equal(newest, f.FullName),
            f => Assert.Equal(middle, f.FullName),
            f => Assert.Equal(older, f.FullName));
    }

    [Fact]
    public void EnumerateDetectedSaves_Returns_Empty_For_Missing_Root()
    {
        Assert.Empty(SaveFileResolver.EnumerateDetectedSaves(@"Z:\does\not\exist"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("erp-save-test-").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
