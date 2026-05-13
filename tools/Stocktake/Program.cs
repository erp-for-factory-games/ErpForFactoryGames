// Stocktake CLI — runs the patched SatisfactorySaveNet fork against a .sav file and
// prints a structured factory inventory suitable for human review or AI agent
// consumption (ADA et al). Default output is Markdown; --json emits a single
// machine-readable JSON document.
//
// Surfaces:
//   * per-miner resource (via mExtractableResource + OutputInventory.mAllowedItemDescriptors)
//   * per-producer recipe (via mCurrentRecipe)
//   * per-generator fuel (via mCurrentFuelClass)
//   * water + oil extractor categorisation

using System.Globalization;
using System.Text.Json;
using SatisfactorySaveNet;
using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;

const string DefaultSaveDir = @"C:\Users\ChrisSimon\AppData\Local\FactoryGame\Saved\SaveGames\76561198103946376";

var (savePath, asJson) = ParseArgs(args);

if (savePath is null)
{
    savePath = FindLatestSave(DefaultSaveDir);
    if (savePath is null)
    {
        Console.Error.WriteLine($"No .sav files found under {DefaultSaveDir} and no path argument given.");
        Console.Error.WriteLine("Usage: stocktake [path-to-save.sav] [--json]");
        return 1;
    }
}

if (!File.Exists(savePath))
{
    Console.Error.WriteLine($"Save file not found: {savePath}");
    return 1;
}

SatisfactorySave save;
var elapsed = System.Diagnostics.Stopwatch.StartNew();
try
{
    save = SaveFileSerializer.Instance.Deserialize(savePath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to parse save: {ex.GetType().Name}: {ex.Message}");
    return 2;
}
elapsed.Stop();

if (save.Body is not BodyV8 body)
{
    Console.Error.WriteLine($"Unsupported body version (got {save.Body?.GetType().Name ?? "null"}).");
    return 3;
}

var report = BuildReport(save.Header, body, savePath, elapsed.ElapsedMilliseconds);

if (asJson)
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
else
    PrintMarkdown(report);

return 0;

// --- helpers -----------------------------------------------------------------

static (string? path, bool json) ParseArgs(string[] args)
{
    string? path = null;
    var json = false;
    foreach (var arg in args)
    {
        if (arg is "--json" or "-j") json = true;
        else if (arg is "--help" or "-h") { PrintUsage(); Environment.Exit(0); }
        else if (path is null) path = arg;
    }
    return (path, json);
}

static void PrintUsage()
{
    Console.WriteLine("Usage: stocktake [path-to-save.sav] [--json]");
    Console.WriteLine();
    Console.WriteLine("Parses a Satisfactory .sav file and prints a factory inventory");
    Console.WriteLine("(miners by resource, producers by recipe, generators by fuel, …).");
    Console.WriteLine("Defaults to the most recently modified save under");
    Console.WriteLine($"  {DefaultSaveDir}");
}

static string? FindLatestSave(string dir)
{
    if (!Directory.Exists(dir)) return null;
    return new DirectoryInfo(dir)
        .GetFiles("*.sav")
        .OrderByDescending(f => f.LastWriteTime)
        .FirstOrDefault()?.FullName;
}

static string ShortName(string typePath)
{
    if (string.IsNullOrEmpty(typePath)) return "(empty)";
    var lastDot = typePath.LastIndexOf('.');
    return lastDot < 0 ? typePath : typePath[(lastDot + 1)..];
}

static string Strip(string name, string prefix, string suffix)
{
    if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
    if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];
    return name;
}

static string ShortClass(string typePath)    => Strip(ShortName(typePath), "Build_", "_C");
static string ShortRecipe(string typePath)   => Strip(ShortName(typePath), "Recipe_", "_C");
static string ShortFuel(string typePath)     => Strip(ShortName(typePath), "Desc_", "_C");
static string ShortResource(string typePath)
{
    var s = ShortName(typePath);
    if (s.EndsWith("_C", StringComparison.Ordinal)) s = s[..^2];
    if (s.StartsWith("Desc_", StringComparison.Ordinal)) s = s["Desc_".Length..];
    if (s.StartsWith("BP_ResourceNode", StringComparison.Ordinal))
        s = "Node" + s["BP_ResourceNode".Length..];
    return s;
}

