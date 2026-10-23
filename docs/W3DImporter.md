# Binary W3D import: Generals / Zero Hour

Version: 0.8.0 (experimental). Binary W3D is imported in Unity without modifying any game files. The existing W3X pipeline is separate and unchanged.

## Installation

Use the same Git UPM URL as before: https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git .
Unity 2022.3 LTS or later, Package Manager > + > Add package from git URL. Select an approved release after the feature PR is merged; branch testing can use https://github.com/MusafirEx/Unity-CnC-SAGE-Tools.git#feature/w3d-importer-generals .

## Importing W3D

1. Extract .w3d game files from your legitimately obtained Generals/Zero Hour installation. BIG extraction is not included.
2. Place related *_SKN.w3d, *_SKL.w3d, animation .w3d and extracted texture images in the same Assets/ folder.
3. Allow Unity to import; select a W3D file in the Project window, expand the subassets and inspect the GameObject.
4. The first uncompressed AnimationClip is configured as a legacy Animation on an animation W3D root object. To animate a model, use a rig with identical pivot paths.
5. Check Unity Console for warnings; unsupported chunk types are not silently converted to arbitrary data.

## What to expect

- Source vertex axes X,Y,Z are converted to Unity X,Z,Y; triangle winding is reversed.
- MeshHeader3, vertex positions, triangle indices, normals, primary UVs and the first extracted texture are supported.
- W3D hierarchy pivots appear beneath SAGE_Pivots; HModel same-file mesh nodes can attach to a matching pivot.
- Skin weights use a single W3D bone index per vertex; bind poses are approximate and W3D pivot-fixup matrices are not implemented.
- Uncompressed animation X/Y/Z translation and quaternion channels are represented in a legacy Unity AnimationClip; uncompressed visibility bits are skipped.
- Standalone animation references look for the named hierarchy or matching *_SKL.w3d in the same directory.

## Limitations

- Source-only W3D assets. No W3D export, BIG extraction, or W3D-to-W3X conversion.
- No full HLOD assembly, compressed animation, rich material passes, emitter or collision rendering.
- Meshes stored in different W3D files are not automatically assembled into an HLOD model.
- Complex skinned rigs may require correction (fixups and vertex binding semantics have not been verified with real game assets).
- Basic materials require textures already imported into Unity; references inside game BIG files are not resolved.
- The file importer supports one source file at a time, and is designed to fail on invalid chunk bounds rather than skip malformed data.

## No-game-asset smoke test

In Unity Package Manager, import the Minimal W3D Import Test sample. Run Python 3 in the sample directory:

```bat
python generate_sample.py
```

This creates SampleTriangle.w3d, SAMPLE_SKL.w3d and SampleAnimation.w3d.
Expected after Unity asset refresh:

- SampleTriangle has one Mesh subasset with three vertices, one triangle, UVs and normals.
- SAMPLE_SKL has a ROOTTRANSFORM GameObject beneath SAGE_Pivots.
- SampleAnimation has a SAMPLE_MOVE AnimationClip (3 frames, 30fps) and resolves SAMPLE_SKL.w3d as a source dependency.
- The sample contains no original EA content.

## Diagnostic checklist

- Script compilation errors: confirm Unity package version 0.8.0 and only one installed copy of the plugin.
- Missing skeleton: ensure exact matching W3D hierarchy name and companion files exist side-by-side in Assets.
- Mesh missing: may be an HLOD assembly referencing meshes in separate W3D files; inspect the Console.
- Texture missing: extract texture to Unity folder as an importable image format.
- Pose wrong: our first version does not support all FixupMatrix semantics; report a model example and logs.
- Animation missing: compressed W3D channels are not yet decoded; inspect Console for compressed-clip notice.

References: OpenSAGE W3D file-format docs (https://opensage.readthedocs.io/file-formats/w3d/) and the published EA Generals W3D structure header.
