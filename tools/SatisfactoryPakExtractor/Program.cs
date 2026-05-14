// Extracts vanilla resource-node placements (BP_ResourceNode_C and friends)
// from Satisfactory's shipped pak/utoc archive, emitting the curated dataset
// consumed by src/Satisfactory/Save/Data/known-resource-nodes.json.
//
// Run:
//   dotnet run --project tools/SatisfactoryPakExtractor -- \
//       --paks "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Satisfactory\\FactoryGame\\Content\\Paks" \
//       --out  src/Satisfactory/Save/Data/known-resource-nodes.json
//
// Re-run after every game patch — Coffee Stain occasionally moves nodes.

using System.Text.Json;
using Serilog;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace SatisfactoryPakExtractor;

internal static class Program
{
    // The five BP classes the planner cares about. KnownResourceNodes only
    // *uses* the mining/fracking ones; we still emit the rest for parity
    // and as a sanity check that we hit all expected actor types.
    private static readonly HashSet<string> ResourceNodeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "BP_ResourceNode_C",
        "BP_ResourceNodeGeyser_C",
        "BP_ResourceDeposit_C",
        "BP_FrackingCore_C",
        "BP_FrackingSatellite_C",
    };

    private static int Main(string[] args)
    {
        string? paksDir = null;
        string? outPath = null;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--paks" when i + 1 < args.Length:
                    paksDir = args[++i];
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

        if (string.IsNullOrEmpty(paksDir) || string.IsNullOrEmpty(outPath))
        {
            PrintUsage();
            return 1;
        }

        if (!Directory.Exists(paksDir))
        {
            Console.Error.WriteLine($"Paks directory not found: {paksDir}");
            return 2;
        }

        // CUE4Parse surfaces "failed to mount" reasons through Serilog; route
        // them at Verbose level so we can diagnose IoStore mount failures.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        // CUE4Parse decompresses most UE5-packaged assets via Oodle, which
        // is a closed-source native library not redistributable in source.
        // master's OodleHelper.Initialize() auto-downloads the right build
        // from the OodleUE GitHub release if not already present alongside
        // the binary. One-time per machine. The DLL is not committed.
        var oodlePath = Path.Combine(AppContext.BaseDirectory, OodleHelper.OodleFileName);
        try
        {
            OodleHelper.Initialize(oodlePath);
            Console.WriteLine($"Oodle ready: {oodlePath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialise Oodle: {ex.Message}");
            return 4;
        }

        Console.WriteLine($"Mounting paks from: {paksDir}");

        // Satisfactory 1.x runs on Coffee Stain's UE5.3.2 fork, but the
        // serialised property layout matches CUE4Parse's GAME_UE5_6 profile
        // — every other UE5_x flag overruns the export read window with a
        // VersionException. Probably Coffee Stain backports newer-engine
        // property-tag changes into their fork. Override via --ue-version
        // if a future game patch lands more changes.
        var ueVersion = GetUeVersionArg(args, EGame.GAME_UE5_6);
        Console.WriteLine($"UE5 version: {ueVersion}");
        var versions = new VersionContainer(ueVersion);
        var provider = new DefaultFileProvider(paksDir, SearchOption.TopDirectoryOnly, versions);
        provider.Initialize();

        // Satisfactory's main pak ships unencrypted; submit an empty key for
        // the default GUID so CUE4Parse marks every vfs as readable. If a
        // future patch encrypts the pak the SubmitKey call will throw —
        // wrap in try and surface the error to the operator.
        try
        {
            provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Empty AES key rejected: {ex.Message}");
            Console.Error.WriteLine("If the pak is now encrypted, find the public Satisfactory AES key");
            Console.Error.WriteLine("and plumb it through this tool. Aborting.");
            return 3;
        }

        // SubmitKey only mounts entries that need its GUID. IoStore .utoc
        // containers Satisfactory ships are unencrypted but stay in
        // UnloadedVfs after Initialize() — force a Mount() pass to pull
        // them in. Idempotent for already-mounted vfs.
        provider.Mount();
        provider.PostMount();

        Console.WriteLine($"Mounted vfs:    {provider.MountedVfs.Count}");
        Console.WriteLine($"Indexed files:  {provider.Files.Count}");
        foreach (var vfs in provider.MountedVfs)
        {
            Console.WriteLine($"  [mounted]  {vfs.Name,-32} type={vfs.GetType().Name} files={vfs.FileCount}");
        }
        foreach (var vfs in provider.UnloadedVfs)
        {
            var hasDirIdx = vfs.GetType().GetProperty("HasDirectoryIndex")?.GetValue(vfs);
            Console.WriteLine($"  [unloaded] {vfs.Name,-32} type={vfs.GetType().Name} encrypted={vfs.IsEncrypted} hasDirIdx={hasDirIdx}");
        }

        // Bail early if the content IoStore failed to mount. The loose pak
        // (FactoryGame-Windows.pak) only carries audio + UI; without the
        // .utoc IoStore there are no levels or BPs to scan. An IoStore in
        // UnloadedVfs with HasDirectoryIndex=true is a parser failure
        // signal — Coffee Stain's UE5 fork has its own container header
        // layout CUE4Parse doesn't yet recognize. See README.
        var hasUnmountedIoStoreWithIndex = provider.UnloadedVfs.Any(v =>
            v.GetType().Name == "IoStoreReader"
            && (v.GetType().GetProperty("HasDirectoryIndex")?.GetValue(v) as bool? ?? false));
        if (hasUnmountedIoStoreWithIndex || provider.MountedVfs.Count == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("IoStore container mount failed — check the Serilog warnings above.");
            Console.Error.WriteLine("The loose pak alone holds no level data, so extraction can't proceed.");
            Console.Error.WriteLine("See tools/SatisfactoryPakExtractor/README.md for known limitations.");
            return 5;
        }

        if (verbose)
        {
            var extCounts = provider.Files.Keys
                .Select(p => Path.GetExtension(p).ToLowerInvariant())
                .GroupBy(e => e)
                .OrderByDescending(g => g.Count())
                .Take(10);
            Console.WriteLine("Top file extensions:");
            foreach (var g in extCounts)
            {
                Console.WriteLine($"  {(string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key),-10} {g.Count()}");
            }

        }

        // Locate level/map assets. UE5 IoStore packages drop the .umap
        // extension on disk (everything is .uasset under the IoStore vfs),
        // but loose paks still carry .umap. Match both, plus a path filter
        // so we don't iterate every .uasset in the game.
        var mapFiles = provider.Files.Keys
            .Where(p =>
                p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)
                || (p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                    && p.Contains("/Map/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Console.WriteLine($"Candidate map files: {mapFiles.Count}");

        var entries = new List<JsonEntry>();
        var classCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedPackages = 0;
        var mapsWithHits = 0;

        foreach (var path in mapFiles)
        {
            try
            {
                if (!provider.TryLoadPackage(path, out var package) || package is null) continue;

                var hitsBefore = entries.Count;

                foreach (var export in package.GetExports())
                {
                    if (export is null) continue;

                    var className = export.ExportType;
                    if (string.IsNullOrEmpty(className) || !ResourceNodeClasses.Contains(className))
                    {
                        continue;
                    }

                    // Class-default-object (CDO) exports appear under names
                    // starting with "Default__"; we want placement instances.
                    if (export.Name?.StartsWith("Default__", StringComparison.Ordinal) ?? false)
                    {
                        continue;
                    }

                    var transform = TryReadWorldTransform(export);
                    if (transform is null) continue;

                    var resource = TryReadResourceClassName(export);
                    var purity = TryReadPurity(export);
                    if (!string.IsNullOrEmpty(purity))
                    {
                        _explicitPurityCounts[purity] = _explicitPurityCounts.GetValueOrDefault(purity) + 1;
                    }

                    // UE elides serialized properties that match the BP
                    // archetype default. Empirically (Satisfactory 1.x),
                    // resource-node placements only carry mPurity when it
                    // differs from the default, and the only values that
                    // appear in the data are Impure and Pure — the BP CDO
                    // sets Normal as the default. Anything missing is Normal.
                    if (string.IsNullOrEmpty(purity)
                        && (className == "BP_ResourceNode_C"
                            || className == "BP_ResourceNodeGeyser_C"
                            || className == "BP_FrackingCore_C"
                            || className == "BP_FrackingSatellite_C"))
                    {
                        purity = "Normal";
                    }

                    // BP_ResourceNodeGeyser_C always emits Geothermal "power".
                    // Recorded as Desc_Geyser_C for parity with other entries.
                    if (string.IsNullOrEmpty(resource) && className == "BP_ResourceNodeGeyser_C")
                    {
                        resource = "Desc_Geyser_C";
                    }

                    entries.Add(new JsonEntry
                    {
                        X = transform.Value.x,
                        Y = transform.Value.y,
                        Z = transform.Value.z,
                        Resource = resource,
                        Purity = purity,
                        Class = className,
                    });

                    classCounts[className] = classCounts.GetValueOrDefault(className) + 1;
                }

                if (entries.Count > hitsBefore) mapsWithHits++;
            }
            catch (Exception ex)
            {
                skippedPackages++;
                if (verbose)
                {
                    Console.Error.WriteLine($"Skipped {path}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Maps with placements: {mapsWithHits}");
        Console.WriteLine($"Skipped on error:     {skippedPackages}");
        Console.WriteLine($"Total placements:     {entries.Count}");
        Console.WriteLine();
        Console.WriteLine("Class counts:");
        foreach (var kvp in classCounts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {kvp.Key,-32} {kvp.Value}");
        }

        // Diagnostic: how many of the entries carry a resolved purity/resource.
        var resolvedResource = entries.Count(e => !string.IsNullOrEmpty(e.Resource));
        var resolvedPurity = entries.Count(e => !string.IsNullOrEmpty(e.Purity));
        Console.WriteLine();
        Console.WriteLine($"Entries with resource:  {resolvedResource}/{entries.Count}");
        Console.WriteLine($"Entries with purity:    {resolvedPurity}/{entries.Count}");
        if (_unmappedPurityNames.Count > 0)
        {
            Console.WriteLine("Unmapped purity FNames:");
            foreach (var kvp in _unmappedPurityNames.OrderByDescending(k => k.Value))
            {
                Console.WriteLine($"  {kvp.Key} -> {kvp.Value}");
            }
        }
        if (_explicitPurityCounts.Count > 0)
        {
            Console.WriteLine("Explicit purity reads (pre-inference):");
            foreach (var kvp in _explicitPurityCounts.OrderByDescending(k => k.Value))
            {
                Console.WriteLine($"  {kvp.Key,-10} {kvp.Value}");
            }
        }

        var wrote = WriteJson(outPath!, entries);
        Console.WriteLine();
        Console.WriteLine($"Wrote {wrote} entries to {outPath} (filtered from {entries.Count}; deposits + no-resource entries dropped).");
        return 0;
    }

    private static (double x, double y, double z)? TryReadWorldTransform(UObject export)
    {
        // Placement actors hold their transform on a child SceneComponent
        // referenced by the RootComponent property. The actor itself doesn't
        // expose RelativeLocation in the BP archetype.
        if (export.TryGetValue<FPackageIndex>(out var rootCompIdx, "RootComponent")
            && rootCompIdx is not null
            && !rootCompIdx.IsNull)
        {
            UObject? root = null;
            try { root = rootCompIdx.Load(); } catch { /* lazy load may fail on world-partition imports */ }
            if (root is not null && TryReadLocation(root, out var rootLoc))
            {
                return rootLoc;
            }
        }

        // Fallback: some exports inline the location.
        if (TryReadLocation(export, out var direct))
        {
            return direct;
        }

        return null;
    }

    private static bool TryReadLocation(UObject obj, out (double x, double y, double z) loc)
    {
        // UE5 uses double-precision FVector in serialized properties.
        if (obj.TryGetValue<FVector>(out var v, "RelativeLocation"))
        {
            loc = (v.X, v.Y, v.Z);
            return true;
        }
        loc = default;
        return false;
    }

    private static string? TryReadResourceClassName(UObject export)
    {
        // BP_ResourceNode_C exposes mResourceClass (UClass*). The wire form
        // is an FPackageIndex pointing at the Desc_*_C class. For BP-default
        // placements the field may be inherited from the archetype; in that
        // case the placement won't carry the property and we leave it null.
        if (export.TryGetValue<FPackageIndex>(out var idx, "mResourceClass")
            && idx is not null && !idx.IsNull)
        {
            var name = idx.Name;
            if (!string.IsNullOrEmpty(name) && name != "None")
            {
                return NormaliseClassName(name);
            }
        }

        return null;
    }

    private static string NormaliseClassName(string raw)
    {
        // The save format / planner expects names like "Desc_OreIron_C".
        // FPackageIndex.Name may surface as the bare class without the _C
        // suffix when it points to the package rather than the class.
        if (raw.EndsWith("_C", StringComparison.Ordinal)) return raw;
        return raw + "_C";
    }

    private static string? TryReadPurity(UObject export)
    {
        // mPurity is an EResourcePurity { Impure=0, Normal=1, Pure=2 } byte
        // property. Depending on how the property was serialised it surfaces
        // as an FName, byte, or int.
        if (export.TryGetValue<FName>(out var purityName, "mPurity"))
        {
            var mapped = PurityFromName(purityName.Text);
            if (mapped is null && !string.IsNullOrEmpty(purityName.Text))
            {
                _unmappedPurityNames[purityName.Text] = _unmappedPurityNames.GetValueOrDefault(purityName.Text) + 1;
            }
            return mapped;
        }
        if (export.TryGetValue<byte>(out var purityByte, "mPurity"))
        {
            return purityByte switch
            {
                0 => "Impure",
                1 => "Normal",
                2 => "Pure",
                _ => null,
            };
        }
        if (export.TryGetValue<int>(out var purityInt, "mPurity"))
        {
            return purityInt switch
            {
                0 => "Impure",
                1 => "Normal",
                2 => "Pure",
                _ => null,
            };
        }
        return null;
    }

    private static readonly Dictionary<string, int> _unmappedPurityNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> _explicitPurityCounts = new(StringComparer.Ordinal);

    private static string? PurityFromName(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // Format: "EResourcePurity::RP_Impure" / "RP_Normal" / "RP_Pure".
        // Coffee Stain ship the typo "RP_Inpure" in their EResourcePurity
        // enum (verified against Satisfactory 1.x .pak content); accept both
        // spellings so we don't drop ~180 impure-marked nodes per world.
        var last = text.LastIndexOf(':');
        var tail = last >= 0 ? text[(last + 1)..] : text;
        return tail switch
        {
            "RP_Impure" or "RP_Inpure" or "Impure" => "Impure",
            "RP_Normal" or "Normal" => "Normal",
            "RP_Pure" or "Pure" => "Pure",
            _ => null,
        };
    }

    private static int WriteJson(string path, List<JsonEntry> entries)
    {
        // KnownResourceNodes requires non-null Resource on every record (the
        // record's Resource property is a non-nullable string). Drop entries
        // where extraction couldn't resolve a resource — they'd cause a NRE
        // downstream and aren't useful to the planner.
        // BP_ResourceDeposit_C carries its resource via mResourceDepositTableIndex,
        // not mResourceClass, so we never resolve a name here. SaveFileReader
        // handles deposits separately; we drop them from the JSON to keep the
        // file lean and unambiguous.
        var sorted = entries
            .Where(e => !string.IsNullOrEmpty(e.Resource))
            .Where(e => e.Class != "BP_ResourceDeposit_C")
            .OrderBy(e => e.Resource, StringComparer.Ordinal)
            .ThenBy(e => e.Class, StringComparer.Ordinal)
            .ThenBy(e => e.X)
            .ThenBy(e => e.Y)
            .ThenBy(e => e.Z)
            .ToList();

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var e in sorted)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", Math.Round(e.X, 2));
            writer.WriteNumber("y", Math.Round(e.Y, 2));
            writer.WriteNumber("z", Math.Round(e.Z, 2));
            if (!string.IsNullOrEmpty(e.Resource))
            {
                writer.WriteString("resource", e.Resource);
            }
            if (!string.IsNullOrEmpty(e.Purity))
            {
                writer.WriteString("purity", e.Purity);
            }
            // Diagnostic-only: the KnownResourceNodes loader ignores unknown
            // properties. Keeping the BP class makes the JSON self-documenting.
            writer.WriteString("class", e.Class);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.Flush();
        return sorted.Count;
    }

    private static EGame GetUeVersionArg(string[] args, EGame fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--ue-version" && Enum.TryParse<EGame>(args[i + 1], true, out var v))
            {
                return v;
            }
        }
        return fallback;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/SatisfactoryPakExtractor -- \\");
        Console.WriteLine("    --paks <path-to-FactoryGame/Content/Paks> \\");
        Console.WriteLine("    --out  <output-json>");
        Console.WriteLine("  Flags:");
        Console.WriteLine("    --verbose / -v   Print per-package skip reasons.");
    }

    private sealed class JsonEntry
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public string? Resource { get; init; }
        public string? Purity { get; init; }
        public string Class { get; init; } = string.Empty;
    }
}
