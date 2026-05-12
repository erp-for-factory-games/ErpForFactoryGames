# Known resource node data

Static lookup table mapping resource node coordinates to their
`(Resource, Purity)` for vanilla Satisfactory worlds. Consumed by
[`KnownResourceNodes`](../KnownResourceNodes.cs); referenced from
SaveFileReader to fill in fields the `.sav` doesn't carry directly.

## Why a static table

`BP_ResourceNode_C` actors in the save file carry only `mResourcesLeft`
— their resource type (`mResourceClass`) and purity (`mPurity`) are
blueprint-class defaults baked into the game's `.pak` assets, not
serialized into the save. Without an external map, we can't tell what's
in the ground at a given position.

## Format

`known-resource-nodes.json` is a JSON array of entries:

```json
[
  {
    "x": -50000,
    "y": 260000,
    "z": -2500,
    "resource": "Desc_OreIron_C",
    "purity": "Normal"
  }
]
```

- **`x` / `y` / `z`** — world-space coordinates in centimetres (Unreal units).
- **`resource`** — the `ItemId` of the resource. Matches the class name from
  the catalogue (e.g. `Desc_OreIron_C`, `Desc_OreCopper_C`, `Desc_Stone_C`,
  `Desc_Coal_C`, `Desc_LiquidOil_C`).
- **`purity`** — one of `Impure`, `Normal`, `Pure`. (`Unknown` is the
  fallback when no entry matches a node, so don't list nodes as Unknown.)

Lookups match the *nearest* entry within a tolerance (default 500 cm /
5 m). Vanilla nodes are tens of metres apart, so 5 m is plenty without
risk of mis-matching neighbours.

## Sourcing the data

The community has already done this work — pick one:

- **Satisfactory wiki** (CC-BY-SA) — each resource has a page listing
  all nodes by coordinate.
- **Greyhak / satisfactory-save-parser** — has a Python dict of node
  positions, but the project is GPL-3 (license-incompatible with this
  repo). Don't copy-paste.
- **satisfactory-calculator.com** — has the data but the site
  explicitly forbids redistribution.
- **ficsit-felix** (MIT, archived) — older data, probably stale by now.
- **Extract from FactoryGame.pak** — using umodel or similar to read
  the level's blueprint instances. One-time job.

The dataset is small (~600 entries across all node types). When it lands,
contribute it back to the upstream
[SatisfactorySaveNet TODO #7](../../../../../vendor/SatisfactorySaveNet/TODO.md).

## Easy wins that don't need this table

Some node identifications work without the coordinate lookup:

- **`BP_ResourceNodeGeyser_C`** — always Geothermal. BP type alone is
  enough. Done in SaveFileReader.
- **`BP_ResourceDeposit_C`** — small ore piles you can punch. These
  carry `mResourceDepositTableIndex` in the save; a small hardcoded
  index→resource map covers them.
- **`BP_FrackingSatellite_C` / `BP_FrackingCore_C`** — oil derricks /
  fracking wells. Currently fall back to Unknown until the table covers
  them.

The static table is for `BP_ResourceNode_C` actors (the main mining nodes
that miners are placed on).
