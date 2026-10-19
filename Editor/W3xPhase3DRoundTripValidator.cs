using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3D READ-ONLY acceptance audit:
    /// original W3X + Phase 3B edited .anim + reimported Phase 3C W3X.
    /// A structural XML check protects unknown source data. Frame-by-frame
    /// sampled position and quaternion comparisons detect playback drift.
    /// Does not modify source assets, clips, prefabs, scenes or XML.
    /// </summary>
    internal static class W3xPhase3DRoundTripValidator
    {
        private const string Menu =
            "Tools/C&C SAGE/Validate Phase 3D Animation Round Trip";
        private const float PositionTolerance = 0.002f;
        private const float RotationToleranceDegrees = 0.5f;
        private const int MaxMessages = 30;

        internal sealed class Report
        {
            internal string AnimationId;
            internal int Frames;
            internal float FrameRate;
            internal int SourceChannels;
            internal int ExportedChannels;
            internal int AddedChannels;
            internal int ComparedPositions;
            internal int ComparedRotations;
            internal float MaximumPositionError;
            internal float MaximumRotationErrorDegrees;
            internal int Mismatches;
            internal readonly List<string> Messages = new List<string>();
            internal bool Passed => Mismatches == 0;

            internal void Issue(string issue)
            {
                Mismatches++;
                if (Messages.Count < MaxMessages)
                    Messages.Add(issue);
            }

            internal string Describe()
            {
                var b = new StringBuilder();
                b.AppendLine("[C&C SAGE Phase 3D] Animation Round-Trip Audit");
                b.AppendLine("Result: " + (Passed ? "PASS" : "FAIL"));
                b.AppendLine("Animation: " + AnimationId);
                b.AppendLine("Frames/FPS: " + Frames + " / " +
                    FrameRate.ToString("G9", CultureInfo.InvariantCulture));
                b.AppendLine("W3X original/exported channels: " +
                    SourceChannels + " / " + ExportedChannels +
                    " (added " + AddedChannels + ")");
                b.AppendLine("Samples: position=" + ComparedPositions +
                    ", rotation=" + ComparedRotations);
                b.AppendLine("Maximum local position difference: " +
                    MaximumPositionError.ToString("G6",
                        CultureInfo.InvariantCulture));
                b.AppendLine("Maximum quaternion angle difference: " +
                    MaximumRotationErrorDegrees.ToString("G6",
                        CultureInfo.InvariantCulture) + " degrees");
                b.AppendLine("Validation mismatches: " + Mismatches);
                foreach (string note in Messages)
                    b.AppendLine("  " + note);
                if (Messages.Count >= MaxMessages &&
                    Mismatches > Messages.Count)
                    b.AppendLine("  Further mismatches suppressed.");
                b.AppendLine("Only samples at source frame times are " +
                    "compared. Unity tangent/interpolation behavior " +
                    "between frames and actual Kane's Wrath compilation " +
                    "are not validated by this audit.");
                return b.ToString();
            }
        }

        [MenuItem(Menu)]
        private static void Run()
        {
            try
            {
                AnimationClip edited, imported;
                W3xEditableAnimationSource metadata;
                string sourcePath, exportedPath, reason;
                if (!TryResolve(out edited, out imported, out metadata,
                    out sourcePath, out exportedPath, out reason))
                {
                    EditorUtility.DisplayDialog("C&C SAGE Phase 3D",
                        reason, "OK");
                    return;
                }
                XDocument source = W3xExportCore.LoadSource(
                    Path.GetFullPath(sourcePath));
                XDocument output = W3xExportCore.LoadSource(
                    Path.GetFullPath(exportedPath));
                W3xDocument parsedSource = W3xReader.Read(sourcePath);
                var report = Compare(source, output, parsedSource,
                    edited, imported, metadata);
                Debug.Log(report.Describe());
                EditorUtility.DisplayDialog("C&C SAGE Phase 3D",
                    (report.Passed ? "PASS\n\n" : "FAIL\n\n") +
                    report.Describe(), "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3D] Round-trip audit failed: " + ex);
                EditorUtility.DisplayDialog("C&C SAGE Phase 3D",
                    "Validation could not finish:\n" + ex.Message,
                    "OK");
            }
        }

        private static bool TryResolve(out AnimationClip edited,
            out AnimationClip imported,
            out W3xEditableAnimationSource metadata,
            out string sourcePath, out string exportedPath,
            out string reason)
        {
            edited = null;
            imported = null;
            metadata = null;
            sourcePath = null;
            exportedPath = null;
            reason = null;

            var selection = Selection.objects ?? new UnityEngine.Object[0];
            var independentClips = selection.OfType<AnimationClip>()
                .Where(c => string.Equals(
                    Path.GetExtension(AssetDatabase.GetAssetPath(c)),
                    ".anim", StringComparison.OrdinalIgnoreCase)).ToArray();
            var provenance = selection
                .OfType<W3xEditableAnimationSource>().ToArray();
            if (independentClips.Length > 1 || provenance.Length > 1)
            {
                reason = "Select only ONE independent editable clip " +
                    "(or its single source metadata asset).";
                return false;
            }
            metadata = provenance.FirstOrDefault();
            edited = independentClips.FirstOrDefault() ??
                (metadata != null ? metadata.EditableClip : null);
            if (edited == null ||
                metadata != null && metadata.EditableClip != edited)
            {
                reason = "Select the Phase 3B *_Editable.anim " +
                    "(or its *_Source.asset), plus the EXPORTED W3X " +
                    "file from Phase 3C in Unity's Project window.";
                return false;
            }

            string editedPath = AssetDatabase.GetAssetPath(edited);
            string folder = Path.GetDirectoryName(editedPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                reason = "Edited animation folder does not exist.";
                return false;
            }
            AnimationClip selectedClip = edited; // never capture out param
            var allMetadata = Directory.GetFiles(folder, "*.asset")
                .Select(p => AssetDatabase.LoadAssetAtPath<
                    W3xEditableAnimationSource>(p.Replace('\\', '/')))
                .Where(m => m != null && m.EditableClip == selectedClip)
                .ToArray();
            if (allMetadata.Length != 1 ||
                metadata != null && metadata != allMetadata[0])
            {
                reason = "Exactly one matching Phase 3B provenance " +
                    "sidecar must exist beside the .anim file.";
                return false;
            }
            metadata = allMetadata[0];
            sourcePath = AssetDatabase.GUIDToAssetPath(
                metadata.SourceW3xGuid);
            if (string.IsNullOrEmpty(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Cannot resolve the original source W3X GUID.";
                return false;
            }

            var selectedW3x = selection
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) &&
                    string.Equals(Path.GetExtension(p), ".w3x",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selectedW3x.Length != 1 ||
                string.Equals(selectedW3x[0], sourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Select the newly EXPORTED .w3x file " +
                    "(not the original), together with your " +
                    "Phase 3B *_Editable.anim in the Project window. " +
                    "Use Ctrl+click for multi-selection.";
                return false;
            }
            exportedPath = selectedW3x[0];
            string animationId = metadata.OriginalAnimationId;
            imported = AssetDatabase.LoadAllAssetsAtPath(exportedPath)
                .OfType<AnimationClip>().SingleOrDefault(c =>
                    string.Equals(c.name, animationId,
                        StringComparison.OrdinalIgnoreCase));
            if (imported == null)
            {
                reason = "The exported W3X does not have an imported " +
                    "AnimationClip sub-asset named " + animationId +
                    ". Reimport the W3X and retry.";
                return false;
            }
            return true;
        }

        internal static Report Compare(XDocument source, XDocument output,
            W3xDocument parsedSource, AnimationClip edited,
            AnimationClip reimported,
            W3xEditableAnimationSource metadata)
        {
            if (source == null || output == null || parsedSource == null ||
                edited == null || reimported == null || metadata == null)
                throw new InvalidDataException(
                    "Phase 3D requires W3X source/output documents " +
                    "and both imported/edited clips with metadata.");

            var result = new Report
            {
                AnimationId = metadata.OriginalAnimationId,
                Frames = metadata.OriginalFrameCount,
                FrameRate = metadata.OriginalFrameRate
            };
            XElement srcAnim = FindAnimation(source, result.AnimationId);
            XElement outAnim = FindAnimation(output, result.AnimationId);
            if (srcAnim == null || outAnim == null)
                throw new InvalidDataException(
                    "Original or exported W3DAnimation ID is missing.");
            if (!string.Equals(
                    (string)srcAnim.Attribute("Hierarchy"),
                    metadata.OriginalHierarchyId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Phase 3B original hierarchy ID differs from W3X.");
            string[] timing = { "Hierarchy", "NumFrames", "FrameRate" };
            foreach (string attr in timing)
                if (!string.Equals((string)srcAnim.Attribute(attr),
                    (string)outAnim.Attribute(attr),
                    StringComparison.Ordinal))
                    result.Issue("W3DAnimation " + attr +
                        " was modified.");
            int sourceFrames;
            float fps;
            if (!int.TryParse((string)srcAnim.Attribute("NumFrames"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out sourceFrames) ||
                !float.TryParse((string)srcAnim.Attribute("FrameRate"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out fps) ||
                sourceFrames != metadata.OriginalFrameCount ||
                Mathf.Abs(fps - metadata.OriginalFrameRate) > .001f)
                throw new InvalidDataException(
                    "Original W3X frames/FPS do not match Phase 3B metadata.");
            if (Mathf.Abs(edited.frameRate - fps) > .001f ||
                Mathf.Abs(reimported.frameRate - fps) > .001f)
                result.Issue("Edited or reimported clip FPS differs.");

            VerifyXmlPreservation(source, output,
                srcAnim, outAnim, result);

            var rig = parsedSource.Hierarchies.SingleOrDefault(h =>
                string.Equals(h.Identifier,
                    metadata.OriginalHierarchyId,
                    StringComparison.OrdinalIgnoreCase));
            if (rig == null)
                throw new InvalidDataException(
                    "Original SAGE W3DHierarchy is unavailable.");
            var editedBindings = Bindings(edited);
            var exportedBindings = Bindings(reimported);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int pivot = 0; pivot < rig.Pivots.Count; pivot++)
            {
                string path = PivotPath(rig, pivot);
                if (!paths.Add(path))
                    throw new InvalidDataException(
                        "Ambiguous duplicate SAGE pivot path " + path);
                ComparePivot(path, rig.Pivots[pivot],
                    editedBindings, exportedBindings, sourceFrames,
                    fps, result);
            }
            foreach (string key in editedBindings.Keys.Concat(
                exportedBindings.Keys).Distinct())
            {
                var parts = key.Split('\n');
                string pivotPath = parts.Length > 0 ? parts[0] : "";
                if (!paths.Contains(pivotPath))
                    result.Issue("Curve is outside original SAGE rig: " +
                        key.Replace('\n', ' '));
                else if (parts.Length != 3 ||
                    parts[1] != typeof(Transform).FullName ||
                    !SupportedProperty(parts[2]))
                    result.Issue("Unsupported Unity animation binding: " +
                        key.Replace('\n', ' '));
            }
            return result;
        }

        private static XElement FindAnimation(XDocument document,
            string id)
        {
            var matches = document.Root == null ?
                new XElement[0] : document.Root.Elements()
                    .Where(e => e.Name.LocalName == "W3DAnimation" &&
                        string.Equals((string)e.Attribute("id"), id,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException(
                    "Multiple W3DAnimation nodes named " + id);
            return matches.FirstOrDefault();
        }

        internal static void VerifyXmlPreservation(
            XDocument source, XDocument output, XElement srcAnim,
            XElement outAnim, Report result)
        {
            // Allow modifications only in the chosen W3DAnimation's
            // supported existing frame values or appended channel nodes.
            var before = new XDocument(source);
            var after = new XDocument(output);
            var outputAnimation = FindAnimation(after,
                (string)srcAnim.Attribute("id"));
            if (outputAnimation == null)
                throw new InvalidDataException(
                    "Exported W3DAnimation is missing.");
            outputAnimation.ReplaceWith(new XElement(srcAnim));
            if (!XNode.DeepEquals(before, after))
                result.Issue("Unrelated W3X XML declarations, attributes " +
                    "or metadata changed.");

            XElement oldChannels = srcAnim.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Channels");
            XElement newChannels = outAnim.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Channels");
            if (oldChannels == null || newChannels == null)
                throw new InvalidDataException(
                    "W3DAnimation must contain Channels.");
            var original = oldChannels.Elements().ToArray();
            var exported = newChannels.Elements().ToArray();
            result.SourceChannels = original.Length;
            result.ExportedChannels = exported.Length;
            result.AddedChannels = exported.Length - original.Length;
            if (result.AddedChannels < 0)
                result.Issue("Original W3X channels were removed.");
            var knownChannels = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (var originalChannel in original)
            {
                string key = ChannelKey(originalChannel);
                if (key != null && !knownChannels.Add(key))
                    result.Issue("Duplicate source animation channel: " +
                        key);
            }
            for (int i = original.Length; i < exported.Length; i++)
            {
                var addition = exported[i];
                int pivot, first;
                string channel = ChannelKey(addition);
                if (channel == null ||
                    !int.TryParse((string)addition.Attribute("Pivot"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out pivot) ||
                    !int.TryParse((string)addition.Attribute("FirstFrame"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out first) || first != 0 ||
                    addition.Elements().Count(e =>
                        e.Name.LocalName == "Frame") != result.Frames ||
                    addition.Elements().Any(e =>
                        e.Name.LocalName != "Frame"))
                    result.Issue("Appended W3X channel is not a supported " +
                        "full-frame scalar/quaternion channel at index " +
                        i);
                else if (!knownChannels.Add(channel))
                    result.Issue("Appended W3X channel is duplicated: " +
                        channel);
            }
            for (int i = 0; i < Math.Min(original.Length,
                exported.Length); i++)
            {
                XElement left = original[i];
                XElement right = exported[i];
                bool isScalar = left.Name.LocalName == "ChannelScalar" &&
                    new[] { "XTranslation", "YTranslation", "ZTranslation" }
                        .Contains((string)left.Attribute("Type"));
                bool isQuat = (left.Name.LocalName == "ChannelQuaternion" ||
                    left.Name.LocalName == "ChannelQuat") &&
                    new[] { "Orientation", "Quaternion", "Rotation" }
                        .Contains((string)left.Attribute("Type"));
                if (!isScalar && !isQuat)
                {
                    if (!XNode.DeepEquals(left, right))
                        result.Issue("Unsupported channel changed at " + i);
                    continue;
                }

                // Normalize ONLY the Frame payload; all channel
                // attributes, child counts and other data must agree.
                XElement candidate = new XElement(right);
                var rightFrames = candidate.Elements().Where(e =>
                    e.Name.LocalName == "Frame").ToArray();
                var leftFrames = left.Elements().Where(e =>
                    e.Name.LocalName == "Frame").ToArray();
                if (rightFrames.Length != leftFrames.Length)
                {
                    result.Issue("Original channel frame count changed " +
                        "at index " + i);
                    continue;
                }
                for (int j = 0; j < leftFrames.Length; j++)
                    rightFrames[j].ReplaceWith(new XElement(leftFrames[j]));
                if (!XNode.DeepEquals(left, candidate))
                    result.Issue("Original channel metadata altered at " +
                        "index " + i);
            }
        }

        private static string ChannelKey(XElement element)
        {
            if (element == null)
                return null;
            string type = (string)element.Attribute("Type") ?? "";
            string normalized;
            if (element.Name.LocalName == "ChannelScalar" &&
                new[] { "XTranslation", "YTranslation", "ZTranslation" }
                    .Contains(type))
                normalized = type;
            else if ((element.Name.LocalName == "ChannelQuaternion" ||
                element.Name.LocalName == "ChannelQuat") &&
                new[] { "Orientation", "Quaternion", "Rotation" }
                    .Contains(type))
                normalized = "Quaternion";
            else
                return null;
            return ((string)element.Attribute("Pivot") ?? "") +
                ":" + normalized;
        }

        private static Dictionary<string, AnimationCurve> Bindings(
            AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .ToDictionary(b => b.path + "\n" +
                    (b.type == null ? "" : b.type.FullName) + "\n" +
                    b.propertyName,
                    b => AnimationUtility.GetEditorCurve(clip, b),
                    StringComparer.Ordinal);
        }

        private static string PivotPath(W3xHierarchyData hierarchy,
            int pivot)
        {
            var seen = new HashSet<int>();
            var names = new List<string>();
            int index = pivot;
            while (index >= 0 && index < hierarchy.Pivots.Count &&
                seen.Add(index))
            {
                names.Add(hierarchy.Pivots[index].Name);
                index = hierarchy.Pivots[index].Parent;
            }
            if (index >= 0 && index < hierarchy.Pivots.Count)
                throw new InvalidDataException(
                    "Cyclic W3X hierarchy prevents path comparison.");
            names.Reverse();
            return "SAGE_Pivots/" + string.Join("/", names.ToArray());
        }

        private static void ComparePivot(string path, W3xPivotData pivot,
            Dictionary<string, AnimationCurve> edited,
            Dictionary<string, AnimationCurve> output,
            int frames, float fps, Report report)
        {
            Vector3 rest = new Vector3(pivot.Translation.x,
                pivot.Translation.z, pivot.Translation.y);
            for (int frame = 0; frame < frames; frame++)
            {
                float time = frame / fps;
                foreach (var p in new[]
                {
                    new { Name = "m_LocalPosition.x", Rest = rest.x },
                    new { Name = "m_LocalPosition.y", Rest = rest.y },
                    new { Name = "m_LocalPosition.z", Rest = rest.z }
                })
                {
                    string key = Signature(path, p.Name);
                    bool left = edited.ContainsKey(key);
                    bool right = output.ContainsKey(key);
                    if (!left && !right)
                        continue;
                    float v0 = left ? edited[key].Evaluate(time) : p.Rest;
                    float v1 = right ? output[key].Evaluate(time) : p.Rest;
                    if (!Finite(v0) || !Finite(v1))
                    {
                        report.Issue("Nonfinite local position on " + path);
                        continue;
                    }
                    float delta = Mathf.Abs(v0 - v1);
                    report.MaximumPositionError =
                        Mathf.Max(report.MaximumPositionError, delta);
                    report.ComparedPositions++;
                    if (delta > PositionTolerance)
                        report.Issue("Frame " + frame + " " + path +
                            " " + p.Name + ": edited=" +
                            v0.ToString("G6",
                                CultureInfo.InvariantCulture) +
                            ", reimported=" + v1.ToString("G6",
                                CultureInfo.InvariantCulture));
                }

                bool sourceRot = HasRotation(edited, path);
                bool outputRot = HasRotation(output, path);
                if (!sourceRot && !outputRot)
                    continue;
                Quaternion restRot = new Quaternion(
                    -pivot.Rotation.x, -pivot.Rotation.z,
                    -pivot.Rotation.y, pivot.Rotation.w);
                Quaternion a, b;
                string error;
                if (!GetRotation(edited, path, time, restRot,
                    out a, out error) ||
                    !GetRotation(output, path, time, restRot,
                    out b, out error))
                {
                    report.Issue("Frame " + frame + " " + path +
                        ": " + error);
                    continue;
                }
                float degrees = Quaternion.Angle(a, b);
                report.MaximumRotationErrorDegrees =
                    Mathf.Max(report.MaximumRotationErrorDegrees, degrees);
                report.ComparedRotations++;
                if (degrees > RotationToleranceDegrees)
                    report.Issue("Frame " + frame + " " + path +
                        " quaternion angle differs by " +
                        degrees.ToString("G6",
                            CultureInfo.InvariantCulture) + " degrees");
            }
        }

        private static bool GetRotation(
            Dictionary<string, AnimationCurve> curves, string path,
            float time, Quaternion rest,
            out Quaternion result, out string error)
        {
            result = rest;
            error = null;
            string prefix = Signature(path, "m_LocalRotation.");
            int count = new[] { "x", "y", "z", "w" }.Count(axis =>
                curves.ContainsKey(prefix + axis));
            string eulerPrefix = null;
            foreach (string form in new[] {
                "localEulerAnglesRaw.", "localEulerAnglesBaked.",
                "localEulerAngles." })
                if (new[] { "x", "y", "z" }.Any(axis =>
                    curves.ContainsKey(Signature(path, form + axis))))
                {
                    if (eulerPrefix != null)
                    {
                        error = "Mixed Euler angle representations";
                        return false;
                    }
                    eulerPrefix = form;
                }
            if (count == 0 && eulerPrefix == null)
                return true;
            if (count > 0 && eulerPrefix != null)
            {
                error = "Mixed quaternion and Euler bindings";
                return false;
            }
            if (count > 0)
            {
                if (count != 4)
                {
                    error = "Missing quaternion component";
                    return false;
                }
                result = new Quaternion(
                    curves[prefix + "x"].Evaluate(time),
                    curves[prefix + "y"].Evaluate(time),
                    curves[prefix + "z"].Evaluate(time),
                    curves[prefix + "w"].Evaluate(time));
            }
            else
            {
                string e = eulerPrefix;
                if (!new[] { "x", "y", "z" }.All(axis =>
                    curves.ContainsKey(Signature(path, e + axis))))
                {
                    error = "Incomplete Euler rotation";
                    return false;
                }
                result = Quaternion.Euler(
                    curves[Signature(path, e + "x")].Evaluate(time),
                    curves[Signature(path, e + "y")].Evaluate(time),
                    curves[Signature(path, e + "z")].Evaluate(time));
            }
            if (!Finite(result.x) || !Finite(result.y) ||
                !Finite(result.z) || !Finite(result.w) ||
                Quaternion.Dot(result, result) < 0.000001f)
            {
                error = "Nonfinite/zero quaternion";
                return false;
            }
            result.Normalize();
            return true;
        }

        private static bool HasRotation(
            Dictionary<string, AnimationCurve> curves, string path)
        {
            return curves.Keys.Any(key =>
                key.StartsWith(Signature(path, "m_LocalRotation."),
                    StringComparison.Ordinal) ||
                key.StartsWith(Signature(path, "localEulerAngles"),
                    StringComparison.Ordinal));
        }

        private static string Signature(string path, string property)
        {
            return path + "\n" + typeof(Transform).FullName +
                "\n" + property;
        }

        private static bool SupportedProperty(string property)
        {
            if (property == "m_LocalPosition.x" ||
                property == "m_LocalPosition.y" ||
                property == "m_LocalPosition.z")
                return true;
            foreach (string prefix in new[] {
                "m_LocalRotation.", "localEulerAnglesRaw.",
                "localEulerAnglesBaked.", "localEulerAngles." })
            {
                if (!property.StartsWith(prefix,
                    StringComparison.Ordinal))
                    continue;
                var component = property.Substring(prefix.Length);
                return component.Length == 1 &&
                    (prefix == "m_LocalRotation." ?
                        "xyzw".Contains(component) :
                        "xyz".Contains(component));
            }
            return false;
        }

        private static bool Finite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
