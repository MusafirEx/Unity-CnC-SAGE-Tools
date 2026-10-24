# Phase 5A v0.9.10 — Automatic WBData Road Texture Matching

## Motivation

In the user's actual Unity project, `Assets/CnC3KW` contains **13,008 assets**, including five maps, five `Cnc3RoadLibrary` assets, and thousands of extracted WBData files. The inventory's first 2,000 assets confirms DDS textures such as `ASRoad01.dds`, `ASDirtRoad01.dds`, `ASRoadMarking01.dds` and `ASRsidewalk01.dds`, but the sample map `eamap_sb05_4` uses **different** road type names. The initial alphabetical scan stopped before the UK/Washington naming region, so their actual DDS presence has not been verified.

Source map `map_object_placements.csv` contains 1,110 Start/End road pairs and these 13 road type names (counts are individual control points):

| Source Road Type | Control points |
|---|---:|
| UKSidewalk03 | 644 |
| UKSidewalk01 | 504 |
| UKSidewalk02 | 262 |
| UKRoad02 | 170 |
| UKRoad01 | 148 |
| WashingtonRoadMarking02 | 126 |
| UKDirtRoad01 | 90 |
| WashingtonRoadMarking01 | 74 |
| AusDirtRoad01 | 68 |
| UKRoadMarking01 | 64 |
| WashingtonRoadMarking06 | 48 |
| WashingtonRoadMarking04 | 12 |
| WashingtonRoadMarking05 | 10 |

These names **must not be automatically linked to ASRoad DDSs by vague substring matching**. The binding algorithm in this PR performs exact case-insensitive basename matching only.

## Logic

1. If a `Cnc3RoadLibrary.Template.texture` has an explicit `Texture2D`, use it first, never overwrite it.
2. Index texture assets in the explicitly selected `Road Texture Folder` (highest search priority), any `WBData` directory found by walking upward from the map path (e.g. sibling `Assets/CnC3KW/WBData`), the folder of any source Roads.ini/XML TextAsset, and finally the source map folder.
3. Search for the declared `RoadTemplate.Texture` basename first, stripping directories and extensions.
4. If none is declared/matched and **Auto Resolve Road Textures** is enabled, try the road `TypeName` as an exact filename basename. Mark this match as an **unverified candidate**, not a verified game source texture assignment.
5. If unmatched, keep the previous solid-color preview and record the road name with reason in the map's Manifest, including searched directories and total textures indexed.
6. Respect the existing atlas shader's support/fallback; image presence does not guarantee original road-atlas layout is reproduced perfectly.

Supported texture extensions: `.dds`, `.tga`, `.png`, `.tif`, `.tiff`. `_NRM` and `_SPM` variants are never substituted for a matching albedo automatically.

## One-click fix for existing RoadLibrary assets

Select any generated `*_RoadLibrary.asset` in Unity Project. Then choose **Assets → C&C SAGE → Auto-Match Road Library Textures**. The command indexes the correct `WBData` automatically and fills only the *empty* `Texture2D` fields for exact matches. It preserves manual assignments, supports Undo and saves the library asset. Console/dialog shows the number auto-matched, preserved and missing plus searched folder paths.

Alternatively no asset mutation is necessary: the `.map` importer now has **Auto Resolve Road Textures** enabled by default, so **Import Road Surfaces** can auto-bind exact-name candidates during reimport. The map `MapInfo` reports:

- `Road Types Textured` — active road atlas shader and assigned source texture
- `Auto-Matched Road Textures` — bound from indexed project assets without a manual Texture2D override
- `Indexed Road Texture Candidates` and `Texture Folders` — diagnostic trace
- One line per road type showing its bound source method or missing reason
- `RoadTypeEntry.textureAssetPath` — exact Unity asset path for the matched Texture2D

## Test procedure

1. Update Git package to `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#fix/cnc3-phase5-road-texture-autobind`.
2. Reimport `Assets/CnC3KW/Maps/eamap_sb05_4/eamap_sb05_4.map` with **Import Road Surfaces** and **Auto Resolve Road Textures** enabled.
3. Check `Texture Folders` includes `Assets/CnC3KW/WBData`, and `Indexed Road Texture Candidates` is positive.
4. Check `Road Types Textured` and list any missing texture names. A 13/13 result is **not guaranteed** until the UK/Washington road DDSs and original RoadTemplate.Texture values are verified.
5. Optionally select `eamap_sb05_4_RoadLibrary.asset` and run the one-click auto-match menu. Ensure manually assigned textures are unchanged. Reimport map.
6. Verify straight road atlas UV orientation and alpha against World Builder. Exact curves/intersections and the atlas's source-specific tile layout remain pending Phase 5B.
7. Test all other four maps and verify original terrain/blending, W3X imports and water remain unchanged.

**Validation state:** GitHub source changes committed and statically checked; live Unity compile and full WBData DDS scan not yet verified because the Unity IPC reported disconnected when checked. No compiled road assets are distributed.
