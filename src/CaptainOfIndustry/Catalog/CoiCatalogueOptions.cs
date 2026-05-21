namespace CaptainOfIndustry.Catalog;

/// <summary>
/// Bound from the <c>Catalogue:CaptainOfIndustry</c> configuration section.
/// </summary>
/// <remarks>
/// Captain of Industry has no equivalent of Satisfactory's <c>Docs.json</c> — its
/// catalogue is encoded in C# assemblies. The user runs the extractor tool
/// (<c>tools/CaptainOfIndustryExtractor</c>, see #177) once per game patch to
/// produce a JSON catalogue; the runtime app then points at that file.
/// </remarks>
public sealed class CoiCatalogueOptions
{
    /// <summary>
    /// Absolute path to the extracted CoI catalogue JSON. May be <c>null</c> if
    /// the user hasn't configured one — in which case the resolver falls back
    /// to <see cref="CoiCataloguePathResolver.DefaultPath"/>.
    /// </summary>
    public string? CataloguePath { get; set; }
}
