# 0015. Map backdrops sourced from wiki under fair use

- Status: Accepted
- Date: 2026-05-13
- Deciders: Chris
- Amends: [0013](0013-map-visualiser-approach.md)

## Context

[ADR 0013](0013-map-visualiser-approach.md) set the rule that no
Coffee Stain map art is shipped with the planner: the factory map's
backdrop would be procedural, generated from public-data resource-node
coordinates only. That backdrop was implemented and shipped, but the
quality is sparse-sample-limited: 684 resource-node Z values across an
8 km × 7 km world produces a low-frequency, blurry surface that
doesn't look like a Satisfactory map.

The [Satisfactory Fandom wiki](https://satisfactory.wiki.gg/) hosts
three world-scale map images that the community uses as the de-facto
reference: a terrain map, a biome map, and a water/coastline map.
These are community-uploaded media under the wiki's fair-use policy
for non-commercial fan tools.

This planner is a non-commercial fan tool. The maps are used as a
selectable backdrop, not redistributed independently.

## Decision

Ship the three wiki maps in `src/Web/wwwroot/lib/maps/` and surface
them as user-selectable backdrops on the factory map page, alongside
the existing procedural option and a "none" option. Default to the
terrain map. Persist the choice in `localStorage` (key
`erp-map-backdrop`). The setting is exposed on the Settings page.

The fair-use claim rests on: (a) non-commercial use, (b) limited scope
(three images, used as backdrops only, not redistributed as
standalone assets), (c) clear attribution to the wiki in the
`/lib/maps/README.md` and on the Settings page, (d) Coffee Stain has
not objected to similar community tools shipping the same images.

The procedural backdrop is retained as a fallback option for users
who prefer it or who load saves on modded maps where the wiki images
don't apply.

## Alternatives considered

- **Keep procedural only** (the ADR-0013 default). Rejected after
  shipping it — the result looks like a blurry blob, not a map. Chris
  immediately asked for something recognisable.
- **User-supplied local tile cache** (ADR-0013 mentioned as a future
  option). Higher friction — every user has to source their own
  image. Doesn't help the demo / first-run experience.
- **Render game terrain from .pak assets**. Would produce
  pixel-accurate maps but pulls Coffee Stain's raw terrain data into
  the build pipeline, which is well past fair use. Rejected.
- **Skip backdrops entirely**. Map markers on a dark canvas are
  readable but uninspiring; doesn't match the "looks like Satisfactory"
  bar we set elsewhere.

## Consequences

- The map page no longer claims "no Coffee Stain art is shipped" —
  it ships three wiki-sourced images and one procedural fallback.
- ADR-0013's tile-layer paragraph is superseded by this ADR. The rest
  of 0013 (Leaflet + JSInterop + GeoJSON contract) still stands.
- If Coffee Stain or the wiki ever objects, the three images can be
  removed by deleting `src/Web/wwwroot/lib/maps/`. The procedural
  backdrop and "none" option remain functional.
- ~11 MB of image data lives in the repo. Acceptable — `lib/maps/`
  is opt-in (lazy-loaded on the map page only) and image files are
  highly compressible by HTTP caches.
- Adding a fourth map (e.g. resource-overlay, infrastructure) is a
  data drop + one entry in the `MAP_BACKDROPS` table in
  `factory-map.js` and one radio option in Settings.
