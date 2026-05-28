namespace Erp.Domain.Common;

/// <summary>
/// A persisted factory bottleneck alert (#116). Written by the post-ingest
/// analysis pass when current factory state crosses a saturation / shortfall
/// threshold; surfaced to the user via <c>GET /factory/alerts</c> and read by
/// the ADA agent so she can lead with active alerts on her next turn.
///
/// <para>
/// Lifecycle: <c>Created</c> by the analysis pass → optionally <c>Refresh</c>ed
/// when a subsequent pass finds the same condition (same <see cref="Key"/>)
/// still active → either <c>Resolve</c>d (auto, by analysis once the condition
/// clears) or <c>Dismiss</c>ed (manual, via the API). <see cref="IsActive"/>
/// becomes false either way.
/// </para>
///
/// <para>
/// Mutable class (not a record) because the entity has a lifecycle and EF Core
/// tracks mutable entities more naturally. Matches the <see cref="PlanShareToken"/>
/// shape from #80.
/// </para>
/// </summary>
public sealed class FactoryAlert
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Stable dedup key. Same underlying condition produces the same key
    /// across analysis passes, so the pass can decide refresh-vs-create
    /// without trial-and-error. Typical shape: <c>"{severity}:{itemId}"</c>
    /// (e.g. <c>"blocker:Desc_OreIron_C"</c>). Not unique on its own — a
    /// dismissed alert and a fresh alert can share a key if the condition
    /// recurs.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    public AlertSeverity Severity { get; private set; }

    /// <summary>
    /// What context produced this alert. Free-form. Examples:
    /// <c>"save:Beta Game_autosave_1.sav"</c>, <c>"plan:Iron Plates 60/min"</c>.
    /// </summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>One-line title shown by ADA above the structured block.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Numbers + what's saturated. Multi-line OK.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>What to build / change to clear the alert.</summary>
    public string Fix { get; private set; } = string.Empty;

    public DateTime CreatedUtc { get; private set; }

    /// <summary>Set by the next analysis pass once the underlying condition no longer holds.</summary>
    public DateTime? ResolvedUtc { get; private set; }

    /// <summary>Set when the user manually dismisses the alert. Independent of <see cref="ResolvedUtc"/>.</summary>
    public DateTime? DismissedUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private FactoryAlert() { }

    public FactoryAlert(
        Guid id,
        string key,
        AlertSeverity severity,
        string source,
        string title,
        string detail,
        string fix,
        DateTime createdUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));

        Id = id;
        Key = key;
        Severity = severity;
        Source = source;
        Title = title;
        Detail = detail ?? string.Empty;
        Fix = fix ?? string.Empty;
        CreatedUtc = createdUtc;
    }

    /// <summary>True iff neither <see cref="ResolvedUtc"/> nor <see cref="DismissedUtc"/> is set.</summary>
    public bool IsActive => ResolvedUtc is null && DismissedUtc is null;

    /// <summary>
    /// Update the human-readable fields when a subsequent analysis pass finds
    /// the same condition still active (with possibly different numbers).
    /// Does NOT clear <see cref="ResolvedUtc"/> or <see cref="DismissedUtc"/>
    /// — refreshing data must not un-dismiss a user's explicit choice.
    /// </summary>
    public void Refresh(string title, string detail, string fix)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        Title = title;
        Detail = detail ?? string.Empty;
        Fix = fix ?? string.Empty;
    }

    /// <summary>Mark the alert resolved (condition no longer holds). Idempotent.</summary>
    public void Resolve(DateTime nowUtc)
    {
        ResolvedUtc ??= nowUtc;
    }

    /// <summary>Mark the alert dismissed (user said "ignore"). Idempotent.</summary>
    public void Dismiss(DateTime nowUtc)
    {
        DismissedUtc ??= nowUtc;
    }
}
