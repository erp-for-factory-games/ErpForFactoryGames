namespace ERP.Domain;

/// <summary>
/// Strongly-typed identifier for a <see cref="Player"/> aggregate (ADR-0025 §1).
/// Wraps a <see cref="Guid"/> so a <c>PlayerId</c> can't be accidentally passed
/// where an <c>AgentTokenId</c> is expected.
/// </summary>
public readonly record struct PlayerId(Guid Value)
{
    public static PlayerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
