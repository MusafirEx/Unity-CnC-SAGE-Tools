# Unity-CnC-SAGE-Tools

Experimental **Unity 2022.3+** Editor importer for uncompiled **Command & Conquer SAGE W3X** model sources, intended to grow into W3X/W3D import/export tooling.

**Status: 0.7.2 Phase 3D preview: suppress unchanged implicit Unity position channels during new SAGE channel detection.** Geometry/UVs, SAGE pivot transforms, basic bone weights/SkinnedMeshRenderer and first-pass FXShader materials are implemented experimentally. Edited W3X export remains experimental; **existing-channel editing and new-track export on source pivots** is now available as a Phase 3C preview, while animation roundtrip/game-toolchain validation, binary W3D, exact SAGE shading, full HLOD assembly and validated Kane's Wrath interoperability are **not yet completed**.

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


## Phase 2C: new static W3X mesh export (v0.3.0 preview)

### Confirmed Pitbull discrepancy from Unity Editor.log

The **Compare Pitbull Editable vs Unity Skin Export** log identified
\`GPPITBULL.NEWLabel\` as the single missing GameObject mesh in
\`GPPitbull_unity_skin.w3x\`: one static MeshFilter/MeshRenderer with
**24 vertices**. It was not present in original \`GPPitbull.w3x\`;
Phase 2B silently skipped it because only original W3DMesh declarations
were exported. **Phase 2C specifically fixes this class of omission.**

### Export an editable Project prefab (no scene instance)

1. Keep \`GPPitbull_Editable.prefab\` in \`Assets/CncSageEditable/\`, including
   \`GPPITBULL.NEWLabel\`, a valid MeshFilter/MeshRenderer, and
   \`CncSageMeshIdentity.SageIdentifier=GPPITBULL.NEWLabel\`.
2. Select **the editable prefab asset in the Project window**.
3. For static-only changes, choose **Tools > C&C SAGE > Export Selected W3X
   (Including New Static Meshes)...**. This preserves the original skinned
   source data and generates new static W3DMesh declarations.
4. For edits to existing skin weights/vertices and new static meshes together,
   choose **Tools > C&C SAGE > Export Selected W3X
   (Skin & Shader Textures)...**; this Phase 2C mode now includes new static
   meshes too. It retains Phase 2B's experimental skin restrictions.
5. Save to a **new W3X filename** beside the original W3X (e.g.
   \`GPPitbull_unity_meshes.w3x\`), select the output in Project, reimport,
   and run **Tools > C&C SAGE > Compare Pitbull Editable vs Unity Skin Export**
   when output name matches \`GPPitbull_unity_skin.w3x\`.
   The comparator uses the two fixed Pitbull file names; use the new output
   name \`GPPitbull_unity_skin.w3x\` to run that specific comparison.
6. Run **Tools > C&C SAGE > Run Phase 2C New Mesh Self-Test** and check the
   Console for \`New mesh self-test PASS\`. This tests 24 vertices, 8 triangles,
   a known source FXShader, pivot attachment and XML validation entirely in
   an isolated preview scene.

### Exported structure

The exporter appends a new \`W3DMesh\` with source-space positions,
normals, UV0/UV1, shade indices, bounding data and triangles. It reverses
Unity triangle winding for the W3X Y/Z coordinate convention. If the source
contains one \`W3DContainer\`, the exporter appends a new \`SubObject\`
referencing the mesh and its nearest existing SAGE pivot. No source
declarations or assets are overwritten. Existing mesh declarations are
preserved or edited by the previously selected mode.

If the original file contains an \`FXShader\`, the first shader definition is
copied to the new mesh, and its existing texture references can be updated
from a valid Unity Project material. This is **template-based**, not a
fully authored SAGE shader/texture-stage exporter. Verify the shader technique
and texture stages for each added mesh in the target mod builder.

### Phase 2C limitations

- **New static meshes only**. The exporter explicitly rejects new
  \`SkinnedMeshRenderer\` declarations. Existing skinned meshes follow the
  Phase 2B export mode.
- Each added mesh requires \`CncSageMeshIdentity\` with a new, unique
  \`MODEL.PART\` ID, e.g. \`GPPITBULL.NEWLabel\`. The mesh must have exactly
  one Unity submesh and at most one material.
- Negative/singular transforms, malformed indices, degenerate triangles,
  unsupported multi-container source files and untagged Mesh objects are
  rejected to avoid incomplete W3X output.
- With a single source \`W3DContainer\`, attachment is to the nearest existing
  \`CncSagePivotInfo\` ancestor, or to pivot zero. Pivot positions and
  topology are otherwise unchanged. New bones, skinning and animations are
  not authored.
- The default **Safe Roundtrip** command still intentionally does not add
  new geometry, to preserve its prior behavior.
- Reimport into Unity is the acceptance test for this milestone. The generated
  XML is **not yet certified for the C&C3/Kane's Wrath asset-building toolchain**.
- The RTS bridge currently reports Unity IPC disconnected; the PR requires
  an actual editor compile and regression run before merge.


## Phase 2C material identity (v0.3.1)

A newly added static mesh such as \`GPPITBULL.NEWLabel\` already needs
\`CncSageMeshIdentity\` for its unique W3DMesh ID. It can now **optionally**
also have \`CncSageMaterialIdentity\` to choose its SAGE FXShader source and
texture references. The original Unity MeshRenderer material still controls
the editable prefab preview; the material identity controls which SAGE
shader/texture declarations are **exported**.

### Project-only authoring

1. Open \`Assets/CncSageEditable/GPPitbull_Editable.prefab\` in **Prefab Mode**
   (or select its child from the prefab hierarchy). You do not need to place
   the model into the game's active scene.
2. Select the added \`GPPITBULL.NEWLabel\` mesh and ensure it has
   \`CncSageMeshIdentity\` with \`GPPITBULL.NEWLabel\`.
3. Add component **C&C SAGE > Material Identity** to the same GameObject.
4. In **Shader Template Mesh Id**, enter the ID of a mesh **already inside
   original \`GPPitbull.w3x\`**, such as \`GPPITBULL.NEWSKIN\`, whose FXShader
   technique/stages are suitable for the added mesh. This copies that shader's
   \`ShaderName\`, \`TechniqueIndex\`, and original shader constants.
5. Leave **Use Renderer Textures** enabled to read the assigned Unity
   \`.mat\`'s \`_MainTex/_BaseMap\`, \`_BumpMap/_NormalMap\`, and
   \`_SpecGlossMap\` when possible. Or explicitly drag a \`Texture2D\`
   into **Diffuse/Normal/Specular Texture** fields. A dragged reference takes
   precedence over the assigned Unity material.
6. Alternatively, enter just the basename in the **Diffuse/Normal/Specular
   Texture Name** field (for example \`GUPitbull_NRM\`). The exporter searches
   for supported \`.tga/.dds/.png/.jpg/.jpeg/.bmp\` image files **beside the
   emitted W3X**. An explicit image reference has priority, then a resolvable
   texture name, then the assigned material texture.
7. Select \`GPPitbull_Editable.prefab\` **in Project** and choose
   **Tools > C&C SAGE > Export Selected W3X (Including New Static Meshes)...**.
   Export beside the original W3X/texture files. Reimport the new W3X
   and inspect \`GPPITBULL.NEWLabel\`'s material and texture.

### Safety and compatibility

- When **Shader Template Mesh Id** is empty, the Phase 2C legacy behavior
  (first source FXShader) remains available for backward compatibility. If
  the specified template mesh is missing, or has multiple FXShader passes,
  the export is rejected instead of guessing.
- Texture files must be valid imported images in the Unity \`Assets/\` folder
  and next to the output W3X. Explicit invalid/misplaced texture assignments
  cause an error. A missing optional texture **name** or unusable Unity
  material texture produces a report note and retains the original shader
  constant. External \`ART:\` paths are **not copied or resolved globally**.
- The exporter modifies only compatible texture constants **already present
  in the chosen template** (\`DiffuseTexture\`/\`Texture_0\`, \`NormalMap\`,
  \`SpecMap\`). An explicit override with no corresponding FXShader constant
  is rejected. The SAGE shader cannot be inferred safely from a Unity
  Standard/URP material and the actual TW/KW shader pass must be validated.
- This component is **optional for new meshes**; it does not alter the
  previously preserved shader declarations of existing source meshes.
- **Tools > C&C SAGE > Run Phase 2C New Mesh Self-Test** now checks shader
  template selection, explicit unsaved texture rejection and (when the
  GUPitbull sample texture exists) file/name-based normal texture mapping.
- Full game-ready material authoring, arbitrary new FXShader creation and
  automatic texture copying are not yet implemented.


## Phase 2D: Unity skinned mesh authoring (v0.3.2 preview)

ProBuilder does **not** provide the equivalent of safe geometry/weight editing
for imported SAGE \`SkinnedMeshRenderer\` objects. Phase 2D adds a restricted
**Tools > C&C SAGE > Phase 2D Skinned Mesh Editor** window. This tool edits
the independent \`Mesh.asset\` already copied by
**Create Editable W3X Project Prefab**. It does **not** convert meshes to
\`ProBuilderMesh\` and does **not** modify the original imported W3X.

### Editing \`GPPITBULL.NEWSKIN\` without the active Unity scene

1. In **Project**, double-click \`Assets/CncSageEditable/GPPitbull_Editable.prefab\`
   to open **Prefab Mode**. This is an isolated authoring stage, not a
   persistent instance in \`MainMenu\`.
2. Select the child \`GPPITBULL.NEWSKIN\` which has \`SkinnedMeshRenderer\`.
3. Open **Tools > C&C SAGE > Phase 2D Skinned Mesh Editor**.
   Click **Use Selected**, then **Refresh Source Validation**. The editor
   requires the prefab's \`CncSageEditableSource\` GUID, mesh identity and
   independent Mesh asset in \`Assets/CncSageEditable\`.
4. Use **Vertex** to choose an existing vertex. Optionally select one of
   the highlighted vertex dots in Scene view, or move the selected vertex
   with the Scene-view handle. The Inspector window also exposes **Local
   Rest Position**, **UV0**, and **UV1**.
5. For W3X skin meshes with **two original BoneInfluences** and distinct
   existing bone indices, use **Primary weight** to redistribute influence
   between *those same two bones*. The opposite weight becomes
   \`1 - primary\`. Single-bone vertex indices cannot be changed.
6. Click **Save Mesh Asset**. The tool records Undo for edits and preserves
   original triangle indices and bindposes.
7. Select **the editable prefab in Project** and export using
   **Tools > C&C SAGE > Export Selected W3X (Skin & Shader Textures)...**.
   Do not use **Safe Roundtrip**, which intentionally preserves the original
   skinned source block. Reimport and check the edited vertex/weights.
8. Run **Tools > C&C SAGE > Run Phase 2D Skin Editing Self-Test** to validate
   the temporary synthetic rest-position/weight edit guards.

### Safety and current scope

- **No topology editing** (no add/delete/merge/split of vertices or faces),
  no new bone indices, bindposes, weight channels or skin meshes.
- Only independent \`.asset\` meshes inside \`Assets/CncSageEditable/\` may
  be edited. Original imported \`.w3x\` mesh sub-assets are protected.
- The Scene view displays rest-position vertex handles; avoid posing or
  animating bones while editing. This is an MVP, **not** a full deformed
  surface sculptor or weight-paint brush.
- If the original skinned source contains \`AABTree\`, rest-position edits
  are disabled because collision/spatial-tree rebuild is unavailable.
- Editing existing weights may produce a new source-space rest position
  during inverse weighted transform export. The exporter may reject it
  when \`AABTree\` is present; the Editor checks topology/bone indices
  but **does not guarantee every edited weight set is exportable**.
- The Phase 2B experimental skin exporter still requires the original
  influence groups, bone indices and valid SAGE rig data. Test an actual
  \`GPPITBULL.NEWSKIN\` export/reimport before treating Phase 2D as complete.
- Editor source code has been statically audited, but actual Unity
  compilation, UI interaction and end-to-end Pitbull skin round-trip
  **still require live validation**.


### Phase 2D: handling degenerate triangles and GUI state (v0.3.3)

On October 18, 2026, the actual \`GPPitbull_Editable_Mesh_2.asset\`
editor saved changes, but the advanced skin export stopped with:

\`\`\`
InvalidDataException: Degenerate triangle after vertex edit; export aborted.
W3xExportCore.UpdateTrianglePlanes() -> W3xExportAdvanced.PatchSkin()
\`\`\`

The old exporter recomputed **every triangle plane after changing a single
skin vertex**. Existing game models can contain zero-area triangles that
should be preserved, not recalculated if their vertices are untouched.

The fix:
- Preserve the original \`Nrm\` and \`Dist\` of **unaffected triangles**,
  even if an unrelated part of the source mesh has a degenerate face.
- If an **affected** triangle is still degenerate, stop with its triangle
  index and the three vertex indices. It is not safe to fabricate a plane.
- When moving a skinned vertex in the Phase 2D editor, reject collapsed
  triangles **before mutating the Mesh.asset**, and show a warning.
- Mark mesh assets dirty while dragging/typing, **without synchronous
  \`AssetDatabase.SaveAssets\` for every edit**. The explicit
  **Save Mesh Asset** button queues saving on the next editor callback.
  This reduces reimports during IMGUI events, which may contribute to
  repeated \`the GUIStateObj is deleted, but is accessed\` warnings.
- Extend **Run Phase 2D Skin Editing Self-Test** with unchanged-degenerate
  source preservation, modified-degenerate rejection and safe move checks.

**Recovery:** Reopen the editable prefab's \`GPPITBULL.NEWSKIN\`, select the
recently moved vertex, and use **Undo** or move it away from neighboring
vertices if it formed a zero-area triangle. Save the Mesh asset, select the
editable Project prefab, and rerun **Export Selected W3X
(Skin & Shader Textures)...**. If a failure remains, the exception now gives
the exact affected triangle and vertex indices to investigate.

The editor's position guard works in **Unity mesh-local space**. The exporter
also reconstructs SAGE source-space geometry from the original skin weights
and pivots, so **some invalid source-space edits may only be detected during
export**. This PR does not add mesh topology authoring, AABTree rebuilds or
new bone assignments. Unity recompile and real Pitbull re-export remain to
be validated after merge.


### Phase 2D v0.3.4 — Unity compilation hotfix

The Phase 2D self-test uses `sourcePlanes.Root.Elements().ToArray()` to verify the generated triangle planes, but v0.3.3 omitted the `System.Linq` namespace import. Unity 2022.3.62f3 therefore emitted CS1061 at `Editor/W3xPhase2DSkinEditSelfTest.cs:106`. Version 0.3.4 adds `using System.Linq;` to the Editor-only self-test. This fixes that specific missing extension-method import; no mesh, importer, exporter or Prefab Mode behavior was changed. After installing the Git package update, wait for Unity compilation to finish, then run **Tools > C&C SAGE > Run Phase 2D Skin Editing Self-Test**. Repeated Unity IMGUI `GUIStateObj is deleted` messages are a separate issue not proven resolved by this hotfix.


### Phase 2D v0.3.5 — locked W3X output during advanced export

The latest Unity Editor.log (18 October 2026) shows the **Phase 2D self-test
PASS**, followed by a new **Windows file-commit failure**:

\`\`\`
System.IO.IOException: Unable to remove the file to be replaced
  at System.IO.File.Replace(...)
  at W3xExportMenu.ExportWithOptions(...)
\`\`\`

The export reached the staged file commit. The destination may have been
locked by Unity's asset import, marked read-only or held by another Windows
process; the log alone cannot establish which.

**Changed behavior:**
- The exporter writes and validates a W3X staging file in the destination
  folder before replacing anything.
- If no file exists at the destination, export uses that filename as usual.
- If an existing destination cannot be replaced because of
  \`IOException\` or \`UnauthorizedAccessException\`, the exporter **preserves
  the existing W3X** and writes the validated output as a **new sibling**,
  such as \`GPPitbull_unity_skin_copy_001.w3x\`. If 001 exists, it chooses
  the next available number instead of overwriting.
- The actual saved path is shown explicitly in the export dialog, console
  and report. Reimport/inspect **the new copy**, not the old locked output.
- If even the fallback cannot be saved, the validated staging file is
  **retained** with an error message so it can be recovered manually.
- The temporary filename contains a unique GUID, to avoid collisions with
  concurrent or interrupted exports. Original source W3X remains protected.

Run **Tools > C&C SAGE > Run Phase 2D Export File Safety Self-Test** to
simulate a locked existing W3X and confirm the old file plus both generated
copies are retained. This test only uses an isolated OS temporary folder.

**To retry Pitbull:** select \`GPPitbull_Editable.prefab\` in Project,
export with **Skin & Shader Textures**, and inspect the exact filename in
the success dialog. A \`_copy_001.w3x\` result is a successful separate
export, not a replacement of \`GPPitbull_unity_skin.w3x\`.

This change does not solve Windows file locks globally; it safely avoids
losing data when a previously generated W3X cannot be replaced. Full C&C3
or Kane's Wrath game-builder validation is still separate.


## Phase 3A: W3DAnimation import (v0.4.0 preview)

Phase 2 \`W3X → Unity → editable Prefab → W3X\` remains the supported
**source-backed** workflow. Phase 3A **does not** implement native
FBX/Unity-prefab → W3X or animation editing/export.

### What is now imported

The \`.w3x\` ScriptedImporter recognizes \`W3DAnimation\` declarations and
attempts to generate **AnimationClip sub-assets** under the imported W3X
asset. A clip is created **only when its declared \`Hierarchy\` matches a
fully resolved \`W3DHierarchy\`** whose pivots are represented as Unity
Transforms. This includes hierarchies in the same W3X file and a
separate sibling \`<HierarchyId>_SKL.W3X\` or \`<HierarchyId>.W3X\`
when that sibling declares exactly the referenced hierarchy.

Initial supported XML channels:

- \`ChannelScalar Type="XTranslation"\`, \`YTranslation\`,
  \`ZTranslation\` with \`Pivot\`, \`FirstFrame\` and numeric
  \`<Frame>...</Frame>\` values.
- \`ChannelQuaternion Type="Quaternion"\` or \`Type="Rotation"\`
  with \`<Frame X="..." Y="..." Z="..." W="..."/>\` entries.
- \`NumFrames\` / \`FrameRate\` and per-channel \`FirstFrame\`.
- Source pivot indices and Unity's X/Y/Z coordinate conversion,
  reusing the Phase 1B \`SAGE_Pivots\` Transform hierarchy.

The importer samples these tracks at W3X frame rate and emits Unity
local-position and local-quaternion curves. **It provisionally treats
animation translation and rotation channels as deltas from the source
pivot rest transform**; this assumption must be verified against
actual Kane's Wrath \`_IDLA\` and \`_ATKA\` animations. Empty,
undecodable, or unsupported channels do not produce fabricated curves.
Unknown channel types (e.g. \`Visibility\`, Euler rotation, compressed
channels), unsupported animation files, and clips lacking a
matching hierarchy are reported as warnings; the original
\`W3DAnimation\` XML remains untouched for Phase 2 export.

### Try Phase 3A in Unity

1. Update the Git package to version **0.4.0** and allow scripts to compile.
2. Run **Tools > C&C SAGE > Run Phase 3A Animation Import Self-Test**.
   The PASS log validates sample frame timing, local pivot paths,
   coordinate conversions, quaternion curves and missing-hierarchy safety.
3. In Package Manager, import the **Minimal W3X Mesh** package sample
   into Assets, or copy \`Samples~/MinimalW3X/Phase3AAnimatedPivot.w3x\`
   from this repository into the project's Assets folder.
4. Select \`Phase3AAnimatedPivot.w3x\` and expand the Project asset.
   Look for the \`PHASE3A_DEMO\` **AnimationClip** sub-asset and
   \`SAGE_Pivots/ROOT/ARM\` in the imported hierarchy.
5. Open the clip in an Animation/Animator preview workflow.
   The importer does not create a controller, rig avatar, standalone
   \`.anim\` asset or legacy \`Animation\` component automatically.
6. For real \`_IDLA.w3x\` or \`_ATKA.w3x\` files, place a matching
   source \`_SKL.w3x\` in the same folder (or ensure the hierarchy is
   included). Check warnings to learn which channel formats remain
   unsupported before proceeding to Phase 3B.

### Important limits

- No animation export or edit round-trip in Phase 3A.
- Clips are **imported read-only sub-assets**. Duplicate or extract
  them to independent \`.anim\` assets before direct Unity editing;
  Phase 3B will formalize an editable workflow.
- No binding to unrelated skeletons, guessed pivots or fabricated
  hierarchy data. No binary \`.w3d\` or compressed channels.
- Timeline interpolation and quaternion/rest-pose semantics may
  differ on real SAGE source assets; current coverage uses repository
  fixtures rather than proven in-game samples.
- Existing source-preserving export continues to retain
  \`W3DAnimation\` XML verbatim; it does not use the imported curves
  for writing animation changes.
- The new code **still requires Unity compilation and in-editor
  fixture testing** after this branch is merged.


### Phase 3A v0.4.1 — Pitbull AnimationClip missing

The 0.4.0 synthetic \`TEST_IDLE\` import and regression test passed, but
the real \`GUPitbull_IDLA.W3X\` / \`GUPitbull_ATKA.W3X\` import created no
visible \`AnimationClip\` sub-asset. The real files import SAGE pivots,
so a lack of hierarchy alone cannot explain the missing clip.

**Compatibility fix:** Existing W3X exporters, including the OpenSAGE
Blender Plugin, write quaternion channels with
\`<ChannelQuaternion Type="Orientation" ...>\`, whereas v0.4.0 accepted
only \`Type="Quaternion"\` and \`Type="Rotation"\`. Version **0.4.1**
also accepts \`Orientation\`, case-insensitively. It preserves the
original XML and does not change the Phase 2 W3X exporter.

**Diagnostic command:** Select an actual animation \`.w3x\` in the
**Unity Project** window, e.g. \`GUPitbull_IDLA.W3X\`, then run:

**Tools > C&C SAGE > Diagnose Selected W3X Animation**

The Unity Console will now list:
- The selected W3X path, \`W3DAnimation\` IDs, \`NumFrames\` and \`FrameRate\`
- Referenced hierarchy name and the resolved pivot count
- Raw channel node types (\`ChannelScalar\`, \`ChannelQuaternion\`, etc.)
  plus their \`Type\` labels and sample \`Pivot\` / \`FirstFrame\` / key count
- Number of parsed animation channels and actual generated
  \`AnimationClip\` sub-assets
- Unsupported formats, invalid keyframes and missing-hierarchy warnings

The diagnostic is **read-only**. It does not mutate the asset, scene,
animations or XML source. Copy the Console diagnostic output to the
issue discussion if clips remain missing. This is necessary before
adding speculative or unsupported channel encodings.

The v0.4.1 **Run Phase 3A Animation Import Self-Test** now includes
an \`Orientation\`-only animation without translation channels,
and the package's sample uses that native W3X channel label.
ScriptedImporter version **5 → 6** forces a reimport of existing W3X
assets on package update.

**Validation boundary:** Source-code checks are complete, but Unity
compilation and **real Pitbull AnimationClip presence are not confirmed**
until this PR is merged and the user's Unity project reloads it.
Separate Phase 3B editing and Phase 3C export remain postponed.


### Phase 3A v0.4.2 — verify SAGE_Pivots / ROOTTRANSFORM binding

**Why this matters:** A generated Unity \`AnimationClip\` sub-asset does
not prove that the clip can animate the actual edited Pitbull model.
The importer creates the \`SAGE_Pivots\` GameObject **as an organizational
holder**; it is not a W3X pivot index, and it normally has no animation
curves. The genuine SAGE pivot at index 0 is typically named
\`ROOTTRANSFORM\`, and an imported pivot-0 channel should target the
Unity curve path \`SAGE_Pivots/ROOTTRANSFORM\` (or its actual resolved
source hierarchy path). Do **not** synthesize missing root motion or
move it to \`SAGE_Pivots\` without checking the original W3X.

Use **Tools > C&C SAGE > Audit Selected Animation Pivot Bindings**:
1. In Unity's Project window select all three Pitbull W3X
   \`AnimationClip\` sub-assets, or select their parent source
   \`.W3X\` assets together using Ctrl/Shift.
2. Optionally also select \`GPPitbull_Editable.prefab\` in the
   Project window to compare the imported animation curves to the
   **target model's actual** pivot hierarchy.
3. Run the binding-audit menu and inspect the full Unity Console
   report. It prints each clip's curve count, distinct transform
   binding paths, original W3X **pivot-0 channel count/types**,
   expected imported pivot-0 path, root-pivot curve count and
   missing transform-path count on the target prefab.
4. If XML declares pivot-0 channels but the clip has no matching
   root curves, investigate unsupported channel types, malformed
   frames, or a pivot path mismatch. If XML declares no pivot-0
   channel, missing root motion may be **intentional**, not a
   dropped channel.
5. The audit **does not create/edit** animation, model assets,
   W3X sources or scene objects. It is intended to diagnose which
   real-world bindings must be supported before Phase 3B playback.

The existing Phase 3A synthetic self-test now also checks that
the generated pivot-0 curves resolve against a valid imported
\`SAGE_Pivots/ROOT\` hierarchy, that the holder itself has no curves,
and that a mismatched target root is detected.

**Important limitation:** The audit requires the Unity Editor bridge
or a direct run in Unity for observed results. This change does not
yet claim that the real Pitbull animations play correctly, and it
does not alter the Phase 2 source-preserving W3X export.


### Phase 3A v0.4.3 — unexpected BODY / B_TURRET position tracks

**Issue:** The v0.4.2 clip builder emitted all three Unity
\`m_LocalPosition.x/y/z\` curves whenever a pivot had *any*
\`ChannelScalar Type="XTranslation"/"YTranslation"/"ZTranslation"\`
source. This can introduce unwanted rest-pose overrides on
\`BODY\` and \`B_TURRET\`, and it obscures which axes the actual W3X
authored.

**Fix:** Each scalar channel now emits only its own axis:
source X → Unity X, source Y → Unity Z, source Z → Unity Y. Absent
source axes remain **unkeyed**; they retain the prefab's own rest
position. Source scalar channels whose values are **all zero** are
treated as no-op rest-pose offsets and also do not generate curves.
Actual **nonzero** translation channels are preserved. Quaternion
rotation is unchanged. The importer still assumes source scalar
values are rest-pose deltas, so cross-model retargeting is not yet
validated.

The original W3X XML remains unchanged; Phase 2 export remains
source preserving. Importer version **6 → 7** reimports existing
W3X assets when this update is installed.

**Concrete check with \`AURazDro_RUNA\`:**
1. After installing v0.4.3, reimport the original
   \`AURazDro_RUNA.w3x\` and inspect its **AnimationClip sub-asset**.
2. Run **Tools > C&C SAGE > Audit Selected Animation Pivot Bindings**
   while selecting the imported clip or original W3X asset. You may
   also select the intended visual prefab for a hierarchy check.
3. The updated audit prints focused \`BODY\`, \`B_TURRET\` and
   \`ROOTTRANSFORM\` source/clip comparisons: source X/Y/Z translation
   channel presence, imported X/Y/Z position curve presence, raw
   scalar keyframe ranges and \`ZERO-ONLY\` markers.
4. Any \`WARNING: EXTRA POSITION CURVE\` means the chosen clip contains
   a Unity position curve for an axis **not authored in the source**.
   If real W3X source contains nonzero translation keys, the importer
   must not discard them merely because the bone is named BODY or
   B_TURRET.
5. **Important:** A standalone \`AURAZDRO_RUNA.anim\` is an independent
   copy. Updating the W3X importer will **not** rewrite that existing
   \`.anim\` file; extract/copy a fresh clip from the reimported W3X
   after verifying the source clip. The binding audit attempts to
   locate a same-named sibling \`.w3x\` to compare with such older copies.
6. Run **Tools > C&C SAGE > Run Phase 3A Animation Import Self-Test**;
   it now asserts that absent position axes and all-zero translation
   channels do not result in unwanted curves.

**Verification boundary:** The v0.4.3 changes are source-reviewed
but need Unity compilation, real-source clip-curve inspection and
visual playback validation before Phase 3A is considered complete.
Inherited/world-space motion from a rotating parent is not evidence
that a pivot has a local position curve.


## Phase 3B: Editable animation workspace (v0.5.0 preview)

Phase 3A can import W3X \`W3DAnimation\` into Unity AnimationClip
sub-assets. These imported clips are not reliable permanent editing
targets: **reimporting their W3X replaces importer-owned sub-assets**.
Phase 3B adds a non-destructive creation workflow for independent,
editable \`.anim\` copies, a matching \`AnimatorController\`, and a
**separate preview prefab** that references the selected model.
The existing Phase 2 W3X model/mesh exporter is unchanged.

### Create a workspace in the Project window

1. Import your original SAGE model \`.w3x\` and any corresponding
   animation \`_IDLA.w3x\`, \`_ATKA.w3x\` and other W3X sources.
   Alternatively use an existing **editable W3X Project prefab** for
   the target model. Do **not** pick an arbitrary FBX/native prefab:
   these still lack the SAGE pivot IDs required for this workflow.
2. Ctrl/Shift-select **one model** (\`.prefab\` or imported model
   \`.w3x\`) **AND 1–32 original W3X AnimationClip sub-assets** in
   the Unity **Project** window. Selecting the parent animation
   \`.w3x\` files instead of their clips also works. Do **not**
   select old standalone \`.anim\` copies as original sources.
3. Open **Tools > C&C SAGE > Create Phase 3B Editable Animation Workspace**.
   The validation rejects clips whose Transform paths or source pivot
   indices/names do not match the selected model, rather than
   silently retargeting them.
4. Confirm creation. A new, uniquely named directory will appear
   under \`Assets/CncSageEditable/Animations/\` containing:
   - \`<animation>_Editable.anim\`: independent writable animation
     copy, retaining source curves and frame rate
   - \`<animation>_Source.asset\`: source W3X GUID, original animation
     ID, hierarchy ID, FPS/frame count, target model GUID and original
     curve-binding signatures for future Phase 3C comparison
   - \`<model>_Preview.controller\`: separate Animator Controller
     with each editable clip as a state
   - \`<model>_AnimationPreview.prefab\`: separate preview model
     with the Animator Controller on its root
5. Double-click the generated \`_AnimationPreview.prefab\` to open
   **Prefab Mode**, then use **Window > Animation > Animation**.
   Choose the editable clip and edit keyframes/curves on the
   model's \`SAGE_Pivots\` hierarchy. The default Animator state is
   the first selected clip. Use its controller to switch states
   when inspecting multiple animations.
6. Check \`BODY\`, \`B_TURRET\`, and pivot 0 using
   **Tools > C&C SAGE > Audit Selected Animation Pivot Bindings**.
   The audit can locate the original W3X source of new Phase 3B
   \`.anim\` files through their GUID-based metadata sidecars.

**Run self-test:** **Tools > C&C SAGE > Run Phase 3B Animation Editing Self-Test**.
The test creates an in-memory model and clip to check:
- Original imported curve values are unchanged after modifying a clone
- Binding paths, frame rate and source provenance are preserved
- Compatible pivot IDs/names are accepted, incompatible ones rejected
- No scene or Project assets are saved by the self-test

### Limits and safety

- **No animation export back to W3X yet.** Phase 3C will map edited
  .anim curves to SAGE channels while preserving all source XML not
  understood by the importer. Phase 3B is only safe clip editing and
  preview.
- Workspace creation **never overwrites** the original W3X,
  selected model prefab, existing AnimationClips or controller. New
  workspaces use unique directories; incomplete creation removes
  only the new directory. The active scene is not modified.
- The preview prefab is for **animation editing/inspection**, not
  a replacement model export asset. Preview mesh/material references
  can still refer to the selected model's original assets; only the
  animation clips and workspace controller are independent.
- Changing W3X import settings or reinstalling a package updates
  importer-generated sub-assets but **does not alter** independent
  Phase 3B \`.anim\` files. If the source changes, run the pivot
  audit again before editing/exporting.
- Phase 3B does not invent missing pivot translation/rotation
  channels, retarget mismatched models, build new rigs, or handle
  unsupported compressed animation formats. Some real SAGE clips
  may still have interpolation/rest-pose differences.
- Code is prepared for Unity 2022.3 LTS and requires **live Unity
  compilation plus a real-model preview test** before Phase 3B is
  considered complete.


### Unity 2022.3 compiler error CS1628 (fixed in v0.6.2)

If Unity shows `W3xPhase3CAnimationExportMenu.cs(213,60): error CS1628`,
the Git package revision contains a LINQ lambda referring to the
`out AnimationClip edited` parameter. Version **0.6.2** captures a
normal local `selectedClip` instead, preserving selection validation.

After refreshing the package from Git `main`, confirm Unity's
Console compilation completes without CS1628. Then use
**Tools > C&C SAGE > Export Animation to W3X...**. Existing original
W3X files and Phase 3B independent .anim clips are unaffected.

### Unity menu is missing?

The Phase 3C command is under **Tools > C&C SAGE > Export Animation to W3X...**.
Starting with **0.6.1**, this menu always appears, even when a model,
pivot or no asset is selected. Clicking it without a matching Phase 3B
`*_Editable.anim` or `*_Source.asset` shows a selection guide and
does not create files. A model/prefab or an imported W3X clip sub-asset
is **not** a valid export selection.

If the command is **absent**, the Unity Git UPM package is almost
certainly still on Phase 3B `0.5.0` or Unity has not finished compiling.
Use **Window > Package Manager > C&C SAGE Model Tools** and confirm the
package version. The Git dependency uses
`https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git`. Refresh/update
the Git dependency from `main`, and verify the Editor folder contains
`W3xPhase3CAnimationExportMenu.cs`. Check Console for compiler errors.
Unity may retain an older Git revision in its UPM lock/cache until the
dependency is refreshed. Do not edit files directly in `Library/PackageCache`.

## Phase 3C: Export edited AnimationClip to W3X (v0.6.3 preview)

Phase 3C connects the previous two animation stages:

\`\`\`text
Original _IDLA/_ATKA.W3X  →  Phase 3A importer  →  AnimationClip
                                                     ↓
                                    Phase 3B editable .anim copy
                                                     ↓
                                  Phase 3C source-backed exporter
                                                     ↓
                               New *_unity_anim.w3x (source preserved)
\`\`\`

This **does not replace Phase 2 geometry export**. The animation
exporter requires both the \`_Editable.anim\` file AND its generated
\`_Source.asset\` provenance from the Phase 3B workspace. It never
exports an arbitrary unlinked Unity or FBX animation.

### Use in Unity 2022.3 LTS

1. Update the Git UPM package to **0.6.3**, let Unity compile, then run
   **Tools > C&C SAGE > Run Phase 3C Animation Export Self-Test**.
   This validates round-trip conversion, source XML preservation and
   strict export rejections without writing any Unity Project assets.
2. In the **Project** window, select a Phase 3B
   \`*_Editable.anim\` asset (or its matching \`*_Source.asset\` file).
   An imported W3X clip sub-asset is **not editable export input**.
3. Choose **Tools > C&C SAGE > Export Animation to W3X...**
4. After reading the warning, choose a **NEW \`.w3x\` filename in the
   SAME directory as the original source animation W3X**. The
   exporter blocks existing paths, the original source path and
   unrelated folders (relative ART dependencies must remain valid).
5. Review the report: number of original/supported/unsupported
   channels, changed channels, and changed keyframes. If no
   existing-frame values or new channel values changed, no export is written.
6. Import the generated W3X file into Unity and compare the resulting
   AnimationClip to the edited Phase 3B \`.anim\`. Only after
   visual verification should it be tested with
   WrathEd/Kane's Wrath tooling.


### Phase 3C v0.6.3 — new channels on an existing SAGE pivot

Example from \`AURAZDRO_RUNA_Editable.anim\`: a Unity curve on
\`SAGE_Pivots/ROOTTRANSFORM/B_TURRET/B_TILT/EYE\` for
\`m_LocalPosition.y\` previously triggered an error
\`New or unsupported Unity animation curve\`. This version permits
it **only if EYE is a real pivot in the original W3X _SKL hierarchy**
and the source has no conflicting ZTranslation channel.

- New \`m_LocalPosition.x\` → SAGE \`ChannelScalar Type="XTranslation"\`
- New \`m_LocalPosition.z\` → SAGE \`ChannelScalar Type="YTranslation"\`
- New \`m_LocalPosition.y\` → SAGE \`ChannelScalar Type="ZTranslation"\`
- New complete Unity quaternion
  \`m_LocalRotation.x/y/z/w\` → SAGE
  \`ChannelQuaternion Type="Orientation"\`
- New complete Unity \`localEulerAnglesRaw.x/y/z\`
  (or all three \`localEulerAnglesBaked\` /
  \`localEulerAngles\` axes) → one SAGE
  \`ChannelQuaternion Type="Orientation"\`, converted via
  \`Quaternion.Euler()\` first.

**Channel structure:** new channels use the correct original
indexed pivot number, \`FirstFrame="0"\` and one sampled
\`Frame\` per original \`NumFrames\` at the source \`FrameRate\`.
The source pivot's rest translation and orientation are subtracted.
An all-zero translation delta or all-identity rotation delta
is a no-op and **does not** create a redundant new channel.

**Safety:** duplicate or malformed pre-existing channels, pivots
not in the original hierarchy, missing rotation components,
mixed quaternion/Euler curves, unsupported events/scale curves,
out-of-range key times and changes to original FPS/NumFrames
are rejected. Original source channels and unknown
\`Visibility\` data remain unchanged. The exporter adds new
nodes only after all original channel nodes and verifies that
the Phase 3A reader decodes the staged export before saving.

Use **Tools > C&C SAGE > Run Phase 3C Animation Export Self-Test**
after upgrading. The regression suite now also verifies a new
EYE Unity-Y → SAGE-Z scalar channel, a new quaternion
Orientation channel, Euler conversion, unmodified XML,
invalid-pivot rejection and no-op channel suppression.

**Limit:** Source-backed *W3X XML* only, not compiled W3D or
a guaranteed WrathEd/Kane's Wrath-compatible game mod.
Real-animation import/export playback must still be checked.

### Supported changes

- Edit **existing** \`ChannelScalar Type="XTranslation"\`,
  \`YTranslation\`, or \`ZTranslation\` keyframes. Each axis is
  converted back to SAGE space (Unity X→source X, Unity Z→source Y,
  Unity Y→source Z), subtracting the source pivot's rest position.
- Edit **existing** \`ChannelQuaternion Type="Orientation"\`
  (\`Quaternion\` / \`Rotation\` variants supported). The output
  reconstructs the original hierarchy's local quaternion delta,
  accounts for the coordinate handedness conversion, normalizes
  quaternions, and preserves source quaternion hemisphere sign
  where possible.
- Keep the original \`W3DAnimation\` \`Hierarchy\`, \`NumFrames\`,
  \`FrameRate\`, \`Pivot\`, \`FirstFrame\` and frame count.
- Modify XML values **only for changed supported source frames**.
  Untouched source frames, unknown channel types (e.g.
  \`Visibility\`), auxiliary animation metadata, FXShader, models,
  collisions, comments and other \`AssetDeclaration\` nodes are
  preserved structurally.

### Fail-closed restrictions

Export is refused if:
- The \`_Source.asset\` sidecar/source GUID is missing, ambiguous or
  does not reference the selected Phase 3B clip.
- The source W3X hierarchy, source animation ID, FPS or original
  frame count changed; or the newly imported baseline bindings
  no longer match the Phase 3B source-binding signature.
- The edited clip adds unsupported bindings or removes an
  imported nonzero source track.
- The clip contains animation events or object-reference curves,
  duplicate source channels, invalid quaternion data, or edits
  before the source channel's \`FirstFrame\`.
- New *pivots*, unsupported Transform property types, non-source rig
  paths, new frame counts/FPS or unsupported time ranges are requested.
  New translation/quaternion channels on existing source pivots ARE
  supported. Only complete Euler XYZ tracks (one Unity Euler convention
  per pivot) can create a new W3X Orientation channel.
- New rotation tracks with missing quaternion/euler components or
  mixed quaternion/Euler representations are requested. Existing
  quaternion W3X channels must retain their imported rotation
  component curves; deletion/ambiguous conversions are refused.

**Output safety:** The exporter validates serialized XML using a
staged temporary file and only then moves it to a new destination
path. It never overwrites original W3X, existing outputs, package
files, or the Phase 3B editable \`.anim\`; the output is a separate
uncompiled XML source copy.

**Validation boundary:** v0.6.3 is an *experimental*
implementation. Source review and the in-memory test source have
been added, but actual Unity compilation, real RazorDrones/Pitbull
edited-animation roundtrip and WrathEd/game toolchain checks are
needed before treating Phase 3C as complete. Full animation
roundtrip verification is the separate Phase 3D.


### Phase 3D v0.7.1 — no-op export and source-channel preservation

Unity testing of v0.7.0 found that the Phase 3B self-test **passed**
and the real RazorDrones W3X export completed, but two regression
self-tests remained red:

- Phase 3C: `Unedited clip altered W3X XML`
- Phase 3D: `Expected exactly one new EYE channel and one modified
  existing BODY XTranslation channel`

This update makes the exporter compare **every original scalar channel
and quaternion rotation track** against its Phase 3B imported baseline
at original frame timestamps, **before deciding to patch existing
source XML**. If the edited clip samples identically to its baseline,
it preserves the source XML frames exactly, even if Unity synthesizes
component curves, alters resting Transform defaults or normalizes
quaternion values during import/serialization. If the user actually
changes supported channel frame values, the original Phase 3C update
logic continues to write the intended changed values.

This generalizes the zero-only YTranslation guard from v0.7.0 and
preserves the existing EYE `ZTranslation` new-channel support.
The regression tests additionally print actual changed/new frame
counts when their no-op assertions fail.

**Acceptance:** Refresh Git UPM to v0.7.1, run the Phase 3C and
Phase 3D self-tests, and verify both report **PASS** in Unity's
Console. Repeat the Phase 3D validation menu on the original Phase 3B
editable clip + Phase 3C exported W3X. Do not assume that this
source-level patch passed runtime tests until the Console confirms it.

### Phase 3D v0.7.2 — prevent implicit Unity axes from generating W3X channels

The RTS Unity Editor installed v0.7.1 (Git SHA \`325345bca1\`),
compiled successfully, and both Phase 3C and Phase 3D synthetic
self-tests were executed. Those tests reported:

- Phase 3C: \`Unedited clip altered W3X XML. Existing edits=0,
  changed frames=0, added channels=1\`
- Phase 3D: \`Actual new channels=2, changed channels=1,
  changed frames=1\`; unexpectedly added \`BODY ZTranslation\`
  as well as intended \`EYE ZTranslation\`.

**Cause:** the Unity AnimationClip can contain an automatically
generated sibling \`m_LocalPosition.y\` binding on BODY, even though
the source W3X has no \`ZTranslation\` for BODY. That binding is
already present in the **imported Phase 3B baseline**; it is NOT
evidence that the user authored a new SAGE channel. Earlier
v0.7.1 logic only compared already-existing W3X channels against
the baseline; the *new-channel classifier* did not do so.

**Fix:** Before adding a new scalar channel on an existing W3X
pivot, compare the candidate Unity position curve to the matching
baseline curve at every **original source frame**. If that exact
position component exists in the baseline and did not change,
it must not generate a new W3X channel. An intentional edit of
that baseline component, or a genuinely new EYE position curve
not in the baseline, **is still exported**. The input path must
remain an existing indexed SAGE hierarchy pivot.

The exporter still preserves all original XML nodes, and only
writes to a newly selected W3X output file. This also sorts
newly appended scalar X/Y/ZTranslation channels before new
Quaternion Orientation channels within each pivot to give a
stable, predictable XML layout.

**Validation after updating to 0.7.2:**
1. **Tools > C&C SAGE > Run Phase 3C Animation Export Self-Test**:
   no-op export should create zero changed/new channels, while
   a deliberately edited implicit BODY curve should create exactly
   one corresponding new \`ZTranslation\`.
2. **Tools > C&C SAGE > Run Phase 3D Round-Trip Self-Test**:
   the fixture should edit BODY.X, append only EYE.ZTranslation,
   reimport, and pass sampled position/quaternion/XML comparison.
3. Select the Phase 3B \`AURAZDRO_RUNA_Editable.anim\` and a
   reimported export W3X, then run
   **Tools > C&C SAGE > Validate Phase 3D Animation Round Trip**.
4. Check any real-model \`BODY\`, \`B_TURRET\`, \`EYE\` translation
   edits against the original SKN. Runtime WrathEd/Kane's Wrath
   compiler tests remain outside Phase 3D's numerical validator.

This release is **source-reviewed only** until Unity's Console
actually reports both updated self-tests PASS.

## Phase 3D v0.7.0 — Full animation round-trip audit (preview)

The original RazorDrones \`AURAZDRO_RUNA\` has already been edited,
exported to a **NEW W3X**, reimported into Unity, and the animation
played successfully on the original SKN according to user testing.
This confirms that **that particular real-animation playback**
worked; it does **not** establish general compatibility across
different SAGE animations or with the Kane's Wrath compiler.

Phase 3D provides a **read-only, numerical and structural comparison**
so the same workflow can be tested consistently on Run/Idle/Attack
animations with the original W3DHierarchy intact.

### Check a real edited animation

1. Update Git UPM package to **0.7.0**; ensure Unity compiles.
2. Run **Tools > C&C SAGE > Run Phase 3C Animation Export Self-Test**
   and **Tools > C&C SAGE > Run Phase 3D Round-Trip Self-Test**.
3. In Unity's **Project** window Ctrl+select **exactly one**
   Phase 3B \`*_Editable.anim\` (or its corresponding
   \`*_Source.asset\`) **and exactly one exported Phase 3C \`.w3x\`
   source file**. Do not select the original source \`.w3x\` here.
   The original source is discovered by the Phase 3B metadata's GUID.
4. Open **Tools > C&C SAGE > Validate Phase 3D Animation Round Trip**.
   It resolves the output W3X's imported AnimationClip by the
   original animation ID, then reads the original and exported W3X
   XML **without modifying any assets**.
5. Check the Unity Console for a **PASS/FAIL** audit:
   - Source vs exported W3X channel count and added channels
   - Non-animation XML preserved, unknown channel blocks unchanged,
     original channel count and metadata intact
   - Every **source frame**, on every source pivot path, comparing
     local position X/Y/Z and quaternion rotation after Unity
     reimport, including new EYE translation tracks
   - Maximum sampled position difference (tolerance **0.002 Unity
     units**) and rotation angle difference (**0.5°**)
   - Up to 30 individual mismatch descriptions with pivot paths
6. If **FAIL**, compare the reported curve path and frame with
   the exported clip in Unity's Animation window and the source
   W3X channel XML. Do **not** alter the source/prefab automatically
   in response to an audit.

**Important:** At any pivot lacking a position/rotation curve,
the audit uses its **original hierarchy rest transform** as the
default. This is a local-pose comparison, not motion in world
space. A hierarchy that uses more complex \`FixupMatrix\`,
interpolation, Unity-authored subframe keys or SAGE-specific
runtime rules can require separate visual investigation.
The audit samples at the original integer frame rate only:
**Unity tangents, intermediate subframes, compiled-game W3D and
Kane's Wrath runtime compatibility are outside this validation**.

### Zero-offset channel regression fix

The original Phase 3C synthetic self-test failed on
\`BODY.YTranslation\` although its W3X source values were
**all zero**. Phase 3A deliberately does not author such a
position delta curve; Unity may independently generate
unmodified sibling local position components when animating
the same Transform. Phase 3D's narrowly scoped correction:
- Does **not** claim that a zero-only source channel should
  evaluate at a nonzero coordinate in the Unity baseline.
- Preserves untouched all-zero source scalar XML channels if
  the corresponding sampled edited curve matches the imported
  baseline, even if Unity generated an implicit component.
- Still exports a **real edited change** to a source channel;
  the rest of the Phase 3C exporter and new EYE channel support
  is unchanged.

### Automated synthetic regression

**Tools > C&C SAGE > Run Phase 3D Round-Trip Self-Test** runs
entirely in memory and checks:
- W3X animation import, independent editable clip, exported
  source-backed X/ZTranslation edits, and reimport
- A new EYE Unity \`m_LocalPosition.y\` → SAGE
  \`ZTranslation\` channel remains present in the reimported clip
- Original zero-only translation and unknown Visibility
  channels remain untouched
- Edited versus reimported sampled frame values pass numerical
  tolerance and unrelated XML is preserved
- Deliberately altered reimported position or unrelated
  non-animation XML is detected and fails

**Acceptance boundary:** The C# source has been reviewed and the
self-tests authored, but this version still requires Unity
compilation and the actual Phase 3D audit on the real
\`AURAZDRO_RUNA_Editable.anim\` and
\`AURazDro_RUNA_unity_anim.w3x\`. Do not mark the feature
fully validated until both pass. WrathEd compilation and
Kane's Wrath runtime loading require separate tests.

## Roadmap

1. Validate geometry, coordinate system and UVs against legal user-provided sample W3X models.
2. Validate Phase 1B hierarchy and materials, including companion _SKL W3X and external ART resolution.
3. Validate Phase 2A/B/C export, including newly added static W3DMesh declarations, then implement new skin meshes, complete rigging/material round-trips and game toolchain validation.
4. Add animation support, then investigate binary W3D and WrathEd toolchain validation.
