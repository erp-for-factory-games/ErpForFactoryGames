namespace CaptainOfIndustry.Infrastructure;

/// <summary>
/// Resolves where the extracted Captain of Industry catalogue JSON lives,
/// following the same priority order ADR-0011 established for Satisfactory:
/// environment variable → configured path → default location under
/// <c>%LocalAppData%\ErpForFactoryGames\</c>.
/// </summary>
/// <remarks>
/// Returns the *expected* path (whether or not the file exists) so callers can
/// surface "configured but missing" distinctly from "not configured" in the UI.
/// Use <see cref="ResolveExisting"/> when you only care about a readable file.
/// </remarks>
public static class CoiCataloguePathResolver
{
    /// <summary>
    /// Environment variable consulted first. Per-game key chosen over a generic
    /// <c>ERP_CATALOGUE_PATH</c> so a dev box configuring both games stays
    /// unambiguous (see ADR-0011 'Consequences').
    /// </summary>
    public const string EnvironmentVariable = "ERP_CAPTAIN_OF_INDUSTRY_CATALOGUE_PATH";

    private const string DefaultFileName = "coi-catalogue.json";
    private const string AppFolderName = "ErpForFactoryGames";

    /// <summary>
    /// Default JSON location used when neither the env var nor configuration
    /// provides an explicit path. Lives next to other per-user app data so the
    /// extractor and the runtime app converge by convention.
    /// </summary>
    public static string DefaultPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            DefaultFileName);

    /// <summary>
    /// Resolves the *expected* path without checking whether the file exists.
    /// </summary>
    public static string Resolve(CoiCatalogueOptions options) =>
        Resolve(options.CataloguePath);

    /// <summary>
    /// Resolves the *expected* path without checking whether the file exists.
    /// </summary>
    public static string Resolve(string? configuredPath)
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        if (!string.IsNullOrWhiteSpace(configuredPath)) return configuredPath;

        return DefaultPath;
    }

    /// <summary>
    /// Returns the resolved path if the file exists on disk, else <c>null</c>.
    /// </summary>
    public static string? ResolveExisting(CoiCatalogueOptions options)
    {
        var path = Resolve(options);
        return File.Exists(path) ? path : null;
    }
}
