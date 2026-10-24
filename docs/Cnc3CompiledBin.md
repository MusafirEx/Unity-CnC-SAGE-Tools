# C&C3 / Kane's Wrath compiled map.bin — reverse-engineered asset index

## Status

Source-verified against user-provided `eamap_sb05_4` seven-file map folder. v0.9.2 preview adds read-only B/I/M/R indexing and embedded DDS texture generation inside the imported `.map` sub-assets. Unity Editor integration still requires live testing.

## Exact file linkage

- `map.bin`, `map.imp`, `map.relo` begin with the same stream checksum `0x9CCE1238`; `map.manifest` carries it at file offset `0x04`.
- Manifest version **5**, header exactly **0x30 (48) bytes** and **966** asset entries, each exactly **0x2C (44) bytes**.
- Asset reference IDs buffer follows the entries, followed by referenced-manifest names, asset-name buffer and source-filename buffer. Declared sizes and buffers end exactly at the last byte of the **119,070-byte** manifest.
- Each entry records type ID, instance ID, type and instance hashes, reference-buffer offset and count, UTF-8 name/source offsets, and per-asset binary/relocation/import lengths. Exact per-asset offsets are running sums starting at file byte 4 (after stream checksum). The cumulative binary lengths equal the full `map.bin` size, including its four-byte signature.

## Verified asset bodies

| Index | Name | map.bin offset | Binary bytes |
|---|---|---:|---:|
| 941 | `GameMap:EAMAP_SB05_4` | `0x00000004` | 293,420 |
| 942 | `TerrainTextureAtlas:EAMAP_SB05_4` | `0x00047A30` | 16,761,632 |
| 943–965 | 23 `GameScriptList:*` instances | after `0x01043D50` | 28 bytes each |

**941 other entries have zero-length binary bodies**, including 241 W3DMesh and 142 GameObject definitions, and therefore cannot be reconstructed as full meshes from this map's `map.bin` alone. They refer to assets in other streams, depending on the game's asset system.

## TerrainTextureAtlas binary layout

At atlas asset base offset `0x00047A30`:

| Asset-relative offset | Type | Meaning / real sample |
|---|---|---|
| `+0x04` | uint32 | 43 tile descriptors |
| `+0x08` | uint32 | tile-list relative offset `0x1C` |
| `+0x0C` | uint32 | base DDS relative offset `0x220` |
| `+0x10` | uint32 | base DDS bytes `5,587,072` |
| `+0x14` | uint32 | normal DDS relative offset `0x005542A0` |
| `+0x18` | uint32 | normal DDS bytes `11,174,016` |

Tile descriptors are **12 bytes each**: little-endian `uint32 TextureID`, then `uint16 x0,y0,x1,y1`. There are 43 and all rectangle coordinates fit the 2048×2048 atlases. The first descriptor is ID `0x000A0695`, rectangle `(16,16)-(272,272)`; pixel-space rects can be normalized by dividing by 2048.

- Base DDS at absolute `0x00047C50`: BC3/DXT5, 2048×2048, five mip levels.
- Second DDS at absolute `0x0059BCD0`: RGB555/16-bit, 2048×2048, five mip levels.
- Only the top mip is currently decoded into Unity RGBA32 Texture2D; all source `.bin` bytes/mips are retained in the original file. The second atlas appears normal-like but that semantic assignment requires shader confirmation.

## .imp / .relo and manifest data integrity

- `map.imp`: 2,483 increasing primary import-address offsets.
- `map.relo`: 6,831 increasing primary relocation offsets, followed by unparsed versioned tail bytes.
- `map.manifest`: 966 asset records and 25 non-zero data bodies; all per-asset B/I/R lengths sum **exactly** to `.bin`, `.imp`, and `.relo` lengths respectively. The source names and hashes are stored for future asset linking.

## Unity v0.9.2 behavior

Select the **`.map`**, not `.bin`; the same-folder `map.bin` is optional. With `Import Compiled Atlases` enabled, two embedded DDS textures become Texture2D sub-assets. With `Inspect Compiled Indexes` enabled, the importer validates the B/I/M/R siblings, saves every compiled manifest entry and 43 atlas rectangles in Cnc3MapManifest, and shows their counts and owner name in Inspector. Missing/unsupported siblings do not block source terrain/object import.

**Not done:** Decode `GameMap` compiled property semantics, reconstruct W3DMesh/GameObject definitions from base game streams, resolve terrain BlendTileData→TextureID→atlas rect mappings, map shader parameters, or recreate textures beyond validated DDS. These tasks are distinct from successfully reading/validating the `map.bin` stream.

## References

- https://github.com/Qibbi/BinaryAssetBuilder (ManifestHeader and AssetEntry structure; MIT license)
- https://github.com/Qibbi/WrathEd2012 (v5 manifest and TerrainTextureAtlas layout)
- https://github.com/OpenSAGE/OpenSAGE (SAGE asset/map structures)

All analysis here is based on user-provided binary assets. No game binary has been modified.
