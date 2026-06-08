using System.Collections.Concurrent;

namespace Satisfactory.Presentation.Web.Auth;

/// <summary>
/// Server-side store of the Keycloak access token per signed-in user, keyed by
/// the OIDC <c>sub</c> (ADR-0028 §3, #292). Populated in the OIDC
/// <c>OnTokenValidated</c> event — the login callback, where the
/// <c>HttpContext</c> (and therefore the freshly-issued token) is guaranteed.
///
/// <para>This sidesteps the interactive-Blazor-Server gotcha where
/// <c>IHttpContextAccessor</c> is null inside the SignalR circuit: the token is
/// captured once at login and looked up later from the circuit via
/// <see cref="UserAccessTokenAccessor"/>. Tokens stay server-side and are never
/// serialized to the browser.</para>
///
/// <para>In-memory only — adequate for the local-dev core slice (single host).
/// A multi-replica prod deployment + token refresh is a follow-up.</para>
/// </summary>
public sealed class ServerTokenStore
{
    private readonly ConcurrentDictionary<string, string> _accessTokens = new(StringComparer.Ordinal);

    public void Set(string subject, string accessToken) => _accessTokens[subject] = accessToken;

    public string? Get(string subject) =>
        _accessTokens.TryGetValue(subject, out var token) ? token : null;

    public void Remove(string subject) => _accessTokens.TryRemove(subject, out _);
}
