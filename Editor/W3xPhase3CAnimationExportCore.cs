using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3C: patch supported existing W3X channels and append supported
    /// new translation/quaternion channels ONLY to source-defined pivots.
    /// Unknown XML declarations, formats and source files remain untouched.
    /// </summary>
    internal static class W3xPhase3CAnimationExportCore
    {
        private const float PositionTolerance = 0.0001f;
        private const float QuaternionDotTolerance = 0.00001f;
        private const float FpsTolerance = 0.001f;

        internal sealed class Report
        {
            internal int OriginalChannels;
            internal int ChangedChannels;
            internal int ChangedFrames;
            internal int AddedChannels;
            internal int AddedFrames;
            internal int UnsupportedPreserved;
            internal string AnimationId;
            internal readonly List<string> Notes = new List<string>();

            internal string Describe()
            {
                return "Animation: " + AnimationId +
                    "\nOriginal channels: " + OriginalChannels +
                    "\nChanged supported channels: " + ChangedChannels +
                    "\nChanged source frames: " + ChangedFrames +
                    "\nNew SAGE channels: " + AddedChannels +
                    "\nNew channel frames: " + AddedFrames +
                    "\nUnsupported channels preserved: " +
                    UnsupportedPreserved +
                    (Notes.Count == 0 ? "" :
                        "\n" + string.Join("\n", Notes.ToArray()));
            }
        }

        /// <summary>
        /// The passed XML is never mutated. The returned document contains
        /// only edits to scalar Frame text or quaternion Frame attributes.
        /// The caller must provide the current W3X imported baseline clip;
        /// metadata identifies the exact Phase 3B source relationship.
        /// </summary>
        internal static XDocument BuildDocument(XDocument sourceXml,
            W3xDocument sourceDocument, AnimationClip importedBaseline,
            AnimationClip edited, W3xEditableAnimationSource provenance,
            out Report report)
        {
            report = new Report();
            if (sourceXml == null || sourceXml.Root == null ||
                sourceXml.Root.Name.LocalName != "AssetDeclaration")
                throw new InvalidDataException("Not a W3X AssetDeclaration.");
            if (sourceDocument == null || importedBaseline == null ||
                edited == null || provenance == null ||
                provenance.EditableClip != edited)
                throw new InvalidDataException(
                    "Missing original animation, Phase 3B metadata, or " +
                    "the selected .anim does not match its provenance.");

            var id = provenance.OriginalAnimationId;
            if (string.IsNullOrWhiteSpace(id) ||
                !string.Equals(importedBaseline.name, id,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The source AnimationClip ID no longer matches metadata.");
            report.AnimationId = id;
            var matching = sourceXml.Root.Elements()
                .Where(n => n.Name.LocalName == "W3DAnimation" &&
                    string.Equals((string)n.Attribute("id"), id,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length != 1)
                throw new InvalidDataException(
                    "W3X must have exactly one W3DAnimation with id " + id);
            var sourceAnimation = sourceDocument.Animations
                .SingleOrDefault(a => string.Equals(a.Identifier, id,
                    StringComparison.OrdinalIgnoreCase));
            if (sourceAnimation == null)
                throw new InvalidDataException(
                    "W3X animation was not decoded by the Phase 3A reader.");
            if (!string.Equals(sourceAnimation.HierarchyName,
                    provenance.OriginalHierarchyId,
                    StringComparison.OrdinalIgnoreCase) ||
                sourceAnimation.NumFrames != provenance.OriginalFrameCount ||
                Mathf.Abs(sourceAnimation.FrameRate -
                    provenance.OriginalFrameRate) > FpsTolerance)
                throw new InvalidDataException(
                    "Source hierarchy, frame count or FPS changed since " +
                    "the Phase 3B workspace was created.");
            if (Mathf.Abs(edited.frameRate -
                    sourceAnimation.FrameRate) > FpsTolerance ||
                Mathf.Abs(importedBaseline.frameRate -
                    sourceAnimation.FrameRate) > FpsTolerance)
                throw new InvalidDataException(
                    "Animation frame rate was changed; resampling is " +
                    "not supported. Restore the source FPS.");
            if (AnimationUtility.GetObjectReferenceCurveBindings(edited)
                    .Length != 0 || AnimationUtility.GetAnimationEvents(edited)
                    .Length != 0)
                throw new InvalidDataException(
                    "Edited AnimationClip has Unity object-reference " +
                    "curves or events. Phase 3C cannot encode them.");

            var hierarchy = sourceDocument.Hierarchies.SingleOrDefault(h =>
                string.Equals(h.Identifier,
                    sourceAnimation.HierarchyName,
                    StringComparison.OrdinalIgnoreCase));
            if (hierarchy == null || hierarchy.Pivots.Count == 0)
                throw new InvalidDataException(
                    "Matching source W3DHierarchy was not resolved.");
            var sourceChannels = matching[0].Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Channels");
            if (sourceChannels == null)
                throw new InvalidDataException(
                    "Source W3DAnimation has no Channels.");
            var originalRaw = sourceChannels.Elements().ToArray();
            report.OriginalChannels = originalRaw.Length;
            var rawKeys = new HashSet<string>(StringComparer.Ordinal);
            var supported = new List<ChannelMapping>();
            foreach (var element in originalRaw)
            {
                int pivot;
                string kind;
                if (!TryIdentify(element, out pivot, out kind))
                {
                    report.UnsupportedPreserved++;
                    continue;
                }
                if (pivot < 0 || pivot >= hierarchy.Pivots.Count)
                    throw new InvalidDataException(
                        "Supported channel pivot is outside hierarchy.");
                string key = pivot.ToString(CultureInfo.InvariantCulture) +
                    ":" + kind;
                if (!rawKeys.Add(key))
                    throw new InvalidDataException(
                        "Duplicate source channel " + key +
                        "; export is ambiguous.");
                var parsed = sourceAnimation.Channels.FirstOrDefault(c =>
                    c.PivotIndex == pivot && c.Kind == kind);
                if (parsed == null)
                    throw new InvalidDataException(
                        "Supported source channel " + key +
                        " could not be parsed; export refused.");
                var frames = element.Elements()
                    .Where(e => e.Name.LocalName == "Frame").ToArray();
                int length = parsed.Scalars != null ?
                    parsed.Scalars.Length :
                    parsed.Quaternions == null ? 0 :
                        parsed.Quaternions.Length;
                if (frames.Length != length || length == 0)
                    throw new InvalidDataException(
                        "Source channel " + key +
                        " has an inconsistent number of frames.");
                supported.Add(new ChannelMapping
                {
                    Pivot = pivot,
                    Kind = kind,
                    Xml = element,
                    Parsed = parsed,
                    Path = PivotPath(hierarchy, pivot)
                });
            }

            // Existing source channels are editable. New Unity curves
            // can be exported ONLY when they map unambiguously to an
            // existing indexed SAGE pivot and a supported channel kind.
            // Build this map strictly from the W3X companion hierarchy,
            // not the current arbitrary Unity Transform names.
            var allowed = new HashSet<string>(
                supported.SelectMany(ChannelProperties),
                StringComparer.Ordinal);
            var pivotPaths = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (int i = 0; i < hierarchy.Pivots.Count; i++)
            {
                string path = PivotPath(hierarchy, i);
                if (pivotPaths.ContainsKey(path))
                    throw new InvalidDataException(
                        "W3X hierarchy contains duplicate pivot paths: " +
                        path);
                pivotPaths.Add(path, i);
            }
            var baseBindings = GetBindings(importedBaseline);
            // Fail rather than exporting based on an importer interpretation
            // that no longer reproduces the declared source frames.
            VerifyImportedBaseline(sourceAnimation, hierarchy,
                supported, baseBindings);
            var editBindings = GetBindings(edited);
            var additions = ClassifyNewChannels(
                editBindings, allowed, pivotPaths, rawKeys, originalRaw);
            foreach (var binding in baseBindings.Keys)
                if (!editBindings.ContainsKey(binding))
                    throw new InvalidDataException(
                        "A source animation curve was deleted: " +
                        binding.Replace('\n', ' ') +
                        ". Restore it or edit its values explicitly.");
            var baselineNames = W3xPhase3BAnimationWorkspace
                .GetFloatBindingSignatures(importedBaseline);
            if (!baselineNames.SequenceEqual(
                    provenance.SourceFloatBindings ??
                    new string[0], StringComparer.Ordinal))
                throw new InvalidDataException(
                    "Source clip curve bindings changed since Phase 3B " +
                    "workspace creation; rebuild the workspace first.");

            // Reconstruct output independently. Existing source nodes
            // retain their original FirstFrame/count and unknown content;
            // only validated new channels are appended after them.
            var output = new XDocument(sourceXml);
            var clonedAnimation = output.Root.Elements()
                .Single(e => e.Name.LocalName == "W3DAnimation" &&
                    string.Equals((string)e.Attribute("id"), id,
                        StringComparison.OrdinalIgnoreCase));
            var clonedNodes = clonedAnimation.Elements()
                .Single(e => e.Name.LocalName == "Channels").Elements()
                .ToArray();
            if (clonedNodes.Length != originalRaw.Length)
                throw new InvalidDataException(
                    "Output source channel count unexpectedly changed.");

            for (int index = 0; index < originalRaw.Length; index++)
            {
                var mapping = supported.FirstOrDefault(m =>
                    ReferenceEquals(m.Xml, originalRaw[index]));
                if (mapping == null)
                    continue; // Unsupported source channel is untouched.

                var targetFrames = clonedNodes[index].Elements()
                    .Where(e => e.Name.LocalName == "Frame").ToArray();
                var parsed = mapping.Parsed;
                var translated = mapping.Kind != "Quaternion";
                AnimationCurve scalar = translated ?
                    GetCurve(editBindings, mapping.Path,
                        PositionProperty(mapping.Kind)) : null;
                AnimationCurve[] rotation = translated ? null :
                    RotationCurves(editBindings, mapping.Path);
                if (translated && scalar == null)
                {
                    // A zero-only source channel was intentionally
                    // omitted by the importer and remains unchanged.
                    if (parsed.Scalars.All(x =>
                            Mathf.Abs(x) <= PositionTolerance))
                        continue;
                    throw new InvalidDataException(
                        "Nonzero source channel was removed: " +
                        mapping.Path + " " + mapping.Kind);
                }
                if (!translated && rotation == null)
                    throw new InvalidDataException(
                        "Quaternion channel has missing rotation components " +
                        "for " + mapping.Path);

                bool changed = false;
                for (int frame = 0; frame < sourceAnimation.NumFrames; frame++)
                {
                    float time = frame / sourceAnimation.FrameRate;
                    if (translated)
                    {
                        float originalValue = OriginalScalarAt(
                            parsed, frame);
                        float updatedValue = scalar == null ?
                            originalValue :
                            scalar.Evaluate(time) -
                            RestValue(hierarchy.Pivots[mapping.Pivot],
                                mapping.Kind);
                        if (float.IsNaN(updatedValue) ||
                            float.IsInfinity(updatedValue))
                            throw new InvalidDataException(
                                "Nonfinite scalar keyframe in edited clip.");
                        if (frame < parsed.FirstFrame &&
                            Mathf.Abs(updatedValue) > PositionTolerance)
                            throw new InvalidDataException(
                                "Edited values before FirstFrame cannot be " +
                                "represented in " + mapping.Kind);
                        if (frame >= parsed.FirstFrame &&
                            frame < parsed.FirstFrame + parsed.Scalars.Length &&
                            Mathf.Abs(updatedValue - originalValue) >
                                PositionTolerance)
                        {
                            targetFrames[frame - parsed.FirstFrame]
                                .Value = Format(updatedValue);
                            changed = true;
                            report.ChangedFrames++;
                        }
                        if (frame >= parsed.FirstFrame + parsed.Scalars.Length &&
                            Mathf.Abs(updatedValue -
                                (scalar.Evaluate((parsed.FirstFrame +
                                    parsed.Scalars.Length - 1) /
                                    sourceAnimation.FrameRate) -
                                    RestValue(hierarchy.Pivots[
                                        mapping.Pivot], mapping.Kind))) >
                                PositionTolerance)
                            throw new InvalidDataException(
                                "Edited values after the final W3X channel " +
                                "frame cannot be represented.");
                    }
                    else
                    {
                        Quaternion initial = OriginalQuaternionAt(
                            parsed, frame);
                        Quaternion updated = EditedSourceQuaternion(
                            rotation, time,
                            hierarchy.Pivots[mapping.Pivot].Rotation);
                        if (frame < parsed.FirstFrame &&
                            !Equivalent(updated, Quaternion.identity))
                            throw new InvalidDataException(
                                "Edited quaternion before FirstFrame " +
                                "cannot be represented.");
                        if (frame >= parsed.FirstFrame &&
                            frame < parsed.FirstFrame +
                                parsed.Quaternions.Length &&
                            !Equivalent(updated, initial))
                        {
                            var closest = Quaternion.Dot(updated, initial) <
                                0f ? Negate(updated) : updated;
                            var target = targetFrames[
                                frame - parsed.FirstFrame];
                            target.SetAttributeValue("X", Format(closest.x));
                            target.SetAttributeValue("Y", Format(closest.y));
                            target.SetAttributeValue("Z", Format(closest.z));
                            target.SetAttributeValue("W", Format(closest.w));
                            changed = true;
                            report.ChangedFrames++;
                        }
                        if (frame >= parsed.FirstFrame +
                                parsed.Quaternions.Length &&
                            !Equivalent(updated,
                                EditedSourceQuaternion(rotation,
                                    (parsed.FirstFrame +
                                        parsed.Quaternions.Length - 1) /
                                        sourceAnimation.FrameRate,
                                    hierarchy.Pivots[mapping.Pivot].Rotation)))
                            throw new InvalidDataException(
                                "Quaternion after the final W3X channel " +
                                "frame cannot be represented.");
                    }
                }
                if (changed)
                {
                    report.ChangedChannels++;
                    report.Notes.Add("Edited: pivot " + mapping.Pivot +
                        " " + mapping.Kind + " (" + mapping.Path + ")");
                }
            }
            AppendNewChannels(clonedAnimation, hierarchy,
                sourceAnimation, editBindings, additions, report);
            report.Notes.Add("All unsupported XML channels were preserved.");
            report.Notes.Add("Edited keys are sampled at original W3X FPS; " +
                "Unity tangents are not exported.");
            return output;
        }

        private sealed class ChannelMapping
        {
            internal int Pivot;
            internal string Kind;
            internal string Path;
            internal XElement Xml;
            internal W3xAnimationChannel Parsed;
        }

        private static bool TryIdentify(XElement channel,
            out int pivot, out string kind)
        {
            pivot = -1;
            kind = null;
            var type = (string)channel.Attribute("Type") ?? "";
            var tag = channel.Name.LocalName;
            bool isScalar = tag == "ChannelScalar";
            bool isQuat = tag == "ChannelQuaternion" ||
                tag == "ChannelQuat";
            if (isScalar && (type == "XTranslation" ||
                type == "YTranslation" || type == "ZTranslation"))
                kind = type;
            else if (isQuat && (string.IsNullOrEmpty(type) ||
                string.Equals(type, "Orientation",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Quaternion",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Rotation",
                    StringComparison.OrdinalIgnoreCase)))
                kind = "Quaternion";
            if (kind == null)
                return false;
            return int.TryParse((string)channel.Attribute("Pivot"),
                NumberStyles.Integer, CultureInfo.InvariantCulture,
                out pivot);
        }

        private static void VerifyImportedBaseline(
            W3xAnimationData animation, W3xHierarchyData hierarchy,
            IEnumerable<ChannelMapping> supported,
            Dictionary<string, AnimationCurve> bindings)
        {
            foreach (var mapping in supported)
            {
                var parsed = mapping.Parsed;
                if (mapping.Kind != "Quaternion")
                {
                    var curve = GetCurve(bindings, mapping.Path,
                        PositionProperty(mapping.Kind));
                    if (curve == null)
                    {
                        if (parsed.Scalars.All(x =>
                            Mathf.Abs(x) <= PositionTolerance))
                            continue; // Expected for Phase 3A zero-only.
                        throw new InvalidDataException(
                            "Importer baseline is missing nonzero channel " +
                            mapping.Kind + " on " + mapping.Path);
                    }
                    for (int frame = 0; frame < animation.NumFrames; frame++)
                    {
                        float time = frame / animation.FrameRate;
                        float sourceValue = OriginalScalarAt(parsed, frame) +
                            RestValue(hierarchy.Pivots[mapping.Pivot],
                                mapping.Kind);
                        if (Mathf.Abs(curve.Evaluate(time) -
                            sourceValue) > PositionTolerance)
                            throw new InvalidDataException(
                                "Current imported source clip does not " +
                                "reproduce W3X scalar channel " + mapping.Kind +
                                " on " + mapping.Path + ". Reimport source.");
                    }
                }
                else
                {
                    var q = RotationCurves(bindings, mapping.Path);
                    if (q == null)
                        throw new InvalidDataException(
                            "Imported baseline lacks original quaternion " +
                            "curves on " + mapping.Path);
                    for (int frame = 0; frame < animation.NumFrames; frame++)
                    {
                        var sample = EditedSourceQuaternion(q,
                            frame / animation.FrameRate,
                            hierarchy.Pivots[mapping.Pivot].Rotation);
                        if (!Equivalent(sample,
                            OriginalQuaternionAt(parsed, frame)))
                            throw new InvalidDataException(
                                "Current imported source clip does not " +
                                "reproduce W3X quaternion channel on " +
                                mapping.Path + ". Reimport source.");
                    }
                }
            }
        }

        private static string PivotPath(W3xHierarchyData hierarchy,
            int pivot)
        {
            var names = new List<string>();
            var visited = new HashSet<int>();
            int n = pivot;
            while (n >= 0 && n < hierarchy.Pivots.Count &&
                visited.Add(n))
            {
                names.Add(hierarchy.Pivots[n].Name);
                n = hierarchy.Pivots[n].Parent;
            }
            if (n >= 0 && n < hierarchy.Pivots.Count)
                throw new InvalidDataException(
                    "Cyclic hierarchy prevents safe pivot mapping.");
            names.Reverse();
            return "SAGE_Pivots/" + string.Join("/", names.ToArray());
        }

        private static string PositionProperty(string kind)
        {
            if (kind == "XTranslation") return "m_LocalPosition.x";
            if (kind == "YTranslation") return "m_LocalPosition.z";
            if (kind == "ZTranslation") return "m_LocalPosition.y";
            throw new InvalidDataException(
                "Unknown source translation axis " + kind);
        }

        private static IEnumerable<string> ChannelProperties(ChannelMapping m)
        {
            if (m.Kind != "Quaternion")
            {
                yield return Signature(m.Path, PositionProperty(m.Kind));
                yield break;
            }
            foreach (var axis in new[] { "x", "y", "z", "w" })
                yield return Signature(m.Path, "m_LocalRotation." + axis);
        }

        private static string Signature(string path, string property)
        {
            return path + "\n" + typeof(Transform).FullName +
                "\n" + property;
        }

        private static Dictionary<string, AnimationCurve> GetBindings(
            AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .ToDictionary(b => b.path + "\n" +
                    (b.type == null ? "" : b.type.FullName) + "\n" +
                    b.propertyName,
                    b => AnimationUtility.GetEditorCurve(clip, b),
                    StringComparer.Ordinal);
        }

        private static AnimationCurve GetCurve(
            Dictionary<string, AnimationCurve> bindings, string path,
            string property)
        {
            AnimationCurve result;
            return bindings.TryGetValue(Signature(path, property),
                out result) ? result : null;
        }

        private static AnimationCurve[] RotationCurves(
            Dictionary<string, AnimationCurve> bindings, string path)
        {
            var result = new AnimationCurve[4];
            var components = new[] { "x", "y", "z", "w" };
            for (int i = 0; i < components.Length; i++)
            {
                result[i] = GetCurve(bindings, path,
                    "m_LocalRotation." + components[i]);
                if (result[i] == null)
                    return null;
            }
            return result;
        }

        private static float RestValue(W3xPivotData pivot,
            string kind)
        {
            if (kind == "XTranslation") return pivot.Translation.x;
            if (kind == "YTranslation") return pivot.Translation.y;
            if (kind == "ZTranslation") return pivot.Translation.z;
            throw new InvalidDataException("Unknown translation channel.");
        }

        private static float OriginalScalarAt(
            W3xAnimationChannel channel, int frame)
        {
            if (frame < channel.FirstFrame)
                return 0f;
            return channel.Scalars[Mathf.Min(
                frame - channel.FirstFrame,
                channel.Scalars.Length - 1)];
        }

        private static Quaternion OriginalQuaternionAt(
            W3xAnimationChannel channel, int frame)
        {
            if (frame < channel.FirstFrame)
                return Quaternion.identity;
            return channel.Quaternions[Mathf.Min(
                frame - channel.FirstFrame,
                channel.Quaternions.Length - 1)];
        }

        private static Quaternion EditedSourceQuaternion(
            AnimationCurve[] curves, float time, Quaternion rest)
        {
            var q = new Quaternion(
                curves[0].Evaluate(time), curves[1].Evaluate(time),
                curves[2].Evaluate(time), curves[3].Evaluate(time));
            if (float.IsNaN(q.x) || float.IsNaN(q.y) ||
                float.IsNaN(q.z) || float.IsNaN(q.w) ||
                float.IsInfinity(q.x) || float.IsInfinity(q.y) ||
                float.IsInfinity(q.z) || float.IsInfinity(q.w) ||
                Quaternion.Dot(q, q) < 0.000001f)
                throw new InvalidDataException(
                    "Invalid or zero Unity quaternion in edited clip.");
            q.Normalize();
            var source = new Quaternion(-q.x, -q.z, -q.y, q.w);
            return (Quaternion.Inverse(rest) * source).normalized;
        }

        private static bool Equivalent(Quaternion a, Quaternion b)
        {
            a.Normalize();
            b.Normalize();
            return 1f - Mathf.Abs(Quaternion.Dot(a, b)) <=
                QuaternionDotTolerance;
        }

        private static Quaternion Negate(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, -q.z, -q.w);
        }

        private static string Format(float v)
        {
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }
    }
}
