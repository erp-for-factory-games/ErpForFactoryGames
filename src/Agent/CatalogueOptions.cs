namespace Agent;

/// <summary>
/// Where the agent finds the user's <c>Docs.json</c> to upload to the
/// hosted planner (ADR-0025 §4-§5). Reuses the same config key
/// (<c>Catalogue:Satisfactory:DocsPath</c>) the server-side
/// DocsCatalogProvider used pre-#238 so existing user configs work
/// unchanged.
/// </summary>
public sealed class CatalogueUploadOptions
{
    public const string SectionName = "Catalogue:Satisfactory";

    /// <summary>
    /// Absolute path to the user's <c>Docs.json</c> (e.g.
    /// <c>C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs\Docs.json</c>).
    /// Empty disables catalogue uploads — the agent logs a warning and
    /// otherwise carries on shipping saves + log lines.
    /// </summary>
    public string? DocsPath { get; set; }

    /// <summary>
    /// Override via env var <c>ERP_AGENT_SATISFACTORY_DOCS_PATH</c>.
    /// Mirrors the server-side <c>ERP_SATISFACTORY_DOCS_PATH</c> escape
    /// hatch from ADR-0011.
    /// </summary>
    public const string EnvironmentVariable = "ERP_AGENT_SATISFACTORY_DOCS_PATH";
}
