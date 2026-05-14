using System.Reflection;
using System.Text.Json;
using ERP.Domain;

namespace Satisfactory.Save;

/// <summary>
/// Coordinate-keyed lookup of vanilla Satisfactory flora pickups (Bacon
/// Agaric, Paleberry, Beryl Nut, Mycelia). Unlike resource nodes, flora are
/// instanced foliage — not actors — so they're not present in the save body
/// at all unless picked up (in which case the save stores a "removed"
/// record). For v1 we treat vanilla flora positions as world-fixed and ship
/// a static dataset, matching the <see cref="KnownResourceNodes"/> approach.
///
/// See <see cref="LoadEmbedded"/> for the data source and Data/README.md
/// for the dataset format.
/// </summary>
public sealed class KnownFlora
{
    private readonly List<FloraEntry> _entries;

    public KnownFlora(IEnumerable<FloraEntry> entries)
    {
        _entries = entries.ToList();
    }

    /// <summary>Empty lookup — <see cref="All"/> returns no entries.</summary>
    public static KnownFlora Empty { get; } = new([]);

    /// <summary>
    /// Loads the embedded `Data/known-flora.json` (shipped as a manifest
    /// resource). Returns <see cref="Empty"/> if the resource is missing or
    /// the JSON is malformed — never throws at load time so a corrupted
    /// dataset can't break the app.
    /// </summary>
    public static KnownFlora LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("known-flora.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return Empty;

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return Empty;

            var entries = JsonSerializer.Deserialize<List<FloraEntry>>(stream, JsonOptions);
            return entries is null ? Empty : new KnownFlora(entries);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>All entries in the dataset (no filtering — small dataset).</summary>
    public IReadOnlyList<FloraEntry> All => _entries;

    /// <summary>Returns the count of entries in this dataset (for diagnostics).</summary>
    public int Count => _entries.Count;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>One entry in the known-flora dataset.</summary>
/// <param name="X">World X in centimetres.</param>
/// <param name="Y">World Y in centimetres.</param>
/// <param name="Z">World Z in centimetres.</param>
/// <param name="Species">ItemId of the harvested item — one of
/// <c>Desc_Berry_C</c> (Paleberry), <c>Desc_Nut_C</c> (Beryl Nut),
/// <c>Desc_Shroom_C</c> (Bacon Agaric), <c>Desc_Mycelia_C</c> (Mycelia).</param>
public sealed record FloraEntry(double X, double Y, double Z, string Species)
{
    /// <summary>Human-readable display name for the species ItemId. Falls back to the raw id.</summary>
    public string DisplayName => Species switch
    {
        "Desc_Berry_C" => "Paleberry",
        "Desc_Nut_C" => "Beryl Nut",
        "Desc_Shroom_C" => "Bacon Agaric",
        "Desc_Mycelia_C" => "Mycelia",
        _ => Species,
    };

    /// <summary>Convenience for callers that want a <see cref="Position"/>.</summary>
    public Position Position => new(X, Y, Z);
}
