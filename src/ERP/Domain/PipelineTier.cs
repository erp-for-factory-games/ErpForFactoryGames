namespace ERP.Domain;

/// <summary>
/// Fluid pipeline tier. Satisfactory ships two tiers; Mk2 raises throughput
/// from 300 m³/min to 600 m³/min but uses the same domain shape.
/// </summary>
public enum PipelineTier
{
    Mk1 = 1,
    Mk2 = 2,
}
