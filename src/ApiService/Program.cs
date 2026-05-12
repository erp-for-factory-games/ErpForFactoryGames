using ERP.Application;
using ERP.Application.Commands.IngestSave;
using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using ERP.Infrastructure;
using Satisfactory.Save;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ICatalogProvider).Assembly);
});

builder.Services.AddErpInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running. See /catalog/items, /plan, /factory/state.");

app.MapGet("/catalog/items", (ICatalogProvider catalog) =>
    catalog.Items
        .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        .Select(i => new ItemDto(i.Id.Value, i.Name)));

app.MapGet("/catalogue/status", (ICatalogProvider catalog) => catalog.GetStatus());

app.MapPost("/catalogue/configure", (ConfigureCatalogueRequest request, ICatalogProvider catalog) =>
{
    if (string.IsNullOrWhiteSpace(request.DocsPath))
        return Results.BadRequest(new { error = "DocsPath is required." });

    try
    {
        var status = catalog.LoadFromPath(request.DocsPath);
        return Results.Ok(status);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to load catalogue", detail: ex.Message, statusCode: 422);
    }
});

app.MapGet("/factory/state", (IFactoryStateProvider provider, ICatalogProvider catalog) =>
    FactoryStateView.From(provider, catalog));

app.MapGet("/factory/state.geojson", (IFactoryStateProvider provider, ICatalogProvider catalog) =>
    Results.Json(FactoryStateGeoJson.From(provider, catalog), contentType: "application/geo+json"));

app.MapGet("/factory/saves", () =>
    SaveFileResolver.EnumerateDetectedSaves()
        .Select(f => new DetectedSaveView(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
        .ToList());

app.MapPost("/factory/ingest", async (
    IngestSaveRequest request, IMessageBus bus, IFactoryStateProvider provider,
    ICatalogProvider catalog, ILoggerFactory loggerFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.SavePath))
        return Results.BadRequest(new { error = "SavePath is required." });

    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await bus.InvokeAsync<FactoryStateStatus>(new IngestSaveCommand(request.SavePath));
        sw.Stop();
        loggerFactory.CreateLogger("FactoryIngestEndpoint")
            .LogInformation("Ingested save in {Elapsed}ms", sw.ElapsedMilliseconds);
        return Results.Ok(FactoryStateView.From(provider, catalog));
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to parse save", detail: ex.Message, statusCode: 422);
    }
});

app.MapPost("/plan", async (PlanRequest request, IMessageBus bus, ICatalogProvider catalog, ILoggerFactory loggerFactory) =>
{
    if (!catalog.IsLoaded || catalog.Recipes.Count == 0)
    {
        return Results.Problem(
            title: "Catalogue not loaded",
            detail: "Configure the Docs.json path via POST /catalogue/configure before planning.",
            statusCode: 409);
    }

    var query = new PlanProductionQuery(
        Targets: request.Targets.Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute)).ToList(),
        Available: request.Available.Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute)).ToList());

    var logger = loggerFactory.CreateLogger("PlannerEndpoint");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var plan = await bus.InvokeAsync<ProductionPlan>(query);
    sw.Stop();
    logger.LogInformation(
        "Planner: {Targets} target(s) → {Steps} step(s), {Missing} missing input(s) in {Elapsed}ms",
        query.Targets.Count, plan.Steps.Count, plan.MissingInputs.Count, sw.ElapsedMilliseconds);

    return Results.Ok(PlanDto.From(plan, catalog));
});

app.MapDefaultEndpoints();

app.Run();

public sealed record ItemDto(string Id, string Name);

public sealed record ConfigureCatalogueRequest(string DocsPath);

public sealed record IngestSaveRequest(string SavePath);

public sealed record DetectedSaveView(string Path, string Name, DateTime LastWriteTimeUtc, long SizeBytes);

public sealed record SaveMetadataView(
    string SessionName,
    int SaveVersion,
    int BuildVersion,
    double PlayedSeconds,
    DateTime SaveDateTimeUtc);

public sealed record CountView(string Key, int Count);

/// <summary>One row in the "buildings by type × recipe" table on /factory/ingest.</summary>
public sealed record BuildingGroupView(
    string Building,
    string? Recipe,
    string? RecipeName,
    int Count);

