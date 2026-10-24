# C&C3 / Kane's Wrath maps — Road guides, cliff mapping and terrain light

## Experimental package v0.9.8

Extends the existing *working* `.map` heightmap, compiled diffuse atlas and two-/three-way blend shader. Water remains optional. The three additional visual import options are **OFF by default** so existing textured terrain does not change during a package update.

### Source-verified `eamap_sb05_4` data

| Content | Extracted value |
|---|---|
| `GlobalLighting` | Version 9; 1320-byte payload; four time-of-day presets |
| Active source time | 3 (Evening; Morning 1, Afternoon 2, Evening 3, Night 4) |
| Terrain light vectors | Nine 36-byte light groups per time; terrain Sun + Accent1 + Accent2 are parsed |
| `BlendTileData` cliffs | 330,400 cliff index samples, zero active; one sentinel, zero mapping records |
| Road control objects | 2,220 (nonzero RoadType flags) |
| Consecutive RoadType Start (2) / End (4) pairs of the same TypeName | 1,110 |
| Distinct road source type names | 13 |

### Roads

`ObjectsList` stores road control endpoints as ordinary `Object` records. RoadType flags are bitmask values: `Start=2`, `End=4`, `Angled=8`, `TightCurve=64`, `EndCap=128`, etc. The example sample's 1,110 road start/end pairs occur **consecutively** in file order, always sharing TypeName; they must not be connected using nearest-neighbor guesses.

`Editor/Cnc3MapRoadBuilder.cs` generates a heightfield-draped strip mesh for each paired source segment, subdividing into roughly 20-SAGE-unit steps. Generated meshes are **grouped by road TypeName** under `SAGE_RoadGuides_PreviewOnly`. Roads are approximate editor alignment guides: widths (road 24 SAGE units, sidewalk 14, marking 2.5) and solid colors are heuristics. No original SAGE `Road` source asset visual meshes/textures are embedded into these strips. Curve and angle flags are counted but **not geometrically recreated**. These assumptions must be replaced using road material/path resources from the original game for accurate geometry.

**Controls:** `Import Road Guides` (default OFF), `Road Guide Width Multiplier`, `Road Guide Surface Offset`, `Maximum Road Guide Segments`. With the test sample default max 5000, expect 1,110 paired segments spread across 13 type meshes; verify in Unity and confirm no z-fighting. The existing ObjectsList marker import remains available separately.

### Source terrain lighting

`GlobalLighting` v9 has 4 preset configurations. Each has 9 groups of 36 bytes `(Ambient xyz, Color xyz, Direction xyz)` representing Terrain, Object and Infantry Sun/Accent1/Accent2. The preview extracts only the 3 **terrain** light groups per preset; it does **not** introduce a Unity scene Directional Light, change RenderSettings, or alter other user objects.

The bundled `MusafirEx/SAGE Terrain Atlas Blend` shader now accepts source Ambient, Sun and two Accent values and evaluates simple per-pixel Lambert lighting using imported terrain vertex normals. Converted light vector `(sageX,sageZ,sageY)` supplies Unity X/Y/Z; source SAGE shader models `dot(normal, -lightDirection)`. Set `Use SAGE Terrain Lighting` (default OFF) and optionally override the time preset with `0=source`, `1=Morning`, `2=Afternoon`, `3=Evening`, `4=Night`. Strength mixes between previous unlit appearance and source-light preview. **Not** an exact replacement for SAGE terrain shadow, clouds, color grading or macro lighting.

### Cliff textures

`BlendTileData` stores one cliff texture index per heightmap cell and optional 38-byte mapping records (`TextureTile` u32, four UV float2 corners, final ushort). The importer now reads and validates indices and records. An experimental `Preview Cliff Textures` checkbox (default OFF) feeds per-corner coordinates and compiled diffuse atlas sub-rectangles to the terrain shader. It is implemented as an **approximate cliff override**, not original cliff blending. Its exact orientation and UV repeat behavior cannot be validated on `eamap_sb05_4` because all of its cliff cells are zero. Testing needs another authentic `.map` with nonzero cliff indices.

### Regression and Unity Editor checks

1. Update Git package to `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-map-roads-cliffs-lighting`, then select the source `.map` and Reimport.
2. Leave the three new toggles OFF first. Confirm the base atlas + two/three-way blending remain unchanged.
3. Turn on **Import Road Guides**. Inspect `SAGE_RoadGuides_PreviewOnly` and the number of segments/type meshes in the map Inspector; visually align roads with terrain. These are preview geometry, not exported assets.
4. Turn on **Use SAGE Terrain Lighting**, compare map Morning/Afternoon/Evening/Night and intensity slider. Verify the terrain changes but other scene lighting is not modified.
5. Inspect **Active Cliff Cells**. Zero means there is nothing to render. Once a map with nonzero cliff entries is available, enable experimental cliff preview and check UV orientation.
6. Check Console for C# or shader compile errors, and test a map with no compiled atlas to verify heightmap fallback.

### References

- [OpenSAGE `GlobalLighting`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/GlobalLighting.cs)
- [OpenSAGE `GlobalLightingConfiguration`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/GlobalLightingConfiguration.cs)
- [OpenSAGE `CliffTextureMapping`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/CliffTextureMapping.cs)
- [OpenSAGE `MapObject` and `RoadType`](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/MapObject.cs)

**Status:** Binary structures validated against the user's source sample. C# and ShaderLab source changes committed; live Unity Editor compile, shader render and road visual acceptance pending. Keep PR Draft until verified.
