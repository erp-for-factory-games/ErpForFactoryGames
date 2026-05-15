# SatisfactoryPakExtractor

One-shot extractor that reads vanilla world-fixed placement data from a local
Satisfactory install and emits the JSON datasets consumed by
[`Satisfactory.Save.KnownResourceNodes`](../../src/Satisfactory/Save/KnownResourceNodes.cs)
and [`Satisfactory.Save.KnownFlora`](../../src/Satisfactory/Save/KnownFlora.cs).

Re-run after every game patch — Coffee Stain occasionally moves nodes and
shifts flora placements with biome rework.

## Run

Resource nodes only:

```powershell
dotnet run --project tools/SatisfactoryPakExtractor -- `
    --paks "C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\FactoryGame\Content\Paks" `
    --out  src/Satisfactory/Save/Data/known-resource-nodes.json
```

Flora only:

```powershell
dotnet run --project tools/SatisfactoryPakExtractor -- `
    --paks "C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\FactoryGame\Content\Paks" `
    --flora-out src/Satisfactory/Save/Data/known-flora.json
```

Both in one run (~3-minute mount + walk):

```powershell
dotnet run --project tools/SatisfactoryPakExtractor -- `
    --paks "C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\FactoryGame\Content\Paks" `
    --out       src/Satisfactory/Save/Data/known-resource-nodes.json `
    --flora-out src/Satisfactory/Save/Data/known-flora.json
```

Options:
- `--paks <dir>` — pak directory (required).
- `--out <file>` — resource-node JSON output path.
- `--flora-out <file>` — flora JSON output path.
- `--flora-explore` — dump an actor-class histogram instead of writing flora
  JSON. Use this to refresh `FloraActorMap` in `Program.cs` if Coffee Stain
  rename the plant BP classes in a future patch.
- `--ue-version <EGame>` — override the UE5 version flag (default `GAME_UE5_6`).
- `--verbose` / `-v` — extra diagnostics (top file extensions, per-package
  skip reasons).

At least one of `--out`, `--flora-out`, or `--flora-explore` is required.

On first run it downloads `oodle-data-shared.dll` (Oodle decompressor) next
to the binary — needed for UE5 chunk decompression. The DLL is **not**
committed.

## Output schema

See [`Data/README.md`](../../src/Satisfactory/Save/Data/README.md). Keys: `x`,
`y`, `z` (cm), `resource` (`Desc_*_C`), `purity` (`Impure`/`Normal`/`Pure`).
The extractor also writes a diagnostic `class` field (`BP_ResourceNode_C`
etc.); the loader ignores unknown properties.

## CUE4Parse vendor pin

This tool depends on `CUE4Parse` master (vendored as a submodule at
[`vendor/CUE4Parse`](../../vendor/CUE4Parse/)) rather than the NuGet 1.2.2
release. The NuGet build can't parse Satisfactory 1.x's `FactoryGame-Windows.utoc`
container header — it throws `ParserException: Invalid bool value` in
`FIoContainerHeaderSoftPackageReferences..ctor` regardless of which `EGame`
flag is supplied. Master mounts the container cleanly.

Pinned commit: **`7ac7b29d799a1303c5e21198d87cf67ec8cafde2`** ("Snowbreak lua
decryption", picked at time of authoring this extractor). Bump by running
`git -C vendor/CUE4Parse pull origin master` and committing the new submodule
pointer. CUE4Parse fixes container-header layouts frequently; bumping is
expected after every major Satisfactory patch.

## UE5 version flag

Satisfactory ships on Coffee Stain's UE5.3.2 fork, but `GAME_UE5_3` overruns
serialized property reads with `VersionException`. Empirically the only flag
that parses `Persistent_Level` cells cleanly is `GAME_UE5_6` — Coffee Stain
appear to have backported newer-engine property-tag changes into their fork.
Bumping the default may be needed after future patches; override with
`--ue-version`.

## Coffee Stain quirks

- **`EResourcePurity::RP_Inpure`** (sic) — the in-game enum has a typo for
  the Impure value. The extractor accepts both `RP_Impure` and `RP_Inpure`.
- **Archetype default is `Normal`** — placements with `Normal` purity have
  their `mPurity` property elided per the UE serialisation rules; only
  `Impure` and `Pure` are stored explicitly. The extractor treats missing
  `mPurity` on a mining-node-class placement as `Normal`.
- **Deposits don't store `mResourceClass`** — `BP_ResourceDeposit_C`
  placements look up their resource via `mResourceDepositTableIndex`, which
  isn't on the placement instance either. The extractor counts deposits but
  drops them from the JSON; `SaveFileReader` handles deposits via a
  separate index table.

## Coverage

Against Satisfactory 1.x (build 444486) the extractor yields:

### Resource nodes

| Class                       | Count |
| --------------------------- | ----- |
| `BP_ResourceNode_C`         |   472 |
| `BP_ResourceNodeGeyser_C`   |    34 |
| `BP_FrackingCore_C`         |    18 |
| `BP_FrackingSatellite_C`    |   123 |
| **Total**                   | **647** |

(Plus 2,662 surface deposits which are intentionally dropped — see above.)

Purity split for mining nodes: ~25% Impure, ~45% Normal (inferred from
elided default), ~30% Pure. Resource distribution skews toward Iron (128),
Stone/Limestone (95), Coal (63), Water (63), Liquid Oil (58), Copper (56),
Nitrogen (51).

### Flora

Plant actor placements (each plant is an individual actor — vanilla flora
are **not** instanced foliage components, despite the issue #62 spike's
initial guess):

| BP class            | Placements | Species dropped                          |
| ------------------- | ---------: | ---------------------------------------- |
| `BP_BerryBush_C`    |      2,164 | `Desc_Berry_C` (Paleberry)               |
| `BP_NutBush_C`      |      1,525 | `Desc_Nut_C` (Beryl Nut)                 |
| `BP_Shroom_01_C`    |      1,615 | `Desc_Shroom_C` + `Desc_Mycelia_C`       |
| **Total actors**    |      5,304 |                                          |
| **JSON entries**    |      6,919 | (one entry per actor-species pair)       |

Bacon Agaric (`BP_Shroom_01_C`) drops both Bacon Agaric and Mycelia in-game,
so the extractor emits one entry per species — same coordinates, different
`species`. The planner can then answer "where is Mycelia?" without a manual
cross-reference.

If a future game patch renames the plant BP classes, the extractor will
silently emit zero flora entries. To diagnose, run `--flora-explore`: it
walks every map and prints a histogram of all `BP_*` / `FG*` actor classes
it saw. Update `FloraActorMap` in `Program.cs` to point at the new names.

## Project structure

- `SatisfactoryPakExtractor.csproj` — console project; pins net8.0 to match
  CUE4Parse's TFM. References `vendor/CUE4Parse/CUE4Parse/CUE4Parse.csproj`
  as a `ProjectReference` (no NuGet `CUE4Parse` entry).
- `Program.cs` — mount + iterate + emit. Heavily commented.

## Constraints

- OSS only. CUE4Parse is Apache-2.0; Oodle is closed-source but distributed
  by Epic for asset access and fetched at runtime (not committed).
- Never commit the pak file or the Oodle DLL — both are large and licence-
  encumbered.
- The Steam install path is *not* hardcoded in committed source; pass it via
  `--paks`.
