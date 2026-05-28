using System.Collections.Concurrent;
using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiService;

/// <summary>
/// In-memory cached <see cref="IAgentTokenAuthenticator"/> (ADR-0025 §3).
///
/// <para>
/// The hash function is deterministic (SHA-256, no salt — see
/// <see cref="IAgentTokenHasher"/>), so the auth pipeline is:
/// </para>
/// <list type="number">
///   <item>Hash the plaintext.</item>
///   <item>Look up the row by hash (indexed seek, <c>O(log N)</c>).</item>
///   <item>Cache the resolved <see cref="CachedToken"/> by hash so subsequent requests skip the DB.</item>
/// </list>
///
/// <para>
/// Cache TTL is sliding so a hot token stays resident; a quiet one falls
/// out naturally. <see cref="Invalidate"/> evicts on revoke.
/// </para>
///
/// <para>
/// <c>LastSeenUtc</c> writes are coalesced per
/// <see cref="AgentTokenAuthenticatorOptions.LastSeenDebounce"/> so
/// log-tail traffic doesn't hammer the DB.
/// </para>
/// </summary>
internal sealed class AgentTokenAuthenticator : IAgentTokenAuthenticator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentTokenHasher _hasher;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<AgentTokenAuthenticator> _logger;
    private readonly AgentTokenAuthenticatorOptions _options;

    // Maps AgentTokenId → cache-key so Invalidate can evict without scanning.
    // Cleared on cache eviction by a PostEvictionCallback.
    private readonly ConcurrentDictionary<AgentTokenId, string> _idIndex = new();

    // Most recent LastSeenUtc we persisted per token, for debouncing.
    private readonly ConcurrentDictionary<AgentTokenId, DateTime> _lastSeenMemo = new();

    public AgentTokenAuthenticator(
        IServiceScopeFactory scopeFactory,
        IAgentTokenHasher hasher,
        IMemoryCache cache,
        TimeProvider clock,
        ILogger<AgentTokenAuthenticator> logger,
        IOptions<AgentTokenAuthenticatorOptions> options)
    {
        _scopeFactory = scopeFactory;
        _hasher = hasher;
        _cache = cache;
        _clock = clock;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<AgentAuthResult> AuthenticateAsync(string? plaintextToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
        {
            return AgentAuthResult.MissingHeader();
        }

        var hash = _hasher.Hash(plaintextToken);
        var cacheKey = "agent-token:" + Convert.ToHexString(hash);

        if (_cache.TryGetValue<CachedToken>(cacheKey, out var cached) && cached is not null)
        {
            return await OnAuthenticatedAsync(cached, cancellationToken).ConfigureAwait(false);
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAgentTokenRepository>();
        var row = await repo.GetByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return AgentAuthResult.Unknown();
        }
        if (!row.IsActive)
        {
            return AgentAuthResult.Revoked();
        }

        var entry = new CachedToken(row.Id, row.PlayerId);
        _cache.Set(cacheKey, entry, new MemoryCacheEntryOptions
        {
            SlidingExpiration = _options.CacheTtl,
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (_, _, _, _) => _idIndex.TryRemove(row.Id, out _),
                },
            },
        });
        _idIndex[row.Id] = cacheKey;
        return await OnAuthenticatedAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public void Invalidate(AgentTokenId tokenId)
    {
        if (_idIndex.TryRemove(tokenId, out var key))
        {
            _cache.Remove(key);
        }
        _lastSeenMemo.TryRemove(tokenId, out _);
    }

    private Task<AgentAuthResult> OnAuthenticatedAsync(CachedToken cached, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var shouldWrite = !_lastSeenMemo.TryGetValue(cached.TokenId, out var previous)
            || (now - previous) >= _options.LastSeenDebounce;

        if (shouldWrite)
        {
            _lastSeenMemo[cached.TokenId] = now;
            // Fire-and-forget the DB bump so request latency is never
            // gated on it. Failures are logged but never fail the auth —
            // the token was valid, the bump is best-effort metadata.
            _ = Task.Run(() => BumpLastSeenAsync(cached.TokenId, now), cancellationToken);
        }

        return Task.FromResult(AgentAuthResult.Ok(cached.PlayerId, cached.TokenId));
    }

    private async Task BumpLastSeenAsync(AgentTokenId tokenId, DateTime now)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAgentTokenRepository>();
            var token = await repo.GetAsync(tokenId, CancellationToken.None).ConfigureAwait(false);
            if (token is null || !token.IsActive) return;
            token.Touch(now);
            await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bump LastSeenUtc for {TokenId}", tokenId);
        }
    }

    private sealed record CachedToken(AgentTokenId TokenId, PlayerId PlayerId);
}
