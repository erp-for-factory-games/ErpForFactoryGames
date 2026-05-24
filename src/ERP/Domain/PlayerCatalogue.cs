namespace ERP.Domain;

/// <summary>
/// The catalogue (game data dump) a <see cref="Player"/> has uploaded
/// from their machine via the agent (ADR-0025 §4-§5). One row per
/// <c>(PlayerId, Game)</c> — re-uploads overwrite, with the previous
/// <see cref="DocsHash"/> being what the agent uses to short-circuit
/// no-op uploads via <c>If-None-Match</c>.
///
/// <para>
/// The catalogue bytes themselves live outside the database (filesystem
/// blob today, possibly object storage later). <see cref="StorageKey"/>
/// is the opaque pointer the catalogue store understands; this row is
/// the metadata + dedup record.
/// </para>
/// </summary>
public sealed class PlayerCatalogue
{
    public const string SatisfactoryGame = "satisfactory";

    public PlayerId PlayerId { get; private set; }

    /// <summary>Game adapter key — currently always <see cref="SatisfactoryGame"/>.</summary>
    public string Game { get; private set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 of the uploaded bytes. The agent
    /// echoes this back as <c>If-None-Match</c> to skip identical re-uploads.</summary>
    public string DocsHash { get; private set; } = string.Empty;

    /// <summary>Game-version string parsed out of the catalogue bytes
    /// at upload time. Nullable until per-game parsing lands (Phase B
    /// of #238); the column exists so Phase B doesn't need a migration.</summary>
    public string? GameVersion { get; private set; }

    /// <summary>Opaque pointer the catalogue store knows how to resolve
    /// to the actual bytes. Currently a filesystem-relative path.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTime UploadedUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private PlayerCatalogue() { }

    public PlayerCatalogue(
        PlayerId playerId,
        string game,
        string docsHash,
        string storageKey,
        long sizeBytes,
        DateTime uploadedUtc,
        string? gameVersion = null)
    {
        if (playerId.Value == Guid.Empty) throw new ArgumentException("PlayerId must not be empty.", nameof(playerId));
        if (string.IsNullOrWhiteSpace(game)) throw new ArgumentException("Game is required.", nameof(game));
        if (string.IsNullOrWhiteSpace(docsHash)) throw new ArgumentException("DocsHash is required.", nameof(docsHash));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("StorageKey is required.", nameof(storageKey));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        PlayerId = playerId;
        Game = game;
        DocsHash = docsHash;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        UploadedUtc = uploadedUtc;
        GameVersion = gameVersion;
    }

    /// <summary>
    /// Replace this row's contents with a freshly uploaded catalogue.
    /// Idempotent at the caller — the upload endpoint checks
    /// <see cref="DocsHash"/> before invoking this.
    /// </summary>
    public void ReplaceWith(string docsHash, string storageKey, long sizeBytes, DateTime uploadedUtc, string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(docsHash)) throw new ArgumentException("DocsHash is required.", nameof(docsHash));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("StorageKey is required.", nameof(storageKey));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        DocsHash = docsHash;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        UploadedUtc = uploadedUtc;
        GameVersion = gameVersion;
    }
}
