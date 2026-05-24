using Microsoft.Extensions.Options;

namespace ApiService;

/// <summary>
/// Filesystem-backed <see cref="ICatalogueStorage"/>. Stores each blob
/// at <c>{Root}/{playerId}/{game}/{hash}.json</c>. Atomic writes via
/// temp + rename so a crash mid-upload can't leave a half-written file.
/// </summary>
internal sealed class FileSystemCatalogueStorage : ICatalogueStorage
{
    private readonly string _root;

    public FileSystemCatalogueStorage(IOptions<CatalogueStorageOptions> options)
    {
        var root = options.Value.Root;
        if (string.IsNullOrWhiteSpace(root))
        {
            var baseDir = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            root = Path.Combine(baseDir, "ErpForFactoryGames", "catalogues");
        }
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreAsync(Guid playerId, string game, string docsHash, byte[] bytes, CancellationToken ct = default)
    {
        var key = $"{playerId}/{game}/{docsHash}.json";
        var fullPath = ResolveFullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath))
        {
            // Idempotent — identical hash already on disk, nothing to do.
            return key;
        }

        var tmp = fullPath + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
        File.Move(tmp, fullPath, overwrite: true);
        return key;
    }

    public async Task<byte[]?> ReadAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolveFullPath(storageKey);
        if (!File.Exists(fullPath)) return null;
        return await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
    }

    private string ResolveFullPath(string storageKey)
    {
        // Defence against path traversal — the key shape is fixed by
        // StoreAsync but ReadAsync could be called with arbitrary input.
        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key must not contain parent-directory segments.", nameof(storageKey));
        }
        return Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar));
    }
}
