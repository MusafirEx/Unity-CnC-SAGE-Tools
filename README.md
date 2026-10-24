# Unity-CnC-SAGE-Tools

Unity 2022.3 LTS+ Editor tools for **Generals / Zero Hour** binary **W3D importing**, alongside **Tiberium Wars / Kane's Wrath** source **W3X** model/animation import and export.

**Version 0.9.1 — C&C 3 / Kane's Wrath `.map` Mode 1 terrain and object placement importer, alongside W3D/W3X.** The normal Unity package no longer imports nine development smoke/self-test scripts or exposes any `Run ... Self-Test` menu items. Existing importers, animation creation, model export, animation export, and read-only inspection/audit tools remain available.

> W3D support is **import only**; this package is not a W3D exporter, game asset extractor or confirmed game-ready mod builder. Keep backups and validate with the game toolchain separately.

## Install / update

In Unity, select **Window > Package Manager > + > Add package from git URL**:

```text
https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git
```

Git and Unity 2022.3 LTS or later are required. After updating a Git-based package, ensure Package Manager resolves **0.9.1** and scripts compile. Do not keep a duplicate installed copy under `Assets/`, or duplicate Editor assemblies may conflict.

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
