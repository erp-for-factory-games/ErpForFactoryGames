using ERP.Application;
using ERP.Domain;
using Microsoft.Extensions.Options;

namespace ApiService;

/// <summary>
/// v2 <see cref="ICurrentPlayer"/> adapter: returns the dev player from
/// <see cref="AuthOptions.DevPlayerId"/>. The future authenticated
/// adapter reads from <c>HttpContext.User</c>; swapping it out is a
/// single-line DI change.
/// </summary>
public sealed class CurrentPlayerFromAuthOptions : ICurrentPlayer
{
    private readonly AuthOptions _auth;

    public CurrentPlayerFromAuthOptions(IOptions<AuthOptions> auth) => _auth = auth.Value;

    public PlayerId Id => new(_auth.DevPlayerId);
}
