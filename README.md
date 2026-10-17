# Unity-CnC-SAGE-Tools

Experimental **Unity 2022.3+** Editor importer for uncompiled **Command & Conquer SAGE W3X** model sources, intended to grow into W3X/W3D import/export tooling.

**Status: 0.2.3 Phase 2B Project-asset workflow and missing-mesh diagnostics.** Geometry/UVs, SAGE pivot transforms, basic bone weights/SkinnedMeshRenderer and first-pass FXShader materials are implemented experimentally. Export, binary W3D, animation clips, exact SAGE shading, full HLOD assembly and validated Kane's Wrath round-tripping are **not yet implemented**.

## Install in Unity

Open **Window > Package Manager > + > Add package from git URL** and enter:

```text
https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git
```

Requires Git installed and Unity 2022.3 LTS or later.

## Try the importer

1. In Package Manager, select **C&C SAGE Model Tools** and import the **Minimal W3X Mesh** sample.
2. Select the imported `.w3x` asset to see the generated Unity model.
3. Drag it into the scene. A child mesh, MeshFilter, MeshRenderer and a component preserving the source mesh ID should appear.
4. Place your own **uncompiled XML `.w3x`** file inside `Assets/` to trigger import.

### Present support

- XML `AssetDeclaration` containing one or more `W3DMesh` elements.
- Recognizes hierarchy (`_SKL`), animation (`_ATKA`, `_IDLA`), container, collision and texture declarations without falsely requiring mesh geometry.
- `CncSageSourceInfo` on the root records detected declaration names and imported mesh count. Zero-mesh assets are **metadata-only placeholders**, not functioning skeletons or animations.
- Direct vertices, triangle indices, optional per-vertex normals and **first two UV sets** (Unity Mesh.uv / Mesh.uv2).
- Relative local `<Includes><Include source="ART:..."/></Includes>` resources when present.
- Auto-generated model hierarchy with one child GameObject per mesh and retained mesh ID.
- Basic preliminary axis conversion: `(X,Y,Z) -> (X,Z,Y)`, reversing triangle winding.
- `W3DHierarchy` pivots reconstructed into named Unity Transform objects. Parent indices and local translations/rotations are applied; `CncSagePivotInfo` retains original NameID and FixupMatrix XML for later export.
- Tries to resolve a companion `*_SKL.w3x` file by `W3DContainer.Hierarchy` reference or conservative same-model naming.
- `GeometryType="Skin"` meshes with valid `BoneInfluences` and pivots generate `SkinnedMeshRenderer`, normalized weights and bindposes.
- Initial `FXShader` materials are generated as W3X sub-assets; the first shader pass can use neighboring TGA/DDS/PNG textures referenced by shader constants. `CncSageMaterialMetadata` retains the original shader name/constants for later export.
- `W3DContainer` subobject BoneIndex can attach non-skinned mesh parts to pivot transforms.

### Phase 1B rig and material regression

- Import `Samples~/MinimalW3X/SkinnedTriangle.w3x` through Package Manager Samples.
- Expect a `SAGE_Pivots` hierarchy with ROOT and WHEEL transforms, a `SkinnedMeshRenderer`, 2 bones, and a generated material.
- Real Pitbull: reimport `GUPitbull_SKN.W3X` and `GUPitbullR_SKN.w3x`; check that a matching local `GUPitbull_SKL.W3X` was associated and the bone influence count matches vertex count. If no reliable hierarchy is resolved, the importer deliberately falls back to a static mesh with a warning.
- For missing texture definitions (for example `GURig.xml`), the importer can create a placeholder material even without a matching texture. It does not invent texture files.
- No automatic animation clip conversion or game-correct shader reproduction is claimed. Rotation conversion and bind pose placement still need a Blender versus Unity comparison on the actual Pitbull.

### Phase 1 regression check

The `Minimal W3X Mesh` sample now includes:

- `MinimalTriangle.w3x`: should still generate a `MeshFilter` and mesh.
- `HierarchyOnly.W3X`: should import with a `CncSageSourceInfo` component, zero meshes, and no import exception.
- `AnimationOnly.W3X`: same metadata-only behavior.

