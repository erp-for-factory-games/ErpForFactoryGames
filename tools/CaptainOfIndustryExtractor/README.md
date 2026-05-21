# CaptainOfIndustryExtractor

One-shot extractor that emits a JSON catalogue of products, recipes, and
buildings by loading Captain of Industry's shipped game assemblies and walking
the prototype DB **outside the Unity runtime**. The runtime app
([`src/CaptainOfIndustry/Catalog`](../../src/CaptainOfIndustry/Catalog/))
ingests the result.

Re-run after every game patch — Mafi ship new recipes and tweak existing
ones with each release.

## Run

```powershell
dotnet run --project tools/CaptainOfIndustryExtractor -- `
    --install "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry" `
    --out    "$env:LocalAppData\ErpForFactoryGames\coi-catalogue.json"
```

Options:
- `--install <dir>` — Captain of Industry root (containing `Captain of Industry_Data\`). Required.
- `--out <file>` — output JSON path. Default: `%LocalAppData%\ErpForFactoryGames\coi-catalogue.json`.
- `--verbose` / `-v` — extra diagnostics on stderr.
- `--help` / `-h` — usage.

On a typical run against vanilla CoI 0.8.4 the extractor emits ~227 products,
~500 recipes, ~84 production buildings, plus a couple of warnings (see
[Coverage](#coverage)).

## How it works

The methodology comes out of [issue #175](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/175):

1. Load `Mafi.dll`, `Mafi.Core.dll`, `Mafi.Base.dll` from the user's install
   into a `System.Runtime.Loader.AssemblyLoadContext` with a resolver pointing
   at `Captain of Industry_Data\Managed\`. CoI's net4.x Mono assemblies load
   into a .NET 10 host without complaint — no Unity runtime required.
2. Construct the minimum scaffold CoI's mod API needs:
   `ModManifest → BaseMod → ProtosDb → EntityLayoutParser → ProtoRegistrator`.
3. Invoke `BaseMod.IMod.RegisterPrototypes(registrator)` — the same entry
   point CoI uses to register its base content at game startup. This populates
   `ProtosDb`.
4. Walk `ProtosDb.All<ProductProto>()`, `All<RecipeProto>()`, `All<MachineProto>()`,
   project into a stable DTO shape, and serialise to JSON.

This is the *extractor* path — not the *mod* path. Per the
[#175 pivot](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/175#issuecomment-4503791461),
end-user friction wins: one `dotnet run` against the install dir, no need to
install a mod or launch CoI.

## License posture

We do **not** redistribute CoI's DLLs or the extracted JSON. The DLLs stay in
the user's Steam install; the extractor source lives in this repo; the JSON
is generated on the user's machine and lives in their per-user app data. Same
posture [ADR-0009](../../docs/adr/0009-runtime-ingestion-of-game-catalogue.md)
established for Satisfactory's `Docs.json`. CoI's stance is actually more
permissive (it ships an explicit modding API and Roslyn), but the
non-redistribution principle still applies — the products/recipes/buildings
are Mafi's creative content.

## Output schema

Versioned by `extractorVersion`. The runtime ingestion ([#177b](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/177))
reads against this contract. Breaking changes require a coordinated bump.

```json
{
  "extractorVersion": "0.4.0.0",
  "coiVersion": "0.8.4.0",
  "extractedAt": "2026-05-21T02:14:06+00:00",
  "items": [
    {
      "id": "Product_Wheat",
      "name": "Wheat",
      "kind": "Loose",
      "isStorable": true,
      "isWaste": false,
      "radioactivity": 0
    }
  ],
  "recipes": [
    {
      "id": "WheatMilling",
      "name": "Wheat milling",
      "durationTicks": 300,
      "inputs":  [{ "productId": "Product_Wheat", "quantity": 8 }],
      "outputs": [
        { "productId": "Product_Flour",      "quantity": 8 },
        { "productId": "Product_AnimalFeed", "quantity": 1 }
      ]
    }
  ],
  "buildings": [
    {
      "id": "ArcFurnace",
      "name": "Arc furnace",
      "electricityKw": 4000
    }
  ],
  "warnings": []
}
```

- `kind` values for products: `Countable` (items), `Fluid`, `Loose` (sand /
  ore / aggregate), `Molten` (molten metals), `Virtual` (abstract /
  computed).
- `quantity` is in CoI's internal unit. A unit of 50 corresponds to one belt
  slot / one visual item in-game. The runtime ingestion is responsible for
  presenting whatever units the planner UI prefers.
- `durationTicks` is in CoI sim ticks (40 ticks/second at default sim speed).

## Coverage

Against CoI **0.8.4.0** (vanilla, no DLCs installed) the extractor yields:

| Category | Count |
|----------|------:|
| Products (total)              | 227 |
| — `CountableProductProto`     | 109 |
| — `FluidProductProto`         |  40 |
| — `LooseProductProto`         |  56 |
| — `MoltenProductProto`        |   8 |
| — `VirtualProductProto`       |  14 |
| Recipes                       | 500 |
| Machines (production buildings) |  84 |

### Known limitations

- **Registration aborts midway** at the first `IModData` that fails to build.
  In vanilla 0.8.4 that's `ResearchLabsData`, which references the Islands
  DLC category `UpointsCat_IslandBuildings`. Everything registered *before*
  the failure is captured cleanly; everything *after* is silently lost. Per
  observation: products / recipes / machines all register before research
  trees, so the planner-relevant data is intact. Resilient per-`IModData`
  registration (skip-list with continuation) is tracked as a follow-up.
- **DLC content is not loaded.** The extractor loads `Mafi.Base.dll` only;
  any installed DLC `.dll` next to it is ignored for now. A future flag
  could enumerate and chain-load DLCs.
- **No icon / sprite / visual data.** Anything Unity-coupled
  (`UnityEngine.Sprite`, `Mesh`) is intentionally skipped — the planner
  doesn't need it for v1.
- **`ImmutableArray<T>` is Mafi's, not the BCL's.** Mafi ship their own
  `Mafi.Collections.ImmutableCollections.ImmutableArray<T>` which doesn't
  implement `System.Collections.IEnumerable`. The extractor enumerates via
  the type's own `ToArray()` method.

## Project structure

- `CaptainOfIndustryExtractor.csproj` — net10 console project. Only NuGet
  dep is `System.Reflection.MetadataLoadContext` (currently unused at
  runtime but kept for future schema-diff diagnostics).
- `Program.cs` — `Main` + arg parsing, `Extractor` class encapsulating the
  load → construct → register → walk → serialise pipeline, plus the JSON
  DTOs.

Not part of `ErpForFactoryGames.slnx` — parity with
[`tools/SatisfactoryPakExtractor`](../SatisfactoryPakExtractor/README.md).
Built via `dotnet build tools/CaptainOfIndustryExtractor/` directly.

## Constraints

- OSS only.
- Never commit CoI DLLs or the extracted JSON — both are licence-encumbered
  / IP.
- The Steam install path is *not* hardcoded in committed source; pass it via
  `--install`.
