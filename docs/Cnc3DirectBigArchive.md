# v0.9.12 — Direct EA SAGE BIG Archive Access

## Why

Extracting WBData.big to `Assets/CnC3KW/WBData` produced thousands of imported W3X, DDS and XML assets (13,008 assets under the user's CnC3KW folder with five maps), consuming Unity import time and disk space. This feature instead stores **one index ScriptableObject** referring to the unchanged original `WBData.big` or `StaticStream.big` on disk. Archive members are **virtual entries**, not Unity import assets.

## Implemented scope (Phase BIG-1)

- Read `BIG4` and `BIGF` header/index for C&C3/Kane's Wrath archives: four-character signature; archive length u32 little-endian; directory entries and counts big-endian; zero-terminated member names.
- Verify directory and file offsets, prevent traversal/absolute paths, reject duplicate names and malformed/bounded out-of-range member data.
- Store original archive path, byte count, last-write time, fourCC and member list in `Cnc3BigArchiveIndex.asset` (Unity Editor only). It is a reference to the original archive, not an extracted copy.
- Window **Tools → C&C SAGE → BIG Archive Browser**: choose external BIG (original Steam installation), save index asset, filter filenames, inspect member size and offset, rescan changed files and **Extract only this entry** on explicit command.
- Read member on demand, including bounded RefPack streams (markers `10FB`, `11FB`, `90FB`, `91FB`); output and back-reference limits apply. Export large uncompressed compiled streams using 64 KiB chunks, not a giant buffer. Compressed reads are capped at 96 MiB input / 128 MiB decoded.
- Optional `.map` field **Road BIG Archive** accepts `Cnc3BigArchiveIndex`. If an exact matching standalone `.dds` (BC1/DXT1, BC3/DXT5) or `.png` entry exists, that road's Texture2D is constructed on demand **as a map subasset**. No separate DDS asset is created. Prefers explicit RoadLibrary Texture2D and indexed loose WBData textures before reading BIG. DDS basename candidates are never fuzzily mapped between unlike road types.
- `Cnc3MapManifest` records indexed archive count, archive status, and number of road textures loaded directly from BIG.
- Menu **Tools → C&C SAGE → Run BIG Archive Self-Test** builds synthetic BIG4/BIGF files and checks header reading, member bytes, RefPack literal terminal and malicious relative path rejection.

## Important limitation — BIG archive is not equivalent to a decoded asset library

For later SAGE titles, e.g. C&C3 TW/KW, the BIG may hold compiled `.bin` plus `.manifest/.imp/.relo` streams rather than loose road DDS/W3X assets. This phase **does not yet decode all compiled GameObject/W3D/texture entries in WBData.bin**. Consequently assigning `WBData.big` may index the archive successfully while **Road Types Textured remains unchanged**. The browser can inspect/extract individual compiled stream members without dumping the entire WBData extraction, but identifying and instantiating game-native models/materials requires a dedicated compiled-resource resolver and cross-stream hash/name mapping (Phase BIG-2).

Do NOT delete the already-extracted WBData assets before testing and implementing BIG-2; current W3X model and material links may still depend on those assets. The archive's external absolute file path will need relinking after moving computers or game installations. Rescan if BIG contents change.

## How to use

1. Update Unity Git package to `https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/cnc3-direct-big-archive`.
2. Open **Tools → C&C SAGE → BIG Archive Browser**.
3. Choose **Open BIG...** and browse to your existing game installation, e.g. `C:/Program X/Steam/steamapps/common/Command and Conquer 3 - Kane's Wrath/Core/1.0/WBData.big`.
4. When asked to save, create e.g. `Assets/CnC3KW/WBData_BigIndex.asset`. Only this lightweight file is created, not the extracted contents.
5. In the browser search `.dds`, `road`, `.manifest` and `.bin` to see precisely how resources are stored in the archive. If the archive contains only compiled resource streams, this confirms BIG-2 decoding is required.
6. To test road DDS lookup: select a `.map`; enable Import Road Surfaces; assign `WBData_BigIndex.asset` to **Road BIG Archive**; Reimport. MapInfo shows BIG Archive Members and DDS Loaded Directly From BIG.
7. Run Tools → C&C SAGE → Run BIG Archive Self-Test; check Console. Next test selected entry export, and verify changing original `.big` is detected as stale.

## Avoiding unintended bulk imports

This is a read-only index; no archive is automatically extracted. Selecting **Extract only this entry** creates at most the selected entry outside the project unless the user chooses an Assets destination. No `AssetDatabase.ImportAsset` for the archive member list.

## References

- [OpenSAGE BIG format](https://github.com/OpenSAGE/Docs/blob/master/file-formats/big/index.rst)
- [Jonwil SAGE tools archive reader](https://github.com/jonwil/sagetools/tree/main/bigfile) (format comparison only; this package uses independent C# code)

## Verification status

Source-level static balance and GitHub file presence checked. Synthetic self-test is committed but **must be run within Unity**. No Unity compilation/visual test has yet been performed while RTS IPC is disconnected. BIG-2 compiled resource decoding and full W3X/model virtualization are not claimed here.
