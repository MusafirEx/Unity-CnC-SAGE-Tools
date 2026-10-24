# Phase 5A — C&C3/Kane's Wrath Road Network & Road Atlas Materials

## Status

Phase 5A is a **separate opt-in** feature introduced in package v0.9.9. It does not replace or alter the existing `SAGE_RoadGuides_PreviewOnly` importer. It builds SAGE road **network geometry**, but is NOT yet an exact World Builder road renderer. The exact engine curve/quadrant/crossing atlas layouts, joined-end cap types, and road source textures remain the next phase (5B).

## Source and topology

`ObjectsList` stores roads as **consecutive MapObject RoadType.Start (2) and RoadType.End (4)** records of the same `TypeName`. Shared positions *of the same road type* identify connected nodes (roads of unlike types do not auto-connect). On `eamap_sb05_4.map`, independent analysis of `map_object_placements.csv` confirms:

| Data | Count |
|---|---:|
| MapObjects | 4,729 |
| Valid road start/end pairs | 1,110 |
| Unique road types | 13 |
| Open endpoints (degree 1) | 702 |
| Connected corners (degree 2) | 680 |
| Three-way intersections (degree 3) | 52 |
| Four-way intersections (degree 4) | 0 |

Phase 5A identifies network nodes by normalized road type plus source X/Y rounded to 0.001 SAGE units. It guards empty/non-finite coordinates, duplicate segments and zero-length segments. Roads follow the original heightfield by bilinear sampling of the 16-bit source elevations. Two-way corners use bounded miter joins to reduce gaps; source flags are preserved for later exact curve reconstruction. Three-/four-way junctions currently use **provisional pads** that cover road-end overlaps. Each type has its own mesh subasset, grouped under `SAGE_RoadSurfaces_Phase5A`.

## Road data and widths

Road graphic dimensions **cannot** be reliably inferred from a `.map` alone. The optional `Cnc3RoadLibrary` ScriptableObject stores:

- The original road type name (`TypeName` in source map)
- Source road atlas filename and optional Unity `Texture2D`
- `RoadWidth` and `RoadWidthInTexture` from original road definitions

The importer computes visible width as `RoadWidth * RoadWidthInTexture`, then applies the existing global `Road Guide Width Multiplier`. Widths in the auto-generated library are explicitly **guesses** until replaced with original extracted values; it does NOT claim to recover SAGE road definition bytes from `map.bin`.

The library supports manually entered road types and an optional **TextAsset** containing a subset of Roads.ini records, or XML `<Road .../>`/`<RoadTemplate .../>` records with `id`/`name`, `Texture`, `RoadWidth`, `RoadWidthInTexture`. For `Roads.ini`, copy its text to a `.txt` inside Unity Assets if `.ini` does not import as TextAsset. The parser requires both source width fields instead of silently assuming missing values.

Imported source texture matching is exact by basename (case-insensitive), searching only the current map folder, the road definitions' directory, and an optionally supplied `Road Texture Folder` within `Assets`. You may also assign `Texture2D` directly per road template. **No EA art files are redistributed.**

## Road texture atlas

`Shaders/Cnc3RoadAtlas.shader` has a transparent road color preview and an optional single-atlas texture mode. The straight road repeats horizontally using length / `(4*RoadWidth)` and samples the upper region of a SAGE 3x3 atlas, respecting `RoadWidthInTexture`. The 3-/4-way network pads use preliminary top-middle/top-right atlas tiles; **not** original angle-specific road junction patterns or end caps. Source texture `Texture2D` imported into Unity should retain its alpha channel. Road material and mesh are subassets of the map.

### How to test

1. Install `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-phase5-road-network-materials` and reimport `eamap_sb05_4.map`.
2. Select the `.map` in Project and choose **Assets → C&C SAGE → Create Road Library From Map**. The library generates 13 source road-type entries with guess widths and no textures.
3. Assign the generated road library to **Road Library** under **Phase 5A — Connected Road Surfaces**.
4. Enable **Import Road Surfaces** and reimport. Expect hierarchy `SAGE_RoadSurfaces_Phase5A` with up to 13 type-group meshes, **1,110 segments**, **680 degree-2 corners**, **52 crossings**; validation of exact Unity output is pending.
5. Replace preview width values with extracted Roads.ini/XML definition values; assign original per-type road atlas textures directly in the library, or place them in **Road Texture Folder** as assets. Reimport and check `Road Types Textured` and entries in imported `MapInfo`.
6. Compare against C&C3 World Builder. In the meantime, **Import Road Guides** (legacy) remains a separate fallback/debug view; turn it OFF to avoid overlaying the same roads twice.

## Safe fallback and known limitations

- Import Road Surfaces is OFF by default, to avoid affecting existing maps. User-supplied road definitions/assets are optional; without them, geometries render in diagnostic solid colors with width guesses.
- Network intersections and corner radii are approximations in 5A; broad/tight curves, T/Y variants, exact road type-specific corner atlas UVs, multi-template joins, and endcaps need 5B.
- Road surfaces are editor-only mesh previews, not pathfinding graphs. World Builder road geometry may differ and C&C3/KW road definitions may use game-specific XML structures requiring further parser extensions.
- Material shader compatibility with the active render pipeline, original alpha blending and normal orientation must be tested live before merging.

## Source references

- [OpenSAGE road series part 2](https://opensage.github.io/blog/roads-how-boring-part-2-inspecting-the-map-file/) — source road pairs
- [OpenSAGE part 3](https://opensage.github.io/blog/roads-how-boring-part-3-building-a-graph-data-structure/) — node/edge topology and orientation
- [OpenSAGE part 4](https://opensage.github.io/blog/roads-how-boring-part-4-rendering-straight-roads/) — width and straight atlas UV
- [OpenSAGE part 9](https://opensage.github.io/blog/roads-how-boring-part-9-rendering-curves/) — next-stage exact curves

**Validation:** Source CSV road topology validated independently; code static checks done. Unity C# and shader compilation and Scene rendering NOT yet verified while Unity MCP is disconnected.