Real `*_SKL.W3X`, `*_ATKA.W3X` and `*_IDLA.W3X` sources should no longer throw `No W3DMesh geometry found`. Missing local `ART:` reference warnings and skinning limitations remain until later phases.

If an earlier repository ZIP was extracted into `Assets/Extension/Unity-CnC-SAGE-Tools-main`, replace that copy with the updated files. Updating a Git package **does not update an independent Assets folder copy**. Avoid installing both copies because duplicate assemblies may conflict.

### Inspect W3X UV mapping

1. Select `GUPitbull_FP.w3x` in the Unity Project window (or its imported scene instance).
2. Select **Tools > C&C SAGE > Inspect Selected W3X UV Channels**.
3. Read the report in the Console: per-mesh vertex count, number of `TexCoords` blocks, coordinates per channel, UV bounds, imported Mesh.uv / Mesh.uv2 counts, and original `FXShader` texture names.
4. If the file contains multiple `TexCoords` blocks, the first now imports to `Mesh.uv` (UV0) and the second to `Mesh.uv2` (UV1). The synthetic triangle sample has two intentionally distinct UV sets for regression testing.

**UV mapping note:** A visible mismatch can be caused by choosing the wrong material/texture, by the mesh using a secondary UV stage, or by texture coordinate transforms in SAGE shader declarations. Simply enabling UV1 does not automatically make Unity's Standard shader use it for the main texture. Do not flip/overwrite the original UVs without inspecting the model's source.

### Not yet supported

- Animation clips, all source pivot fixup matrices, multibone deformation variants and fully validated SAGE skinning.
- Exact FXShader effect graphs, multipass triangle material assignments, all texture stages, UV orientation validation.
- Full HLOD/LOD model assembly, collision boxes, proxies and aggregate export.
- Binary `.w3d` files or compiled game archives.
- Full W3X/W3D **game-ready export**, creation of new models from scratch, or tested Kane's Wrath in-game interoperability.

**Warning:** Coordinate conversion, bindposes and complex FXShader material behavior still need verification with real game assets. A default Unity material may appear where SAGE shader definitions are missing. Do not use this prototype to overwrite original game files.

## Reference and licensing

The XML element names were checked against the OpenSAGE Blender plug-in source:
https://github.com/OpenSAGE/OpenSAGE.BlenderPlugin

This project contains an **independent C# implementation**, not a port or copy of OpenSAGE's LGPL-3.0 Python implementation. Original SAGE/C&C game assets are not bundled. See `LICENSE` for this project's MIT license.


## Phase 2A: W3X source-preserving export (v0.2.0)

This is the **first experimental export milestone**, not a finished Kane's Wrath exporter. No scene objects are needed for importing or exporting Project assets. Existing sources are never overwritten.

1. In the Unity Project window, select the original imported `.w3x` model, or select an instance of that imported W3X prefab in the scene.
2. Open **Tools > C&C SAGE > Export Selected W3X (Safe Roundtrip)...**.
3. Save a **new** file next to the original (recommended filename: `Model_unity_roundtrip.w3x`).
4. The exporter validates mesh IDs and topology, emits a W3X XML copy, reloads it and checks that non-mesh declarations were preserved.
5. For a quick internal test use **Tools > C&C SAGE > Run Phase 2 Exporter Self-Test** and check Unity Console for `PASS`.

### Export behavior

- Retains original XML includes, `W3DHierarchy`, `W3DContainer`, `W3DAnimation`, `FXShader`, bone weights and unknown SAGE nodes as source declarations, without attempting to regenerate unsupported data.
- For static `W3DMesh`, exports compatible **vertex positions, normals, and existing UV0/UV1 channels**, applying inverse Y/Z axis conversion. The triangle indices/order must match the original importer. Position edits update bounding volumes and triangle planes where possible.
- Source meshes with `AABTree` cannot have changed vertex positions exported until spatial-tree rebuild is implemented.
- For `GeometryType="Skin"` models the **original source mesh block** is retained. The Phase 1B importer bakes rest-pose transforms, so writing its generated vertices directly as original skin coordinates would be unsafe.
- Material edits, new Unity mesh topology, Unity transform/pivot edits, animation changes, external ART reference relocation, shader pass reassignment and new models created entirely inside Unity are **not exported** yet.
- The exporter rejects destructive topology mismatches or attempts to overwrite the original. W3X files with external includes must be saved alongside the source, because include locations are unchanged.

