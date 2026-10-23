using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    internal static class SageImporterPostprocessor
    {
        internal static void Finish(AssetImportContext context, GameObject root,
            SageImportOptions options, IList<AnimationClip> clips)
        {
            if (options == null) options = new SageImportOptions();
            root.transform.localScale = Vector3.one *
                Mathf.Clamp(options.unitsToMeters, 0.0001f, 100000f);
            ConfigureMaterials(context, root, options);
            ConfigureRig(context, root, options, clips);
        }

        private static void ConfigureMaterials(AssetImportContext context,
            GameObject root, SageImportOptions options)
        {
            var index = ScriptableObject.CreateInstance<SageMaterialManifest>();
            index.name = root.name + "_Materials";
            var overrides = new Dictionary<string, Material>(StringComparer.Ordinal);
            if (options.materialOverrides != null)
            {
                foreach (SageMaterialOverride entry in options.materialOverrides)
                    if (entry != null && !string.IsNullOrEmpty(entry.key) &&
                        entry.material != null && !overrides.ContainsKey(entry.key))
                        overrides.Add(entry.key, entry.material);
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] generated = renderer.sharedMaterials;
                Material[] assigned = new Material[generated.Length];
                for (int slot = 0; slot < generated.Length; slot++)
                {
                    Material source = generated[slot];
                    string relative = IndexedPath(root.transform, renderer.transform);
                    string key = relative + "|slot" + slot;
                    var entry = new SageMaterialEntry
                    {
                        key = key,
                        sourceName = source != null ? source.name : renderer.name,
                        rendererPath = relative,
                        slot = slot,
                        generatedMaterial = source,
                        texture = source != null ? source.mainTexture : null
                    };
                    index.entries.Add(entry);
                    if (source != null && options.materialShaderOverride != null)
                        source.shader = options.materialShaderOverride;

                    if (!options.importMaterials)
                        continue;

                    assigned[slot] = source;
                    if (overrides.TryGetValue(key, out Material external))
                    {
                        string externalPath = AssetDatabase.GetAssetPath(external);
                        bool isExternalMat = !string.IsNullOrEmpty(externalPath) &&
                            externalPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(externalPath, context.assetPath, StringComparison.OrdinalIgnoreCase);
                        if (isExternalMat)
                        {
                            assigned[slot] = external;
                            if (externalPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                                context.DependsOnSourceAsset(externalPath);
                        }
                        else
                            Debug.LogWarning("[C&C SAGE] Material override '" + key +
                                "' must reference a separate .mat file under Assets.");
                    }
                }
                renderer.sharedMaterials = assigned;
            }

            context.AddObjectToAsset("SAGE_MaterialManifest", index);
        }

        // Include sibling index to avoid collisions for duplicated bone/mesh names.
        private static string IndexedPath(Transform root, Transform leaf)
        {
            var fragments = new List<string>();
            Transform t = leaf;
            while (t != null && t != root)
            {
                fragments.Add(t.name + "[" + t.GetSiblingIndex() + "]");
                t = t.parent;
            }
            fragments.Reverse();
            return string.Join("/", fragments.ToArray());
        }

        private static void ConfigureRig(AssetImportContext context, GameObject root,
            SageImportOptions options, IList<AnimationClip> clips)
        {
            if (options.rigType == SageRigType.None)
                return;

            if (options.rigType == SageRigType.Legacy)
            {
                if (clips == null || clips.Count == 0)
                    return;
                Animation component = root.GetComponent<Animation>();
                if (component == null) component = root.AddComponent<Animation>();
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;
                    clip.legacy = true;
                    component.AddClip(clip, clip.name);
                    if (component.clip == null) component.clip = clip;
                }
                return;
            }

            if (options.rigType != SageRigType.Generic)
                return;

            Avatar avatar = null;
            bool created = false;
            if (options.avatarDefinition == SageAvatarDefinition.CopyFromOtherAvatar)
            {
                if (options.sourceAvatar == null || !options.sourceAvatar.isValid ||
                    options.sourceAvatar.isHuman)
                {
                    Debug.LogWarning("[C&C SAGE] Generic rig requires a valid external Generic Avatar.");
                    return;
                }
                avatar = options.sourceAvatar;
                string path = AssetDatabase.GetAssetPath(avatar);
                if (!string.IsNullOrEmpty(path) &&
                    path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    context.DependsOnSourceAsset(path);
            }
            else
            {
                string motionBone = options.genericRootBone ?? "";
                if (!string.IsNullOrWhiteSpace(motionBone) &&
                    !root.GetComponentsInChildren<Transform>(true).Any(t =>
                        string.Equals(t.name, motionBone, StringComparison.Ordinal)))
                {
                    Debug.LogWarning("[C&C SAGE] Generic root motion pivot '" +
                        motionBone + "' missing, disabling root motion.");
                    motionBone = "";
                }
                try
                {
                    avatar = AvatarBuilder.BuildGenericAvatar(root, motionBone);
                    if (avatar == null || !avatar.isValid)
                    {
                        if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                        Debug.LogWarning("[C&C SAGE] Generic Avatar creation failed; source meshes/clips remain.");
                        return;
                    }
                    created = true;
                    avatar.name = root.name + "_Avatar";
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[C&C SAGE] Generic Avatar creation failed: " + ex.Message);
                    return;
                }
            }

            if (created)
                context.AddObjectToAsset("SAGE_GenericAvatar", avatar);
            foreach (AnimationClip clip in clips ?? Array.Empty<AnimationClip>())
                if (clip != null) clip.legacy = false;

            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
        }
    }
}
