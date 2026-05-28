namespace Erp.Domain.Common;

/// <summary>
/// Severity tier for an in-game factory alert (#116). Mirrors the
/// vocabulary used by the ADA agent (see <c>.claude/agents/ada.md</c>).
/// Numeric ordering is intentional: higher value = more urgent, so
/// callers can sort by severity descending without bespoke comparators.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Fine today, will break at the next phase / scale-up.</summary>
    Risk = 0,

    /// <summary>Runs but underclocked, capped, or wasting capacity.</summary>
    Degraded = 1,

    /// <summary>The build doesn't work / a downstream module is starved.</summary>
    Blocker = 2,
}
