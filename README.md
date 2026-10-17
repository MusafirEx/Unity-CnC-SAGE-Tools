# Unity-CnC-SAGE-Tools

Experimental **Unity 2022.3+** Editor importer for uncompiled **Command & Conquer SAGE W3X** model sources, intended to grow into W3X/W3D import/export tooling.

**Status: 0.1.1 Phase 1 prototype — geometry import and metadata-only recognition of non-mesh W3X sources.** Export, binary W3D, compiled C&C3 assets, skeletons, animation, shader materials, texture resolution, and HLOD/container bindings are **not yet implemented**.

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
- Direct vertices, triangle indices, optional per-vertex normals and first UV channel.
- Relative local `<Includes><Include source="ART:..."/></Includes>` resources when present.
- Auto-generated model hierarchy with one child GameObject per mesh and retained mesh ID.
- Basic preliminary axis conversion: `(X,Y,Z) -> (X,Z,Y)`, reversing triangle winding.

### Phase 1 regression check

The `Minimal W3X Mesh` sample now includes:

- `MinimalTriangle.w3x`: should still generate a `MeshFilter` and mesh.
- `HierarchyOnly.W3X`: should import with a `CncSageSourceInfo` component, zero meshes, and no import exception.
- `AnimationOnly.W3X`: same metadata-only behavior.

Real `*_SKL.W3X`, `*_ATKA.W3X` and `*_IDLA.W3X` sources should no longer throw `No W3DMesh geometry found`. Missing local `ART:` reference warnings and skinning limitations remain until later phases.

If an earlier repository ZIP was extracted into `Assets/Extension/Unity-CnC-SAGE-Tools-main`, replace that copy with the updated files. Updating a Git package **does not update an independent Assets folder copy**. Avoid installing both copies because duplicate assemblies may conflict.

### Not yet supported

- Full SAGE hierarchy, skinning/bone influences, animations and attachment pivots.
- FXShader/material passes, textures and UV orientation validation.
- HLOD/container binding to geometry and textures beyond simple includes.
- Binary `.w3d` files or compiled game archives.
- **Any W3X/W3D export** or tested Kane's Wrath in-game interoperability.

**Warning:** The coordinate conversion is a working assumption, not yet verified with a real game asset. The generated model uses Unity's default material. Do not use this prototype to overwrite original game files.

## Reference and licensing

The XML element names were checked against the OpenSAGE Blender plug-in source:
https://github.com/OpenSAGE/OpenSAGE.BlenderPlugin

This project contains an **independent C# implementation**, not a port or copy of OpenSAGE's LGPL-3.0 Python implementation. Original SAGE/C&C game assets are not bundled. See `LICENSE` for this project's MIT license.

## Roadmap

1. Validate geometry, coordinate system and UVs against legal user-provided sample W3X models.
2. Preserve hierarchy, pivots and materials; resolve neighboring mesh files referenced by containers.
3. Build W3X exporter and verify import/export/reimport structural equivalence.
4. Add animation support, then investigate binary W3D and WrathEd toolchain validation.
