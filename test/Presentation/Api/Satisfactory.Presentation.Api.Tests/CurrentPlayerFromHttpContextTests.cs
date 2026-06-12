using System.Security.Claims;
using Erp.Domain.Common;
using Erp.Presentation.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Satisfactory.Presentation.Api.Tests;

/// <summary>
/// ADR-0028 §1/§3 (#292) — the Keycloak-backed <see cref="ICurrentPlayer"/>.
/// Resolves the player id from the authenticated OIDC <c>sub</c>, and falls back
/// to the configured dev player when there's no authenticated user (background
/// jobs, anonymous internal calls) rather than throwing.
/// </summary>
public sealed class CurrentPlayerFromHttpContextTests
{
    private static readonly Guid DevPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CurrentPlayerFromHttpContext Build(HttpContext? context)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new AuthOptions { DevPlayerId = DevPlayerId });
        return new CurrentPlayerFromHttpContext(accessor, options);
    }

    private static HttpContext AuthenticatedWith(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestKeycloak");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void Authenticated_sub_resolves_to_that_player()
    {
        var sub = Guid.NewGuid();
        var sut = Build(AuthenticatedWith(new Claim("sub", sub.ToString())));

        Assert.Equal(new PlayerId(sub), sut.Id);
    }

    [Fact]
    public void Falls_back_to_dev_player_when_no_http_context()
    {
        var sut = Build(context: null);

        Assert.Equal(new PlayerId(DevPlayerId), sut.Id);
    }

    [Fact]
    public void Falls_back_to_dev_player_when_unauthenticated()
    {
        // A principal with no authenticated identity (IsAuthenticated == false).
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var sut = Build(context);

        Assert.Equal(new PlayerId(DevPlayerId), sut.Id);
    }

    [Fact]
    public void Falls_back_to_dev_player_when_sub_is_not_a_guid()
    {
        var sut = Build(AuthenticatedWith(new Claim("sub", "not-a-guid")));

        Assert.Equal(new PlayerId(DevPlayerId), sut.Id);
    }

    [Fact]
    public void TryGetSubject_accepts_NameIdentifier_when_inbound_mapping_left_on()
    {
        var sub = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, sub.ToString())], "TestKeycloak"));

        Assert.True(CurrentPlayerFromHttpContext.TryGetSubject(user, out var resolved));
        Assert.Equal(sub, resolved);
    }
}
