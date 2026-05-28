namespace Satisfactory.Infrastructure;

/// <summary>
/// Resolves a user-supplied path (which may point at a file or at the game's
/// <c>Docs</c> directory) to the actual JSON file we should parse.
/// </summary>
/// <remarks>
/// Modern Satisfactory ships <c>CommunityResources/Docs/&lt;locale&gt;.json</c>
/// (e.g. <c>en-US.json</c>, <c>de-DE.json</c>) instead of a single
/// <c>Docs.json</c>. We default to <c>en-US</c>; localised UI is a separate epic.
/// </remarks>
public static class CatalogueFileResolver
{
    private const string PreferredLocaleFile = "en-US.json";
    private const string LegacyFile = "Docs.json";

    /// <summary>
    /// Resolves <paramref name="path"/> to a readable JSON file:
    /// returns the path itself if it's a file, or the preferred locale file
    /// (or a sensible fallback) when it's a directory. Returns <c>null</c> if
    /// nothing usable was found.
    /// </summary>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (File.Exists(path)) return path;

        if (Directory.Exists(path))
        {
            var preferred = Path.Combine(path, PreferredLocaleFile);
            if (File.Exists(preferred)) return preferred;

            var legacy = Path.Combine(path, LegacyFile);
            if (File.Exists(legacy)) return legacy;

            // Fall back to any locale-shaped JSON file in that directory
            // (xx-YY.json), then any .json. Sorted so the result is deterministic.
            var jsons = Directory.EnumerateFiles(path, "*.json").OrderBy(p => p).ToList();
            return jsons.FirstOrDefault(IsLocaleFile) ?? jsons.FirstOrDefault();
        }

        return null;
    }

    private static bool IsLocaleFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length == 5 && name[2] == '-' &&
               char.IsLetter(name[0]) && char.IsLetter(name[1]) &&
               char.IsLetter(name[3]) && char.IsLetter(name[4]);
    }
}
