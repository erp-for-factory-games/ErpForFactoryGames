using System.Text.Json;
using Erp.Domain.Common;

namespace Satisfactory.Infrastructure;

/// <summary>
/// User-curated overrides for <c>BP_ResourceNode_C</c> resource + purity,
/// resolved by nearest-neighbour position match (same lookup shape as
/// <see cref="KnownResourceNodes"/>).
///
/// Persisted to a JSON file outside the repo so the dataset is user-local:
/// the path is resolved by <see cref="ManualNodeOverridesPath.Resolve"/>,
/// defaulting to <c>%LOCALAPPDATA%\ERP.Satisfactory\manual-node-overrides.json</c>.
///
/// Consulted by <see cref="SaveFileReader"/> before the bundled dataset, so
/// any user override wins. Issue #42 (Option B).
/// </summary>
public sealed class ManualNodeOverrides
{
    private readonly string _filePath;
    private readonly List<KnownNode> _entries;
    private readonly object _writeLock = new();

    public ManualNodeOverrides(string filePath, IEnumerable<KnownNode> entries)
    {
        _filePath = filePath;
        _entries = entries.ToList();
    }

    /// <summary>In-memory only, no persistence. Useful for tests.</summary>
    public static ManualNodeOverrides Empty { get; } = new(string.Empty, []);

    /// <summary>
    /// Loads overrides from the JSON file at <paramref name="filePath"/>.
    /// Returns an empty instance pointing at the same path when the file
    /// is missing or malformed — Upsert will create it on first write.
    /// </summary>
    public static ManualNodeOverrides LoadOrCreate(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return new ManualNodeOverrides(filePath, []);

        try
        {
            using var stream = File.OpenRead(filePath);
            var entries = JsonSerializer.Deserialize<List<KnownNode>>(stream, JsonOptions);
            return new ManualNodeOverrides(filePath, entries ?? []);
        }
        catch (JsonException)
        {
            return new ManualNodeOverrides(filePath, []);
        }
        catch (IOException)
        {
            return new ManualNodeOverrides(filePath, []);
        }
    }

    /// <summary>Same shape as <see cref="KnownResourceNodes.Lookup"/>.</summary>
    public KnownNode? Lookup(Position position, double tolerance = 500.0)
    {
        var maxDistSquared = tolerance * tolerance;
        KnownNode? best = null;
        var bestDistSquared = maxDistSquared;
        foreach (var node in _entries)
        {
            var dx = node.X - position.X;
            var dy = node.Y - position.Y;
            var dz = node.Z - position.Z;
            var distSquared = (dx * dx) + (dy * dy) + (dz * dz);
            if (distSquared < bestDistSquared)
            {
                best = node;
                bestDistSquared = distSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// Adds or replaces the override at <paramref name="position"/>. If an
    /// existing entry lies within <paramref name="tolerance"/> world units,
    /// it's replaced; otherwise a new entry is added. Atomic write to disk.
    /// </summary>
    public void Upsert(Position position, string resource, NodePurity purity, double tolerance = 500.0)
    {
        if (string.IsNullOrEmpty(_filePath))
            throw new InvalidOperationException("ManualNodeOverrides has no file path — can't persist.");

        lock (_writeLock)
        {
            var existing = Lookup(position, tolerance);
            if (existing is not null)
            {
                _entries.Remove(existing);
            }
            _entries.Add(new KnownNode(position.X, position.Y, position.Z, resource, purity));
            PersistUnlocked();
        }
    }

    /// <summary>
    /// Removes the override at <paramref name="position"/> if one exists
    /// within <paramref name="tolerance"/>. No-op otherwise. Atomic write.
    /// </summary>
    public bool Delete(Position position, double tolerance = 500.0)
    {
        if (string.IsNullOrEmpty(_filePath))
            throw new InvalidOperationException("ManualNodeOverrides has no file path — can't persist.");

        lock (_writeLock)
        {
            var existing = Lookup(position, tolerance);
            if (existing is null) return false;
            _entries.Remove(existing);
            PersistUnlocked();
            return true;
        }
    }

    public int Count => _entries.Count;
    public IReadOnlyList<KnownNode> Entries => _entries;
    public string FilePath => _filePath;

    private void PersistUnlocked()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, _entries, JsonWriteOptions);
        }
        // Move is atomic on the same volume; replaces existing file on Windows.
        File.Move(tmp, _filePath, overwrite: true);
    }

    // Both read and write paths need the string-enum converter so NodePurity
    // round-trips as "Pure" / "Normal" / "Impure" rather than its underlying int.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}

/// <summary>
/// Resolves the on-disk path for <see cref="ManualNodeOverrides"/>. Mirrors
/// the catalogue / save path pattern from ADR-0011: env var → config →
/// default under <c>%LOCALAPPDATA%</c>.
/// </summary>
public static class ManualNodeOverridesPath
{
    public const string EnvVar = "ERP_SATISFACTORY_OVERRIDES_PATH";
    public const string DefaultRelativePath = @"ERP.Satisfactory\manual-node-overrides.json";

    public static string Resolve(string? configuredPath = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        if (!string.IsNullOrWhiteSpace(configuredPath)) return configuredPath;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, DefaultRelativePath);
    }
}
