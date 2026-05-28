namespace Erp.Application.Common;

/// <summary>
/// Hashing seam for agent token plaintexts (ADR-0025 §2).
///
/// <para>
/// v2 uses SHA-256 of the plaintext, no salt. Plaintext is 32 bytes of
/// CSPRNG output (256 bits of entropy), so offline cracking is infeasible
/// regardless of memory-hardness — same threat model GitHub PATs and
/// Stripe API keys use. See ADR-0025 §2 rationale.
/// </para>
///
/// <para>
/// <see cref="MintPlaintext"/> generates the human-visible token format —
/// <c>eafg_&lt;43 url-safe-base64 chars&gt;</c>. The <c>eafg_</c> prefix
/// is a leak-detection aid; bumping to <c>eafg2_</c> is the migration
/// knob for a future format change.
/// </para>
/// </summary>
public interface IAgentTokenHasher
{
    /// <summary>
    /// Mint a fresh plaintext token. Cryptographically random, URL-safe,
    /// prefixed with the version tag.
    /// </summary>
    string MintPlaintext();

    /// <summary>
    /// Compute the indexed hash of <paramref name="plaintext"/>.
    /// Deterministic given the same input — same plaintext always
    /// produces the same hash.
    /// </summary>
    byte[] Hash(string plaintext);
}
