using ERP.Domain;
using SatisfactorySaveNet;
using SatisfactorySaveNet.Abstracts.Extra;
using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;

namespace Satisfactory.Save;

/// <summary>
/// Parses a Satisfactory <c>.sav</c> file via the patched
/// <c>SatisfactorySaveNet</c> fork and projects the result into game-agnostic
/// domain entities.
/// </summary>
public sealed class SaveFileReader
{
    private readonly KnownResourceNodes _knownNodes;
    private readonly ManualNodeOverrides _overrides;

    public SaveFileReader() : this(KnownResourceNodes.LoadEmbedded(), ManualNodeOverrides.Empty) { }

    public SaveFileReader(KnownResourceNodes knownNodes, ManualNodeOverrides overrides)
    {
        _knownNodes = knownNodes;
        _overrides = overrides;
    }

    public LiveFactoryState Read(string savePath)
    {
        var save = SaveFileSerializer.Instance.Deserialize(savePath);

        var metadata = new SaveMetadata(
            SessionName: save.Header.SessionName ?? "(unknown)",
            SaveVersion: save.Header.SaveVersion,
            BuildVersion: save.Header.BuildVersion,
            PlayedTime: TimeSpan.FromSeconds(save.Header.PlayedSeconds),
            SaveDateTimeUtc: save.Header.SaveDateTimeUtc);

        if (save.Body is not BodyV8 body)
        {
            return new LiveFactoryState(
                metadata, [], [], [], [], [], [],
                [$"Unsupported body version: {save.Body?.GetType().Name ?? "null"}. v1.2 saves are expected to be BodyV8."]);
        }

        var resourceNodes = new List<ResourceNode>();
        var miners = new List<Miner>();
        var buildings = new List<ProductionBuilding>();
        var belts = new List<ConveyorBelt>();
        var pipelines = new List<Pipeline>();
        var generators = new List<PowerGenerator>();
        var warnings = new List<string>();

        // Belt spline data lives on FGConveyorChainActor objects, not on the
        // individual Build_ConveyorBeltMk* actors — Coffee Stain consolidated
        // chain routing into a single per-chain actor. Build the lookup first
        // (single pass over Levels), then populate Polyline on each belt below.
        var beltSplines = BuildBeltSplineLookup(body);

        foreach (var obj in body.Levels.SelectMany(l => l.Objects))
        {
            if (obj is not ActorObject actor) continue;

            var typePath = actor.TypePath ?? string.Empty;
            var reference = actor.ObjectReference?.PathName ?? string.Empty;
            var position = new Position(actor.Position.X, actor.Position.Y, actor.Position.Z);

            if (BuildingIdentifiers.MinerTier(typePath) is { } tier)
            {
                // `mExtractableResource` (not `mExtractResourceNode`) is the live binding
                // from the miner to the resource node it's pulling from.
                var nodeRef = actor.TryGetObjectPath("mExtractableResource");
                miners.Add(new Miner(reference, tier, position, ResourceNodeReference: nodeRef));
            }
            else if (BuildingIdentifiers.IsProductionBuilding(typePath))
            {
                var buildingId = new BuildingId(BuildingIdentifiers.ShortName(typePath));
                var recipePath = actor.TryGetObjectPath("mCurrentRecipe");
                var recipeId = PropertyExtensions.ShortClassName(recipePath) is { Length: > 0 } recipeShort
                    ? new RecipeId(recipeShort)
                    : (RecipeId?)null;
                // `mCurrentPotential` only appears in the save when overclocked away
                // from default 1.0 — default-clock machines have no value serialized.
                var clock = actor.TryGetFloat("mCurrentPotential");
                buildings.Add(new ProductionBuilding(
                    reference, buildingId, position,
                    Recipe: recipeId,
                    ClockSpeed: clock is null ? 1.0m : (decimal)clock.Value));
            }
            else if (BuildingIdentifiers.BeltTier(typePath) is { } beltTier)
            {
                var polyline = beltSplines.GetValueOrDefault(reference);
                belts.Add(new ConveyorBelt(reference, beltTier, position, polyline));
            }
            else if (BuildingIdentifiers.PipelineTier(typePath) is { } pipeTier)
            {
                var polyline = ExtractPipePolyline(actor);
                pipelines.Add(new Pipeline(reference, pipeTier, position, polyline));
            }
            else if (BuildingIdentifiers.GeneratorKind(typePath) is { } genKind)
            {
                generators.Add(new PowerGenerator(reference, genKind, position));
            }
            else if (BuildingIdentifiers.IsResourceNode(typePath))
            {
                resourceNodes.Add(BuildResourceNode(typePath, reference, position));
            }
        }

        return new LiveFactoryState(metadata, resourceNodes, miners, buildings, belts, pipelines, generators, warnings);
    }

