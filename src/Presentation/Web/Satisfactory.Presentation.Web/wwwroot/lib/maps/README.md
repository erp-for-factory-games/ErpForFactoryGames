# Map backdrops

User-selectable backdrops for the `/factory/map` page. Each one is loaded
lazily — only the currently-selected backdrop fetches its image on page
load.

## Files

| File          | Source       | Bytes | Notes                                  |
|---------------|--------------|------:|----------------------------------------|
| `terrain.jpg` | Wiki "Map"   |  ~2 MB | The standard game world map.           |
| `biome.jpg`   | Wiki "Biome Map" |  ~4 MB | Biome-coloured variant.                |
| `water.png`   | Wiki "Water Map" |  ~4 MB | Water bodies + coastline only.         |

All three are sourced from the [Satisfactory Fandom wiki](https://satisfactory.wiki.gg/),
which licenses community-uploaded content under fair-use terms for
non-commercial fan tools. See [ADR-0015](../../../../../docs/adr/0015-map-backdrops-fair-use.md)
for the licensing decision.

## Calibration

All three wiki images share the same projection — they're rendered from
the in-game world map data — so they use a single shared bounds entry
`WIKI_MAP_BOUNDS` in `factory-map.js`'s `MAP_BACKDROPS` table:

```
X: -324698 to +425302   (Unreal cm, east-positive)
Y: -375000 to +375000   (Unreal cm, south-positive)
```

These are the standard Satisfactory world bounds — 750,000 cm × 750,000
cm, centered slightly east of world origin. Verified against marker
alignment in issue [#43](https://github.com/ChrisonSimtian/ERP.Satisfactory/issues/43):
miners and production buildings sit on land in the terrain and water
backdrops; biome zones line up cleanly under markers.

If a future backdrop uses a different framing, give it its own `bounds`
entry. If `bounds` is omitted, `factory-map.js` falls back to the padded
resource-node extent (roughly correct but pixel-imprecise).

## Adding a new backdrop

1. Drop the image in this directory.
2. Add an entry to `MAP_BACKDROPS` in `src/Web/wwwroot/js/factory-map.js`.
   If the image uses the same projection as the existing wiki maps, point
   its `bounds` at `WIKI_MAP_BOUNDS`; otherwise calibrate per-image.
3. Add a `MudSelectItem` in `src/Web/Components/Pages/Settings.razor`.
