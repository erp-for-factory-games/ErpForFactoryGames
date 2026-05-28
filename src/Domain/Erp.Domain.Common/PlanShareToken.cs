namespace Erp.Domain.Common;

/// <summary>
/// Opaque, URL-safe token that grants read-only access to a single
/// <see cref="SavedPlan"/>. Persisted independently of the plan (separate table
/// + repository port) so that sharing concerns stay out of the plan aggregate.
///
/// <para>
/// A token is "active" when <see cref="RevokedAt"/> is <c>null</c> and either
/// <see cref="ExpiresAt"/> is <c>null</c> or in the future. Inactive tokens
/// resolve to 404 on the public read-only endpoint — they are kept in the
/// table for auditability rather than hard-deleted.
/// </para>
/// </summary>
public sealed class PlanShareToken
{
    public string Token { get; private set; } = string.Empty;
    public Guid PlanId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public DateTime? ExpiresUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private PlanShareToken() { }

    public PlanShareToken(string token, Guid planId, DateTime createdUtc, DateTime? expiresUtc = null)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.", nameof(token));
        if (planId == Guid.Empty) throw new ArgumentException("PlanId must not be empty.", nameof(planId));

        Token = token;
        PlanId = planId;
        CreatedUtc = createdUtc;
        ExpiresUtc = expiresUtc;
    }

    /// <summary>Returns <c>true</c> if the token can currently be redeemed.</summary>
    public bool IsActive(DateTime nowUtc) =>
        RevokedUtc is null && (ExpiresUtc is null || ExpiresUtc > nowUtc);

    /// <summary>Marks the token as revoked. Idempotent — calling twice keeps the first revocation time.</summary>
    public void Revoke(DateTime nowUtc)
    {
        RevokedUtc ??= nowUtc;
    }
}
