using Erp.Domain.Common;

namespace ApiService;

/// <summary>
/// Outcome of an <see cref="IAgentTokenAuthenticator.AuthenticateAsync"/>
/// call. <see cref="Success"/> carries the resolved player + token ids;
/// any non-success status returns 401 to the caller.
/// </summary>
public readonly record struct AgentAuthResult(
    AgentAuthStatus Status,
    PlayerId PlayerId,
    AgentTokenId TokenId)
{
    public static AgentAuthResult Ok(PlayerId playerId, AgentTokenId tokenId) =>
        new(AgentAuthStatus.Success, playerId, tokenId);

    public static AgentAuthResult MissingHeader() => new(AgentAuthStatus.MissingHeader, default, default);

    public static AgentAuthResult Unknown() => new(AgentAuthStatus.Unknown, default, default);

    public static AgentAuthResult Revoked() => new(AgentAuthStatus.Revoked, default, default);

    public bool IsAuthenticated => Status == AgentAuthStatus.Success;
}

public enum AgentAuthStatus
{
    Success,
    MissingHeader,
    Unknown,
    Revoked,
}
