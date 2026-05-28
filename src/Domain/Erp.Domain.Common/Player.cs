namespace Erp.Domain.Common;

/// <summary>
/// A planner user (ADR-0025 §1). v2 has no login — a <c>Player</c> is the
/// identity an agent token is bound to, created lazily by the Web UI or
/// seeded from <c>Auth:DevPlayerId</c> at startup. When real login lands
/// (a future ADR) this aggregate gains an <c>IdentityUserId</c> foreign
/// key; the rest of the shape stays.
///
/// <para>
/// Mutable class (not a record): the aggregate has a lifecycle (rename),
/// and EF Core tracks mutable entities more naturally — same call as
/// <see cref="SavedPlan"/> and <see cref="FactoryAlert"/>.
/// </para>
/// </summary>
public sealed class Player
{
    public PlayerId Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }

    /// <summary>
    /// Sticky flag set by <c>POST /players/{id}/re-ingest-catalogue</c>
    /// (ADR-0025 §7) and cleared by the next successful catalogue upload
    /// from any of this player's agents. The agent reads this on each
    /// log-tail poll; when true, it forces a re-upload regardless of its
    /// cached hash, so the user can manually nudge an out-of-sync agent
    /// without waiting for a Docs.json change.
    /// </summary>
    public bool ReIngestRequested { get; private set; }

    /// <summary>When <see cref="ReIngestRequested"/> was last set. Audit only.</summary>
    public DateTime? ReIngestRequestedUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private Player() { }

    public Player(PlayerId id, string displayName, DateTime createdUtc)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName is required.", nameof(displayName));

        Id = id;
        DisplayName = displayName;
        CreatedUtc = createdUtc;
    }

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName is required.", nameof(displayName));
        DisplayName = displayName;
    }

    /// <summary>Mark the player as wanting a fresh catalogue upload. Idempotent.</summary>
    public void RequestReIngest(DateTime nowUtc)
    {
        ReIngestRequested = true;
        ReIngestRequestedUtc = nowUtc;
    }

    /// <summary>Clear the flag — invoked when a catalogue upload for this
    /// player lands, regardless of which agent sent it.</summary>
    public void ClearReIngestRequest()
    {
        ReIngestRequested = false;
    }
}