static RawProperty? FindRawProperty(ComponentObject obj, string name)
{
    foreach (var p in obj.Properties)
        if (p is RawProperty raw && string.Equals(raw.Name, name, StringComparison.Ordinal))
            return raw;
    return null;
}

static StocktakeReport BuildReport(Header header, BodyV8 body, string savePath, long parseMs)
{
    var allObjects = body.Levels.SelectMany(l => l.Objects).ToList();
    var actors     = allObjects.OfType<ActorObject>().ToList();

    // Build a lookup keyed by the object's pathName, e.g.
    //   "Persistent_Level:PersistentLevel.Build_MinerMk1_C_2147060123"
    // so we can resolve a miner's "<pathName>.OutputInventory" child component.
    var byPathName = new Dictionary<string, ComponentObject>(StringComparer.Ordinal);
    foreach (var o in allObjects)
    {
        var pn = o.ObjectReference?.PathName;
        if (!string.IsNullOrEmpty(pn)) byPathName[pn] = o;
    }

    // Bucket actors by class semantics.
    var miners        = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_MinerMk", StringComparison.Ordinal)).ToList();
    var smelters      = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_SmelterMk", StringComparison.Ordinal)).ToList();
    var foundries     = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_FoundryMk", StringComparison.Ordinal)).ToList();
    var constructors  = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_ConstructorMk", StringComparison.Ordinal)).ToList();
    var assemblers    = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_AssemblerMk", StringComparison.Ordinal)).ToList();
    var manufacturers = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_ManufacturerMk", StringComparison.Ordinal)).ToList();
    var refineries    = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_OilRefinery", StringComparison.Ordinal)).ToList();
    var packagers     = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_Packager", StringComparison.Ordinal)).ToList();
    var blenders      = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_Blender", StringComparison.Ordinal)).ToList();
    var hadron        = actors.Where(a => ShortName(a.TypePath).Contains("HadronCollider", StringComparison.Ordinal)
                                       || ShortName(a.TypePath).Contains("ParticleAccelerator", StringComparison.Ordinal)).ToList();

    var coalGens      = actors.Where(a => ShortName(a.TypePath).Contains("GeneratorCoal", StringComparison.Ordinal)).ToList();
    var fuelGens      = actors.Where(a => ShortName(a.TypePath).Contains("GeneratorFuel", StringComparison.Ordinal)).ToList();
    var biomassBurns  = actors.Where(a => ShortName(a.TypePath).Contains("GeneratorBiomass", StringComparison.Ordinal)
                                       || ShortName(a.TypePath).Contains("GeneratorBio", StringComparison.Ordinal)).ToList();
    var geothermal    = actors.Where(a => ShortName(a.TypePath).Contains("GeneratorGeoThermal", StringComparison.OrdinalIgnoreCase)).ToList();
    var nuclear       = actors.Where(a => ShortName(a.TypePath).Contains("GeneratorNuclear", StringComparison.Ordinal)).ToList();

    var waterExt      = actors.Where(a => ShortName(a.TypePath).StartsWith("Build_WaterPump", StringComparison.Ordinal)).ToList();
    var oilExt        = actors.Where(a => ShortName(a.TypePath).Contains("OilPump", StringComparison.Ordinal)
                                       || ShortName(a.TypePath).Contains("CrudeOilPump", StringComparison.Ordinal)).ToList();
    var fracking      = actors.Where(a => ShortName(a.TypePath).Contains("FrackingExtractor", StringComparison.Ordinal)
                                       || ShortName(a.TypePath).Contains("FrackingSmasher", StringComparison.Ordinal)).ToList();

    var beltsByTier   = ActorsByClass(actors, "Build_ConveyorBeltMk");
    var liftsByTier   = ActorsByClass(actors, "Build_ConveyorLiftMk");
    var pipesByTier   = ActorsByClass(actors, "Build_PipelineMk");

    var topClasses = actors
        .GroupBy(a => ShortName(a.TypePath))
        .Select(g => new ClassCount(g.Key, g.Count()))
        .OrderByDescending(c => c.Count)
        .Take(25)
        .ToList();

    return new StocktakeReport
    {
        SavePath           = savePath,
        ParsedInMs         = parseMs,
        Session            = header.SessionName,
        SaveName           = header.SaveName ?? "",
        SaveVersion        = header.SaveVersion,
        BuildVersion       = header.BuildVersion,
        PlayedSeconds      = header.PlayedSeconds,
        SaveDateTimeUtc    = header.SaveDateTimeUtc,
        IsPartitionedWorld = header.IsPartitionedWorld == 1,
        Levels             = body.Levels.Count,
        TotalObjects       = allObjects.Count,
        TotalActors        = actors.Count,

        Miners             = MinersWithResource(miners, byPathName),
        Smelters           = Producers(smelters),
        Foundries          = Producers(foundries),
        Constructors       = Producers(constructors),
        Assemblers         = Producers(assemblers),
        Manufacturers      = Producers(manufacturers),
        Refineries         = Producers(refineries),
        Packagers          = Producers(packagers),
        Blenders           = Producers(blenders),
        HadronColliders    = Producers(hadron),

        Generators = new GeneratorReport
        {
            Coal       = FuelTally(coalGens),
            Fuel       = FuelTally(fuelGens),
            Biomass    = FuelTally(biomassBurns),
            Geothermal = geothermal.Count,
            Nuclear    = FuelTally(nuclear)
        },

        WaterExtractors    = ExtractorList(waterExt),
        OilExtractors      = OilExtractorList(oilExt),
        FrackingExtractors = fracking.Count,

        BeltsByTier        = beltsByTier,
        LiftsByTier        = liftsByTier,
        PipesByTier        = pipesByTier,

        ResourceNodes      = actors.Count(a => a.TypePath.Contains("BP_ResourceNode_C",       StringComparison.Ordinal)),
        Geysers            = actors.Count(a => a.TypePath.Contains("BP_ResourceNodeGeyser_C", StringComparison.Ordinal)),
        Deposits           = actors.Count(a => a.TypePath.Contains("BP_ResourceDeposit_C",    StringComparison.Ordinal)),
        FrackingSatellites = actors.Count(a => a.TypePath.Contains("BP_FrackingSatellite_C",  StringComparison.Ordinal)),

        TopClasses         = topClasses
    };
}

