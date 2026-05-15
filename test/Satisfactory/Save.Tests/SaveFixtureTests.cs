using ERP.Domain;

namespace Satisfactory.Save.Tests;

public class SaveFixtureTests
{
    public static TheoryData<string, int> Fixtures => new()
    {
        { "Fixtures/v1_0/Finally-1.0.sav", 41 },
        { "Fixtures/v1_2/EmptyWorld.sav", 51 },
        { "Fixtures/v1_2/TheHub.sav", 51 },
        { "Fixtures/v1_2/Pedestal.sav", 51 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Parses_bundled_fixture_with_expected_shape(string relativePath, int expectedSaveVersion)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(path), $"Fixture missing at {path}. Run `git lfs pull` if using LFS, or check the build copy-to-output config.");

        var state = new SaveFileReader().Read(path);

        Assert.True(state.Save.SaveVersion >= expectedSaveVersion,
            $"{relativePath} SaveVersion={state.Save.SaveVersion}; expected ≥{expectedSaveVersion}.");
        Assert.NotEqual(0, state.Save.BuildVersion);
        Assert.NotEmpty(state.Save.SessionName);

        foreach (var belt in state.Belts.Where(b => b.Polyline is not null))
        {
            Assert.True(belt.Polyline!.Count >= 2,
                $"{relativePath}: belt {belt.Reference} has Polyline with {belt.Polyline.Count} points; expected ≥2.");
        }
    }
}
