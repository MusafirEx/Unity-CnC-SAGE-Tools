# Unity-CnC-SAGE-Tools

Unity 2022.3 LTS+ Editor tools for **Command & Conquer: Tiberium Wars / Kane's Wrath** SAGE **source W3X XML** models and animations.

**Version 0.7.4 — streamlined Unity Editor menus.** The normal Unity package no longer imports nine development smoke/self-test scripts or exposes any `Run ... Self-Test` menu items. Existing importers, animation creation, model export, animation export, and read-only inspection/audit tools remain available.

> This is an experimental source W3X toolkit, **not** a compiled `.w3d` exporter or a confirmed game-ready mod builder. Keep backups and validate with the game toolchain separately.

## Install / update

In Unity, select **Window > Package Manager > + > Add package from git URL**:

```text
https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git
```

Git and Unity 2022.3 LTS or later are required. After updating a Git-based package, ensure Package Manager resolves **0.7.4** and scripts compile. Do not keep a duplicate installed copy under `Assets/`, or duplicate Editor assemblies may conflict.

## Main workflow — import, edit, export animations

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