// --- per-category extraction ------------------------------------------------

static IReadOnlyList<TieredEntry> ActorsByClass(IEnumerable<ActorObject> actors, string prefix) =>
    actors
        .Where(a => ShortName(a.TypePath).StartsWith(prefix, StringComparison.Ordinal))
        .GroupBy(a => ShortName(a.TypePath))
        .Select(g => new TieredEntry(g.Key, g.Count(), []))
        .OrderBy(e => e.ClassName, StringComparer.Ordinal)
        .ToList();

static IReadOnlyList<MinerEntry> MinersWithResource(IEnumerable<ActorObject> miners, Dictionary<string, ComponentObject> byPathName)
{
    var list = new List<MinerEntry>();
    foreach (var m in miners)
    {
        var refValue = FindRawProperty(m, "mExtractableResource")?.ObjectValue;
        var resource = ResolveResource(m, byPathName);
        list.Add(new MinerEntry
        {
            Class    = ShortClass(m.TypePath),
            X        = (int)m.Position.X,
            Y        = (int)m.Position.Y,
            Z        = (int)m.Position.Z,
            Resource = resource ?? "(unknown)",
            NodeRef  = refValue is { } v ? ShortName(v.PathName) : "(none)"
        });
    }
    return list.OrderBy(e => e.Class, StringComparer.Ordinal).ThenBy(e => e.Resource, StringComparer.Ordinal).ToList();
}

static string? ResolveResource(ActorObject miner, Dictionary<string, ComponentObject> byPathName)
{
    // The miner's mExtractableResource points at the world's resource-node Actor,
    // but the node itself doesn't carry the resource class on save. Instead the miner
    // owns an OutputInventory child component whose mAllowedItemDescriptors[0] is
    // locked to the mined resource descriptor (Desc_OreIron_C, Desc_Coal_C, …).
    var pn = miner.ObjectReference?.PathName;
    if (string.IsNullOrEmpty(pn)) return null;

    if (!byPathName.TryGetValue(pn + ".OutputInventory", out var outInv)) return null;
    var allowed = FindRawProperty(outInv, "mAllowedItemDescriptors");
    if (allowed?.ArrayObjectValues is not { Count: > 0 } arr) return null;

    return ShortResource(arr[0].PathName);
}

