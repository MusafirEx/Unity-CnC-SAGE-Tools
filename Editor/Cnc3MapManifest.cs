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
        public int cliffMappingCount;
        public int cliffOverlayCellCount;
        public bool cliffPreviewEnabled;
        public bool terrainSageLightingApplied;
        public int sourceTimeOfDay;
        public int displayedTimeOfDay;
        public int roadSourceControlCount;
        public int roadGuideSegments;
        public int roadGuideMeshCount;
        public int curvedRoadGuideSegments;
        public string roadGuideStatus;
        public int roadSurfaceSegments;
        public int roadSurfaceMeshes;
        public int roadSurfaceConnectedJunctions;
        public int roadSurfaceCrossings;
        public int roadSurfaceFourWayCrossings;
        public int roadSurfaceResolvedTypes;
        public int roadSurfaceTexturedTypes;
        public int roadSurfaceAutoMatchedTypes;
        public int roadSurfaceIndexedTextures;
        public int roadFromBigTextureCount;
        public int roadFromCompiledBinTextureCount;
        public int compiledBigVirtualAssetCount;
        public int compiledBigVirtualTextureCount;
        public string roadCompiledBigStatus;
        public int roadBigArchiveEntryCount;
        public string roadBigArchiveStatus;
        public string roadSurfaceSearchedFolders;
        public List<string> roadSurfaceMissingTextures = new List<string>();
        public int roadSurfaceUnresolvedTypes;
        public int roadSurfaceDuplicateSegments;
        public string roadSurfaceStatus;
        public List<Cnc3RoadTypeEntry> roadSurfaceTypes =
            new List<Cnc3RoadTypeEntry>();


        public bool terrainAtlasApplied;
        public string terrainAtlasStatus;
        public int terrainSourceTextureCount;
        public int terrainTextureMatchCount;
        public bool terrainBlendingApplied;
        public int terrainBlendDescriptionCount;
        public int terrainFirstBlendCells;
        public int terrainThreeWayBlendCells;
        public string terrainBlendStatus;
        public int objectCount;
        public int standingWaterCount;
        public int standingWaterGenerated;
        public bool globalWaterReflectionEnabled;
        public float globalWaterReflectionHeight;
        public List<Cnc3StandingWaterEntry> standingWaters =
            new List<Cnc3StandingWaterEntry>();
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
        public uint compiledStreamId;
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
    public sealed class Cnc3RoadTypeEntry
    {
        public string roadType;
        public string sourceTextureName;
        public float roadWidth;
        public float roadWidthInTexture;
        public int segmentCount;
        public int crossingCount;
        public bool foundRoadDefinition;
        public bool hasTexture;
        public string textureAssetPath;
        public string textureMatchSource;
        public Mesh mesh;
        public Material material;
    }

    [Serializable]
    public sealed class Cnc3StandingWaterEntry
    {
        public uint uniqueId;
        public string name;
        public string layerName;
        public uint sageHeight;
        public int pointCount;
        public float uvScrollSpeed;
        public bool additive;
        public string bumpMap;
        public string skyTexture;
        public string fxShader;
        public string depthColors;
        public bool generated;
        public Mesh waterMesh;
        public Material previewMaterial;
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
