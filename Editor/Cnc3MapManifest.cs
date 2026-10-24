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

        // Persist the decoded placements even when placeholder GameObjects
        // are disabled, for later asset-binding in Mode 2.
        public List<Cnc3MapPlacement> placements = new List<Cnc3MapPlacement>();
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
