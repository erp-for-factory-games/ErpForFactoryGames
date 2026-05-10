using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Domain;

namespace Satisfactory.Catalog;

/// <summary>
/// Parses the Satisfactory <c>Docs.json</c> shipped with the game install into
/// the in-memory catalogue model used by the planner.
/// </summary>
/// <remarks>
/// The schema drifts between game versions; this parser tolerates missing fields
/// rather than rejecting the file, and skips entries it cannot make sense of —
/// each skip is recorded in <see cref="ParsedCatalog.Warnings"/>.
/// Items, buildings, and recipes are extracted; fluids are treated as items
/// (they have their own dedicated epic later); extractors and generators are
/// skipped entirely (separate epic).
/// </remarks>
public static class DocsJsonParser
{
    private static readonly Regex IngredientRegex = new(
        @"\.([A-Za-z][A-Za-z0-9_]*_C)[^,]*,\s*Amount\s*=\s*(-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProducedInRegex = new(
        @"\.([A-Za-z][A-Za-z0-9_]*_C)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NativeClassRegex = new(
        @"FactoryGame\.([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ItemNativeClasses = new(StringComparer.Ordinal)
    {
        "FGItemDescriptor",
        "FGItemDescriptorBiomass",
        "FGItemDescriptorNuclearFuel",
        "FGItemDescriptorPowerBoosterFuel",
        "FGResourceDescriptor",
        "FGEquipmentDescriptor",
        "FGConsumableDescriptor",
        "FGAmmoTypeProjectile",
        "FGAmmoTypeInstantHit",
        "FGAmmoTypeSpreadshot",
    };

    /// <summary>
    /// Native classes whose entries are extracted from the world (ores, water,
    /// crude oil, nitrogen gas, …) — never produced via a manufacturing recipe.
    /// Recipes whose outputs include any of these are 1.0+ Converter recipes
    /// that transmute one resource into another at huge cost; we skip them so
    /// the planner doesn't pick them as default producers.
    /// </summary>
    private static readonly HashSet<string> RawResourceNativeClasses = new(StringComparer.Ordinal)
    {
        "FGResourceDescriptor",
    };

    /// <summary>
    /// Buildings whose recipes are skipped: they don't produce anything new,
    /// they shuffle existing items (Packager packages/unpackages fluids).
    /// </summary>
    private static readonly HashSet<string> SkippedRecipeBuildingClassNames = new(StringComparer.Ordinal)
    {
        "Build_Packager_C",
    };

    private static readonly HashSet<string> BuildingNativeClasses = new(StringComparer.Ordinal)
    {
        "FGBuildableManufacturer",
        "FGBuildableManufacturerVariablePower",
    };

    private static readonly HashSet<string> RecipeNativeClasses = new(StringComparer.Ordinal)
    {
        "FGRecipe",
    };

    // Crafting benches the player uses by hand — we ignore recipes that only run there.
    private static readonly HashSet<string> CraftingBenchClassNames = new(StringComparer.Ordinal)
    {
        "BP_WorkshopComponent_C",
        "BP_WorkBenchComponent_C",
        "FGBuildableAutomatedWorkBench_C",
    };

    public static ParsedCatalog Parse(Stream stream)
    {
        var text = ReadAllText(stream);
        using var doc = JsonDocument.Parse(text);
        return Parse(doc.RootElement);
    }

    public static ParsedCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    private static ParsedCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new FormatException("Docs.json root must be an array of { NativeClass, Classes } blocks.");

        var items = new List<Item>();
        var rawResources = new HashSet<ItemId>();
        var fluidItems = new HashSet<ItemId>();
        var buildings = new List<Building>();
        var recipeBlocks = new List<JsonElement>();
        var warnings = new List<string>();
        var totalBlocks = 0;
        var recognizedBlocks = 0;

        // First pass: items + buildings. Recipes are deferred so we can resolve building references.
        foreach (var block in root.EnumerateArray())
        {
            totalBlocks++;
            if (!block.TryGetProperty("NativeClass", out var nativeClassEl) ||
                !block.TryGetProperty("Classes", out var classesEl) ||
                classesEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var typeName = ExtractNativeTypeName(nativeClassEl.GetString());
            if (typeName is null) continue;

            if (ItemNativeClasses.Contains(typeName))
            {
                recognizedBlocks++;
                var isRaw = RawResourceNativeClasses.Contains(typeName);
                foreach (var entry in classesEl.EnumerateArray())
                {
                    if (TryParseItem(entry, warnings) is { } item)
                    {
                        items.Add(item);
                        if (isRaw) rawResources.Add(item.Id);
                        if (IsFluidForm(entry)) fluidItems.Add(item.Id);
                    }
                }
            }
            else if (BuildingNativeClasses.Contains(typeName))
            {
                recognizedBlocks++;
                foreach (var entry in classesEl.EnumerateArray())
                {
                    if (TryParseBuilding(entry, warnings) is { } building)
                        buildings.Add(building);
                }
            }
            else if (RecipeNativeClasses.Contains(typeName))
            {
                recognizedBlocks++;
                recipeBlocks.Add(classesEl);
            }
        }

        // If we saw blocks but recognized none of them, the file shape is wrong
        // OR the game has renamed every native class we know about. Either way,
        // the planner can't use this catalogue — fail loudly instead of silently
        // returning an empty result.
        if (totalBlocks > 0 && recognizedBlocks == 0)
        {
            throw new FormatException(
                $"Docs.json has {totalBlocks} block(s) but none of their NativeClass entries " +
                "match a known FGItemDescriptor / FGBuildableManufacturer / FGRecipe class. " +
                "This usually means an unsupported game version — capture a fixture and add a test.");
        }

        // Second pass: recipes (now that we know the buildings).
        var buildingByClassName = buildings.ToDictionary(b => b.Id.Value, StringComparer.Ordinal);
        var itemByClassName = items.ToDictionary(i => i.Id.Value, StringComparer.Ordinal);

        var recipes = new List<Recipe>();
        foreach (var classesEl in recipeBlocks)
        {
            foreach (var entry in classesEl.EnumerateArray())
            {
                if (TryParseRecipe(entry, buildingByClassName, itemByClassName, rawResources, fluidItems, warnings) is { } recipe)
                    recipes.Add(recipe);
            }
        }

        return new ParsedCatalog(items, buildings, recipes, rawResources.ToList(), warnings);
    }

    private static bool IsFluidForm(JsonElement entry)
    {
        if (!entry.TryGetProperty("mForm", out var form)) return false;
        var s = form.GetString();
        return s == "RF_LIQUID" || s == "RF_GAS";
    }

    private static Item? TryParseItem(JsonElement entry, List<string> warnings)
    {
        var className = entry.TryGetProperty("ClassName", out var classNameEl) ? classNameEl.GetString() : null;
        if (string.IsNullOrEmpty(className))
        {
            warnings.Add("Item entry missing ClassName; skipped.");
            return null;
        }

        var displayName = entry.TryGetProperty("mDisplayName", out var dn) ? dn.GetString() : null;
        return new Item(new ItemId(className), string.IsNullOrEmpty(displayName) ? className : displayName!);
    }

    private static Building? TryParseBuilding(JsonElement entry, List<string> warnings)
    {
        var className = entry.TryGetProperty("ClassName", out var classNameEl) ? classNameEl.GetString() : null;
        if (string.IsNullOrEmpty(className))
        {
            warnings.Add("Building entry missing ClassName; skipped.");
            return null;
        }

        var displayName = entry.TryGetProperty("mDisplayName", out var dn) ? dn.GetString() : null;
        var power = ParseDecimal(entry, "mPowerConsumption");
        return new Building(
            new BuildingId(className),
            string.IsNullOrEmpty(displayName) ? className : displayName!,
            (double)power);
    }

    private static Recipe? TryParseRecipe(
        JsonElement entry,
        IReadOnlyDictionary<string, Building> buildingByClassName,
        IReadOnlyDictionary<string, Item> itemByClassName,
        IReadOnlySet<ItemId> rawResources,
        IReadOnlySet<ItemId> fluidItems,
        List<string> warnings)
    {
        if (!entry.TryGetProperty("ClassName", out var classNameEl)) return null;
        var className = classNameEl.GetString();
        if (string.IsNullOrEmpty(className)) return null;

        var producedIn = entry.TryGetProperty("mProducedIn", out var pi) ? pi.GetString() : null;
        var building = ResolveBuilding(producedIn, buildingByClassName);
        if (building is null)
        {
            // Recipe is hand-crafted (workbench/workshop only) or runs in a building we don't know.
            // Either way, the planner can't use it.
            return null;
        }

        if (SkippedRecipeBuildingClassNames.Contains(building.Id.Value))
        {
            // Packager etc. — not a producer, just a logistics shuffle.
            return null;
        }

        var displayName = entry.TryGetProperty("mDisplayName", out var dn) ? dn.GetString() : className;
        var ingredients = ParseAmounts(
            entry.TryGetProperty("mIngredients", out var ing) ? ing.GetString() : null,
            itemByClassName,
            fluidItems,
            warnings,
            $"recipe {className} ingredients");
        var product = ParseAmounts(
            entry.TryGetProperty("mProduct", out var prod) ? prod.GetString() : null,
            itemByClassName,
            fluidItems,
            warnings,
            $"recipe {className} product");

        if (product.Count == 0)
        {
            // Building recipes (mProduct references FGBuildDescriptor classes) end up empty
            // because we don't catalogue buildable descriptors as Items. Skip silently.
            return null;
        }

        if (product.Any(p => rawResources.Contains(p.Item)))
        {
            // 1.0+ Converter recipes transmute resources into other resources
            // (e.g. Limestone → Iron Ore). They're not real production paths
            // for the planner and would explode demand if picked as defaults.
            return null;
        }

        var durationSeconds = ParseDecimal(entry, "mManufactoringDuration"); // sic, game spelling
        if (durationSeconds <= 0)
        {
            // Use a sane default so the per-minute math doesn't divide by zero.
            durationSeconds = 1m;
            warnings.Add($"Recipe {className} has no mManufactoringDuration; defaulting to 1s.");
        }

        var isAlternate =
            className.Contains("_Alternate_", StringComparison.Ordinal) ||
            (displayName?.StartsWith("Alternate:", StringComparison.OrdinalIgnoreCase) ?? false);

        return new Recipe(
            Id: new RecipeId(className),
            Name: string.IsNullOrEmpty(displayName) ? className : displayName!,
            Building: building.Id,
            Inputs: ingredients,
            Outputs: product,
            Duration: TimeSpan.FromSeconds((double)durationSeconds),
            IsAlternate: isAlternate);
    }

    private static Building? ResolveBuilding(string? producedIn, IReadOnlyDictionary<string, Building> buildings)
    {
        if (string.IsNullOrEmpty(producedIn)) return null;
        foreach (Match m in ProducedInRegex.Matches(producedIn))
        {
            var candidate = m.Groups[1].Value;
            if (CraftingBenchClassNames.Contains(candidate)) continue;
            if (buildings.TryGetValue(candidate, out var b)) return b;
        }
        return null;
    }

    private static IReadOnlyList<ItemAmount> ParseAmounts(
        string? raw,
        IReadOnlyDictionary<string, Item> items,
        IReadOnlySet<ItemId> fluidItems,
        List<string> warnings,
        string context)
    {
        if (string.IsNullOrEmpty(raw)) return [];
        var result = new List<ItemAmount>();
        foreach (Match m in IngredientRegex.Matches(raw))
        {
            var className = m.Groups[1].Value;
            var amountStr = m.Groups[2].Value;
            if (!decimal.TryParse(amountStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                warnings.Add($"{context}: cannot parse amount '{amountStr}'.");
                continue;
            }
            // We don't reject unknown items here — they may be FGBuildDescriptor entries
            // (building recipes) that we deliberately don't catalogue. The recipe is filtered
            // out later if its product list is empty.
            if (!items.ContainsKey(className))
            {
                // Likely a buildable descriptor (Build_*_C) appearing in mProduct. Skip silently;
                // it'll cause the recipe to be filtered out by the empty-product check.
                continue;
            }
            var itemId = new ItemId(className);
            // Docs.json stores fluid quantities as m³ × 1000 (the game's internal unit).
            // Normalise to m³/min so display + arithmetic stay consistent with solid items.
            if (fluidItems.Contains(itemId)) amount /= 1000m;
            result.Add(new ItemAmount(itemId, amount));
        }
        return result;
    }

    private static decimal ParseDecimal(JsonElement entry, string property)
    {
        if (!entry.TryGetProperty(property, out var el)) return 0m;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m,
        };
    }

    private static string? ExtractNativeTypeName(string? nativeClass)
    {
        if (string.IsNullOrEmpty(nativeClass)) return null;
        var match = NativeClassRegex.Match(nativeClass);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ReadAllText(Stream stream)
    {
        // Detect UTF-16 LE BOM (Satisfactory ships Docs.json as UTF-16 LE on Windows).
        var buffer = new byte[2];
        var read = stream.Read(buffer, 0, 2);
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

        Encoding encoding = Encoding.UTF8;
        if (read == 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
            encoding = Encoding.Unicode;
        else if (read == 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
            encoding = Encoding.BigEndianUnicode;

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