static IReadOnlyList<ProducerEntry> Producers(IEnumerable<ActorObject> producers)
{
    return producers
        .GroupBy(p => (ShortClass(p.TypePath), Recipe: RecipeOf(p)))
        .Select(g => new ProducerEntry(g.Key.Item1, g.Key.Recipe, g.Count()))
        .OrderByDescending(e => e.Count)
        .ThenBy(e => e.Class, StringComparer.Ordinal)
        .ToList();
}

static string RecipeOf(ActorObject producer)
{
    var raw = FindRawProperty(producer, "mCurrentRecipe");
    if (raw?.ObjectValue is { } v && !string.IsNullOrEmpty(v.PathName))
        return ShortRecipe(v.PathName);
    return "(none)";
}

static IReadOnlyList<FuelTallyEntry> FuelTally(IEnumerable<ActorObject> gens)
{
    return gens
        .GroupBy(g => FuelOf(g))
        .Select(g => new FuelTallyEntry(g.Key, g.Count()))
        .OrderByDescending(e => e.Count)
        .ThenBy(e => e.Fuel, StringComparer.Ordinal)
        .ToList();
}

static string FuelOf(ActorObject gen)
{
    var raw = FindRawProperty(gen, "mCurrentFuelClass");
    if (raw?.ObjectValue is { } v && !string.IsNullOrEmpty(v.PathName))
        return ShortFuel(v.PathName);
    return "(none)";
}

static IReadOnlyList<ExtractorEntry> ExtractorList(IEnumerable<ActorObject> ex) =>
    ex.Select(a => new ExtractorEntry(
            ShortClass(a.TypePath),
            (int)a.Position.X, (int)a.Position.Y, (int)a.Position.Z,
            null))
      .ToList();

static IReadOnlyList<ExtractorEntry> OilExtractorList(IEnumerable<ActorObject> ex)
{
    var list = new List<ExtractorEntry>();
    foreach (var a in ex)
    {
        var raw = FindRawProperty(a, "mExtractableResource");
        var res = raw?.ObjectValue?.PathName is { Length: > 0 } pn ? ShortResource(pn) : null;
        list.Add(new ExtractorEntry(ShortClass(a.TypePath), (int)a.Position.X, (int)a.Position.Y, (int)a.Position.Z, res));
    }
    return list;
}

// --- markdown output --------------------------------------------------------

