namespace Erp.Presentation.Api.Common;

/// <summary>
/// v2 auth configuration (ADR-0025). No login yet — the Web UI resolves
/// the "current player" from <see cref="DevPlayerId"/>. A future ADR
/// introduces real login and demotes this to dev-only.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Player id seeded on startup if no row exists. Acts as the
    /// implicit "current player" for the Web UI until login lands.
    /// Defaults to a fixed Guid so a fresh checkout works without
    /// configuration, but production deployments should set this
    /// explicitly.
    /// </summary>
    public Guid DevPlayerId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Display name used when seeding the dev player.</summary>
    public string DevPlayerDisplayName { get; set; } = "Dev Player";
}
