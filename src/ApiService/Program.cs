using ERP.Application;
using ERP.Application.Commands.IngestSave;
using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using ERP.Infrastructure;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Satisfactory.Save;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ICatalogProvider).Assembly);
});

builder.Services.AddErpInfrastructure(builder.Configuration);

// ---- Plan persistence (EF Core, ADR-0018) ----------------------------------
// SQLite by default, Postgres opt-in via `Persistence:Provider=postgres`.
// Connection string lives in `ConnectionStrings:Plans`.
builder.Services.AddErpPersistence(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// In Development, apply pending plan-storage migrations on startup so the
// SQLite default Just Works on a fresh checkout. Production / hosted deploys
// should run `dotnet ef database update` (or equivalent) out-of-band to keep
// schema changes explicit.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
    db.Database.Migrate();
}

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

app.MapGet("/catalog/recipes", (ICatalogProvider catalog) =>
{
    // Per-minute amounts mirror what /plan returns and what the planner UI displays —
    // raw per-cycle counts on the wire would force every consumer to multiply by
    // 60/duration. Recipes with zero duration would be a parser bug, but guard anyway.
    AmountDto ToPerMinute(ItemAmount a, TimeSpan duration) =>
        new(a.Item.Value,
            catalog.FindItem(a.Item)?.Name ?? a.Item.Value,
            duration.TotalSeconds > 0
                ? Math.Round(a.Quantity * 60m / (decimal)duration.TotalSeconds, 4)
                : a.Quantity);

    return catalog.Recipes
        .OrderBy(r => r.IsAlternate)
        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .Select(r =>
        {
            var building = catalog.FindBuilding(r.Building);
            return new RecipeView(
                r.Id.Value,
                r.Name,
                r.Building.Value,
                building?.Name ?? r.Building.Value,
                building?.BasePowerMw ?? 0,
                r.IsAlternate,
                r.Duration.TotalSeconds,
                r.Inputs.Select(i => ToPerMinute(i, r.Duration)).ToList(),
                r.Outputs.Select(o => ToPerMinute(o, r.Duration)).ToList());
        });
});

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

app.MapGet("/factory/state.geojson", (IFactoryStateProvider provider, ICatalogProvider catalog, Satisfactory.Save.KnownFlora flora) =>
    Results.Json(FactoryStateGeoJson.From(provider, catalog, flora), contentType: "application/geo+json"));

