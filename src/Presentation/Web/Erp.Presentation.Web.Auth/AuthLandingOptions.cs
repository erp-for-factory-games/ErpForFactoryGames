namespace Erp.Presentation.Web.Auth;

/// <summary>
/// Configuration for the auth landing's pluggable backend (ADR-0028 §7/§8).
/// Bound from the <c>Auth</c> section.
/// </summary>
public sealed class AuthLandingOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Which <see cref="IAuthBackend"/> backs sign-in:
    /// <list type="bullet">
    ///   <item><c>hardcoded</c> — a single configured username/password. The
    ///         default; lets the app run with zero IdP infrastructure.</item>
    ///   <item><c>keycloak</c> — OIDC against Keycloak. Deferred (issue #292);
    ///         selecting it today throws at startup with a pointer.</item>
    /// </list>
    /// </summary>
    public string Backend { get; set; } = "hardcoded";

    /// <summary>Credentials for the <c>hardcoded</c> backend.</summary>
    public HardcodedCredentials Hardcoded { get; set; } = new();

    public sealed class HardcodedCredentials
    {
        public string Username { get; set; } = "admin";

        /// <summary>
        /// Dev default so a fresh checkout can sign in. Override in production
        /// (or switch <see cref="Backend"/> to <c>keycloak</c> once #292 lands).
        /// </summary>
        public string Password { get; set; } = "erp-dev";

        public string DisplayName { get; set; } = "Local User";
    }
}
