using System.Security.Claims;
using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Keycloak-backed <see cref="ICurrentPlayer"/> (ADR-0028 §1/§3): resolves the
/// player id from the authenticated OIDC <c>sub</c> claim on the current
/// request. Keycloak subjects are UUIDs, so the <c>sub</c> keys the
/// <see cref="Player"/> row directly.
///
/// <para>When there is no authenticated user (a background job with no
/// <see cref="HttpContext"/>, or an anonymous internal call) it falls back to
/// <see cref="AuthOptions.DevPlayerId"/> rather than throwing — user-facing
/// endpoints always carry a forwarded token, while non-user paths
/// (auto-ingest, health) keep working. Replaces
/// <see cref="CurrentPlayerFromAuthOptions"/> when <c>Auth:Backend=keycloak</c>.</para>
/// </summary>
public sealed class CurrentPlayerFromHttpContext : ICurrentPlayer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthOptions _auth;

    public CurrentPlayerFromHttpContext(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> auth)
    {
        _httpContextAccessor = httpContextAccessor;
        _auth = auth.Value;
    }

    public PlayerId Id
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true && TryGetSubject(user, out var subjectId))
            {
                return new PlayerId(subjectId);
            }

            // No authenticated user on this request — fall back to the dev player
            // so background work + anonymous internal calls don't fault.
            return new PlayerId(_auth.DevPlayerId);
        }
    }

    /// <summary>
    /// Pulls the OIDC subject (the Keycloak user id) from the principal. We bind
    /// JWT bearer with <c>MapInboundClaims=false</c>, so the claim is the raw
    /// <c>sub</c>; we also accept <see cref="ClaimTypes.NameIdentifier"/> in case
    /// inbound mapping is left on.
    /// </summary>
    internal static bool TryGetSubject(ClaimsPrincipal user, out Guid subjectId)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out subjectId);
    }
}
