namespace Erp.Infrastructure;

/// <summary>
/// Bound from the <c>Catalogue:Satisfactory</c> configuration section.
/// </summary>
public sealed class CatalogueOptions
{
    public string? DocsPath { get; set; }

    /// <summary>
    /// When <c>true</c>, the planner falls back to the server-local
    /// <c>Docs.json</c> path resolution (ADR-0011) if the request's
    /// player has no uploaded catalogue. Defaults to <c>false</c>;
    /// dev environments override to <c>true</c> via
    /// <c>appsettings.Development.json</c>. See ADR-0025 §4.
    /// </summary>
    public bool AllowServerLocalFallback { get; set; }
}
