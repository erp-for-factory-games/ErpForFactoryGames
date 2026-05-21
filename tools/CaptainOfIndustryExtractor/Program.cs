// Captain of Industry catalogue extractor.
//
// Loads CoI's shipped game assemblies (Mafi.dll, Mafi.Core.dll, Mafi.Base.dll)
// from the user's install, runs the base mod's RegisterPrototypes outside Unity,
// walks the populated ProtosDb, and emits a curated JSON catalogue consumed by
// src/CaptainOfIndustry/Catalog/ at runtime.
//
//   dotnet run --project tools/CaptainOfIndustryExtractor -- \
//       --install "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry" \
//       --out "%LOCALAPPDATA%\ErpForFactoryGames\coi-catalogue.json"
//
// Re-run after every game patch — Mafi ship new recipes and tweak existing
// ones with each release. See tools/CaptainOfIndustryExtractor/README.md for
// the methodology spike (#175), JSON shape, and known limitations.

using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaptainOfIndustryExtractor;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? installDir = null;
        string? outPath = null;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--install" when i + 1 < args.Length:
                    installDir = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (string.IsNullOrEmpty(installDir))
        {
            Console.Error.WriteLine("--install <Captain of Industry install dir> is required.");
            PrintUsage();
            return 1;
        }

        outPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErpForFactoryGames",
            "coi-catalogue.json");

        var managedDir = Path.Combine(installDir, "Captain of Industry_Data", "Managed");
        if (!Directory.Exists(managedDir))
        {
            Console.Error.WriteLine($"Managed dir not found: {managedDir}");
            return 2;
        }

        try
        {
            var catalogue = new Extractor(managedDir, verbose).Run();

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            using var fs = File.Create(outPath);
            JsonSerializer.Serialize(fs, catalogue, JsonOptions);

            Console.WriteLine($"  -> wrote {outPath}");
            Console.WriteLine($"     {catalogue.Items.Count} products, {catalogue.Recipes.Count} recipes, {catalogue.Buildings.Count} buildings, {catalogue.Warnings.Count} warnings");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extraction failed: {ex.GetType().FullName}: {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex);
            return 10;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static void PrintUsage()
    {
        Console.WriteLine("""
            CaptainOfIndustryExtractor — emits a JSON catalogue of products, recipes,
            and buildings by loading CoI's assemblies and walking the prototype DB
            outside Unity. Run once per game patch.

            Usage:
              dotnet run --project tools/CaptainOfIndustryExtractor -- \
                  --install <CoI install dir> \
                  [--out <catalogue.json>] \
                  [--verbose]

            Options:
              --install <dir>   Captain of Industry root (containing Captain of Industry_Data\)
              --out <file>      Output JSON (default: %LocalAppData%\ErpForFactoryGames\coi-catalogue.json)
              --verbose, -v     Extra diagnostics on stderr
              --help, -h        Show this help
            """);
    }
}

internal sealed class Extractor
{
    private readonly string _managedDir;
    private readonly bool _verbose;
    private readonly AssemblyLoadContext _alc;
    private Assembly _mafi = null!;
    private Assembly _core = null!;
    private Assembly _base = null!;

