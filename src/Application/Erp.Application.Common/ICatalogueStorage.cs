namespace Erp.Application.Common;

/// <summary>
/// Filesystem-or-other blob store for uploaded catalogue bytes
/// (ADR-0025 §4-§5). Decoupled from the DB row so the storage backend
/// can swap (S3, Azure Blob, etc.) without touching the
/// <see cref="Erp.Domain.Common.PlayerCatalogue"/> aggregate.
/// </summary>
public interface ICatalogueStorage
{
    /// <summary>
    /// Persist <paramref name="bytes"/> under a key derived from
    /// <paramref name="playerId"/>, <paramref name="game"/>, and the
    /// hash of the bytes. Returns the storage key the caller should
    /// persist on the <see cref="Erp.Domain.Common.PlayerCatalogue"/> row.
    /// Idempotent on re-upload of identical bytes.
    /// </summary>
    Task<string> StoreAsync(Guid playerId, string game, string docsHash, byte[] bytes, CancellationToken ct = default);

    /// <summary>Read the bytes back. Returns null when the key isn't known.</summary>
    Task<byte[]?> ReadAsync(string storageKey, CancellationToken ct = default);
}
