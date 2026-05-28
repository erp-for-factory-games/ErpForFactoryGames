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
}
