using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    // Common, serialized Unity import settings. Both ScriptedImporters own one
    // instance. Defaults preserve the existing W3D/W3X import behavior.
    public enum SageRigType
    {
        None = 0,
        Legacy = 1,
        Generic = 2
    }

    public enum SageAvatarDefinition
    {
        CreateFromThisModel = 0,
        CopyFromOtherAvatar = 1
    }

    [Serializable]
    public sealed class SageMaterialOverride
    {
        public string key;
        public Material material;
    }

    [Serializable]
    public sealed class SageImportOptions
    {
        [Tooltip("Import supported W3D/W3X mesh geometry.")]
        public bool importMeshes = true;

        [Tooltip("Scale the imported Unity model root; does not edit source W3D/W3X files.")]
        [Min(0.0001f)]
        public float unitsToMeters = 1f;

        [Tooltip("Import supported animation tracks as Unity AnimationClip subassets.")]
        public bool importAnimations = true;

        [Tooltip("Assign generated preview materials to imported renderers.")]
        public bool importMaterials = true;

        [Tooltip("Optional replacement shader for generated Unity preview materials.")]
        public Shader materialShaderOverride;

        [Tooltip("Per-renderer material-slot external .mat assignments.")]
        public List<SageMaterialOverride> materialOverrides = new List<SageMaterialOverride>();

        [Tooltip("None preserves clips only; Legacy uses Animation; Generic creates an Animator and Avatar.")]
        public SageRigType rigType = SageRigType.None;

        [Tooltip("For a Generic rig, build an Avatar from this asset or reuse an existing Generic Avatar.")]
        public SageAvatarDefinition avatarDefinition = SageAvatarDefinition.CreateFromThisModel;

        [Tooltip("A previously imported valid Generic Avatar.")]
        public Avatar sourceAvatar;

        [Tooltip("Optional root motion pivot name for Unity Generic Avatar generation; blank disables root motion.")]
        public string genericRootBone = "";
    }

    [Serializable]
    public sealed class SageMaterialEntry
    {
        public string key;
        public string sourceName;
        public string rendererPath;
        public int slot;
        public Material generatedMaterial;
        public Texture texture;
    }

    // Persistent import-time material index; the Editor Inspector reads this,
    // while import/export logic remains independent from Inspector widgets.
    public sealed class SageMaterialManifest : ScriptableObject
    {
        public List<SageMaterialEntry> entries = new List<SageMaterialEntry>();
    }
}
