# Phase 5A v0.9.11 — Resolve Kane's Wrath road types from official EA Roads.xml

## Root cause confirmed

Previously the importer fell back to `MapObject.TypeName` as the road DDS basename. This was incorrect for a large fraction of maps. Electronic Arts publishes authoritative road asset records in:

- `https://github.com/electronicarts/CnC_Modding_Support/blob/main/Kanes%20Wrath/Xml/GlobalData/Roads.xml`
- `https://github.com/electronicarts/CnC_Modding_Support/blob/main/Kanes%20Wrath/Schemas/AssetTypeRoads.xsd`

The official Kane's Wrath file declares **106 Road definitions**, each with a `Road` id, `Texture`, `RoadWidth`, `RoadWidthInTexture`, and `Priority`, often `NormalTexture` too. Most road assets use a **different source texture name**. Example `WashingtonRoadMarking01 -> DCRoadMarking01`, `UKSidewalk03 -> UKRSidewalk03`, `AusDirtRoad01 -> ASDirtRoad01`.

## Source map test: eamap_sb05_4

Independently validated against the source `map_object_placements.csv`: 4,729 placements, 1,110 consecutive road Start/End pairs across **13** road types. **All 13 are present in EA's Roads.xml**:

| World Builder road type | Original Texture | RoadWidth | RoadWidthInTexture |
|---|---|---:|---:|
| UKSidewalk03 | UKRSidewalk03 | 22 | .95 |
| UKSidewalk01 | UKRSidewalk01 | 15 | .95 |
| UKSidewalk02 | UKRSidewalk02 | 5 | .95 |
| UKRoad02 | UKRoad02 | 46 | .95 |
| UKRoad01 | UKRoad01 | 70 | .95 |
| WashingtonRoadMarking02 | DCRoadMarking02 | 5 | .95 |
| UKDirtRoad01 | UKRDirtRoad01 | 65 | .95 |
| WashingtonRoadMarking01 | DCRoadMarking01 | 5 | .95 |
| AusDirtRoad01 | ASDirtRoad01 | 75 | .95 |
| UKRoadMarking01 | UKRoadMarking01 | 15 | .95 |
| WashingtonRoadMarking06 | DCRoadMarking06 | 35 | .95 |
| WashingtonRoadMarking04 | DCRoadMarking04 | 28 | .95 |
| WashingtonRoadMarking05 | DCRoadMarking05 | 28 | .95 |

## Unity workflow (one-time install)

1. Install Git UPM package `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-official-kw-road-definitions`.
2. Choose **Tools → C&C SAGE → Install Official Kane's Wrath Road Definitions**. This initiates an explicit, opt-in download from EA's GitHub to `Assets/CnC3KW/Definitions/Roads_KanesWrath.xml`. If already present, reuses the existing file; never overwrites it.
3. The command finds existing `*RoadLibrary*` assets within `Assets/CnC3KW/Maps`, links the new XML only where no custom `sourceDefinitions` was assigned, and resolves original road template fields and available local DDSs for each source road type. It preserves manually linked `Texture2D` assets and intentionally customized widths.
4. For each `.map`, ensure **Import Road Surfaces** and **Auto Resolve Road Textures** are enabled and **Import Road Guides** disabled, then Reimport. The importer reads the linked source definitions and uses source `Texture`, `RoadWidth`, `RoadWidthInTexture`, `NormalTexture` and `Priority`. It searches extracted `Assets/CnC3KW/WBData` using the actual Texture attribute, so it no longer requires renamed `.dds` files.
5. Inspect **Road Types Textured**, **Texture Folders**, **Missing Road Textures**, source width and per-type texture path. Existing `*RoadLibrary.asset` can also use **Assets → C&C SAGE → Auto-Match Road Library Textures** to repair/refresh one library.
6. Newly created libraries filter only valid consecutive Start/End road pairs, avoiding orphan road flags from non-rendered objects, and use official names/dimensions when the XML is installed.

### Offline fallback

Manually obtain `Roads.xml` from the **official EA repository** above and save it under `Assets/CnC3KW/Definitions/Roads_KanesWrath.xml` (Unity imports `.xml` as TextAsset). Then choose **Tools → C&C SAGE → Install Official Kane's Wrath Road Definitions** again. It will reuse the local file and bind Road Libraries without a network request.

### Compatibility / preservation

- The original `Cnc3RoadLibrary.ReadTemplates` supports locally supplied Roads.ini and `Road`/`RoadTemplate` XML, including namespaced `<AssetDeclaration xmlns="uri:ea.com:eala:asset">`.
- Pre-v0.9.11 generated guessed width values (original 24/14/2.5 and ratio 1) are recognized by comparing against `GuessSourceWidth` plus a blank `sourceTextureName`; when the official definition exists, the real values replace these guesses in memory and in libraries upgraded via the menu. Custom road overrides and manually selected textures remain authoritative.
- Missing or unsupported source road types stay as preview strips and report missing atlas files rather than assigning unrelated ones.
- The official EA definition **data are not embedded in, bundled with, or redistributed by this package**. The user downloads the original XML directly from EA. Their modding-source repository is licensed GPLv3 with additional terms; consult the [EA repository license](https://github.com/electronicarts/CnC_Modding_Support/blob/main/LICENSE.md) before redistributing the downloaded XML or modified data.
- Road textures in WBData remain external user-extracted `.dds`/`.tga` assets; no copyrighted game art is committed.

### Verification state

- EA's official `Roads.xml` was inspected via GitHub and contains 106 `<Road/>` records.
- 13/13 authentic map road type names were matched to official attributes; XML structure matches the existing `XmlReader` parser (namespace-safe via `LocalName`).
- GitHub source checks passed for edited C# files; **Unity compilation and actual road rendering still require a package update and live Editor verification**. Original alpha/UV atlas layout and exact SAGE curve intersections remain Phase 5B.
