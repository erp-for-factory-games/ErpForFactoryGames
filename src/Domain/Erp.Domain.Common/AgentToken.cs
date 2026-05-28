namespace Erp.Domain.Common;

/// <summary>
/// Opaque secret bound to one <c>(Player, AgentInstall)</c> pair (ADR-0025 §2).
/// One <see cref="Player"/> can hold many tokens — one per machine they install
/// the agent on. The plaintext (<c>eafg_&lt;43 url-safe-base64 chars&gt;</c>) is
/// surfaced once at mint time and never stored; only the SHA-256 hash is
/// persisted.
///
/// <para>
/// <b>Hashing:</b> SHA-256 of the plaintext bytes, no salt. The plaintext is
/// 32 bytes of CSPRNG output (256 bits of entropy) — high enough that
/// rainbow tables and offline cracking are infeasible regardless of
/// memory-hardness, so per-token salts add nothing and only complicate the
/// indexed-lookup hot path. Same pattern GitHub Personal Access Tokens and
/// Stripe API keys use. See the rationale paragraph in ADR-0025 §2.
/// </para>
///
/// <para>
/// Revocation is soft (<see cref="RevokedUtc"/> is set) so audit history
/// survives; auth middleware treats <see cref="IsActive"/> as the gate.
/// </para>
/// </summary>
public sealed class AgentToken
{
    public AgentTokenId Id { get; private set; }
    public PlayerId PlayerId { get; private set; }
    public string Label { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the plaintext token. 32 bytes. Never the plaintext.
    /// Indexed in the EF mapping; this is the auth-pipeline hot path.
    /// </summary>
    public byte[] TokenHash { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedUtc { get; private set; }
    public DateTime? LastSeenUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private AgentToken() { }

    public AgentToken(
        AgentTokenId id,
        PlayerId playerId,
        string label,
        byte[] tokenHash,
        DateTime createdUtc)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (playerId.Value == Guid.Empty) throw new ArgumentException("PlayerId must not be empty.", nameof(playerId));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Label is required.", nameof(label));
        if (tokenHash is null || tokenHash.Length == 0) throw new ArgumentException("TokenHash is required.", nameof(tokenHash));

        Id = id;
        PlayerId = playerId;
        Label = label;
        TokenHash = tokenHash;
        CreatedUtc = createdUtc;
    }

    public bool IsActive => RevokedUtc is null;

    /// <summary>
    /// Bump the last-seen timestamp. Caller debounces — the auth pipeline
    /// only persists if the new value is more than the configured coalesce
    /// window past the previous value, to keep log-tail traffic off the DB
    /// hot path (ADR-0025 §3).
    /// </summary>
    public void Touch(DateTime nowUtc)
    {
        LastSeenUtc = nowUtc;
    }

    /// <summary>Mark the token revoked. Idempotent.</summary>
    public void Revoke(DateTime nowUtc)
    {
        RevokedUtc ??= nowUtc;
    }
}