### Phase 2 acceptance testing

- Use `MinimalTriangle.w3x` for export/reimport of static geometry and the two existing UV channels.
- Use `SkinnedTriangle.w3x` and `GUPitbull_SKN.W3X` to confirm the exporter preserves original `BoneInfluences`, `W3DHierarchy` and `FXShader` declarations.
- Compare the source and exported model in Unity, checking mesh vertex counts, UV0/UV1, hierarchy count and material assignment. A separate in-game SAGE asset-builder validation is still required.


## Phase 2B: edited mesh, skin, pivot and texture export (preview)

**Phase 2A's safe export command is unchanged** and remains the recommended default. Phase 2B is experimental and can be tested without writing game assets.

### Test the selected Pitbull without modifying files

1. Select `GPPitbull.w3x` or `GPPitbull_unity_roundtrip.w3x`.
2. Run **Tools > C&C SAGE > Validate Selected W3X Mesh Edits (No Write)**.
3. The tool temporarily duplicates a static mesh in memory, changes UV0 and (when no source AABTree is present) one vertex, then checks the W3X XML output. It destroys its temporary objects and writes **no files**.
4. Check Unity Console for **`[C&C SAGE Phase 2B] Edited W3X test PASS`**. If the source mesh contains AABTree, only UV export is tested.
5. Run **Tools > C&C SAGE > Run Phase 2B Advanced Export Self-Test** for a synthetic skeleton, two-influence skinning, local pivot edits, FixupMatrix rejection and runtime-only texture rejection.

### Project folder structure

The source files remain unchanged in your existing `Assets/RTS Engine/Demo/Menu/Pitbull/` folder.

```text
Assets/
  RTS Engine/Demo/Menu/Pitbull/
    GPPitbull.w3x                   (original source)
    GPPitbull_unity_roundtrip.w3x   (separate export)
  CncSageEditable/
    GPPitbull_Editable.prefab       (editable Project asset)
    GPPitbull_Editable_Mesh_0.asset (independent mesh)
    GPPitbull_Editable_Material_0.mat
```

You can run **Tools > C&C SAGE > Validate Selected W3X Mesh Edits (No Write)** on the original `.w3x` or an editable linked prefab directly from the Project window. The validator uses a disposable Preview Scene internally; it does not save a scene object.

### Editing workflow

1. Select the imported `.w3x` model asset in Project.
2. Choose **Tools > C&C SAGE > Create Editable W3X Project Prefab**. The tool writes a separate linked `.prefab`, copied `.asset` meshes, and `.mat` materials to `Assets/CncSageEditable` without adding a persistent object to the active scene.
3. Select the new `*_Editable.prefab` in **Project** and edit its independent Mesh/Material assets. To edit pivot transforms, use Unity Prefab Mode (not the active game scene). Mesh vertices/UVs need a suitable editor tool or script; this package does not yet include an interactive vertex editor.
4. Select **the editable `.prefab` asset in Project**, then use the safe or advanced W3X export command. Its `CncSageEditableSource` component stores the original W3X GUID, and the exporter reads that source for SAGE metadata. Save a new W3X beside its original if relative ART includes are present.
5. Reimport the newly generated W3X in **Project** and compare both Project assets. To export a separately defined `_SKL.W3X` hierarchy, first create an editable Project prefab from the `_SKL` asset itself; direct source-asset edits are not required.

### Authoring commands

- **Tools > C&C SAGE > Export Selected W3X (Safe Roundtrip)...** — existing Phase 2A behavior (skin/pivots/FXShader remain source-preserved).
- **Tools > C&C SAGE > Export Selected W3X (Skin & Shader Textures)...** — exports compatible edited skin rest vertices and existing one/two-bone weights by inverting the weighted rest-pose transform, and updates **existing** `FXShader` diffuse, normal and specular texture names from the selected Unity material.
- **Tools > C&C SAGE > Export Selected W3X (Local Pivots)...** — exports edited local pivot translations and rotations **only** if the selected W3X file itself defines the `W3DHierarchy`. If the mesh includes an external `*_SKL.W3X` file, select the `_SKL` asset separately and export it. Parent/index/name changes and pivot edits with `FixupMatrix` are rejected.

