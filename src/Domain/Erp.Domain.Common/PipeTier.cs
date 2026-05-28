namespace Erp.Domain.Common;

/// <summary>
/// In-game pipe tier recommendation for a fluid line (#90).
///
/// <para>
/// Throughput caps:
/// <list type="bullet">
///   <item><c>Mk1</c>: ≤ 300 m³/min</item>
///   <item><c>Mk2</c>: ≤ 600 m³/min</item>
///   <item><c>OverMk2</c>: &gt; 600 m³/min — no single pipe can carry it; the
///     user has to split the line across multiple pipes. The recommendation is
///     informational rather than a solver constraint.</item>
/// </list>
/// </para>
/// </summary>
public enum PipeTier
{
    Mk1 = 1,
    Mk2 = 2,
    OverMk2 = 3,
}
