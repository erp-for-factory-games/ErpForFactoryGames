# SatisfactoryPakExtractor

One-shot extractor that reads vanilla resource-node placements from a local
Satisfactory install and emits the JSON dataset consumed by
[`Satisfactory.Save.KnownResourceNodes`](../../src/Satisfactory/Save/KnownResourceNodes.cs).

Re-run after every game patch — Coffee Stain occasionally moves nodes.

## Run

```powershell
dotnet run --project tools/SatisfactoryPakExtractor -- `
    --paks "C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\FactoryGame\Content\Paks" `
    --out  src/Satisfactory/Save/Data/known-resource-nodes.json
```

Options:
- `--paks <dir>` — pak directory (required).
- `--out <file>` — output JSON path (required).
- `--ue-version <EGame>` — override the UE5 version flag (default `GAME_UE5_6`).
- `--verbose` / `-v` — extra diagnostics (top file extensions only).

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
