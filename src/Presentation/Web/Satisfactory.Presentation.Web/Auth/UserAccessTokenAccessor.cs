using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Satisfactory.Presentation.Web.Auth;

/// <summary>
/// Circuit-scoped accessor that supplies the current user's Keycloak bearer for
/// outgoing API calls (ADR-0028 §3, #292). It resolves the signed-in user's
/// <c>sub</c> via <see cref="AuthenticationStateProvider"/> — which works inside
/// the interactive circuit, unlike <c>IHttpContextAccessor</c> — and looks the
/// access token up in the server-side <see cref="ServerTokenStore"/>.
///
/// <para><see cref="AuthenticationStateProvider"/> is resolved lazily/optionally:
/// when <c>Auth:Backend=dev</c> there is no auth and the accessor simply yields
/// no token, so the typed API clients send unauthenticated requests exactly as
/// before.</para>
/// </summary>
public sealed class UserAccessTokenAccessor
{
    private readonly IServiceProvider _services;
    private readonly ServerTokenStore _store;

    private bool _resolved;
    private AuthenticationHeaderValue? _bearer;

    public UserAccessTokenAccessor(IServiceProvider services, ServerTokenStore store)
    {
        _services = services;
        _store = store;
    }

    /// <summary>
    /// Applies the current user's bearer token to <paramref name="httpClient"/>
    /// if one is available. Idempotent per accessor (i.e. per circuit): the
    /// lookup happens once and is cached for the circuit's lifetime.
    /// </summary>
    public async ValueTask ApplyAsync(HttpClient httpClient)
    {
        var bearer = await ResolveAsync().ConfigureAwait(false);
        if (bearer is not null)
        {
            httpClient.DefaultRequestHeaders.Authorization = bearer;
        }
    }

    private async ValueTask<AuthenticationHeaderValue?> ResolveAsync()
    {
        if (_resolved)
        {
            return _bearer;
        }
        _resolved = true;

        // Optional: absent under the dev backend (no authentication wired).
        if (_services.GetService(typeof(AuthenticationStateProvider)) is not AuthenticationStateProvider authProvider)
        {
            return _bearer = null;
        }

        ClaimsPrincipal user;
        try
        {
            var state = await authProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
            user = state.User;
        }
        catch
        {
            return _bearer = null;
        }

        if (user.Identity?.IsAuthenticated != true)
        {
            return _bearer = null;
        }

        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            return _bearer = null;
        }

        var token = _store.Get(sub);
        return _bearer = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }
}
