using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Satisfactory.Catalog;

/// <summary>
/// Best-effort discovery of the user's Satisfactory <c>Docs.json</c> by walking the
/// Steam library configuration. Windows-only for v1 — Linux/macOS detection lands
/// when those platforms become tier-1 supported.
/// </summary>
public static class SteamLibraryDetector
{
    private static readonly Regex VdfPathRegex = new(
        @"""path""\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SatisfactoryFolderNames =
    [
        "Satisfactory",
        "SatisfactoryEarlyAccess",
        "SatisfactoryExperimental",
    ];

    /// <summary>
    /// Returns the first <c>Docs.json</c> found in any Steam library, or <c>null</c>.
    /// </summary>
    public static string? FindDocsJson()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        foreach (var library in EnumerateSteamLibraries())
        {
            foreach (var folder in SatisfactoryFolderNames)
            {
                var candidate = Path.Combine(library, "steamapps", "common", folder, "CommunityResources", "Docs", "Docs.json");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        foreach (var steamRoot in EnumerateSteamRoots())
        {
            yield return steamRoot;

            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string content;
            try { content = File.ReadAllText(vdf); }
            catch { continue; }

            foreach (Match m in VdfPathRegex.Matches(content))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        // Default install paths.
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf86))
        {
            var p = Path.Combine(pf86, "Steam");
            if (Directory.Exists(p)) yield return p;
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
        {
            var p = Path.Combine(pf, "Steam");
            if (Directory.Exists(p)) yield return p;
        }
    }
}
