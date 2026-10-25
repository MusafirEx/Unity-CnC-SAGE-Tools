# Unity-CnC-SAGE-Tools

Unity 2022.3 LTS+ Editor tools for **Generals / Zero Hour** binary **W3D importing**, alongside **Tiberium Wars / Kane's Wrath** source **W3X** model/animation import and export.

**Version 0.9.12 — read-only direct SAGE BIG archive browsing and optional on-demand road textures, plus existing terrain/roads/W3X.** The normal Unity package no longer imports nine development smoke/self-test scripts or exposes any `Run ... Self-Test` menu items. Existing importers, animation creation, model export, animation export, and read-only inspection/audit tools remain available.

> W3D support is **import only**; this package is not a W3D exporter, game asset extractor or confirmed game-ready mod builder. Keep backups and validate with the game toolchain separately.

## Install / update

In Unity, select **Window > Package Manager > + > Add package from git URL**:

```text
https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git
```

Git and Unity 2022.3 LTS or later are required. After updating a Git-based package, ensure Package Manager resolves **0.9.5** and scripts compile. Do not keep a duplicate installed copy under `Assets/`, or duplicate Editor assemblies may conflict.

### FBX-style shared W3D/W3X Inspector (v0.8.2)

The package now follows the **Bress2Unity** `BrresImporterEditor` design: selecting a W3D or W3X source file opens **Model | Rig | Animation | Material** tabs with serialized import options and Unity **Apply / Revert**. The previous read-only statistics are preserved underneath these settings.

| Tab | Import options and inspection |
|---|---|
| **Model** | Import Meshes; Scale Factor (Unity prefab root only); imported mesh, vertex, triangle, UV and renderer information |
| **Rig** | Animation Type: **None**, **Legacy** or **Generic**; for Generic, choose **Create From This Model** or **Copy From Other Avatar** with an external Generic Avatar, and optional root motion bone; inspect pivot hierarchy |
| **Animation** | Import Animations; inspect imported AnimationClip sub-assets, lengths and frame rates |
| **Material** | Import Materials; Generated Material Shader; per-renderer/slot external `.mat` assignments; **Extract Materials...** and **Clear Overrides**; inspect imported materials and textures |

**Defaults preserve v0.8.1 behavior:** W3X rig defaults to None (clips as sub-assets with no new Animation component), W3D rig defaults to Legacy, meshes and animations are enabled, generated preview materials are assigned, model scale is 1.

**How to extract editable materials:** Select a source W3D/W3X file → Material → Extract Materials... → choose a directory inside `Assets/`. The plugin creates standalone `.mat` assets, assigns them as external overrides and reimports the source. To use your own material instead, drag an external `.mat` into its source material slot and click Apply. Clear Overrides restores source-generated preview materials after Apply.

**Scope:** Generic Avatars are experimental and may require a valid matched SAGE pivot hierarchy; Humanoid retargeting is not implemented. W3D compressed animations, distributed HLOD assembly and advanced multipass shaders remain unsupported. Source W3D/W3X files and existing W3X exporters are not changed by importer Inspector settings.