    /// <summary>
    /// Walks every FGConveyorChainActor in the save body and indexes each
    /// chain's per-belt spline points by the belt's PathName. The chain
    /// actor's ConveyorActors collection holds one entry per belt segment
    /// in the chain — each entry carries the belt's ObjectReference plus
    /// its Splines (Location/ArriveTangent/LeaveTangent). We keep only
    /// Location here; tangents are for smooth-curve interpolation that the
    /// 2D map doesn't need.
    /// </summary>
    /// <summary>
    /// Walks every FGConveyorChainActor in the save body and indexes each
    /// chain's per-belt spline points by the belt's PathName. The chain
    /// actor's ConveyorActors collection holds one entry per belt segment
    /// in the chain — each entry carries the belt's ObjectReference plus
    /// its Splines (Location/ArriveTangent/LeaveTangent). We keep only
    /// Location here; tangents are for smooth-curve interpolation that the
    /// 2D map doesn't need.
    /// </summary>
    /// <remarks>
    /// At time of writing the vendored SatisfactorySaveNet fork does not
    /// deserialize ExtraData for FGConveyorChainActor on v1.2+ saves
    /// (ObjectSerializer.cs:130 shortlist excludes IsConveyorActor —
    /// fork TODO.md §5). The lookup will be empty until the fork ports
    /// the v1.2 wire format; downstream callers fall back to point
    /// geometry when Polyline is null. Tracked in issue #44.
    /// </remarks>
    private static Dictionary<string, IReadOnlyList<Position>> BuildBeltSplineLookup(BodyV8 body)
    {
        var lookup = new Dictionary<string, IReadOnlyList<Position>>(StringComparer.Ordinal);
        foreach (var obj in body.Levels.SelectMany(l => l.Objects))
        {
            if (obj is not ActorObject actor) continue;
            if (actor.ExtraData is not ConveyorChainActor chain) continue;

            foreach (var conv in chain.ConveyorActors)
            {
                var key = conv.ConveyorBase?.PathName;
                if (string.IsNullOrEmpty(key)) continue;
                if (conv.Splines.Count == 0) continue;

                var polyline = new Position[conv.Splines.Count];
                var i = 0;
                foreach (var s in conv.Splines)
                {
                    polyline[i++] = new Position((float)s.Location.X, (float)s.Location.Y, (float)s.Location.Z);
                }
                lookup[key] = polyline;
            }
        }
        return lookup;
    }

    /// <summary>
    /// Pulls the polyline geometry from a pipe actor's <c>mSplineData</c>
    /// (<c>Array&lt;Struct&lt;FSplinePointData&gt;&gt;</c>). Each element exposes a
    /// <c>Location</c> StructProperty whose value is a 3-doubles vector.
    /// Returns null when the property is absent (saves predating the fork's
    /// v1.2 Array&lt;Struct&gt; reader, or pipe actors that legitimately have no
    /// spline data) — callers fall back to point-only geometry.
    /// </summary>
    private static IReadOnlyList<Position>? ExtractPipePolyline(ActorObject actor)
    {
        var splineData = actor.TryGetArrayStructValues("mSplineData");
        if (splineData is null || splineData.Count == 0) return null;

        var points = new List<Position>(splineData.Count);
        foreach (var element in splineData)
        {
            if (TryFindLocationVector(element.Properties) is { } xyz)
                points.Add(new Position((float)xyz[0], (float)xyz[1], (float)xyz[2]));
        }
        return points.Count > 0 ? points : null;
    }

    /// <summary>
    /// Finds the <c>Location</c> StructProperty inside one FSplinePointData
    /// element and returns its (X, Y, Z) doubles. Falls back to null when the
    /// element omits Location or when its StructValue isn't a 3-element vector.
    /// </summary>
    private static double[]? TryFindLocationVector(IReadOnlyList<Property> properties)
    {
        foreach (var p in properties)
        {
            if (p is RawProperty raw
                && raw.Name == "Location"
                && raw.StructValue?.Value is double[] xyz
                && xyz.Length == 3)
            {
                return xyz;
            }
        }
        return null;
    }

    private ResourceNode BuildResourceNode(string typePath, string reference, Position position)
    {
        var kind = BuildingIdentifiers.ResourceNodeKind(typePath);

        // Geysers are always geothermal (Pure) — known purely from BP type.
        if (kind == ERP.Domain.ResourceNodeKind.Geyser)
        {
            return new ResourceNode(reference, kind, Resource: null, NodePurity.Pure, position);
        }

        // Mining nodes / fracking sites need a coordinate lookup. We consult
        // user overrides first (per-machine file) then the bundled dataset.
        // Either source can be empty; falls back to Unknown on full miss.
        if (kind is ERP.Domain.ResourceNodeKind.MiningNode
                 or ERP.Domain.ResourceNodeKind.FrackingCore
                 or ERP.Domain.ResourceNodeKind.FrackingSatellite)
        {
            var hit = _overrides.Lookup(position) ?? _knownNodes.Lookup(position);
            if (hit is not null)
            {
                return new ResourceNode(reference, kind, new ItemId(hit.Resource), hit.Purity, position);
            }
        }

        return new ResourceNode(reference, kind, Resource: null, NodePurity.Unknown, position);
    }
}
