namespace Erp.Domain.Common;

public sealed record Miner(
    string Reference,
    MinerTier Tier,
    Position Position,
    string? ResourceNodeReference);
