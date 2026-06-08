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

    // ---- Human login backend (ADR-0028, issue #292) -------------------------

    /// <summary>
    /// Selects how the "current player" is resolved for user-facing surfaces:
    /// <c>dev</c> (default — <see cref="DevPlayerId"/>, zero IdP infra) or
    /// <c>keycloak</c> (resolve from the validated OIDC <c>sub</c>, JIT-provision
    /// the player). The agent-token path (ADR-0027) is unaffected either way.
    /// </summary>
    public string Backend { get; set; } = "dev";

    /// <summary>True when <see cref="Backend"/> selects Keycloak OIDC.</summary>
    public bool UsesKeycloak => string.Equals(Backend, "keycloak", StringComparison.OrdinalIgnoreCase);

    /// <summary>Keycloak validation settings (used only when <see cref="UsesKeycloak"/>).</summary>
    public KeycloakOptions Keycloak { get; set; } = new();

    /// <summary>
    /// Settings the game APIs need to validate Keycloak access tokens (ADR-0028 §3).
    /// </summary>
    public sealed class KeycloakOptions
    {
        /// <summary>Aspire service name of the Keycloak resource (service discovery).</summary>
        public string ServiceName { get; set; } = "keycloak";

        /// <summary>Realm the tokens are issued from.</summary>
        public string Realm { get; set; } = "erp";

        /// <summary>Expected <c>aud</c> claim. Empty disables audience validation
        /// (the dev realm doesn't add an audience mapper — prod hardening does).</summary>
        public string Audience { get; set; } = "";
    }

    // ---- JWT/HMAC agent-token auth (ADR-0027 / phase 5c3) -------------------

    /// <summary>
    /// HMAC-SHA256 signing key shared between the Auth API (mints) and the
    /// game APIs (verify locally, no DB hit). ≥256-bit secret. Flows via the
    /// <c>Auth__JwtSigningKey</c> env var — baked into <c>stack.env</c> as a
    /// Fallout <c>[Secret]</c> in prod, injected by the AppHost in local dev.
    /// Empty means "dev fallback key" (logged, dev-only — never ship empty).
    /// </summary>
    public string JwtSigningKey { get; set; } = "";

    /// <summary>JWT <c>iss</c> claim. Game APIs validate against this.</summary>
    public string JwtIssuer { get; set; } = "erp-for-factory.games/auth";

    /// <summary>JWT <c>aud</c> claim. Game APIs validate against this.</summary>
    public string JwtAudience { get; set; } = "erp-for-factory.games/agents";

    /// <summary>
    /// Token lifetime in days (ADR-0027 §1). Long-lived — agents re-pair on
    /// expiry. Default 365.
    /// </summary>
    public int JwtLifetimeDays { get; set; } = 365;

    /// <summary>
    /// Dev-only fallback signing key used when <see cref="JwtSigningKey"/> is
    /// blank, so a fresh checkout + the test suite work without configuration.
    /// 256 bits of fixed bytes — NOT a secret, never used in prod (prod sets
    /// JwtSigningKey via stack.env). Mirrors the DevPlayerId dev-shortcut.
    /// </summary>
    public const string DevFallbackSigningKey =
        "erp-for-factory-games-dev-only-hs256-signing-key-do-not-ship-0001";

    /// <summary>The effective key: configured value, or the dev fallback.</summary>
    public string EffectiveJwtSigningKey =>
        string.IsNullOrWhiteSpace(JwtSigningKey) ? DevFallbackSigningKey : JwtSigningKey;
}
