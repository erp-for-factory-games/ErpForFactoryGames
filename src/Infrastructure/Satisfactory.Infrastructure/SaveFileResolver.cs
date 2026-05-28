namespace Satisfactory.Infrastructure;

/// <summary>
/// Resolves a user-supplied save-file path. Accepts either a specific
/// <c>.sav</c> file or a SaveGames directory; in the directory case picks the
/// most recently written <c>.sav</c>. Returns <c>null</c> if nothing usable.
/// </summary>
public static class SaveFileResolver
{
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (File.Exists(path)) return path;

        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*.sav", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Best-effort auto-detect of the user's local SaveGames root. Returns the
    /// directory containing the user's most-recently-played save folder, or
    /// <c>null</c> if the standard FactoryGame Saved/SaveGames path doesn't exist.
    /// </summary>
    public static string? AutoDetectLatestSave() =>
        EnumerateDetectedSaves().FirstOrDefault()?.FullName;

    /// <summary>
    /// Enumerates every <c>.sav</c> file under the FactoryGame SaveGames root,
    /// across all per-SteamID subfolders. Sorted most-recently-written first.
    /// Returns an empty sequence if the root doesn't exist.
    /// </summary>
    /// <param name="saveGamesRoot">
    /// Override the SaveGames directory (used by tests). When <c>null</c>,
    /// defaults to <c>%LocalAppData%\FactoryGame\Saved\SaveGames\</c>.
    /// </param>
    public static IReadOnlyList<FileInfo> EnumerateDetectedSaves(string? saveGamesRoot = null)
    {
        var root = saveGamesRoot ?? DefaultSaveGamesRoot();
        if (root is null || !Directory.Exists(root)) return [];

        return new DirectoryInfo(root)
            .EnumerateDirectories()
            .SelectMany(d => d.EnumerateFiles("*.sav", SearchOption.TopDirectoryOnly))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    private static string? DefaultSaveGamesRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "FactoryGame", "Saved", "SaveGames");
    }
}
