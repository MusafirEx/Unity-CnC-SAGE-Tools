using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3A: build AnimationClip sub-assets for explicitly supported
    /// W3DAnimation channels. No game animation export or runtime controller.
    /// Assumes W3D channels contain rest-pose-relative pivot deltas.
    /// </summary>
    internal static class W3xAnimationClipBuilder
    {
        internal static int Build(W3xDocument doc, GameObject root,
            W3xHierarchyData importedHierarchy, Transform[] pivots,
            Action<string, AnimationClip> register)
        {
            if (doc == null || root == null || register == null ||
                doc.Animations.Count == 0)
                return 0;

            int created = 0;
            foreach (var animation in doc.Animations)
            {
                if (importedHierarchy == null ||
                    pivots == null ||
                    pivots.Length != importedHierarchy.Pivots.Count ||
                    !string.Equals(importedHierarchy.Identifier,
                        animation.HierarchyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[C&C SAGE Phase 3A] " +
                        animation.Identifier +
                        " has no matching imported SAGE hierarchy '" +
                        animation.HierarchyName +
                        "'; clip skipped, XML preserved.");
                    continue;
                }

                var valid = animation.Channels.Where(ch =>
                    ch.PivotIndex >= 0 && ch.PivotIndex < pivots.Length)
                    .ToArray();
                if (valid.Length != animation.Channels.Count)
                    Debug.LogWarning("[C&C SAGE Phase 3A] " +
                        animation.Identifier +
                        ": channel refers to a pivot outside the hierarchy; " +
                        "invalid channel skipped.");
                if (valid.Length == 0)
                    continue;

                var clip = new AnimationClip
                {
                    name = animation.Identifier,
                    frameRate = animation.FrameRate
                };

                try
                {
                    int bindings = WriteCurves(animation, valid,
                        importedHierarchy, root.transform, pivots, clip);
                    if (bindings == 0)
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                        continue;
                    }
                    clip.EnsureQuaternionContinuity();
                    register("animation_" + created, clip);
                    created++;
                    Debug.Log("[C&C SAGE Phase 3A] Imported " +
                        animation.Identifier + ": " + animation.NumFrames +
                        " frames @ " + animation.FrameRate +
                        " FPS, " + bindings + " animated pivots.");
                }
                catch (Exception e)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                    Debug.LogWarning("[C&C SAGE Phase 3A] " +
                        animation.Identifier + " skipped: " + e.Message);
                }
            }
            return created;
        }

        private static int WriteCurves(W3xAnimationData animation,
            W3xAnimationChannel[] channels, W3xHierarchyData hierarchy,
            Transform root, Transform[] pivots, AnimationClip clip)
        {
            int bindings = 0;
            var grouped = channels.GroupBy(ch => ch.PivotIndex);
            foreach (var group in grouped)
            {
                int pivot = group.Key;
                string path = AnimationUtility.CalculateTransformPath(
                    pivots[pivot], root);

                // Reject ambiguous duplicate tracks rather than choosing
                // an arbitrary one, which could corrupt pivot motion.
                if (group.GroupBy(ch => ch.Kind).Any(g => g.Count() > 1))
                {
                    Debug.LogWarning("[C&C SAGE Phase 3A] " +
                        animation.Identifier + " pivot " + pivot +
                        ": duplicate channel type; pivot skipped.");
                    continue;
                }

                var lookup = group.ToDictionary(c => c.Kind, c => c,
                    StringComparer.Ordinal);
                W3xAnimationChannel x, y, z, q;
                bool hasX = lookup.TryGetValue("XTranslation", out x);
                bool hasY = lookup.TryGetValue("YTranslation", out y);
                bool hasZ = lookup.TryGetValue("ZTranslation", out z);
                bool hasQ = lookup.TryGetValue("Quaternion", out q);

                var basePivot = hierarchy.Pivots[pivot];
                // A W3X scalar translation channel owns exactly ONE axis.
                // Never manufacture other position curves, even constants:
                // those tracks can override a pivot's authored rest pose
                // (notably BODY/B_TURRET) during Unity animation blending.
                // Source X -> Unity X; source Y -> Unity Z;
                // source Z -> Unity Y, consistent with mesh/pivot import.
                if (hasX)
                    SetPosition(clip, path, "m_LocalPosition.x", x,
                        basePivot.Translation.x, animation.NumFrames,
                        animation.FrameRate);
                if (hasY)
                    SetPosition(clip, path, "m_LocalPosition.z", y,
                        basePivot.Translation.y, animation.NumFrames,
                        animation.FrameRate);
                if (hasZ)
                    SetPosition(clip, path, "m_LocalPosition.y", z,
                        basePivot.Translation.z, animation.NumFrames,
                        animation.FrameRate);
                if (hasQ)
                {
                    var rx = new Keyframe[animation.NumFrames];
                    var ry = new Keyframe[animation.NumFrames];
                    var rz = new Keyframe[animation.NumFrames];
                    var rw = new Keyframe[animation.NumFrames];
                    Quaternion previous = Quaternion.identity;
                    for (int f = 0; f < animation.NumFrames; f++)
                    {
                        Quaternion delta = Rotation(q, f);
                        Quaternion sage = basePivot.Rotation * delta;
                        Quaternion unity = new Quaternion(-sage.x, -sage.z,
                            -sage.y, sage.w).normalized;
                        if (f > 0 && Quaternion.Dot(unity, previous) < 0f)
                            unity = new Quaternion(-unity.x, -unity.y,
                                -unity.z, -unity.w);
                        previous = unity;
                        float time = f / animation.FrameRate;
                        rx[f] = new Keyframe(time, unity.x);
                        ry[f] = new Keyframe(time, unity.y);
                        rz[f] = new Keyframe(time, unity.z);
                        rw[f] = new Keyframe(time, unity.w);
                    }
                    Set(clip, path, "m_LocalRotation.x", rx);
                    Set(clip, path, "m_LocalRotation.y", ry);
                    Set(clip, path, "m_LocalRotation.z", rz);
                    Set(clip, path, "m_LocalRotation.w", rw);
                }
                if (hasX || hasY || hasZ || hasQ)
                    bindings++;
            }
            return bindings;
        }

        private static void SetPosition(AnimationClip clip,
            string path, string property, W3xAnimationChannel channel,
            float restPosition, int numFrames, float fps)
        {
            if (channel == null)
                return;
            var keys = new Keyframe[numFrames];
            for (int frame = 0; frame < numFrames; frame++)
                keys[frame] = new Keyframe(frame / fps,
                    restPosition + Value(channel, frame));
            Set(clip, path, property, keys);
        }

        private static float Value(W3xAnimationChannel channel, int frame)
        {
            if (channel == null || frame < channel.FirstFrame)
                return 0f;
            int index = Mathf.Min(frame - channel.FirstFrame,
                channel.Scalars.Length - 1);
            return channel.Scalars[index];
        }

        private static Quaternion Rotation(
            W3xAnimationChannel channel, int frame)
        {
            if (channel == null || frame < channel.FirstFrame)
                return Quaternion.identity;
            int index = Mathf.Min(frame - channel.FirstFrame,
                channel.Quaternions.Length - 1);
            return channel.Quaternions[index];
        }

        private static void Set(AnimationClip clip, string path,
            string property, Keyframe[] keys)
        {
            // Sampled frame-for-frame from the source, with linear
            // interpolation. No unsupported interpolation is guessed.
            clip.SetCurve(path, typeof(Transform), property,
                new AnimationCurve(keys));
        }
    }
}
