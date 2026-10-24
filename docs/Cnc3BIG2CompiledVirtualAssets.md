# C&C3/Kane's Wrath BIG-2 (v0.9.13) — Virtual SAGE Compiled Assets

## Why you found only .manifest and .bin

Your real WBData.big contains compiled SAGE streams rather than loose `.dds` and `.w3x` members. The BIG-1 archive directory enumerator is therefore working as expected. The manifest provides an asset directory and the matching BIN holds compiled asset payloads. `.imp` and `.relo` streams are optional from the point of view of name/offset indexing, but are essential for interpreting many pointer-based compiled objects later.

**No full WBData extraction** is needed to browse this index. One lightweight Unity `Cnc3BigCompiledAssetIndex.asset` stores virtual names, types and offsets; only explicitly inspected per-asset portions are read from the original BIG.

## Supported BIN/manifest parsing

Uses the **validated BinaryAssetBuilder v5 manifest layout** already employed for compiled `.map`: 48-byte manifest header, 44-byte asset entries, manifest string/reference buffers, cumulative per-asset BIN `.bin` offsets from byte 4. Uses a map-specific compiled stream ID from manifest offset +4, matching the first four bytes of the bin. BIN payload byte counts are checked against the manifest's declared size; nonstandard or packed streams are flagged.

Per virtual record it retains `name`, `source`, `typeId`, `instanceId`, `referenceCount`, `binOffset`/`binSize`, and import/relocation offsets/sizes. No pointers are dereferenced, GameObject logic executed, W3X model assumed, or unsupported formats synthesized.

Only v5 manifests are implemented; other versions return a descriptive error. No `.imp`/`.relo` files need to be extracted merely to list the names. Some compiled object types may require those additional streams to reconstruct their textures/geometry later.

## How to test in Unity

1. Install UPM branch `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-big2-compiled-assets` (candidate v0.9.13).
2. Confirm BIG-1 works using **Tools → C&C SAGE → BIG Archive Browser**, select your original WBData.big (even outside Assets), and save `WBData_Index.asset`.
3. Open **Tools → C&C SAGE → BIG Compiled Asset Browser**. Drag `WBData_Index.asset` to **BIG Archive Index**.
4. Select the paired `<name>.bin + <name>.manifest` shown in **Compiled stream pair** and click **Build Virtual Asset Index**. Save e.g. `Assets/CnC3KW/WBData_CompiledIndex.asset`.
5. Filter/search `Texture:`, `GameObject:`, `Road`, `UKRoad`, `Sidewalk` to inspect asset types and names. Select one virtual asset and click **Read first 128 bytes only** to preview a bounded hexadecimal sample (no file extraction).
6. Optionally select `.map` importer. Set **Import Road Surfaces** ON and drag `WBData_CompiledIndex.asset` into **Road Compiled Asset Index**. On Reimport the MapInfo counts virtual texture records and the number of verified DDS directly decoded from compiled BIN.
7. Run **Tools → C&C SAGE → Run BIG-2 Compiled Index Self-Test**. Expect `[C&C SAGE BIG2] Self-test PASS` in Console. Also run existing BIG-1 self-test to confirm no regression.

## Road materials — verified vs unsupported

Lookup priority is still manual RoadLibrary Texture2D → loose DDS exact matches if present → standalone BIG DDS/PNG → virtual `Texture:<original-name>` record inside compiled BIN. Only a **structurally verified embedded DDS** (classic DXT1/DXT5) in the selected Texture record is eligible for material binding. If the record holds engine-native compressed or pointer-based texture data instead, the importer reports this and leaves the preview color. It does not invent diffuse materials or substitute ASRoad for UKRoad.

The source `RoadTemplate.Texture` mapping is still required whenever a road TypeName differs from its actual Texture asset name. An indexed manifest tells us which Texture names exist; it does not automatically prove which named road uses which texture.

## Current limitations / next steps

- This phase indexes compiled records and supports limited byte access, **not a complete C&C3 GameObject/W3D/W3X importer from BIG**. Correct runtime asset construction will require compiled resource type parsers, reference lists and likely `.imp`/`.relo` handling.
- Big compressed BIN members may exceed the current RefPack memory limits, in which case random reads are deliberately rejected. Uncompressed BIN entries support direct seek without copying the entire stream.
- DDS textures embedded as engine-specific image payloads rather than a literal `DDS ` header remain unsupported; next examine a single genuine Texture asset's BIN hex header and its manifest name/type to implement a source-specific decoder.
- The indexed BIG's absolute path points to the **local original game installation** and needs relinking if moved between computers.
- Existing extracted WBData may still be referenced by W3X meshes and materials: **do not delete it** until maps and W3X are verified with the new virtual asset access.
- Unity Editor compilation, test menu execution and game-file validation are **pending**. Keep draft PR until tested.

## Relevant source file locations

- `Editor/Cnc3BigArchiveIndex.cs` — archive reader + bounded random-access slice
- `Editor/Cnc3BigCompiledAssetIndex.cs` — compiled manifest v5 parser and virtual asset index
- `Editor/Cnc3BigCompiledBrowserWindow.cs` — search/select/hex-preview interface
- `Editor/Cnc3BigCompiledTextureBridge.cs` — guarded embedded DDS decode candidate
- `Editor/Cnc3MapImporter.cs` — optional Road Compiled Asset Index
- `Editor/Cnc3BigCompiledSelfTest.cs` — synthetic pair and bounds validation
