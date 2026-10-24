# C&C 3 World Builder .map terrain sample

This sample generates tiny synthetic CkMp and EAR/RefPack World Builder-like files for import-only tests. They contain no original EA game assets.

Run `python generate_map_samples.py` in this directory (Python 3, standard library). Then refresh Unity Assets. Three files are created:
- `SageTerrainSynthetic.map` — uncompressed CkMp
- `SageTerrainSyntheticCompressed.map` — EAR header + EA RefPack literal encoding
- `SageTerrainSyntheticBackrefs.map` — EAR/RefPack using 2-byte back-reference compression

All three have a 12×10 height grid, one border, 120 samples, v6 16-bit elevations, and two valid v3 Object placement records: `TestTree` at `(40,30,12)`, and `TestBuilding` at `(60,50,18)` with a rotation of approximately 1.57 radians. The expected output is one terrain mesh, `SAGE_ObjectPlacements` containing two named empty GameObject markers, and map metadata showing two Object placements. The Map Inspector reports two top-level sections.

Real C&C 3 / Kane's Wrath maps should be tested separately. Not every asset or compression variant is supported, and actual object meshes, textures, roads and water are not imported yet.
