namespace ApiService;

/// <summary>
/// Where the catalogue blob store keeps uploaded <c>Docs.json</c> bytes
/// (ADR-0025 §4-§5). Filesystem-backed for v2; can swap to S3 etc.
/// without changing the <see cref="ICatalogueStorage"/> contract.
/// </summary>
public sealed class CatalogueStorageOptions
{
    public const string SectionName = "CatalogueStorage";

    /// <summary>
    /// Root directory for catalogue blobs. Each blob lands at
    /// <c>{Root}/{playerId}/{game}/{hash}.json</c>. Defaults to
    /// <c>%ProgramData%/ErpForFactoryGames/catalogues</c> on Windows
    /// or <c>$XDG_DATA_HOME/ErpForFactoryGames/catalogues</c> elsewhere.
    /// Empty falls back to the default.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// Hard ceiling on a single Docs.json upload. Vanilla Satisfactory's
    /// is ~30 MB; modded versions can be larger. Default 200 MB — large
    /// enough for any realistic mod load, small enough that a stuck
    /// upload doesn't fill the LXC.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
}
