# C&C 3 / Kane's Wrath World Builder .map → Unity (terrain-only preview)

## Status

Experimental v0.9.0. The .map binary decoder is new and has **not** been checked against an actual C&C 3 or Kane's Wrath map in Unity yet. It does not export back to World Builder.

## Input

- Uncompressed four-CC `CkMp` plus map name-index table.
- Compressed `EAR\0` + decompressed 32-bit length + EA RefPack stream, then `CkMp`.
- Versioned top-level sections: uint32 name index, uint16 version, uint32 payload byte count, payload.
- `HeightMapData` containing uint32 width, height, border width, border record count, boundary records (four uint32 per v6+, two per previous version), uint32 area and 16-bit elevation samples for v5+. Older versions use 8-bit elevation samples.
- Unknown top-level sections are indexed and skipped without executing scripts. Imported map's source data is never modified.

## Unity preview

Each `.map` becomes one imported prefab/GameObject with `SAGE_Terrain` child holding 64×64-cell mesh tiles. Terrain tiles are **meshes, not native Unity Terrain components**, to preserve the source elevation grid and allow previews without terrain tooling.

- Source ground (X,Y) → Unity (X,Z); source elevation Z → Unity Y.
- Default cell spacing: 10 world units, default 16-bit vertical scale: 0.0390625 per integer sample (per OpenSAGE's map height reader).
- Recalculates normals, sets UV0 across the entire source map and triangulates upward in Unity's X/Z plane.
- The North-South flip is provided as an Inspector orientation aid. It is not yet verified against World Builder map orientation.
- When `includeBorder=false`, trims borders on all four sides using border width.
- Validates bounded asset/section data and restricts input and decoded sizes.

## Inspector controls

- **Import Terrain** — disable to inspect parsed metadata without creating geometry.
- **Horizontal Scale** — space between height sample points, default 10.
- **Vertical Scale** — elevation scale, default 0.0390625 for v5+ heightmaps.
- **Flip Map North South** — optional preview mirroring.
- **Include Border** — import map border area, default true.
- **Add Mesh Colliders** — optional per-tile collision.
- **Terrain Material** — optional Unity preview material; without it a Standard/Lit fallback is created.

Select **Apply** after changing any option. A `Cnc3MapManifest` metadata sub-asset records dimensions, format, version, compression and section counts.

## Testing

1. Update to package v0.9.0 feature branch and confirm no Unity Console C# compilation errors.
2. In Package Manager choose Samples → Minimal C&C3 Map → Import. Run `python generate_map_samples.py` inside that imported sample folder.
3. Select `SageTerrainSynthetic.map`, expand imported prefab, inspect hill mesh.
4. Repeat with `SageTerrainSyntheticCompressed.map` and `SageTerrainSyntheticBackrefs.map`; both should look identical and report 12x10 samples. The manifest distinguishes CkMp and EAR/RefPack compression.
5. Import one small **real** Tiberium Wars or Kane's Wrath World Builder map. Confirm heightmap dimensions, orientation and topographical heights against World Builder.
6. Re-verify that W3X and W3D imports still work, and that no map import touches W3X export.

## Known gaps

No terrain BlendTileData reconstruction or extracted textures, objects, roads, cliffs, water, scripts, waypoints or terrain deformation. Data model is intentionally limited to a visual preview; support for those sections will require further verified reverse engineering and authentic test fixtures. Maps using other compression wrappers are not yet supported. Bound checks will reject unusually large or malformed maps.

## References

- OpenSAGE map format notes: https://github.com/OpenSAGE/OpenSAGE/blob/master/docs/Map%20Format.txt
- OpenSAGE map parsing and height scaling: https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/HeightMapData.cs
- OpenSAGE terrain horizontal scaling: https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Terrain/HeightMap.cs
- C&C World Builder file structure and references: https://kaneswrath.com/wp-content/uploads/2023/05/cc3_worldbuilder_Manual.pdf

No original EA map assets are bundled.