**Reference:** [Bress2Unity's importer Inspector](https://github.com/MusafirEx/Bress2Unity/blob/main/Packages/com.musafirex.chr0-to-unity/Editor/BRRES/BrresImporterEditor.cs).

## C&C 3 / Kane's Wrath — .map Mode 1 terrain + placement importer (0.9.1)

**Only the World Builder `.map` is required for Mode 1.** The `.bin`, `.imp`, `.manifest`, `.relo` and two `.tga` files are optional and are not read by this version.

The importer creates a root GameObject with `SAGE_Terrain` (tiled MeshFilter/MeshRenderer geometry) and `SAGE_ObjectPlacements` (empty named GameObjects, optionally grouped by type). Empty markers represent source object positions and rotations, **not rendered buildings or vegetation**.

**Decoder:** Recognizes uncompressed `CkMp` and `EAR\0`/RefPack map wrappers. Reads `HeightMapData` and versioned `ObjectsList` / `Object` records, extracting names, SAGE XYZ positions, heading radians, road flags and source record version. Other top-level records and Object property payloads remain unmodified in the original `.map` but are not converted.

**Unity fix:** `Cnc3MapManifest` now has its own matching script file `Editor/Cnc3MapManifest.cs`, resolving the observed `AddObjectToAsset` failure. `ScriptedImporter` revision increased from 1 to 2 to force reimport. All source placement metadata is retained in the manifest subasset even when placeholder generation is disabled.

**Inspector:** Import Terrain; Horizontal Scale; Vertical Scale; Flip Map North South; Include Border; Import Objects; Group Objects By Type; Max Object Placeholders (default 20,000); Add Mesh Colliders; Terrain Material. Click **Apply** to rebuild.

**Real input checkpoint:** `eamap_sb05_4.map` independently parsed as 560×590 grid, border=30, 4,729 source objects, 159 distinct type names. With default settings, expect 90 terrain tiles (9 columns × 10 rows) and 4,729 empty object placeholders. These counts are expectations from source parsing, not live Unity verification.

**Synthetic test:** Package sample `Minimal C&C3 Map` generates three uncompressed/compressed test maps with a 12×10 terrain and two valid `Object` records. Use `python generate_map_samples.py` then refresh Unity.

**Deferred to Mode 2:** loading compiled `.bin/.imp/.manifest/.relo`, texture atlases, matching W3X models, accurate roads/water/blending, gameplay and scripts. No World Builder source is modified. See [technical notes](docs/Cnc3MapImporter.md).

**Status:** C# compilation and actual Unity import must be confirmed in Unity 2022.3 before merging.

## Mode 2A — Optional compiled `map.bin` DDS atlases (0.9.2)

Place the C&C3 World Builder `.map` file beside `map.bin` (optional) in the **same Unity Assets folder**. Select the `.map` asset; its Inspector now has **Mode 2 — Compiled Map BIN**.

- **Import Compiled Atlases** (enabled by default): read `map.bin`, validate `0x9CCE1238` signature, locate *complete* supported DDS images by header/length, and decode BC3/DXT5 or RGB555 first mip to Unity `Texture2D` sub-assets. Textures are named with source format/offset. No plug-in or proprietary runtime DLL is required.
- **Inspect Compiled Indexes** (enabled by default): optionally inspect sibling `map.imp`, `map.relo`, `map.manifest`, verify matching signatures/record lengths, count primary indexed offsets and count zero-terminated `GameObject:`/`Texture:` manifest names. Indexes remain read-only; they do not produce real Unity object meshes.
- The imported `.map` still contains existing **SAGE_Terrain** and **SAGE_ObjectPlacements**. If `map.bin` is absent or invalid, the optional compiled import logs a warning but does **not** prevent source terrain/placement import.
- Atlases are **NOT auto-applied to the terrain** because the game's `BlendTileData` texture coordinate/blend mapping has not yet been decoded. Assign extracted texture sub-assets manually or use the future terrain material mapper.
- The `.bin` signature and DDS offsets are checked; the verified version-5 manifest is parsed asset by asset (name, source path, type/instance IDs, references and exact B/I/R byte ranges). TerrainTextureAtlas records yield 43 tile IDs and UV pixel bounds. The source map's 241 W3DMesh and 142 GameObject manifest entries have zero binary body bytes in this stream; original asset models must come from base game streams or other referenced packages.

**Validated source fixture `eamap_sb05_4`:** 17,055,700-byte `map.bin`; 966 version-5 manifest assets (25 include binary bodies), 43 terrain atlas tiles; 293,968-byte pre-DDS binary region; DXT5/BC3 2048×2048 atlas at `0x00047C50` (5,587,072 bytes incl. DDS+mips); RGB555 2048×2048 atlas at `0x0059BCD0` (11,174,016 bytes incl. DDS+mips); 2,483 `.imp` addresses, 6,831 primary `.relo` addresses, 142 `GameObject:` names and 131 `Texture:` names in manifest. *These byte-level values were verified against the user's original files.*

**Unity acceptance test:** update the Git package, select/reimport `eamap_sb05_4.map`, confirm two generated Texture2D sub-assets in the expanded `.map`, open them to inspect the atlas previews, and read the Mode 2 Inspector section and Console. No Unity compile or Editor import success is claimed until that real test passes.

## v0.9.3 — Fix: compiled atlas now appears on terrain

v0.9.2 extracted the two DDS images but never assigned them to the terrain material and used UVs covering the full map. This fix connects the *source map* `BlendTileData` base texture tiles to their compiled atlas locations.

- Decodes `BlendTileData` (v6+, including verified C&C3 v24): per-cell ushort tile indices and the texture name/CellSize table, skipping unrelated packed passability/blend tables within their validated bounds.
- Computes each source name's **lowercase SAGE FastHash** and looks it up against the 12-byte per-texture tile descriptors in `TerrainTextureAtlas:EAMAP_SB05_4`. Source `eamap_sb05_4` maps **all 43/43 source textures**.
- Converts each source tile's Morton-ordered 32×32 atlas subtile into quad-specific UVs, including the DDS-to-Unity V orientation. Unity terrain patches have separate cell vertices so discontinuous atlas UVs cannot be shared incorrectly.
- The importer now loads the compiled bundle **before constructing terrain**. When `Apply Atlas To Terrain` is enabled (default), a compatible `CNC3_Terrain_Preview` material receives the base-color DXT5 `Texture2D` automatically. A supplied external material is cloned rather than modified.
- Supports Built-in and URP/HDRP shader selection where matching shaders exist; atlas UV vertex normals are smoothed using heightfield neighbors; mesh colliders remain available.
- **Mode 1 fallback:** maps with no `map.bin`, missing atlas tile table or missing source BlendTileData still import the untextured terrain and object markers. Inspector displays texture count, matched IDs and a clear reason if atlas could not be applied.

**Important limitation:** this v0.9.3 implementation displays the **base terrain tile texture**. It does not yet reconstruct second/third texture blend masks, cliff blends, terrain macro-texture, or dynamic details. Transitions may have visible seams; resolving precise SAGE blending is a separate next stage.

**Live test:** Update Unity Package Manager to v0.9.3 branch, select `eamap_sb05_4.map` and **Reimport**. Ensure `map.bin`, `map.manifest`, `map.imp`, `map.relo` are beside `.map`, and check Inspector **Apply Atlas To Terrain**. Expected Inspector: **Source Terrain Textures 43**, **Texture IDs Matched 43**, **Atlas Applied To Terrain Yes**. The Scene view terrain should now have varied grass, road and dirt base textures. This update is not yet Unity-compiled/play-tested by the assistant; verify Console before merging.

Technical detail: [Terrain Atlas UV Mapping](docs/Cnc3TerrainAtlasMapping.md).

## v0.9.5 — Terrain texture blending (experimental)

Version 0.9.3 imported the diffuse atlas and applied the **base** `BlendTileData.Tiles` texture to each source cell. It did not render `Blends` or `ThreeWayBlends`, so texture transitions looked hard-edged.

**New:** parse the map's versioned 16-/32-bit blend grids and 18-byte blend descriptions (secondary tile index, blend direction, flipped flag and corner two-sided flag); preserve C&C 3 v24 layout. A bundled `Shaders/Cnc3TerrainAtlasBlend.shader` samples the **same diffuse atlas three times per pixel**, mixing base→secondary→tertiary with the direction rules from OpenSAGE's terrain shader.

**Unity workflow:** import/update the package using the feature branch, then reimport `eamap_sb05_4.map`. In the `.map` Inspector enable **Apply Atlas To Terrain** and **Render Terrain Blending** (both enabled by default). The shader is automatically used for the generated terrain material when available; no material or atlas dragging is needed.

**Expected source counts for `eamap_sb05_4.map`:** 330,400 tile cells; 16,672 blend descriptions; 64,782 cells with a first blending layer; 4,536 cells with a third blending layer; 43/43 texture names resolved against compiled atlas tile IDs. The imported Inspector displays each count and whether the blend shader was applied.

**Fallback:** If `map.bin`, the compiled manifest, `BlendTileData` or a supported blend shader is missing, the existing base-atlas (or untextured) terrain path remains available. The shader is unlit and intended for preview rather than reproducing SAGE lighting, shadows or macro-noise.

**Still pending:** cliff texture mapping, detail/macro textures, lighting, decals, water, roads and base-stream W3D model resolution. Some edge transitions and terrain orientation may differ from World Builder until live Unity visual testing.

See [the texture blending technical note](docs/Cnc3TerrainTextureBlending.md).

## v0.9.6 — Source standing-water polygons (experimental)

The real `eamap_sb05_4.map` has **no active cliff-texture grid cells** but does contain **two standing-water polygons**. This milestone therefore adds visible, editable source-water surfaces before tackling absent cliff examples.

- Decode `StandingWaterAreas` v2 records directly from `.map` (unique ID, names, 2D polygon points, water height, UV scroll, additive-blend flag, bump/sky/FX/depth texture names), plus `GlobalWaterSettings` reflection flag/plane. The original map files are never modified.
- Concave polygons are triangulated using bounded ear clipping, so water surfaces do not incorrectly fill land. Coordinates follow the same C&C3 → Unity scaling/optional north–south flip as other placed objects; water heights follow the terrain vertical scale.
- Create `SAGE_WaterAreas` child GameObjects with mesh/material sub-assets. A package `MusafirEx/SAGE Water Preview` unlit transparent shader offers gentle procedural ripple animation. It is a **preview**, not original game reflection/bump/sky rendering.
- Inspector offers **Import Standing Water**, **Water Tint**, **Water Scroll Multiplier** and **Water Preview Material**. Source metadata remains in `Cnc3MapManifest` even with water meshes disabled.
- Built-in pipeline shader is used where available, with safe shader fallback. The existing textured, 2-way/3-way blended `SAGE_Terrain`, W3D and W3X tools remain unchanged.

**Real-map target:** Standing-water area #1 has 11 points and SAGE water height 87; area #2 has 13 points and water height 40. Both polygons parse to the final byte of their 376-byte source section and successfully triangulate to 9 + 11 upward-facing mesh triangles (including north–south flip). The scene should display both surfaces at the source world coordinates.

**Not yet done:** Original SAGE reflections, shoreline foam, original ripple bump and sky environment assets, river strips, waterfall effects, roads and cliff materials. In this map the source cliff overlay grid contains only zero values; a different sample map will be useful when developing cliffs.

**Test:** Install `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-map-standing-water`, select `eamap_sb05_4.map` and Reimport with **Import Standing Water** enabled. Confirm `SAGE_WaterAreas` contains two child meshes and the Inspector shows **Water Meshes Created: 2**. Console compilation and runtime visual checks must be confirmed in Unity before merging.

See [Standing water technical notes](docs/Cnc3StandingWater.md).

## v0.9.7 — Fix missing terrain textures on other maps

The previous compiled asset reader rejected **every map.bin except those sharing the first sample's 0x9CCE1238 stream identifier**. This was a false assumption: the first four bytes of the compiled stream are an identifier/checksum for that particular map, **not a constant magic header**.

Evidence from the user's Unity Editor.log:
- `map_mp_2_simon_b/map.bin`: identifier `0x5AD3D148`, map terrain 345×345 imports but its atlas was skipped.
- `map_mp_2_black2_redzoned/map.bin`: identifier `0x23E90B79`, terrain 500×400 imports but its atlas was skipped.
- `eamap_sb05_4/map.bin`: `0x9CCE1238`, the original verified example.

The compiled reader now compares each `map.bin` ID with **the same map's** `map.manifest` header at offset +4. The .imp and .relo signatures are also validated against the **current** map.bin ID, rather than a hardcoded literal. The map asset Inspector shows **Compiled Stream ID** for troubleshooting. An incompatible manifest produces a precise mismatch error without blocking the heightmap or source object import.

The Unity Package Manager also previously emitted `Asset ... Shaders has no meta file, but it's in an immutable folder`; this update includes `Shaders.meta` and four previously missing documentation .meta files so the shader directory can import normally.

**Test:** Install the feature branch, reimport `map_mp_2_simon_b.map` and `map_mp_2_black2_redzoned.map`. Check the Inspector's Compiled Stream ID, DDS Atlas count, Source Terrain Textures, Texture IDs Matched and Atlas Applied to Terrain. Successful texture display still depends on each map's actual DDS and compatible TerrainTextureAtlas/BlendTileData records; this fix removes the known checksum gate, not all possible map-format variations.

## v0.9.8 — Experimental cliffs, road guides & terrain lighting

This milestone adds three optional source-map features. Existing diffuse atlas, two-way/three-way blending, objects and water remain unchanged until these features are enabled.

- **Preview Cliff Textures** (OFF): decodes source `BlendTileData` cliff grid and 38-byte corner-UV mapping records. Maps active cells against compiled atlas. The sample `eamap_sb05_4` has **zero active cliff cells**, so accurate preview validation requires a different source map.
- **Import Road Guides** (OFF): reconstructs **1,110** adjacent Start/End road control pairs (13 TypeName groups in `eamap_sb05_4`) as heightfield-draped alignment strips. Material colors, segment widths and straight interpolation are approximate; original SAGE road meshes and curves are NOT reconstructed.
- **Use SAGE Terrain Lighting** (OFF): reads four `GlobalLighting` time-of-day presets and uses source Sun, Ambient and Accent1/Accent2 in the compiled atlas terrain shader. Switch time of day or blend strength without changing Unity scene lights.

The toggles default to OFF to preserve previously validated terrain appearance. After installing this branch, select the `.map`, enable an option and Reimport. The Inspector reports the source road count, active cliff cells and lighting preset.

Technical background and visual test checklist: [Roads, cliffs and terrain lighting](docs/Cnc3TerrainRoadsCliffsLighting.md). **Unity compilation and shader rendering are pending live tests.**

## Phase 5A (v0.9.9) — connected road network surfaces and source road atlas

Roads have been upgraded beyond Phase 4 straight alignment guides. The optional **Import Road Surfaces** path identifies same-type connected nodes across source `ObjectsList` Start/End pairs, builds terrain-following meshes with **degree-2 miter joins** and **provisional** degree-3/4 crossing pads, and groups all meshes by original road `TypeName`.

Important: the `.map` does not carry the complete original road-width definitions or TGA/DDS road texture assets. We therefore introduced a separate `Cnc3RoadLibrary` asset. Use the Project menu **Assets → C&C SAGE → Create Road Library From Map** to generate a per-map library of all road names. Its initial widths are explicitly approximate; replace with original RoadWidth and RoadWidthInTexture values and link original textures. The importer also accepts optional source Roads.ini or simple Road/RoadTemplate XML as a TextAsset, and searches exact texture basenames in your chosen **Road Texture Folder**.

The `MusafirEx/SAGE Road Atlas` shader samples repeating straight road strips from the original 3×3 atlas. Three-/four-way pads use provisional intersection tiles and are **not yet full fidelity**. Final broad/tight curves, crossing orientation, Y/T variants, multi-type endcaps and accurate source materials are Phase 5B. The previous `Import Road Guides` option is retained as a fallback; disable it to avoid duplicate geometry.

The new **Import Road Surfaces** option is OFF by default. In sample `eamap_sb05_4.map`, the verified network has **1,110 segments across 13 types**, **680 two-way junctions**, **52 three-way crossings** and zero four-way crossings. The resulting Unity meshes and shader still require live Editor validation.

See [Phase 5A technical guide](docs/Cnc3Phase5RoadSurfaces.md).

## Phase 5A v0.9.10 — Automatic matching of road DDS textures

**Original road textures can now be discovered automatically in extracted WBData.** The Unity `Cnc3MapImporter` searches its supplied Road Texture Folder first, then the `WBData` sibling of the map folder (for example `Assets/CnC3KW/WBData`), then the road definitions and map folders. It prefers manually assigned `Texture2D`, followed by the **exact** source `RoadTemplate.Texture` basename, followed by an exact `MapObject.TypeName` basename as an *unverified candidate*. It never substitutes `ASRoad01` for `UKRoad01` merely because their names are similar.

Two workflows:

- In the `.map` Inspector enable **Import Road Surfaces** and **Auto Resolve Road Textures** (on by default). Reimport; the map's Imported Map Info now shows **Texture Folders**, **Indexed Road Texture Candidates**, **Road Types Textured**, and the resolved/missing reason for each road type.
- To save the matches into a generated `*_RoadLibrary.asset`, select that asset and choose **Assets → C&C SAGE → Auto-Match Road Library Textures**. It fills empty Texture2D references for exact-name matches and preserves every existing manual assignment.

The reference map has 13 **UK, Washington and Aus** road names; the texture index is not expected to match those names to the earlier alphabetical `ASRoad` assets. If road filenames are different from type names, populate original `RoadTemplate.Texture` names (or select the correct DDS manually). Extracted road atlas orientation is still experimental until visually checked against the original game.

See [road texture matching technical notes](docs/Cnc3Phase5RoadTextureAutobind.md).

## v0.9.11 — Original Kane's Wrath RoadTemplate bindings

The road type names in maps **do not always equal their DDS atlas filenames**. Electronic Arts publishes the authoritative 106 records in [`Kanes Wrath/Xml/GlobalData/Roads.xml`](https://github.com/electronicarts/CnC_Modding_Support/blob/main/Kanes%20Wrath/Xml/GlobalData/Roads.xml). For example: `UKSidewalk03 → UKRSidewalk03`, `WashingtonRoadMarking01 → DCRoadMarking01`, `AusDirtRoad01 → ASDirtRoad01`. All 13 road types used by `eamap_sb05_4` have official entries.

**One-click opt-in setup:** Update the Git UPM package, then choose **Tools → C&C SAGE → Install Official Kane's Wrath Road Definitions** in Unity. This downloads `Roads.xml` directly from EA GitHub into `Assets/CnC3KW/Definitions/Roads_KanesWrath.xml`, connects all generated Road Library assets under `Assets/CnC3KW/Maps` which have no existing custom definitions, upgrades their old guessed road widths/atlas names, and attempts exact DDS matching against extracted `WBData`. User-defined texture assignments and edited widths take precedence. Reimport each `.map` with **Import Road Surfaces** enabled.

This project **does not redistribute** EA road templates, source XML, or game art; official XML is fetched into the user's own project only by explicit menu action. The source repo is GPLv3 with additional terms, so check its [license](https://github.com/electronicarts/CnC_Modding_Support/blob/main/LICENSE.md) before redistributing downloaded resources.

If offline, manually save the official EA XML to `Assets/CnC3KW/Definitions/Roads_KanesWrath.xml` then run the same menu item to link the existing file to Road Libraries. Importer supports XML BOM and the EA default namespace.

See [official Phase 5A road definitions and 13-source-style validation](docs/Cnc3Phase5OfficialRoadDefinitions.md). Live Unity compilation and visual acceptance are still pending.

## v0.9.14 — Categorized extracted WBData folders

The active (non-BIG) import workflow supports this layout:

```text
Assets/CnC3KW/
├── Maps/
│   └── <map name>/<map name>.map + map.bin/.manifest/.imp/.relo
└── WBData/
    ├── DDS/            extracted diffuse/normal/specular textures
    ├── W3X/            extracted model + hierarchy + animation W3X
    │   └── Materials/  optional Unity-generated materials
    └── XML/            extracted XML metadata and optional Roads.xml
```

- **W3X FXShader material import** now searches the W3X's own folder first, then the categorized sibling **`WBData/DDS`** directory, falling back to a legacy flat `WBData` directory. Matching is exact, case-insensitive basename; DDS is preferred over TGA/PNG in collisions. A cached *filename index* avoids repeatedly scanning thousands of W3X models or loading all DDS textures. Resolved DDS assets become import dependencies so their changes trigger correct reimports.
- **Phase 5A road textures** now scan the dedicated `WBData/DDS` folder when it exists; scanning the whole `WBData` tree is unnecessary. A flat WBData layout remains supported. The road library still needs correct `RoadTemplate.Texture` mappings when road type names differ from atlas DDS names.
- **XML:** W3X is XML syntax internally, but the standalone `WBData/XML` texture-definition files are not interchangeable with the `.w3x` model importer. For an actual Roads.ini/Roads.xml definition, drag the matching file into `*_RoadLibrary.asset > Source Definitions` (requires Unity `TextAsset`). W3X hierarchy/animation companion files stay together in the W3X folder.
- **Source `map.bin` and map.manifest/imp/relo remain supported** and should stay next to their `.map`. The BIG-1/BIG-2 archive loaders remain **experimental**; neither BIG index field is needed.
- **Performance caveat:** organizing under `DDS/`, `XML/`, and `W3X/` helps Project window navigation, but Unity still imports all assets under `Assets`. If import time/memory is the real blocker, archive or relocate *unused* extracted resources **outside Assets** and bring only required assets into Unity. Do not move files that existing W3X/map materials or GUID references still use.
- **W3X export caveat:** some game-facing export checks still expect newly exported W3X texture assets beside the output W3X, even though Editor preview materials now resolve from the sibling DDS folder. Package-ready mod asset bundling is a separate validation step.

## Status: Direct BIG loading is experimental / paused

**Development decision (October 2026): Direct `.big` streaming is on hold.** BIG-1 and BIG-2 were previously merged into `main`, but are **not required for normal C&C3/Kane's Wrath map imports**, nor are they considered a stable replacement for extracted WBData assets.

**Recommended active workflow:** import `.map` and its existing compiled map bundle, use the already-extracted W3X/DDS/XML assets where available, and continue **Phase 5 road-material mapping and Phase 5B road geometry**. In map Inspector, leave **Road BIG Archive** and **Road Compiled Asset Index** empty. There is no need to open the two BIG Browser windows or build virtual archive indexes for the current road work.

BIG-1/2 source and tooling are retained for optional future investigation. Full compiled GameObject/Texture decoding is incomplete. Do not delete existing extracted assets while current maps or materials reference them. No BIG-3 development is currently scheduled.

## v0.9.12 — Direct BIG archive browser (no bulk extraction)

**You no longer need to unpack an entire `WBData.big` just to inspect its files.** Use **Tools → C&C SAGE → BIG Archive Browser**, select the original C&C3/Kane's Wrath `.big` (including one outside the Unity Assets directory), and save a compact `Cnc3BigArchiveIndex.asset`. This is an **index only**: thousands of W3X, DDS, XML and compiled entries are not individually imported.

The browser supports `BIG4`/`BIGF`, filename searching, member offset/size reports, stale-index detection, selective extraction of one requested file, and bounded on-demand EA RefPack decompression. Large *uncompressed* entries export via streaming.

For Phase 5A roads, assign the saved index to **Road BIG Archive** in the `.map` Inspector. If the BIG contains individually addressable DXT1/DXT5 `.dds` or `.png` entries matching road texture names, the importer loads **only required textures** as `.map` subassets, rather than generating a separate texture asset for every archive file.

**Important:** The original game archives often contain compiled `.bin`, `.manifest`, `.imp`, `.relo` streams instead of individually stored W3X/DDS. This release indexes and exposes those streams **without decoding their internal asset records**. If Road Types Textured remains zero, Phase BIG-2 must map compiled texture definitions and stream assets. Existing extracted WBData still must be preserved until complete direct-resource support is validated.

[Direct BIG guide, limitations and Unity steps](docs/Cnc3DirectBigArchive.md).

## v0.9.13 — BIG-2 compiled manifest/BIN virtual asset browser

**If your WBData.big exposes only `.manifest` and `.bin`, that is expected:** C&C3/Kane's Wrath compile many texture and model resources into those streams. The BIG archive browser is not failing—it is showing the physical members.

BIG-2 adds **Tools → C&C SAGE → BIG Compiled Asset Browser**. Assign the existing lightweight `WBData_Index.asset`; choose the matching manifest/BIN pair; create a virtual `WBData_CompiledIndex.asset` holding SAGE v5 asset names, types, references and exact BIN byte offsets, without extracting or importing thousands of files. Search `Texture:`, `GameObject:`, or `Road` and inspect a bounded 128-byte raw sample of any selected record.

For Phase 5A roads, the `.map` importer gains **Road Compiled Asset Index**. It can resolve an exact Texture record (original RoadTemplate.Texture first, then exact road TypeName as a candidate) and only attach a road texture if the individual asset contains a verified DXT1/DXT5 DDS stream. Most engine-native compiled texture payloads may **not** have a DDS header, so the initial result can remain zero textured roads until the specific SAGE texture resource format is decoded. Do not confuse virtual asset indexing with a complete GameObject/W3D reconstruction.

New self-test: **Tools → C&C SAGE → Run BIG-2 Compiled Index Self-Test**. No pre-existing extracted files should be deleted yet, and live Editor/real WBData compilation must be tested before merging.

[Technical walkthrough and limits](docs/Cnc3BIG2CompiledVirtualAssets.md).

## Generals / Zero Hour — binary W3D import (new in 0.8.0)

**One Unity plugin, two formats.** Add .w3d files under Assets/ and Unity imports them automatically using a separate import-only reader. The existing W3X tool menus remain available.

1. Extract legally obtained W3D files from your Generals / Zero Hour BIG archives and copy them into your Unity Assets folder.
2. Put companion *_SKL.w3d files beside *_SKN.w3d or animation W3D files when available. Extract referenced textures to the same folder and import the images into Unity.
3. Unity creates an imported GameObject; expand it for Mesh and basic uncompressed AnimationClip subassets. Pivot hierarchy is displayed under SAGE_Pivots.
4. For standalone animations, use the generated legacy AnimationClip on a model with the same skeleton names and paths. Some models still require manual assembly.

**Experimental support:** mesh geometry, triangle normals, primary UV coordinates, simple first-texture materials, pivot parenting, same-file HModel mesh attachment, one-bone vertex influences, uncompressed translation and quaternion animation channels, and same-directory skeleton lookup.

**Not supported yet:** compressed animation, HLOD assembly across separate files, multi-weight skinning, full fixup matrices, exact Generals shaders/material passes, emitters, game BIG extraction or W3D export. Expect imperfect skinned model previews and manual setup for complex assets. Unsupported content should be reported in the Console where recognized.

Try **Package Manager > Samples > Minimal W3D Import Test**, run the included Python generator, and inspect the generated W3D test assets in Unity. The generator uses no EA game content.

See docs/W3DImporter.md for troubleshooting and test expectations.

## Main workflow — import, edit, export W3X animations

1. Place the **original uncompiled `.w3x`** animation and its companion hierarchy **`*_SKL.w3x`** and other relative sources together in their original directory under Unity `Assets/`. The importer creates `AnimationClip` sub-assets when the SAGE hierarchy resolves.
2. Select the model and relevant animation assets in the **Project** window. Choose **Tools > C&C SAGE > Create Phase 3B Editable Animation Workspace**.
3. Edit the independent `*_Editable.anim` clip in the newly created workspace. The original W3X and model are not altered.
4. Select the editable `.anim` or its matching `*_Source.asset` provenance file. Choose **Tools > C&C SAGE > Export Animation to W3X...**.
5. Save a **new** `*_unity_anim.w3x` filename **beside the original source W3X**. The exporter preserves unrelated W3X XML, stages the new file for verification, and refuses overwrites.
6. Optionally select only the editable clip and choose **Tools > C&C SAGE > Validate Phase 3D Animation Round Trip**. Confirm the detected latest importable `*_unity_anim*.w3x` output; alternatively Ctrl+select exactly one specific exported W3X along with the edited clip. The report compares original-frame local positions, rotations, and unrelated XML.

**Important:** Do not copy only an exported animation W3X into the Phase 3B workspace directory. Relative companion `*_SKL.w3x` references can then fail, preventing the AnimationClip from importing.

### Animation exporter boundaries

- Supports source-backed editing of existing scalar `XTranslation`, `YTranslation`, `ZTranslation`, and quaternion `Orientation` channels.
- Supports new translation and complete quaternion/Euler rotation tracks **only on pivots already present** in the source SAGE hierarchy.
- Preserves unknown XML, the original hierarchy, the original W3X and unmodified channel frames; skips unchanged implicit Unity position components.
- Samples edited curves at source frame rate. **Unity animation tangents and subframe interpolation are not exported**.
- The read-only round-trip audit compares sampled position (default tolerance 0.002 Unity units) and quaternion rotation (0.5°) and checks XML preservation. It does not prove compiled Kane's Wrath gameplay compatibility.

## Production Editor menu

The following user-facing tools remain installed under **Tools > C&C SAGE**:

| Tool | Purpose |
|---|---|
| **Create Phase 3B Editable Animation Workspace** | Create independent editable AnimationClips and preview assets |
| **Export Animation to W3X...** | Export selected edited animation to a new, source-backed W3X |
| **Validate Phase 3D Animation Round Trip** | Read-only audit of edited clip vs reimported W3X |
| **Export Selected W3X (Safe Roundtrip)...** | Safe XML/mesh round-trip export |
| **Export Selected W3X (Including New Static Meshes)...** | Export compatible newly authored static meshes |
| **Export Selected W3X (Skin & Shader Textures)...** | Export supported skin/texture modifications |
| **Export Selected W3X (Local Pivots)...** | Export supported original-hierarchy local pivot edits |
| **Phase 2D Skinned Mesh Editor** | Edit supported existing skinned-mesh attributes |
| **Inspect Selected Model Rig & Materials** | Inspect imported model structures |
| **Inspect Selected W3X UV Channels** | Inspect UVs and source texture mappings |
| **Diagnose Selected W3X Animation** | Diagnose parsed source animation channels |
| **Audit Selected Animation Pivot Bindings** | Compare animation paths to the target model |
| **Validate Selected W3X Mesh Edits (No Write)** | Read-only model edit check |
| **Compare Pitbull Editable vs Unity Skin Export** | Specialized legacy Pitbull diagnostic |

The diagnostic tools above are **not** automated self-tests; they remain useful when troubleshooting real source W3X data. The nine `Run Phase ... Self-Test` entries are removed from the normal Editor menu and package script compilation.

## Import and geometry support

Experimental capabilities include W3DMesh vertices, triangles, normals, two UV sets, named `W3DHierarchy` pivots, companion SKL lookup, skinned meshes with compatible BoneInfluences, W3DContainer attachments, and initial FXShader materials/texture resolution.

Known limits include complete HLOD assembly, exact SAGE multipass shaders, arbitrary topology/new skinned meshes, certain `FixupMatrix` operations, binary W3D compilation, and verified Kane's Wrath runtime compatibility. Some advanced mesh/pivot export operations deliberately refuse unsupported changes instead of silently damaging source data.

## Development tests and version history

Historical manual smoke/self-test scripts have been **excluded from Unity's imported Editor folder** and archived in [Tests~/ManualEditorSelfTests](Tests~/ManualEditorSelfTests). Unity ignores the `Tests~` directory of a Git/UPM package, so these scripts add **no default menu items or compile overhead**. They are retained for maintainers to restore **in a development clone** when diagnosing regressions.

Historical design notes, phase reports and old self-test instructions have been moved from the previous README to [docs/DevelopmentHistory.md](docs/DevelopmentHistory.md); they describe older versions and should not be treated as instructions for the default v0.7.4 Editor menu.

## License and references

This is an independent C# implementation, not copied/ported from the [OpenSAGE Blender plug-in](https://github.com/OpenSAGE/OpenSAGE.BlenderPlugin). No original EA game assets are bundled. See [LICENSE](LICENSE) for this repository's MIT license.
