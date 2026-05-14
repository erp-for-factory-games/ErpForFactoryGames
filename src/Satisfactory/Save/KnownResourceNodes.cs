using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Domain;

namespace Satisfactory.Save;

/// <summary>
/// Coordinate-keyed lookup of resource type + purity for the static
/// <c>BP_ResourceNode_C</c> actors that Coffee Stain placed in the world.
/// Those values are blueprint-class defaults, not instance-serialized into
/// the save, so the parser can't read them directly. We ship a curated
/// dataset and resolve each node by nearest-neighbour position match.
///
/// See <see cref="LoadEmbedded"/> for the data source. See
/// <c>vendor/SatisfactorySaveNet/TODO.md</c> item 7 for the long-term plan
/// to bundle this upstream.
/// </summary>
public sealed class KnownResourceNodes
{
    private readonly List<KnownNode> _nodes;

    public KnownResourceNodes(IEnumerable<KnownNode> nodes)
    {
        _nodes = nodes.ToList();
    }

    /// <summary>Empty lookup — every call to <see cref="Lookup"/> returns null.</summary>
    public static KnownResourceNodes Empty { get; } = new([]);

    /// <summary>
    /// Loads the embedded `Data/known-resource-nodes.json` (shipped as a
    /// manifest resource). Returns <see cref="Empty"/> if the resource is
    /// missing or the JSON is malformed — we never throw at load time.
    /// </summary>
    public static KnownResourceNodes LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("known-resource-nodes.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return Empty;

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return Empty;

            var nodes = JsonSerializer.Deserialize<List<KnownNode>>(stream, JsonOptions);
            return nodes is null ? Empty : new KnownResourceNodes(nodes);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Returns the closest known node within <paramref name="tolerance"/>
    /// world units (centimetres) of <paramref name="position"/>, or null if
    /// nothing is in range. Linear scan — fine for the ~600-entry dataset.
    /// </summary>
    public KnownNode? Lookup(Position position, double tolerance = 500.0)
    {
        var maxDistSquared = tolerance * tolerance;
        KnownNode? best = null;
        var bestDistSquared = maxDistSquared;

        foreach (var node in _nodes)
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

    /// <summary>Returns the count of entries in this dataset (mostly for diagnostics).</summary>
    public int Count => _nodes.Count;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // NodePurity is an enum but the JSON ships values as strings
        // ("Impure" / "Normal" / "Pure") for readability. Without this
        // converter the deserialiser only accepts the integer form.
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>One entry in the known-resource-nodes dataset.</summary>
public sealed record KnownNode(
    double X,
    double Y,
    double Z,
    string Resource,
    NodePurity Purity);
