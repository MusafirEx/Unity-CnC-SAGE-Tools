using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Persistent metadata for one imported C&C3 World Builder map.
    /// This ScriptableObject must be declared in Cnc3MapManifest.cs, otherwise
    /// Unity 2022.3 rejects AssetImportContext.AddObjectToAsset.
    /// </summary>
    public sealed class Cnc3MapManifest : ScriptableObject
    {
        public int width;
        public int height;
        public int border;
        public int tileCount;
        public int objectCount;
        public int objectMarkerCount;
        public int distinctObjectTypes;
        public bool wasCompressed;
        public bool terrainGenerated;
        public bool objectsGenerated;
        public ushort heightVersion;
        public float horizontalScale;
        public float verticalScale;
        public float terrainWidth;
        public float terrainLength;
        public string[] sections;

        // Optional compiled map.bin diagnostics: no modification to external files.
        public bool hasCompiledBin;
        public long compiledBinSize;
        public int compiledMetadataBytes;
        public int compiledTextureCount;
        public int compiledImportAddresses;
        public int compiledRelocationAddresses;
        public int manifestGameObjectNames;
        public int manifestAssetCount;
        public bool compiledAtlasLayoutVerified;
        public string compiledAtlasAssetName;
        public int compiledAtlasTileCount;
        public List<Cnc3AtlasTileEntry> compiledAtlasTiles =
            new List<Cnc3AtlasTileEntry>();
        public int manifestAssetsWithData;
        public int manifestTextureNames;
        public bool compiledManifestMatchesBin;
        public string compiledImportNote;
        public List<Cnc3CompiledTextureEntry> compiledTextures =
            new List<Cnc3CompiledTextureEntry>();
        public List<Cnc3CompiledAssetEntry> compiledAssets =
            new List<Cnc3CompiledAssetEntry>();

        // Persist the decoded placements even when placeholder GameObjects
        // are disabled, for later asset-binding in Mode 2.
        public List<Cnc3MapPlacement> placements = new List<Cnc3MapPlacement>();
    }

    [Serializable]
    public sealed class Cnc3CompiledTextureEntry
    {
        public string name;
        public string format;
        public string ownerAsset;
        public int width;
        public int height;
        public int mipCount;
        public long offset;
        public int length;
        public Texture2D texture;
    }

    [Serializable]
    public sealed class Cnc3AtlasTileEntry
    {
        public uint textureId;
        public int x0, y0, x1, y1;
    }

    [Serializable]
    public sealed class Cnc3CompiledAssetEntry
    {
        public string assetName;
        public string sourceFile;
        public uint typeId;
        public uint instanceId;
        public int binaryOffset;
        public int binaryLength;
        public int importOffset;
        public int importLength;
        public int relocationOffset;
        public int relocationLength;
        public uint referenceCount;
    }

    [Serializable]
    public sealed class Cnc3MapPlacement
    {
        public string typeName;
        public Vector3 sagePosition;
        public float sageAngleRadians;
        public uint roadFlags;
        public ushort recordVersion;
    }
}
