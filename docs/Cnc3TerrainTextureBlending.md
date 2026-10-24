# C&C3 terrain texture blending — `.map` BlendTileData and compiled atlas

## v0.9.5: shader blending implementation (Unity 2022.3+)

### Source parser

Actual `eamap_sb05_4.map`, decompressed CkMp: `BlendTileData` section version **24** at offset 663,972, ending at 4,310,803 (3,646,831 bytes). Independent binary read **consumed this section exactly to its end**:

| Decoded source data | Count |
|---|---:|
| Terrain samples | 560 × 590 = 330,400 |
| Base tile indices | 330,400 (ushort) |
| First blend cell indices | 330,400 (ushort), **64,782 nonzero** |
| Three-way blend cell indices | 330,400 (ushort), **4,536 nonzero** |
| Cliff texture IDs | 330,400; all zero in this map |
| Texture definitions | **43** |
| Blend description table | **16,672** distinct records |

`Blends[x,y]` and `ThreeWayBlends[x,y]` store **1-based** indices into the blend-description table, with 0 denoting no blend. The table's count in the file is one greater than the number of descriptions.

Each blend description is exactly **18 bytes**: `uint32 SecondaryTextureTile`, `byte[4] BlendDirection` (1=right, 2=top, 4=top-right, 8=top-left), `byte Flags` (bit 0 flipped), `byte TwoSided`, `uint32 Magic1`, `uint32 Magic2=0x7ADA0000`. `SecondaryTextureTile` indexes the SAME base tile ID table used by the 43 terrain texture families. The shader uses two-sided corner behavior, not ordinary linear blending, for direction 4/8.

### Rendering algorithm

The `.map` importer resolves every tile index into diffuse atlas UV rectangles using SAGE lowercase FastHash of source texture names. Unity mesh surfaces keep four vertices per terrain cell so neighboring source cells can reference different atlas tiles. With blending enabled, mesh UV0 holds the base sample, UV1 the secondary atlas sample, UV2 the third atlas sample, UV3 packs blend direction/flip/two-sided flags, and UV4 holds per-cell fractional UV coordinates. Names `uv2` / `uv3` in Unity's Mesh API correspond to shader `TEXCOORD1/2`.

`Shaders/Cnc3TerrainAtlasBlend.shader` follows OpenSAGE's `CalculateBlendFactor` directional rules and applies `lerp(lerp(base, secondary, f1), tertiary, f2)` for each pixel. All three samples use the same 2048×2048 compiled diffuse DDS. The shader previews terrain unlit; this does not implement SAGE material lighting or normals.

### Unity integration

1. The `.map` `ScriptedImporter` revision is bumped to 5.
2. With **Apply Atlas To Terrain** and **Render Terrain Blending** enabled, the importer loads optional compiled `map.bin`/`map.manifest` **before** building terrain.
3. If the atlas, blend arrays and shader load correctly, a generated material uses `MusafirEx/SAGE Terrain Atlas Blend`. The source `.mat`, if supplied, is cloned, never mutated.
4. In the `.map` Inspector, expect `Texture Blending: Active (two-way + three-way)` with **64,782**, **4,536** and **16,672** for the source-map counts above.
5. If resources are missing, the importer reverts to the base-atlas material and mesh UVs; an unsupported `BlendTileData` version falls back to terrain and object placement import.

### Still unsupported

Cliff texture overlays/mappings, macro-noise terrain texture, lighting/shadows, water, roads, decals and original W3D world object materials/meshes. Texture-cell orientation must be visually compared to World Builder; no claim of pixel-perfect engine reproduction is made until Unity render tests.

### References

- OpenSAGE: [Terrain.frag](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Assets/Shaders/Terrain.frag) for direction/flag blend math.
- OpenSAGE: [BlendTileData](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/BlendTileData.cs), [BlendDescription](https://github.com/OpenSAGE/OpenSAGE/blob/master/src/OpenSage.Game/Data/Map/BlendDescription.cs).

No proprietary shader/source code has been copied into this package; the original shader described the mathematical behavior used to write a Unity-compatible unlit implementation.