public sealed record FactoryStateView(
    bool IsLoaded,
    string? Source,
    SaveMetadataView? Save,
    IReadOnlyList<CountView> Miners,
    int MinersBoundToNode,
    IReadOnlyList<BuildingGroupView> Buildings,
    int BuildingsWithRecipe,
    IReadOnlyList<CountView> Belts,
    IReadOnlyList<CountView> Generators,
    int ResourceNodeCount,
    IReadOnlyList<string> Warnings)
{
    public static FactoryStateView From(IFactoryStateProvider provider, ICatalogProvider catalog)
    {
        var state = provider.Current;
        var meta = provider.IsLoaded
            ? new SaveMetadataView(
                state.Save.SessionName,
                state.Save.SaveVersion,
                state.Save.BuildVersion,
                state.Save.PlayedTime.TotalSeconds,
                state.Save.SaveDateTimeUtc)
            : null;

        return new FactoryStateView(
            IsLoaded: provider.IsLoaded,
            Source: provider.Source,
            Save: meta,
            Miners: state.Miners
                .GroupBy(m => m.Tier.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            MinersBoundToNode: state.Miners.Count(m => !string.IsNullOrEmpty(m.ResourceNodeReference)),
            Buildings: state.Buildings
                .GroupBy(b => (Building: b.Building.Value, Recipe: b.Recipe?.Value))
                .Select(g => new BuildingGroupView(
                    Building: g.Key.Building,
                    Recipe: g.Key.Recipe,
                    RecipeName: g.Key.Recipe is { Length: > 0 } r
                        ? catalog.FindRecipe(new RecipeId(r))?.Name
                        : null,
                    Count: g.Count()))
                .OrderBy(b => b.Building, StringComparer.Ordinal)
                .ThenByDescending(b => b.Count)
                .ToList(),
            BuildingsWithRecipe: state.Buildings.Count(b => b.Recipe is not null),
            Belts: state.Belts
                .GroupBy(b => b.Tier.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            Generators: state.Generators
                .GroupBy(g => g.Kind.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            ResourceNodeCount: state.ResourceNodes.Count,
            Warnings: state.Warnings);
    }
}

// ---------------------------------------------------------------------------
// GeoJSON projection for the map page (ADR-0013).
// FeatureCollection with one Feature per parsed entity. Coordinates are raw
// Unreal world X/Y in centimetres — the JS layer (using Leaflet's CRS.Simple)
// handles axis orientation + zoom bounds.
// ---------------------------------------------------------------------------

public sealed record GeoPoint(string Type, double[] Coordinates)
{
    public static GeoPoint From(Position p) => new("Point", [p.X, p.Y]);
}

public sealed record GeoFeature(
    string Type,
    GeoPoint Geometry,
    Dictionary<string, object?> Properties)
{
    public static GeoFeature Make(string category, string kind, Position position, Dictionary<string, object?>? extra = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["category"] = category,
            ["kind"] = kind,
            ["z"] = position.Z,
        };
        if (extra is not null)
            foreach (var kv in extra) props[kv.Key] = kv.Value;
        return new GeoFeature("Feature", GeoPoint.From(position), props);
    }
}

public sealed record FactoryStateGeoJson(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    Dictionary<string, object?> Metadata)
{
    public static FactoryStateGeoJson From(IFactoryStateProvider provider, ICatalogProvider catalog)
    {
        var s = provider.Current;
        var features = new List<GeoFeature>();

        foreach (var n in s.ResourceNodes)
            features.Add(GeoFeature.Make("resource-node", n.Reference, n.Position, new()
            {
                ["nodeKind"] = n.Kind.ToString(),
                ["purity"] = n.Purity.ToString(),
                ["resource"] = n.Resource?.Value,
                ["resourceName"] = n.Resource is { Value: { Length: > 0 } } id
                    ? catalog.FindItem(id)?.Name
                    : null,
            }));

        foreach (var m in s.Miners)
            features.Add(GeoFeature.Make("miner", m.Reference, m.Position, new()
            {
                ["tier"] = m.Tier.ToString(),
                ["resourceNode"] = m.ResourceNodeReference,
            }));

        foreach (var b in s.Buildings)
            features.Add(GeoFeature.Make("building", b.Building.Value, b.Position, new()
            {
                ["recipe"] = b.Recipe?.Value,
                ["recipeName"] = b.Recipe is { Value: { Length: > 0 } } id
                    ? catalog.FindRecipe(id)?.Name
                    : null,
                ["clockSpeed"] = b.ClockSpeed,
            }));

        foreach (var belt in s.Belts)
            features.Add(GeoFeature.Make("belt", belt.Reference, belt.Position, new()
            {
                ["tier"] = belt.Tier.ToString(),
            }));

        foreach (var g in s.Generators)
            features.Add(GeoFeature.Make("generator", g.Reference, g.Position, new()
            {
                ["genKind"] = g.Kind.ToString(),
            }));

        var meta = new Dictionary<string, object?>
        {
            ["isLoaded"] = provider.IsLoaded,
            ["source"] = provider.Source,
            ["sessionName"] = provider.IsLoaded ? s.Save.SessionName : null,
            ["featureCount"] = features.Count,
        };

        return new FactoryStateGeoJson("FeatureCollection", features, meta);
    }
}

public sealed record TargetDto(string ItemId, decimal ItemsPerMinute);
public sealed record AvailabilityDto(string ItemId, decimal ItemsPerMinute);
public sealed record PlanRequest(IReadOnlyList<TargetDto> Targets, IReadOnlyList<AvailabilityDto> Available);

public sealed record AmountDto(string ItemId, string ItemName, decimal ItemsPerMinute);
public sealed record StepDto(
    string RecipeId,
    string RecipeName,
    string BuildingId,
    decimal BuildingCount,
    IReadOnlyList<AmountDto> Inputs,
    IReadOnlyList<AmountDto> Outputs);

public sealed record PlanDto(
    bool IsFeasible,
    IReadOnlyList<StepDto> Steps,
    IReadOnlyList<AmountDto> RawInputsConsumed,
    IReadOnlyList<AmountDto> MissingInputs)
{
    public static PlanDto From(ProductionPlan plan, ICatalogProvider catalog)
    {
        AmountDto ToAmount(ItemAmount a) =>
            new(a.Item.Value, catalog.FindItem(a.Item)?.Name ?? a.Item.Value, Math.Round(a.Quantity, 4));

        return new(
            IsFeasible: plan.IsFeasible,
            Steps: plan.Steps.Select(s => new StepDto(
                s.Recipe.Id.Value,
                s.Recipe.Name,
                s.Recipe.Building.Value,
                Math.Round(s.BuildingCount, 4),
                s.InputsPerMinute.Select(ToAmount).ToList(),
                s.OutputsPerMinute.Select(ToAmount).ToList())).ToList(),
            RawInputsConsumed: plan.RawInputsConsumed.Select(ToAmount).ToList(),
            MissingInputs: plan.MissingInputs.Select(ToAmount).ToList());
    }
}
