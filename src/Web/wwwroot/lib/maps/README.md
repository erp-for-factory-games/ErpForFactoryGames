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

The user-selectable backdrop is positioned by Leaflet `ImageOverlay`
bounds, currently the same padded resource-node extent the procedural
backdrop uses. If markers don't line up cleanly with a specific image,
override per-image bounds in `factory-map.js`'s `MAP_BACKDROPS` table.

## Adding a new backdrop

1. Drop the image in this directory.
2. Add an entry to `MAP_BACKDROPS` in `src/Web/wwwroot/js/factory-map.js`.
3. Add a radio option in `src/Web/Components/Pages/Settings.razor`.
