# C&C3 World Builder .map — Standing Water Importer (v0.9.6)

## Why this stage

`eamap_sb05_4.map` has no nonzero cliff-overlays in BlendTileData; the next real visual feature is `StandingWaterAreas`, with 2 populated area records. The importer builds only water surfaces from the source `.map`; no compiled support assets are required for geometry.

## Binary specification confirmed from source map

- `StandingWaterAreas` top-level section version 2, 376-byte payload.
- Payload begins with little-endian `uint32 areaCount = 2`.
- Each record includes `uint32 UniqueID`, ushort-length-prefixed ASCII `Name` and `LayerName`, `float UvScrollSpeed`, `byte UseAdditiveBlending`, two ushort-prefixed ASCII names (`BumpMapTexture`, `SkyTexture`), `uint32 pointCount`, array of `pointCount` pairs of float32 XY world coordinates, `uint32 WaterHeight`, then ushort-prefixed `FxShader` and `DepthColors` names.
- The separate `GlobalWaterSettings` payload contains a 32-bit boolean reflection toggle and a float reflection plane height. Both are preserved as metadata.

| Area | ID | Vertices | Height | Shader ID | Ripple bump name |
|---|---:|---:|---:|---|---|
| New Water Area | 1 | 11 | 87 | CNC3_CalmA | WaterRippleBump3 |
| New Water Area | 2 | 13 | 40 | CNC3_BayA | WaterRippleBump |

The independent decoder consumes the complete section with no trailing bytes. Polygons are CCW in source XY, and may extend outside map boundaries. Height is in native SAGE world Z units, not 16-bit heightmap elevation samples.

## Unity construction

- `Cnc3MapReader` adds `WaterArea` records and optional global reflection settings to the source Data object.
- `Cnc3MapWaterBuilder` maps source `(X,Y,WaterHeight)` to Unity `(X * horizontalScale/10, WaterHeight * verticalScale/0.0390625, Y * horizontalScale/10)`, with the same north–south flip as object placement.
- The mesh builder uses a guarded ear-clipping triangulator for concave polygons, tests their signed areas, skips degenerate shapes and constructs upward-facing triangles. Original source polygon points are never edited.
- `Cnc3MapImporter` builds a `SAGE_WaterAreas` hierarchy, registers named mesh/material sub-assets and stores every original record and mesh ref in `Cnc3MapManifest`.
- `Shaders/Cnc3WaterPreview.shader` is a small alpha-blended unlit procedural ripple preview. Scroll speed is read from the source water area, plus additive-vs-alpha blending behavior. It deliberately does not claim to reproduce game reflections, refraction, original bump mapping, depth-tint LUT, caustics or sky textures.

## Real-map validation (offline)

- Area #1: 11 input points → 9 triangles; polygon area = 93,499.5 source square units; sum of triangle areas matches exactly.
- Area #2: 13 input points → 11 triangles; polygon area = 1,049,626.0 source square units; sum of triangle areas matches exactly.
- Both checks pass with `Flip Map North South = false` and `true`.
- Source standing-water section end is byte `5,133,338` in decoded CkMp and reader ends at exactly that byte.

## Unity test

1. Change the installed Git package to branch `feature/cnc3-map-standing-water`, then Reimport `eamap_sb05_4.map`.
2. In .map Inspector turn on **Import Standing Water**. Set **Water Tint** alpha as desired and **Water Scroll Multiplier** to 1.
3. Expand the map root `SAGE_WaterAreas`. Expect two GameObjects containing MeshFilter and MeshRenderer.
4. Inspector should report 2 standing-water areas and 2 generated meshes. Their water heights are 87 and 40 in default height scale.
5. Confirm no regressions in terrain base atlas, two-way and three-way blending, object markers and W3X.

**Unity compile/visual status:** awaiting live Editor test. The code is deliberately kept in a Draft PR until verified.

## Deferred

Full SAGE water shaders and original textures, river meshes, waves/waterfalls, road spline reconstruction and cliff overlays. This map contains zero cliff overlay cells, so cliffs need a separate test map with actual nonzero cells.

Source reference for parser terminology: [OpenSAGE `StandingWaterArea`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/StandingWaterArea.cs) and [`StandingWaterAreas`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/StandingWaterAreas.cs).