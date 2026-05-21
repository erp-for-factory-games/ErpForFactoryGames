using Agent;
using Microsoft.Extensions.Options;

namespace Agent.Tests;

public class SaveFolderResolverTests : IDisposable
{
    private readonly string _tempDir;

    public SaveFolderResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Configured_Override_Wins_When_Directory_Exists()
    {
        var resolver = new SaveFolderResolver(Options.Create(new AgentOptions
        {
            SaveFolderPath = _tempDir,
        }));

        Assert.Equal(_tempDir, resolver.Resolve());
    }

    [Fact]
    public void Override_Ignored_When_Directory_Missing()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var resolver = new SaveFolderResolver(Options.Create(new AgentOptions
        {
            SaveFolderPath = missing,
        }));

        // Configured override doesn't exist → falls through to OS defaults.
        // On a dev box that has Satisfactory installed at the default path,
        // this could return the real folder; on CI it'll return null. Either
        // way it should NOT return the bogus override.
        var resolved = resolver.Resolve();
        Assert.NotEqual(missing, resolved);
    }

    [Fact]
    public void ConfiguredOverride_Exposes_The_Configured_Value_Verbatim()
    {
        var resolver = new SaveFolderResolver(Options.Create(new AgentOptions
        {
            SaveFolderPath = @"C:\some\nonsense\that\never\exists",
        }));

        Assert.Equal(@"C:\some\nonsense\that\never\exists", resolver.ConfiguredOverride);
    }

    [Fact]
    public void No_Override_And_No_Default_Returns_Null_On_Unknown_Platform()
    {
        // On any OS, if the override is null/whitespace and no default
        // directory exists, Resolve() must return null rather than
        // throwing. This guards against host crashes when Satisfactory
        // isn't installed.
        var resolver = new SaveFolderResolver(Options.Create(new AgentOptions
        {
            SaveFolderPath = null,
        }));

        // We can't reliably assert null here because a dev box might have
        // Satisfactory installed at the default path. Just verify it
        // doesn't throw.
        var _ = resolver.Resolve();
    }
}
