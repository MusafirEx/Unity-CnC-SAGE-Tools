# Unity-CnC-SAGE-Tools

Experimental **Unity 2022.3+** Editor importer for uncompiled **Command & Conquer SAGE W3X** model sources, intended to grow into W3X/W3D import/export tooling.

**Status: 0.1.3 Phase 1B preview.** Geometry/UVs, SAGE pivot transforms, basic bone weights/SkinnedMeshRenderer and first-pass FXShader materials are implemented experimentally. Export, binary W3D, animation clips, exact SAGE shading, full HLOD assembly and validated Kane's Wrath round-tripping are **not yet implemented**.

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
- **Any W3X/W3D export** or tested Kane's Wrath in-game interoperability.

**Warning:** Coordinate conversion, bindposes and complex FXShader material behavior still need verification with real game assets. A default Unity material may appear where SAGE shader definitions are missing. Do not use this prototype to overwrite original game files.

## Reference and licensing

The XML element names were checked against the OpenSAGE Blender plug-in source:
https://github.com/OpenSAGE/OpenSAGE.BlenderPlugin

This project contains an **independent C# implementation**, not a port or copy of OpenSAGE's LGPL-3.0 Python implementation. Original SAGE/C&C game assets are not bundled. See `LICENSE` for this project's MIT license.

## Roadmap

1. Validate geometry, coordinate system and UVs against legal user-provided sample W3X models.
2. Preserve hierarchy, pivots and materials; resolve neighboring mesh files referenced by containers.
3. Build W3X exporter and verify import/export/reimport structural equivalence.
4. Add animation support, then investigate binary W3D and WrathEd toolchain validation.
