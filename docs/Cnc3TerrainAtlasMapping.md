# C&C3 World Builder: `BlendTileData` → `map.bin` terrain atlas UVs

## Bug fixed

Earlier v0.9.2 decoded two DDS subassets but did not assign them to MeshRenderer materials. The old terrain meshes had only whole-map UV coordinates; showing the atlas there would render the atlas as a giant image, not the correct source textures.

## Verified sample

- `eamap_sb05_4.map` is `EAR`-compressed and expands to `CkMp`; grid `560×590`.
- `BlendTileData` version `24`, 330,400 `ushort` per-cell base tile indices.
- The BlendTileTexture table has **43 source names**, all with `CellSize=4`; each source texture therefore has 8×8 texture subtiles.
- The compiled `TerrainTextureAtlas:EAMAP_SB05_4` asset exposes **43** `TextureID` entries and source image pixel rectangles within a 2048×2048 BC3 atlas.
- **43 of 43 source texture names match 43 of 43 compiled tile IDs** using SAGE FastHash on the **lowercase ASCII name**. Sample verified pairs: `GrassWashington04 -> 0xB8534D27`, `PavementWashington08 -> 0x84F4590D`, `CliffRedZone03 -> 0x41122345`.
- Source `ushort Tiles[y*Width+x]` is an index into consecutive `(CellSize*2)^2` subtile entries for each source texture. Each subtile is addressed using Morton/deinterleaved X/Y bits, not ordinary linear row-major order.
- Atlas source rectangles use top-left Y coordinates, whereas Unity UV V increases upward. The UV builder transforms pixel bounds accordingly and makes one independent four-vertex quad per source terrain cell.

## v0.9.3 implementation

1. `Cnc3MapReader.ReadTerrainTextureData` extracts base tile indices + texture name/size and skips the packed versioned blend/passability grids within `BlendTileData` boundaries.
2. `Cnc3MapReader.SageTerrainHash` computes the 32-bit case-insensitive hash.
3. `Cnc3CompiledMapBundle.ReadTerrainAtlas` obtains the compiled tile ID→pixel rectangle mapping.
4. `Cnc3TerrainAtlasUv.TryMap` validates matching names, all UV bounds and the decoded BC3 Texture2D.
5. `Cnc3TerrainAtlasUv.BuildTile` generates 64×64-cell UV meshes with duplicated per-cell vertices and normals taken from heightfield gradients.
6. `Cnc3MapImporter` now imports optional compiled map bundle before mesh generation, and uses an auto-generated unlit material referencing the decoded color atlas.
7. Imported `Cnc3MapManifest` records applied status and texture matching counts, and Inspector explains whether fallback was used.

## Live acceptance check

With `.map`, `.bin`, `.imp`, `.manifest`, `.relo` together, select/reimport `.map` in Unity 2022.3:

| Expected imported metadata | Value |
|---|---:|
| Terrain sample grid | 560×590 |
| Source BlendTileData texture names | 43 |
| Compiled atlas tiles | 43 |
| Resolved texture names → atlas IDs | 43 |
| Atlas Applied to Terrain | Yes |
| Original object placement count | 4,729 |

Select a `SAGE_Terrain` child in Scene/Hierarchy. Its MeshRenderer's generated `CNC3_Terrain_Preview` material should reference the compiled BC3 color atlas; vertices use small subrect UVs, not whole-map UVs. Any provided external material is cloned, not modified.

## Current limitations

- Only the `BlendTileData.Tiles` **base color layer** is mapped. `Blends`, `ThreeWayBlends`, `CliffTextures`, secondary texture masks, macro-texture, terrain decals and shaders remain unimplemented. Hard transitions may be visible.
- Native SAGE terrain quads, tile blending and shader rendering may differ from this editor-only preview. Source atlas orientation and terrain winding require visual verification against World Builder.
- If an atlas is missing, invalid, or the map has no supported BlendTileData, fallback terrain remains available and reports the reason in the importer Inspector.
- `SAGE_ObjectPlacements` are still placeholders and compiled W3D model streams are separate.

## References

- OpenSAGE `BlendTileData`: https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/BlendTileData.cs
- OpenSAGE `BlendTileTextureIndex`: https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/BlendTileTextureIndex.cs
- Qibbi BinaryAssetBuilder `FastHash`: https://github.com/Qibbi/BinaryAssetBuilder/blob/master/source/BinaryAssetBuilder.Core/BinaryAssetBuilder/Core/Hashing/FastHash.cs
- Qibbi WrathEd2012 `TerrainTextureAtlas`: https://github.com/Qibbi/WrathEd2012/blob/master/SAGE/Games/Kane's%20Wrath/TerrainTextureAtlas.cs

No original EA assets are included in this package.