    public Extractor(string managedDir, bool verbose)
    {
        _managedDir = managedDir;
        _verbose = verbose;
        _alc = new AssemblyLoadContext("coi", isCollectible: false);
        _alc.Resolving += (ctx, name) =>
        {
            var candidate = Path.Combine(_managedDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };
    }

    public CatalogueDto Run()
    {
        _mafi = _alc.LoadFromAssemblyPath(Path.Combine(_managedDir, "Mafi.dll"));
        _core = _alc.LoadFromAssemblyPath(Path.Combine(_managedDir, "Mafi.Core.dll"));
        _base = _alc.LoadFromAssemblyPath(Path.Combine(_managedDir, "Mafi.Base.dll"));

        var coiVersion = _core.GetName().Version?.ToString() ?? "unknown";
        Log($"Loaded Mafi/Mafi.Core/Mafi.Base — CoI build {coiVersion}");

        var (protosDb, warnings) = RunRegistration();

        var buildings = ExtractBuildings(protosDb, out var recipeToBuilding);

        return new CatalogueDto
        {
            ExtractorVersion = typeof(Extractor).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            CoiVersion = coiVersion,
            ExtractedAt = DateTimeOffset.UtcNow,
            Items = ExtractProducts(protosDb),
            Recipes = ExtractRecipes(protosDb, recipeToBuilding),
            Buildings = buildings,
            Warnings = warnings,
        };
    }

    // ---------------------------------------------------------------------
    // Registration — instantiate BaseMod and invoke RegisterPrototypes.
    // Per the #175 spike, this is the path that proved feasible outside Unity.
    // ---------------------------------------------------------------------

    private (object protosDb, List<string> warnings) RunRegistration()
    {
        var warnings = new List<string>();

        Type T(string name) => _mafi.GetType(name) ?? _core.GetType(name) ?? _base.GetType(name)
            ?? throw new InvalidOperationException($"Type not found: {name}");

        var baseModT = T("Mafi.Base.BaseMod");
        var baseModCfgT = T("Mafi.Base.BaseModConfig");
        var manifestT = T("Mafi.Core.Mods.ModManifest");
        var protosDbT = T("Mafi.Core.Prototypes.ProtosDb");
        var protoRegT = T("Mafi.Core.Mods.ProtoRegistrator");
        var layoutParserT = T("Mafi.Core.Entities.Static.Layout.EntityLayoutParser");
        var iModT = T("Mafi.Core.Mods.IMod");
        var versionSlimT = T("Mafi.VersionSlim");

        var versionSlim = Construct(versionSlimT, [1, 0, 0, 0]);
        var manifest = BuildManifest(manifestT, versionSlim);
        var baseCfg = Activator.CreateInstance(baseModCfgT)!;
        var baseMod = Construct(baseModT, [manifest, baseCfg]);
        var protosDb = Construct(protosDbT, [baseMod]);
        var layoutParser = Construct(layoutParserT, [protosDb]);
        var registrator = BuildProtoRegistrator(protoRegT, protosDb, layoutParser, baseMod, baseModT, iModT);

        var registerMethod = baseModT.GetMethod("RegisterPrototypes", [protoRegT])
            ?? FindInterfaceMethod(baseModT, iModT, "RegisterPrototypes")
            ?? throw new InvalidOperationException("RegisterPrototypes not located on BaseMod");

        try
        {
            registerMethod.Invoke(baseMod, [registrator]);
            Log("BaseMod.RegisterPrototypes completed cleanly");
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            var msg = $"Registration stopped midway: {inner.GetType().Name}: {inner.Message}";
            warnings.Add(msg);
            if (inner.InnerException is not null)
                warnings.Add($"  cause: {inner.InnerException.GetType().Name}: {inner.InnerException.Message}");
            Log(msg);
        }

        return (protosDb, warnings);
    }

    private object BuildManifest(Type manifestT, object versionSlim)
    {
        var ctor = manifestT.GetConstructors().Single();
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            args[i] = ps[i].Name switch
            {
                "rootDirectoryPath" => _managedDir,
                "id" => "Mafi.Base",
                "version" => versionSlim,
                "displayName" => "Mafi.Base",
                "descriptionShort" => "",
                "descriptionLong" => "",
                "minCoiVersion" => versionSlim,
                "maxVerifiedCoiVersion" => versionSlim,
                "assetBundlesDirOverride" => "",
                "primaryModClassName" => "Mafi.Base.BaseMod",
                "primaryDlls" => EmptyMafiImArr(typeof(string)),
                "authors" => EmptyMafiImArr(typeof(string)),
                "links" => EmptyMafiImArr(typeof(string)),
                "mandatoryDependencies" => EmptyMafiImArr(_core.GetType("Mafi.Core.Mods.ModDependency")!),
                "optionalDependencies" => EmptyMafiImArr(_core.GetType("Mafi.Core.Mods.ModDependency")!),
                "incompatibleModIds" => EmptyMafiImArr(typeof(string)),
                "loadErrors" => EmptyMafiImArr(typeof(string)),
                "canAddToSavedGame" => true,
                "canRemoveFromSavedGame" => true,
                "nonLockingDllLoad" => false,
                _ => ps[i].HasDefaultValue ? ps[i].DefaultValue : null,
            };
        }
        return ctor.Invoke(args);
    }