static void PrintMarkdown(StocktakeReport r)
{
    var c = CultureInfo.InvariantCulture;
    var hours = TimeSpan.FromSeconds(r.PlayedSeconds).TotalHours;

    Console.WriteLine($"# Satisfactory Stocktake — `{r.Session}`");
    Console.WriteLine();
    Console.WriteLine($"- **Save file**: `{Path.GetFileName(r.SavePath)}`");
    Console.WriteLine($"- **Saved**: {r.SaveDateTimeUtc:yyyy-MM-dd HH:mm} UTC, {hours.ToString("F1", c)} h played");
    Console.WriteLine($"- **Save version**: {r.SaveVersion} (build {r.BuildVersion}), partitioned-world: {r.IsPartitionedWorld}");
    Console.WriteLine($"- **Levels**: {r.Levels:N0} · **Actors**: {r.TotalActors:N0} · **Total objects**: {r.TotalObjects:N0}");
    Console.WriteLine($"- **Parsed in**: {r.ParsedInMs} ms");
    Console.WriteLine();

    PrintMinersSection(r.Miners);
    PrintProducerSection("Smelters",     r.Smelters);
    PrintProducerSection("Foundries",    r.Foundries);
    PrintProducerSection("Constructors", r.Constructors);
    PrintProducerSection("Assemblers",   r.Assemblers);
    PrintProducerSection("Manufacturers", r.Manufacturers);
    PrintProducerSection("Refineries",   r.Refineries);
    PrintProducerSection("Packagers",    r.Packagers);
    PrintProducerSection("Blenders",     r.Blenders);
    PrintProducerSection("Hadron Colliders", r.HadronColliders);

    Console.WriteLine("## Power generators");
    Console.WriteLine();
    PrintGenSection("Coal generators",   r.Generators.Coal);
    PrintGenSection("Fuel generators",   r.Generators.Fuel);
    PrintGenSection("Biomass burners",   r.Generators.Biomass);
    PrintGenSection("Nuclear plants",    r.Generators.Nuclear);
    if (r.Generators.Geothermal > 0)
        Console.WriteLine($"- **Geothermal generators**: {r.Generators.Geothermal}");
    Console.WriteLine();

    Console.WriteLine("## Fluid extractors");
    Console.WriteLine();
    Console.WriteLine($"- **Water extractors**: {r.WaterExtractors.Count}");
    foreach (var w in r.WaterExtractors)
        Console.WriteLine($"  - `{w.Class}` @ ({w.X}, {w.Y}, {w.Z})");
    Console.WriteLine($"- **Oil extractors**: {r.OilExtractors.Count}");
    foreach (var o in r.OilExtractors)
        Console.WriteLine($"  - `{o.Class}` @ ({o.X}, {o.Y}, {o.Z}) → {o.Resource ?? "(unknown)"}");
    if (r.FrackingExtractors > 0)
        Console.WriteLine($"- **Fracking extractors**: {r.FrackingExtractors}");
    Console.WriteLine();

    Console.WriteLine("## Logistics");
    Console.WriteLine();
    Console.WriteLine("| Kind | Class | Count |");
    Console.WriteLine("|------|-------|------:|");
    foreach (var b in r.BeltsByTier) Console.WriteLine($"| Belt | `{b.ClassName}` | {b.Count} |");
    foreach (var l in r.LiftsByTier) Console.WriteLine($"| Lift | `{l.ClassName}` | {l.Count} |");
    foreach (var p in r.PipesByTier) Console.WriteLine($"| Pipe | `{p.ClassName}` | {p.Count} |");
    Console.WriteLine();

    Console.WriteLine("## World resources");
    Console.WriteLine();
    Console.WriteLine($"- Resource nodes: **{r.ResourceNodes}**");
    Console.WriteLine($"- Geyser nodes: **{r.Geysers}**");
    Console.WriteLine($"- Resource deposits: **{r.Deposits}**");
    Console.WriteLine($"- Fracking satellites: **{r.FrackingSatellites}**");
    Console.WriteLine();

    Console.WriteLine("## Top 25 actor classes");
    Console.WriteLine();
    Console.WriteLine("| Count | Class |");
    Console.WriteLine("|------:|-------|");
    foreach (var c2 in r.TopClasses) Console.WriteLine($"| {c2.Count} | `{c2.ClassName}` |");
    Console.WriteLine();

    Console.WriteLine("---");
    Console.WriteLine();
    Console.WriteLine("_Generated by `stocktake` (ERP.Satisfactory). Use `--json` for machine-readable output._");
}

static void PrintMinersSection(IReadOnlyList<MinerEntry> miners)
{
    if (miners.Count == 0) return;
    Console.WriteLine($"## Miners — total {miners.Count}");
    Console.WriteLine();

    var byClassAndResource = miners
        .GroupBy(m => (m.Class, m.Resource))
        .Select(g => (g.Key.Class, g.Key.Resource, Count: g.Count()))
        .OrderBy(g => g.Class, StringComparer.Ordinal).ThenBy(g => g.Resource, StringComparer.Ordinal)
        .ToList();
    Console.WriteLine("| Class | Resource | Count |");
    Console.WriteLine("|-------|----------|------:|");
    foreach (var g in byClassAndResource)
        Console.WriteLine($"| `{g.Class}` | {g.Resource} | {g.Count} |");
    Console.WriteLine();

    Console.WriteLine("| Class | Position (X, Y, Z) | Resource | Node ref |");
    Console.WriteLine("|-------|--------------------|----------|----------|");
    foreach (var m in miners)
        Console.WriteLine($"| `{m.Class}` | ({m.X}, {m.Y}, {m.Z}) | {m.Resource} | {m.NodeRef} |");
    Console.WriteLine();
}

