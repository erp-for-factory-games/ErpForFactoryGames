namespace Satisfactory.Presentation.Web;

/// <summary>
/// Web-side configuration for the "My Agents" management surface
/// (ADR-0025 §8). Until login lands this is the only auth-related gate
/// — the page lets a visitor mint, list, and revoke tokens for the dev
/// player, so production deployments should keep
/// <see cref="Enabled"/> off until the homelab fronts the Web UI with
/// its own access gate.
/// </summary>
public sealed class AgentManagementOptions
{
    public const string SectionName = "AgentManagement";

    /// <summary>
    /// Feature flag. When <c>false</c>, the page returns 404 and the
    /// NavMenu link is hidden. Defaults to <c>true</c> in dev/local
    /// runs; production <c>appsettings.json</c> should set it explicitly.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Absolute base URL the agent should target — embedded in the
    /// <c>erp-agent://pair?token=…&amp;api=…</c> deep-link. Different
    /// from the Web's internal <c>https+http://apiservice</c> binding
    /// because the agent runs on the user's machine and reaches the
    /// API over the public Cloudflare tunnel.
    ///
    /// <para>
    /// When empty, the dialog hides the "Open in agent" button and
    /// surfaces the CLI wizard instructions only.
    /// </para>
    /// </summary>
    public string PairingApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether the catalogue is sourced from a paired agent (hosted topology)
    /// rather than a path on the API server's own filesystem (dev / self-host).
    ///
    /// <para>
    /// When <c>true</c> (the hosted default) the setup wizard leads with an
    /// agent install + pair step and the catalogue step waits for the agent's
    /// <c>Docs.json</c> upload — the server-local filesystem picker is hidden
    /// because, in the hosted shape, browsing the API host's filesystem
    /// enumerates the LXC, not the user's machine (issues #268 / #270).
    /// </para>
    ///
    /// <para>
    /// When <c>false</c> (set in <c>appsettings.Development.json</c> for the
    /// AppHost-driven local run, where <c>Catalogue:AllowServerLocalFallback</c>
    /// is on) the legacy filesystem-picker flow is kept — the API and the
    /// browser are the same machine, so picking a local <c>Docs.json</c> works.
    /// </para>
    ///
    /// Tracks the deployment-model decision in #267; flip to a server-reported
    /// capability once that lands.
    /// </summary>
    public bool CatalogueViaAgent { get; set; } = true;
}