    private object BuildProtoRegistrator(Type protoRegT, object protosDb, object layoutParser, object baseMod, Type baseModT, Type iModT)
    {
        var ctor = protoRegT.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var pt = ps[i].ParameterType;
            if (pt.IsAssignableFrom(protosDb.GetType())) { args[i] = protosDb; continue; }
            if (pt.IsAssignableFrom(layoutParser.GetType())) { args[i] = layoutParser; continue; }
            if (pt.IsGenericType && pt.Name.StartsWith("ImmutableArray", StringComparison.Ordinal))
            {
                var elem = pt.GetGenericArguments()[0];
                var arr = EmptyMafiImArr(elem);
                if (elem.IsAssignableFrom(baseModT))
                {
                    var addM = arr.GetType().GetMethod("Add", [elem])!;
                    arr = addM.Invoke(arr, [baseMod])!;
                }
                args[i] = arr;
                continue;
            }
            args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
        }
        return ctor.Invoke(args);
    }

    private object EmptyMafiImArr(Type elementType)
    {
        var factory = _mafi.GetType("Mafi.Collections.ImmutableCollections.ImmutableArray")
            ?? throw new InvalidOperationException("Mafi.Collections.ImmutableCollections.ImmutableArray not found");

        // Mafi's static factory class — try Create<T>() / Empty<T>() / CreateEmpty<T>().
        var create = factory.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.IsGenericMethodDefinition
                              && m.GetParameters().Length == 0
                              && m.Name is "Create" or "Empty" or "CreateEmpty");
        if (create is not null)
            return create.MakeGenericMethod(elementType).Invoke(null, null)!;

        // Fall back to the generic type's Empty static member.
        var genericArrayT = _mafi.GetType("Mafi.Collections.ImmutableCollections.ImmutableArray`1")
            ?? throw new InvalidOperationException("Mafi.Collections.ImmutableCollections.ImmutableArray`1 not found");
        var constructed = genericArrayT.MakeGenericType(elementType);

        var emptyField = constructed.GetField("Empty", BindingFlags.Public | BindingFlags.Static);
        if (emptyField is not null) return emptyField.GetValue(null)!;

        var emptyProp = constructed.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
        if (emptyProp is not null) return emptyProp.GetValue(null)!;

        throw new InvalidOperationException($"Cannot build empty Mafi.ImmutableArray<{elementType.Name}>");
    }

    private static object Construct(Type type, object?[] args)
    {
        var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(c =>
            {
                var ps = c.GetParameters();
                if (ps.Length != args.Length) return false;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (args[i] is null) continue;
                    if (!ps[i].ParameterType.IsInstanceOfType(args[i])) return false;
                }
                return true;
            })
            ?? throw new InvalidOperationException($"No matching ctor on {type.FullName} for {args.Length} args");
        return ctor.Invoke(args);
    }

    private static MethodInfo? FindInterfaceMethod(Type implType, Type interfaceType, string methodName)
    {
        var map = implType.GetInterfaceMap(interfaceType);
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
            if (map.InterfaceMethods[i].Name == methodName)
                return map.TargetMethods[i];
        return null;
    }

    // ---------------------------------------------------------------------
    // ProtosDb walk + DTO projection.
    // ---------------------------------------------------------------------

    private List<ProductDto> ExtractProducts(object protosDb)
    {
        var productProtoT = _core.GetType("Mafi.Core.Products.ProductProto")!;
        var result = new List<ProductDto>();
        foreach (var product in EnumerateAll(protosDb, productProtoT))
        {
            var t = product.GetType();
            result.Add(new ProductDto
            {
                Id = ReadString(product, "Id") ?? "",
                Name = ReadString(ReadMember(product, "Strings"), "Name") ?? "",
                Kind = t.Name.EndsWith("ProductProto", StringComparison.Ordinal)
                    ? t.Name[..^"ProductProto".Length]
                    : t.Name,
                IsStorable = ReadBool(product, "IsStorable"),
                IsWaste = ReadBool(product, "IsWaste"),
                Radioactivity = ReadInt(product, "Radioactivity"),
            });
        }
        Log($"  products: {result.Count}");
        return result.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    private List<RecipeDto> ExtractRecipes(object protosDb, Dictionary<string, string> recipeToBuilding)
    {
        var recipeProtoT = _core.GetType("Mafi.Core.Factory.Recipes.RecipeProto")!;
        var result = new List<RecipeDto>();
        foreach (var recipe in EnumerateAll(protosDb, recipeProtoT))
        {
            var id = ReadString(recipe, "Id") ?? "";
            result.Add(new RecipeDto
            {
                Id = id,
                Name = ReadString(ReadMember(recipe, "Strings"), "Name") ?? "",
                Building = recipeToBuilding.GetValueOrDefault(id),
                DurationTicks = ReadInt(ReadMember(recipe, "Duration"), "Ticks"),
                Inputs = ExtractRecipeProducts(recipe, "AllInputs"),
                Outputs = ExtractRecipeProducts(recipe, "AllOutputs"),
            });
        }
        Log($"  recipes:  {result.Count}");
        return result.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    private static List<RecipeProductDto> ExtractRecipeProducts(object recipe, string memberName)
    {
        var list = new List<RecipeProductDto>();
        var collection = ReadMember(recipe, memberName);
        foreach (var rp in EnumerateMafiArray(collection))
        {
            var product = ReadMember(rp, "Product");
            list.Add(new RecipeProductDto
            {
                ProductId = ReadString(product, "Id") ?? "",
                Quantity = ReadInt(ReadMember(rp, "Quantity"), "Value"),
            });
        }
        return list;
    }

    /// <summary>
    /// Mafi's <c>ImmutableArray&lt;T&gt;</c> doesn't implement BCL <c>IEnumerable</c>.
    /// Use its <c>ToArray()</c> method which yields a regular <c>T[]</c> we can iterate.
    /// </summary>
    private static IEnumerable<object?> EnumerateMafiArray(object? mafiArray)
    {
        if (mafiArray is null) yield break;
        var toArray = mafiArray.GetType().GetMethod("ToArray", Type.EmptyTypes);
        if (toArray is null) yield break;
        var arr = toArray.Invoke(mafiArray, null) as Array;
        if (arr is null) yield break;
        foreach (var item in arr) yield return item;
    }

    /// <summary>
    /// Iterate via BCL <c>IEnumerable</c> if available (works for arrays, lists,
    /// <c>IReadOnlyList&lt;T&gt;</c>); else fall through to Mafi's
    /// <c>ImmutableArray</c> protocol.
    /// </summary>
    private static IEnumerable<object?> EnumerateAny(object? collection)
    {
        if (collection is null) yield break;
        if (collection is IEnumerable seq)
        {
            foreach (var item in seq) yield return item;
            yield break;
        }
        foreach (var item in EnumerateMafiArray(collection)) yield return item;
    }

    private List<BuildingDto> ExtractBuildings(object protosDb, out Dictionary<string, string> recipeToBuilding)
    {
        // MachineProto is the most useful "building" type for the planner; the
        // wider StaticEntityProto includes terrain, vehicles, etc.
        var machineProtoT = _core.GetType("Mafi.Core.Factory.Machines.MachineProto")!;
        recipeToBuilding = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<BuildingDto>();
        foreach (var building in EnumerateAll(protosDb, machineProtoT))
        {
            var buildingId = ReadString(building, "Id") ?? "";
            var recipeIds = new List<string>();
            foreach (var recipe in EnumerateAny(ReadMember(building, "Recipes")))
            {
                var recipeId = ReadString(recipe, "Id");
                if (string.IsNullOrEmpty(recipeId)) continue;
                recipeIds.Add(recipeId);
                // Each recipe is owned by exactly one machine in CoI's data model
                // (T1 / T2 tiers are separate recipes), so first-wins is fine.
                recipeToBuilding.TryAdd(recipeId, buildingId);
            }
            recipeIds.Sort(StringComparer.Ordinal);

            result.Add(new BuildingDto
            {
                Id = buildingId,
                Name = ReadString(ReadMember(building, "Strings"), "Name") ?? "",
                ElectricityKw = ReadInt(ReadMember(building, "ElectricityConsumed"), "Value"),
                Recipes = recipeIds,
            });
        }
        Log($"  buildings: {result.Count} ({recipeToBuilding.Count} recipes mapped to buildings)");
        return result.OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<object> EnumerateAll(object protosDb, Type protoType)
    {
        var allM = protosDb.GetType().GetMethod("All", [typeof(Type)])
            ?? throw new InvalidOperationException("ProtosDb.All(Type) not found");
        var seq = (IEnumerable)allM.Invoke(protosDb, [protoType])!;
        foreach (var item in seq)
            if (item is not null)
                yield return item;
    }

    // ---------------------------------------------------------------------
    // Reflection helpers that tolerate the Proto hierarchy's shadowed members.
    // ---------------------------------------------------------------------

    private static object? ReadMember(object? obj, string name)
    {
        if (obj is null) return null;
        for (var t = obj.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p is not null) return p.GetValue(obj);
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (f is not null) return f.GetValue(obj);
        }
        return null;
    }

    private static string? ReadString(object? obj, string name) => ReadMember(obj, name)?.ToString();
    private static bool ReadBool(object? obj, string name) => ReadMember(obj, name) is bool b && b;
    private static int ReadInt(object? obj, string name) => ReadMember(obj, name) switch
    {
        int i => i,
        long l => (int)l,
        _ => 0,
    };

    private void Log(string msg)
    {
        if (_verbose) Console.Error.WriteLine(msg);
    }
}

// ---------------------------------------------------------------------
// JSON shape — bumping any field here is a versioned schema change. The
// runtime ingestion (#177b) reads against this contract.
// ---------------------------------------------------------------------

internal sealed class CatalogueDto
{
    public string ExtractorVersion { get; set; } = "";
    public string CoiVersion { get; set; } = "";
    public DateTimeOffset ExtractedAt { get; set; }
    public List<ProductDto> Items { get; set; } = new();
    public List<RecipeDto> Recipes { get; set; } = new();
    public List<BuildingDto> Buildings { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

internal sealed class ProductDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IsStorable { get; set; }
    public bool IsWaste { get; set; }
    public int Radioactivity { get; set; }
}

internal sealed class RecipeDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Id of the machine that runs this recipe. Null if no machine claimed it.</summary>
    public string? Building { get; set; }
    public int DurationTicks { get; set; }
    public List<RecipeProductDto> Inputs { get; set; } = new();
    public List<RecipeProductDto> Outputs { get; set; } = new();
}

internal sealed class RecipeProductDto
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
}

internal sealed class BuildingDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ElectricityKw { get; set; }
    /// <summary>Ids of recipes this building can run.</summary>
    public List<string> Recipes { get; set; } = new();
}
