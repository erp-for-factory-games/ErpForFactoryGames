namespace ERP.Domain;

/// <summary>
/// Strongly-typed identifier for an <see cref="AgentToken"/> row (ADR-0025 §2).
/// </summary>
public readonly record struct AgentTokenId(Guid Value)
{
    public static AgentTokenId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