app.MapGet("/factory/saves", () =>
    SaveFileResolver.EnumerateDetectedSaves()
        .Select(f => new DetectedSaveView(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
        .ToList());

// Backing endpoint for the in-app filesystem picker (issue #84). Lists the
// directory at `path` so the Blazor `PathPickerDialog` can render breadcrumbs +
// folder/file rows. Read-only enumeration of an inherently-local dev tool —
// no auth gate, but every IO call is wrapped so a denied / missing path
// becomes a structured error instead of a 500.
//
// `purpose` is an optional hint ("catalogue" | "saves") used to compute a
// smart starting directory when the caller hasn't given an explicit `path` —
// the picker can land the user inside Satisfactory's Docs/SaveGames folder
// instead of making them click through ~/Library/... by hand.
app.MapGet("/fs/browse", (string? path, string? filter, string? purpose) =>
{
    var startPath = ResolveStartPath(path, purpose);

    DirectoryInfo dir;
    try
    {
        dir = new DirectoryInfo(startPath);
        if (!dir.Exists)
        {
            // Silently fall back so a stale user-stored path doesn't dead-end the picker.
            dir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Invalid path", detail: ex.Message, statusCode: 400);
    }

    var allowed = ParseFilter(filter);
    var dirs = new List<FsEntryView>();
    var files = new List<FsEntryView>();

    IEnumerable<DirectoryInfo> subDirs;
    IEnumerable<FileInfo> entries;
    try
    {
        subDirs = dir.EnumerateDirectories();
        entries = dir.EnumerateFiles();
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(title: "Access denied", detail: ex.Message, statusCode: 403);
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to enumerate", detail: ex.Message, statusCode: 422);
    }

    foreach (var sub in subDirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
    {
        // Skip hidden entries on Unix-likes — Library, .git, etc. clutter the picker.
        if (sub.Name.StartsWith('.')) continue;
        try
        {
            dirs.Add(new FsEntryView(sub.Name, sub.FullName, true, sub.LastWriteTimeUtc, null));
        }
        catch
        {
            // Symlink target missing, permission etc. — skip silently.
        }
    }

    foreach (var f in entries.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (f.Name.StartsWith('.')) continue;
        if (allowed.Count > 0 && !allowed.Contains(f.Extension.ToLowerInvariant())) continue;
        files.Add(new FsEntryView(f.Name, f.FullName, false, f.LastWriteTimeUtc, f.Length));
    }

    return Results.Ok(new FsBrowseView(dir.FullName, dir.Parent?.FullName, dirs, files));

    static string ResolveStartPath(string? path, string? purpose)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(path))
        {
            var expanded = ExpandHome(path);
            // If the caller hands us a file (e.g. a previously-picked Docs.json
            // or .sav), land in its directory so the picker shows siblings.
            if (File.Exists(expanded))
                return Path.GetDirectoryName(expanded) ?? home;
            return expanded;
        }

        foreach (var candidate in CandidatesFor(purpose, home))
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        return home;
    }

    // Probe order matters: Satisfactory Docs/SaveGames first so the user lands
    // exactly where they need to pick, with the user's profile dir as the
    // universal last-resort. The macOS bottle name varies (users can name
    // bottles anything), so we enumerate `~/Library/Application Support/
    // CrossOver/Bottles/*` rather than hard-coding "Steam".
    static IEnumerable<string> CandidatesFor(string? purpose, string home)
    {
        if (string.IsNullOrWhiteSpace(purpose)) yield break;

        // For each base install root we emit tiered candidates: the exact target
        // first (Docs/), then a less-specific fallback (the install root) so a
        // SF 1.0+ user — whose catalogue lives inside .pak, not in CommunityResources —
        // still lands at the install instead of `~/`.
        if (OperatingSystem.IsMacOS())
        {
            var bottlesRoot = Path.Combine(home, "Library/Application Support/CrossOver/Bottles");
            if (Directory.Exists(bottlesRoot))
            {
                foreach (var bottle in Directory.EnumerateDirectories(bottlesRoot))
                {
                    var install = Path.Combine(bottle, "drive_c/Program Files (x86)/Steam/steamapps/common/Satisfactory");
                    if (purpose == "catalogue")
                    {
                        yield return Path.Combine(install, "CommunityResources/Docs");
                        yield return Path.Combine(install, "CommunityResources");
                        yield return install;
                    }
                    else if (purpose == "saves")
                    {
                        yield return Path.Combine(bottle, "drive_c/users/crossover/AppData/Local/FactoryGame/Saved/SaveGames");
                    }
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            if (purpose == "catalogue")
            {
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs";
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources";
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory";
                yield return @"C:\Program Files\Epic Games\SatisfactoryEarlyAccess\CommunityResources\Docs";
                yield return @"C:\Program Files\Epic Games\SatisfactoryEarlyAccess";
            }
            else if (purpose == "saves")
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                    yield return Path.Combine(localAppData, "FactoryGame", "Saved", "SaveGames");
            }
        }
    }

    static string ExpandHome(string p)
    {
        if (p.StartsWith("~/", StringComparison.Ordinal) || p == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return p == "~" ? home : Path.Combine(home, p[2..]);
        }
        return p;
    }

    static HashSet<string> ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
});

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

// ----- Manual node overrides (#42 Option B) ---------------------------------
// User-curated resource + purity for individual BP_ResourceNode_C actors.
// Persisted to %LOCALAPPDATA%\ERP.Satisfactory\manual-node-overrides.json.
// Body identifies the node by `reference` (the in-save PathName, surfaced in
// GeoJSON as the resource-node feature's `kind`). The server resolves the
// node's position from current state, persists at that position (so the
// override survives across saves of the same world), and refreshes parsed
// state so callers see the change immediately.

app.MapPut("/factory/node-override", (
    NodeOverrideRequest request,
    Satisfactory.Save.ManualNodeOverrides overrides,
    IFactoryStateProvider provider) =>
{
    if (string.IsNullOrWhiteSpace(request.Reference))
        return Results.BadRequest(new { error = "Reference is required." });
    if (string.IsNullOrWhiteSpace(request.Resource))
        return Results.BadRequest(new { error = "Resource is required (e.g. Desc_OreIron_C)." });
    if (!Enum.TryParse<ERP.Domain.NodePurity>(request.Purity, ignoreCase: true, out var purity))
        return Results.BadRequest(new { error = $"Unknown purity '{request.Purity}'. Use Impure, Normal, or Pure." });

    var node = provider.Current.ResourceNodes
        .FirstOrDefault(n => string.Equals(n.Reference, request.Reference, StringComparison.Ordinal));
    if (node is null)
        return Results.NotFound(new { error = $"No resource node with reference '{request.Reference}'." });

    overrides.Upsert(node.Position, request.Resource, purity);
    provider.Refresh();
    return Results.NoContent();
});

app.MapDelete("/factory/node-override", (
    string reference,
    Satisfactory.Save.ManualNodeOverrides overrides,
    IFactoryStateProvider provider) =>
{
    if (string.IsNullOrWhiteSpace(reference))
        return Results.BadRequest(new { error = "reference query parameter is required." });

    var node = provider.Current.ResourceNodes
        .FirstOrDefault(n => string.Equals(n.Reference, reference, StringComparison.Ordinal));
    if (node is null)
        return Results.NotFound(new { error = $"No resource node with reference '{reference}'." });

    var removed = overrides.Delete(node.Position);
    if (removed) provider.Refresh();
    return Results.NoContent();
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

public sealed record RecipeView(
    string Id,
    string Name,
    string BuildingId,
    string BuildingName,
    double BuildingPowerMw,
    bool IsAlternate,
    double DurationSeconds,
    IReadOnlyList<AmountDto> InputsPerMinute,
    IReadOnlyList<AmountDto> OutputsPerMinute);

public sealed record ConfigureCatalogueRequest(string DocsPath);

public sealed record IngestSaveRequest(string SavePath);

/// <summary>
/// PUT /factory/node-override body. Purity arrives as a string (Impure /
/// Normal / Pure) — Minimal APIs JSON binding doesn't string-bind enums by
/// default, and we don't want to enable that globally for everything else.
/// </summary>
public sealed record NodeOverrideRequest(string Reference, string Resource, string Purity);

public sealed record DetectedSaveView(string Path, string Name, DateTime LastWriteTimeUtc, long SizeBytes);

/// <summary>One row in the in-app filesystem picker (issue #84).</summary>
public sealed record FsEntryView(string Name, string FullPath, bool IsDirectory, DateTime LastWriteTimeUtc, long? SizeBytes);

/// <summary>Response from GET /fs/browse — what the picker dialog renders for a directory.</summary>
public sealed record FsBrowseView(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<FsEntryView> Directories,
    IReadOnlyList<FsEntryView> Files);

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

// GeoJSON geometry — Point uses [x, y]; LineString uses [[x, y], …]. We
// serialise both shapes through `object` so the JSON layout matches the
// GeoJSON spec without needing two parallel `GeoFeature` records.
public sealed record GeoGeometry(string Type, object Coordinates)
{
    public static GeoGeometry Point(Position p) => new("Point", new double[] { p.X, p.Y });

    public static GeoGeometry LineString(IReadOnlyList<Position> polyline)
    {
        var coords = new double[polyline.Count][];
        for (var i = 0; i < polyline.Count; i++)
            coords[i] = [polyline[i].X, polyline[i].Y];
        return new GeoGeometry("LineString", coords);
    }
}

public sealed record GeoFeature(
    string Type,
    GeoGeometry Geometry,
    Dictionary<string, object?> Properties)
{
    public static GeoFeature Make(string category, string kind, Position position, Dictionary<string, object?>? extra = null)
        => new("Feature", GeoGeometry.Point(position), BuildProps(category, kind, position, extra));

    public static GeoFeature MakeLine(string category, string kind, IReadOnlyList<Position> polyline, Position fallback, Dictionary<string, object?>? extra = null)
        => new("Feature", GeoGeometry.LineString(polyline), BuildProps(category, kind, fallback, extra));

    private static Dictionary<string, object?> BuildProps(string category, string kind, Position position, Dictionary<string, object?>? extra)
    {
        var props = new Dictionary<string, object?>
        {
            ["category"] = category,
            ["kind"] = kind,
            ["z"] = position.Z,
        };
        if (extra is not null)
            foreach (var kv in extra) props[kv.Key] = kv.Value;
        return props;
    }
}

public sealed record FactoryStateGeoJson(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    Dictionary<string, object?> Metadata)
{
    public static FactoryStateGeoJson From(IFactoryStateProvider provider, ICatalogProvider catalog)
        => From(provider, catalog, Satisfactory.Save.KnownFlora.Empty);

    public static FactoryStateGeoJson From(
        IFactoryStateProvider provider,
        ICatalogProvider catalog,
        Satisfactory.Save.KnownFlora flora)
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
        {
            var props = new Dictionary<string, object?>
            {
                ["tier"] = belt.Tier.ToString(),
            };
            if (belt.Polyline is { Count: >= 2 } poly)
                features.Add(GeoFeature.MakeLine("belt", belt.Reference, poly, belt.Position, props));
            else
                features.Add(GeoFeature.Make("belt", belt.Reference, belt.Position, props));
        }

        foreach (var g in s.Generators)
            features.Add(GeoFeature.Make("generator", g.Reference, g.Position, new()
            {
                ["genKind"] = g.Kind.ToString(),
            }));

        // Flora layer (#62) — static dataset, not from the save. Each feature
        // carries the species ItemId so the JS layer can pick the right wiki
        // item icon (Desc_Berry_C.png etc.) and surface a friendly name.
        foreach (var f in flora.All)
        {
            features.Add(GeoFeature.Make("flora", f.Species, new Position(f.X, f.Y, f.Z), new()
            {
                ["species"] = f.Species,
                ["speciesName"] = f.DisplayName,
            }));
        }

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
    string BuildingName,
    decimal BuildingCount,
    decimal PowerMw,
    IReadOnlyList<AmountDto> Inputs,
    IReadOnlyList<AmountDto> Outputs);

public sealed record PlanDto(
    bool IsFeasible,
    IReadOnlyList<StepDto> Steps,
    decimal TotalPowerMw,
    IReadOnlyList<AmountDto> RawInputsConsumed,
    IReadOnlyList<AmountDto> MissingInputs)
{
    public static PlanDto From(ProductionPlan plan, ICatalogProvider catalog)
    {
        AmountDto ToAmount(ItemAmount a) =>
            new(a.Item.Value, catalog.FindItem(a.Item)?.Name ?? a.Item.Value, Math.Round(a.Quantity, 4));

        var steps = plan.Steps.Select(s => new StepDto(
            s.Recipe.Id.Value,
            s.Recipe.Name,
            s.Recipe.Building.Value,
            catalog.FindBuilding(s.Recipe.Building)?.Name ?? s.Recipe.Building.Value,
            Math.Round(s.BuildingCount, 4),
            Math.Round(s.PowerMw, 4),
            s.InputsPerMinute.Select(ToAmount).ToList(),
            s.OutputsPerMinute.Select(ToAmount).ToList())).ToList();

        return new(
            IsFeasible: plan.IsFeasible,
            Steps: steps,
            TotalPowerMw: Math.Round(steps.Sum(s => s.PowerMw), 4),
            RawInputsConsumed: plan.RawInputsConsumed.Select(ToAmount).ToList(),
            MissingInputs: plan.MissingInputs.Select(ToAmount).ToList());
    }
}