All commands generate **new W3X XML copies**, not binary W3D, and do not overwrite the selected W3X original. Source `ART:` includes remain unchanged, so exported sources with includes must stay beside the original. Do not use these generated files to overwrite any C&C game assets until separately validated with the intended SAGE mod SDK.

### Known constraints and verification

- Skinning requires unchanged topology, at most two existing influence channels, unchanged bone indices, valid bindposes, matching SAGE pivot ordering and invertible source/rest-pose transforms. The exporter refuses unsupported data instead of emitting knowingly malformed skinning XML.
- For skin vertices with an `AABTree`, changing source-space positions is blocked until the collision/spatial tree can be regenerated.
- Local pivots with `FixupMatrix` cannot be safely changed without recomputing the original bind/fixup data. Unchanged source fixups are preserved.
- `FXShader` texture updates require a texture asset under `Assets/`, an existing source texture constant and exactly one source FXShader pass. No new shader stages, material graphs, changed source texture definitions or shader-color mappings are created.
- Material texture changes are applied **only** in the explicit advanced skin/material command, not to the safe exporter.
- Before considering Phase 2B complete, check real-world `GPPitbull` and `GUPitbull_SKL` exports against Blender and Unity reimport; a standalone Unity smoke test cannot certify game-builder compatibility.
- **The current GitHub branch has not yet been compiled or runtime-tested against the user's Unity Editor.**


### Diagnosing missing meshes in Pitbull Unity skin export

Use **Tools > C&C SAGE > Compare Pitbull Editable vs Unity Skin Export** to compare
\`GPPitbull_Editable.prefab\`, its GUID-linked original \`.w3x\`, and
\`GPPitbull_unity_skin.w3x\` **directly from the Project folder**.
The report in Unity Console lists source, edited and exported mesh counts and
all IDs missing from the exported model.

- **NEW MESH** — an identity exists on the editable prefab but did not exist
  in the originally imported W3X model. Phase 2B cannot add W3DMesh declarations
  or W3DContainer bindings for this mesh. Adding \`CncSageMeshIdentity\`
  alone does not make it exportable; Phase 2C support is required.
- **ORIGINAL MESH MISSING** — a mesh existed in the imported W3X and the
  editable model, but not in the reimported output. Check \`ART:\` includes,
  geometry import errors, and Unity's Editor.log.
- **UNTAGGED** — a GameObject with MeshFilter or SkinnedMeshRenderer is missing
  \`CncSageMeshIdentity\`; the exporter has no source identifier to match it.
- **DIFFERENT** — matched meshes differ by renderer type or vertex count; review
  source bindings or edited topology.

The tool only reads Project assets and logs findings. It does not change
the Project assets, active scene, or source W3X. The exact two filenames above
must exist uniquely under \`Assets/\`.

Starting with v0.2.3, the W3X export menu detects mesh IDs absent from the
original source or mesh GameObjects lacking an identity. It warns with the
names and recommends **Cancel Export**, rather than silently producing an
apparently complete model. You can explicitly choose to export existing meshes
only, accepting that new meshes will be omitted.

If the Editor log repeatedly says \`Build asset version error\` for the package
script \`CncSageMeshIdentity.cs\`, let Unity finish recompilation and refresh
the Git package (Window > Package Manager, refresh/update). Do not edit
\`Library/PackageCache\` manually or delete the W3X originals. Reimport the
source and exported assets after resolving the Asset Database mismatch.

## Roadmap

1. Validate geometry, coordinate system and UVs against legal user-provided sample W3X models.
2. Validate Phase 1B hierarchy and materials, including companion _SKL W3X and external ART resolution.
3. Validate Phase 2A/B edited W3X export, compare static/skin changes against Blender, then implement true new-topology and complete rigging/material round-trips.
4. Add animation support, then investigate binary W3D and WrathEd toolchain validation.
