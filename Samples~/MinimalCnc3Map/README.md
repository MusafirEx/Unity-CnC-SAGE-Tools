# C&C 3 World Builder .map terrain sample

This sample generates tiny synthetic CkMp and EAR/RefPack World Builder-like files for import-only tests. They contain no original EA game assets.

Run `python generate_map_samples.py` in this directory (Python 3, standard library). Then refresh Unity Assets. Three files are created:
- `SageTerrainSynthetic.map` — uncompressed CkMp
- `SageTerrainSyntheticCompressed.map` — EAR header + EA RefPack literal encoding
- `SageTerrainSyntheticBackrefs.map` — EAR/RefPack using 2-byte back-reference compression

Both have a 12×10 height grid, one border, 120 samples, v6 16-bit elevations, and a deliberately uninterpreted placeholder ObjectsList section. The expected visual output is a small raised hill made of one terrain mesh. The Map Inspector reports two sections.

Real C&C 3 / Kane's Wrath maps should be tested separately. Not every asset or compression variant is supported, and objects, textures, roads and water are not imported yet.