static void PrintProducerSection(string title, IReadOnlyList<ProducerEntry> producers)
{
    if (producers.Count == 0) return;
    var total = producers.Sum(p => p.Count);
    Console.WriteLine($"## {title} — total {total}");
    Console.WriteLine();
    Console.WriteLine("| Class | Recipe | Count |");
    Console.WriteLine("|-------|--------|------:|");
    foreach (var p in producers)
        Console.WriteLine($"| `{p.Class}` | {p.Recipe} | {p.Count} |");
    Console.WriteLine();
}

static void PrintGenSection(string label, IReadOnlyList<FuelTallyEntry> tally)
{
    if (tally.Count == 0) return;
    var total = tally.Sum(t => t.Count);
    Console.WriteLine($"- **{label}**: {total}");
    foreach (var t in tally)
        Console.WriteLine($"  - {t.Fuel}: {t.Count}");
}

// --- DTOs --------------------------------------------------------------------

internal sealed record StocktakeReport
{
    public required string SavePath { get; init; }
    public required long ParsedInMs { get; init; }

    public required string Session { get; init; }
    public required string SaveName { get; init; }
    public required int SaveVersion { get; init; }
    public required int BuildVersion { get; init; }
    public required int PlayedSeconds { get; init; }
    public required DateTime SaveDateTimeUtc { get; init; }
    public required bool IsPartitionedWorld { get; init; }

    public required int Levels { get; init; }
    public required int TotalObjects { get; init; }
    public required int TotalActors { get; init; }

    public required IReadOnlyList<MinerEntry>    Miners { get; init; }
    public required IReadOnlyList<ProducerEntry> Smelters { get; init; }
    public required IReadOnlyList<ProducerEntry> Foundries { get; init; }
    public required IReadOnlyList<ProducerEntry> Constructors { get; init; }
    public required IReadOnlyList<ProducerEntry> Assemblers { get; init; }
    public required IReadOnlyList<ProducerEntry> Manufacturers { get; init; }
    public required IReadOnlyList<ProducerEntry> Refineries { get; init; }
    public required IReadOnlyList<ProducerEntry> Packagers { get; init; }
    public required IReadOnlyList<ProducerEntry> Blenders { get; init; }
    public required IReadOnlyList<ProducerEntry> HadronColliders { get; init; }

    public required GeneratorReport Generators { get; init; }

    public required IReadOnlyList<ExtractorEntry> WaterExtractors { get; init; }
    public required IReadOnlyList<ExtractorEntry> OilExtractors { get; init; }
    public required int FrackingExtractors { get; init; }

    public required IReadOnlyList<TieredEntry> BeltsByTier { get; init; }
    public required IReadOnlyList<TieredEntry> LiftsByTier { get; init; }
    public required IReadOnlyList<TieredEntry> PipesByTier { get; init; }

    public required int ResourceNodes { get; init; }
    public required int Geysers { get; init; }
    public required int Deposits { get; init; }
    public required int FrackingSatellites { get; init; }

    public required IReadOnlyList<ClassCount> TopClasses { get; init; }
}

internal sealed record ClassCount(string ClassName, int Count);
internal sealed record TieredEntry(string ClassName, int Count, IReadOnlyList<Pos> Positions);
internal sealed record Pos(int X, int Y, int Z);

internal sealed record MinerEntry
{
    public required string Class { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Z { get; init; }
    public required string Resource { get; init; }
    public required string NodeRef { get; init; }
}

internal sealed record ProducerEntry(string Class, string Recipe, int Count);
internal sealed record FuelTallyEntry(string Fuel, int Count);
internal sealed record ExtractorEntry(string Class, int X, int Y, int Z, string? Resource);
internal sealed record GeneratorReport
{
    public required IReadOnlyList<FuelTallyEntry> Coal { get; init; }
    public required IReadOnlyList<FuelTallyEntry> Fuel { get; init; }
    public required IReadOnlyList<FuelTallyEntry> Biomass { get; init; }
    public required int Geothermal { get; init; }
    public required IReadOnlyList<FuelTallyEntry> Nuclear { get; init; }
}
