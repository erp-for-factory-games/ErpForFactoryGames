namespace Erp.Presentation.Web.Auth;

/// <summary>
/// Pluggable sign-in backend for the auth landing (ADR-0028 §7). The landing
/// owns the UI + cookie session; the backend just answers "are these
/// credentials valid, and who is this?". Today the only implementation is
/// <see cref="HardcodedCredentialAuthBackend"/>; the Keycloak OIDC backend is
/// deferred to issue #292.
/// </summary>
public interface IAuthBackend
{
    /// <summary>Short name for logging/diagnostics (e.g. <c>hardcoded</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Validate a username/password. Returns a successful result carrying a
    /// stable subject id + display name, or <see cref="AuthBackendResult.Fail"/>.
    /// Implementations should be constant-time w.r.t. the secret.
    /// </summary>
    Task<AuthBackendResult> ValidateAsync(string username, string password, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IAuthBackend.ValidateAsync"/>.</summary>
public readonly record struct AuthBackendResult(bool Succeeded, string Subject, string DisplayName)
{
    public static AuthBackendResult Fail() => new(false, "", "");

    public static AuthBackendResult Ok(string subject, string displayName) =>
        new(true, subject, displayName);
}
