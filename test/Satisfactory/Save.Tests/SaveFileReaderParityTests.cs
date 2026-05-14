using ERP.Domain;

namespace Satisfactory.Save.Tests;

/// <summary>
/// End-to-end parity check: parse a real v1.2 save and confirm the adapter's
/// shape lines up with what's actually in the file. Gated on the
/// <c>ERP_SATISFACTORY_SAVE_PATH</c> env var or auto-detect — passes silently
/// when no save is available so CI doesn't fail on developers without one.
/// When the env var points at a directory the resolver picks the most-recent
/// <c>.sav</c>.
/// </summary>
public class SaveFileReaderParityTests
{
    [Fact]
    public void Parses_Real_Save_File_With_Expected_Shape()
    {
        var path = SaveFileResolver.Resolve(Environment.GetEnvironmentVariable("ERP_SATISFACTORY_SAVE_PATH"))
            ?? SaveFileResolver.AutoDetectLatestSave();

        if (path is null)
        {
            // No save available — skip silently. xUnit v2 lacks first-class
            // skip-at-runtime; we treat absence as "nothing to verify here".
            return;
        }

        var state = new SaveFileReader().Read(path);

        Assert.NotEqual(0, state.Save.SaveVersion);
        Assert.NotEqual(0, state.Save.BuildVersion);
        Assert.NotEmpty(state.Save.SessionName);
        Assert.NotEmpty(state.ResourceNodes);

        // #35 — deep-parsed fields. Conditional because the Pedestal test
        // fixture has resource nodes but no miners / no buildings.
        if (state.Miners.Count > 0)
        {
            Assert.Contains(state.Miners, m => !string.IsNullOrEmpty(m.ResourceNodeReference));
        }

        if (state.Buildings.Count > 0)
        {
            Assert.Contains(state.Buildings, b => b.Recipe is not null);
            // All buildings default to 1.0 clock speed (only overclocked ones
            // serialize a value); assert default clock is plausible.
            Assert.All(state.Buildings, b => Assert.InRange(b.ClockSpeed, 0.01m, 2.5m));
        }

        // #44 — belt polyline plumbing. When the fork starts emitting
        // ConveyorChainActor ExtraData for v1.2 saves (currently excluded
        // by ObjectSerializer.cs:130 — see fork TODO.md §5), every belt
        // referenced by a chain actor will get a Polyline of ≥2 points.
        // Until then this loop runs but yields no matches. Assertion is
        // a shape check: any polyline we DO produce must be well-formed.
        foreach (var belt in state.Belts.Where(b => b.Polyline is not null))
        {
            Assert.NotNull(belt.Polyline);
            Assert.True(belt.Polyline!.Count >= 2,
                $"Belt {belt.Reference} has Polyline with {belt.Polyline.Count} points; expected ≥2.");
        }
    }
}
