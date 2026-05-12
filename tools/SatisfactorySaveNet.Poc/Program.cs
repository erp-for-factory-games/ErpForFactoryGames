using ERP.Application;
using ERP.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SatisfactorySaveNet;
using SatisfactorySaveNet.Abstracts.Model;

const string DefaultSaveDir = @"C:\Users\ChrisSimon\AppData\Local\FactoryGame\Saved\SaveGames\76561198103946376";

var savePath = args.Length > 0 ? args[0] : FindLatestSave(DefaultSaveDir);
if (savePath is null)
{
    Console.Error.WriteLine($"No .sav files found under {DefaultSaveDir}");
    return 1;
}

Console.WriteLine($"Loading: {savePath}");
Console.WriteLine();

// === Path 1: raw library output (sanity check that v1.2 still parses) ===
RawDiagnostic(savePath);

// === Path 2: live IFactoryStateProvider end-to-end through DI ===
Console.WriteLine();
Console.WriteLine("=== IFactoryStateProvider (adapter under DI) ===");

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> { ["FactoryState:Satisfactory:SavePath"] = savePath }
).Build());
services.AddErpInfrastructure(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

using var sp = services.BuildServiceProvider();
var provider = sp.GetRequiredService<IFactoryStateProvider>();

var status = provider.GetStatus();
Console.WriteLine($"  IsLoaded:       {status.IsLoaded}");
Console.WriteLine($"  Source:         {status.Source}");
Console.WriteLine($"  Session:        {status.SessionName}");
Console.WriteLine($"  Save version:   {status.SaveVersion}  (build {status.BuildVersion})");
Console.WriteLine($"  Save UTC:       {status.SaveDateTimeUtc:O}");
Console.WriteLine();
Console.WriteLine($"  Miners:         {status.MinerCount}");
Console.WriteLine($"  Buildings:      {status.BuildingCount}");
Console.WriteLine($"  Belts:          {status.BeltCount}");
Console.WriteLine($"  Generators:     {status.GeneratorCount}");
Console.WriteLine($"  Resource nodes: {status.ResourceNodeCount}");

var s = provider.Current;
Console.WriteLine();
Console.WriteLine("=== Miners by tier ===");
foreach (var g in s.Miners.GroupBy(m => m.Tier).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("=== Buildings by type ===");
foreach (var g in s.Buildings.GroupBy(b => b.Building.Value).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {g.Count(),5:N0}  {g.Key}");

Console.WriteLine();
Console.WriteLine("=== Belts by tier ===");
foreach (var g in s.Belts.GroupBy(b => b.Tier).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("=== Generators by kind ===");
foreach (var g in s.Generators.GroupBy(g => g.Kind).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("=== RAW PROPERTY DUMP (one sample per actor type) ===");
var save2 = SatisfactorySaveNet.SaveFileSerializer.Instance.Deserialize(savePath);
if (save2.Body is SatisfactorySaveNet.Abstracts.Model.BodyV8 b2)
{
    var actors = b2.Levels.SelectMany(l => l.Objects).OfType<SatisfactorySaveNet.Abstracts.Model.ActorObject>().ToList();
    DumpActor(actors.FirstOrDefault(a => a.TypePath?.Contains("Build_MinerMk1_C") == true), "Miner");
    DumpActor(actors.FirstOrDefault(a => a.TypePath?.Contains("Build_SmelterMk1_C") == true), "Smelter");
    DumpActor(actors.FirstOrDefault(a => a.TypePath?.Contains("Build_ConstructorMk1_C") == true), "Constructor");
    DumpActor(actors.FirstOrDefault(a => a.TypePath?.Contains("BP_ResourceNode_C") == true), "ResourceNode (BP_ResourceNode_C)");
    DumpActor(actors.FirstOrDefault(a => a.TypePath?.Contains("BP_ResourceDeposit_C") == true), "ResourceDeposit");
}

Console.WriteLine();
Console.WriteLine("=== Resource nodes by purity ===");
foreach (var g in s.ResourceNodes.GroupBy(n => n.Purity).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("=== Resource nodes by resource (top 10) ===");
foreach (var g in s.ResourceNodes
    .Where(n => n.Resource is not null)
    .GroupBy(n => n.Resource!.Value)
    .OrderByDescending(g => g.Count())
    .Take(10))
    Console.WriteLine($"  {g.Count(),5}  {g.Key}");

Console.WriteLine();
Console.WriteLine("=== Miners with bound resource node ===");
var minerNodeBound = s.Miners.Count(m => !string.IsNullOrEmpty(m.ResourceNodeReference));
Console.WriteLine($"  {minerNodeBound} / {s.Miners.Count}");

Console.WriteLine();
Console.WriteLine("=== Buildings by clock speed bucket ===");
foreach (var g in s.Buildings.GroupBy(b => ClockBucket(b.ClockSpeed)).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("=== Buildings with bound recipe ===");
var buildingsWithRecipe = s.Buildings.Count(b => b.Recipe is not null);
Console.WriteLine($"  {buildingsWithRecipe} / {s.Buildings.Count}");
Console.WriteLine("  Top 5 recipes:");
foreach (var g in s.Buildings.Where(b => b.Recipe is not null)
    .GroupBy(b => b.Recipe!.Value).OrderByDescending(g => g.Count()).Take(5))
    Console.WriteLine($"    {g.Count(),3}  {g.Key}");

return 0;

static string ClockBucket(decimal cs)
{
    if (cs == 1.0m) return "100% (default)";
    if (cs < 0.5m) return $"< 50% ({cs:P0})";
    if (cs < 1.0m) return $"underclocked ({cs:P0})";
    return $"overclocked ({cs:P0})";
}

static void DumpActor(SatisfactorySaveNet.Abstracts.Model.ActorObject? actor, string label)
{
    Console.WriteLine();
    if (actor is null) { Console.WriteLine($"  [{label}] none found."); return; }
    Console.WriteLine($"  [{label}] TypePath={actor.TypePath}");
    Console.WriteLine($"    Properties ({actor.Properties.Count}):");
    foreach (var p in actor.Properties)
    {
        var t = p.GetType().Name;
        string v = "(?)";
        if (p is SatisfactorySaveNet.Abstracts.Model.Properties.RawProperty r)
        {
            t = $"Raw[{r.Type}]";
            if (r.ObjectValue is { } ov) v = $"obj:\"{ov.PathName}\"";
            else if (r.FloatValue is { } fv) v = $"float:{fv}";
            else if (r.StringValue is { } sv) v = $"str:\"{sv}\"";
            else if (r.IntValue is { } iv) v = $"int:{iv}";
            else if (r.BoolValue is { } bv) v = $"bool:{bv}";
            else if (r.LongValue is { } lv) v = $"long:{lv}";
            else if (r.SByteValue is { } sbv) v = $"sbyte:{sbv}";
            else v = $"(BinarySize={r.BinarySize}, no typed slot)";
        }
        Console.WriteLine($"      {p.Name,-40}  {t,-30}  {v}");
    }
}

static string? FindLatestSave(string dir)
{
    if (!Directory.Exists(dir)) return null;
    return new DirectoryInfo(dir)
        .GetFiles("*.sav")
        .OrderByDescending(f => f.LastWriteTime)
        .FirstOrDefault()?.FullName;
}

static void RawDiagnostic(string savePath)
{
    using var headerStream = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var headerReader = new BinaryReader(headerStream);
    var headerOnly = HeaderSerializer.Instance.Deserialize(headerReader);
    Console.WriteLine("=== Header (raw library) ===");
    Console.WriteLine($"  HeaderVersion={headerOnly.HeaderVersion}  SaveVersion={headerOnly.SaveVersion}  Build={headerOnly.BuildVersion}");
    Console.WriteLine($"  Session={headerOnly.SessionName}  Played={TimeSpan.FromSeconds(headerOnly.PlayedSeconds)}  SaveUtc={headerOnly.SaveDateTimeUtc:O}");
}
