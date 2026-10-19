using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3C in-memory XML and clip regression tests. No project file,
    /// prefab, scene or source W3X is modified.
    /// </summary>
    internal static class W3xPhase3CAnimationExportSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 3C Animation Export Self-Test")]
        private static void Run()
        {
            GameObject root = null;
            AnimationClip imported = null;
            AnimationClip edited = null;
            W3xEditableAnimationSource metadata = null;
            try
            {
                const string xml =
                    "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                    "<W3DHierarchy id='TEST_RIG'>" +
                    "<Pivot Name='ROOTTRANSFORM' NameID='0' Parent='-1'/>" +
                    "<Pivot Name='BODY' NameID='1' Parent='0'>" +
                    "<Translation X='2' Y='4' Z='3'/>" +
                    "<Rotation X='0' Y='0' Z='0' W='1'/>" +
                    "</Pivot>" +
                    "<Pivot Name='EYE' NameID='2' Parent='1'/>" +
                    "</W3DHierarchy>" +
                    "<W3DAnimation id='TEST_RUNA' Hierarchy='TEST_RIG' " +
                    "NumFrames='3' FrameRate='30'><Channels>" +
                    "<ChannelScalar Pivot='1' Type='XTranslation' " +
                    "FirstFrame='0'><Frame>0</Frame><Frame>1</Frame>" +
                    "<Frame>2</Frame></ChannelScalar>" +
                    "<ChannelScalar Pivot='1' Type='YTranslation' " +
                    "FirstFrame='0'><Frame>0</Frame><Frame>0</Frame>" +
                    "<Frame>0</Frame></ChannelScalar>" +
                    "<ChannelQuaternion Pivot='1' Type='Orientation' " +
                    "FirstFrame='0'>" +
                    "<Frame X='0' Y='0' Z='0' W='1'/>" +
                    "<Frame X='0' Y='0' Z='0.38268343' W='0.92387953'/>" +
                    "<Frame X='0' Y='0' Z='0.70710678' W='0.70710678'/>" +
                    "</ChannelQuaternion>" +
                    "<ChannelScalar Pivot='1' Type='Visibility' " +
                    "FirstFrame='0'><Frame>1</Frame><Frame>0</Frame>" +
                    "<Frame>1</Frame></ChannelScalar>" +
                    "</Channels></W3DAnimation>" +
                    "<UnrelatedDeclaration id='DO_NOT_CHANGE' Foo='ABC'/>" +
                    "</AssetDeclaration>";
                var original = XDocument.Parse(xml,
                    LoadOptions.PreserveWhitespace);
                var snapshot = new XDocument(original);
                var doc = new W3xDocument();
                var hierarchy = new W3xHierarchyData
                {
                    Identifier = "TEST_RIG"
                };
                hierarchy.Pivots.Add(new W3xPivotData
                {
                    Name = "ROOTTRANSFORM", Parent = -1,
                    Rotation = Quaternion.identity
                });
                hierarchy.Pivots.Add(new W3xPivotData
                {
                    Name = "BODY", Parent = 0,
                    Translation = new Vector3(2, 4, 3),
                    Rotation = Quaternion.identity
                });
                hierarchy.Pivots.Add(new W3xPivotData
                {
                    Name = "EYE", Parent = 1,
                    Translation = Vector3.zero,
                    Rotation = Quaternion.identity
                });
                doc.Hierarchies.Add(hierarchy);

                var xmlAnimation = original.Root.Elements()
                    .Single(e => e.Name.LocalName == "W3DAnimation");
                var warnings = doc.Warnings;
                var anim = W3xAnimationReader.Read(xmlAnimation, warnings);
                Assert(anim != null && anim.Channels.Count == 3 &&
                    warnings.Any(w => w.Contains("Visibility")),
                    "Test input did not parse supported/unsupported tracks.");
                doc.Animations.Add(anim);

                root = new GameObject("TEST_MODEL");
                var holder = new GameObject("SAGE_Pivots");
                holder.transform.SetParent(root.transform, false);
                var rootPivot = new GameObject("ROOTTRANSFORM");
                rootPivot.transform.SetParent(holder.transform, false);
                var bodyPivot = new GameObject("BODY");
                bodyPivot.transform.SetParent(rootPivot.transform, false);
                bodyPivot.transform.localPosition = new Vector3(2, 3, 4);
                var eyePivot = new GameObject("EYE");
                eyePivot.transform.SetParent(bodyPivot.transform, false);
                int generated = W3xAnimationClipBuilder.Build(
                    doc, root, hierarchy,
                    new[] { rootPivot.transform, bodyPivot.transform,
                        eyePivot.transform },
                    (name, clip) => { imported = clip; });
                Assert(generated == 1 && imported != null,
                    "Phase 3A failed to construct original fixture clip.");
                edited = W3xPhase3BAnimationWorkspace
                    .CloneForEditing(imported);
                metadata = ScriptableObject.CreateInstance<
                    W3xEditableAnimationSource>();
                metadata.SetSource("fixture-guid", "Assets/Fixture.w3x",
                    "TEST_RUNA", "TEST_RIG", 3, 30f, "model-guid",
                    edited, W3xPhase3BAnimationWorkspace
                        .GetFloatBindingSignatures(imported),
                    new string[0]);

                // No changes: deep equality, including unsupported XML.
                W3xPhase3CAnimationExportCore.Report noChanges;
                var untouched = W3xPhase3CAnimationExportCore.BuildDocument(
                    original, doc, imported, edited, metadata,
                    out noChanges);
                Assert(noChanges.ChangedChannels == 0 &&
                       noChanges.ChangedFrames == 0 &&
                       XNode.DeepEquals(original, untouched),
                    "Unedited clip altered W3X XML.");

                string path = "SAGE_Pivots/ROOTTRANSFORM/BODY";
                Modify(edited, path, "m_LocalPosition.x", 1, 3.25f);
                // Rotation is source +Z, Unity -Y. Edit frame 1 to
                // source +35 degrees around Z (Unity -35 degrees Y).
                var newUnityRotation = Quaternion.Euler(0, -35, 0);
                Modify(edited, path, "m_LocalRotation.x", 1,
                    newUnityRotation.x);
                Modify(edited, path, "m_LocalRotation.y", 1,
                    newUnityRotation.y);
                Modify(edited, path, "m_LocalRotation.z", 1,
                    newUnityRotation.z);
                Modify(edited, path, "m_LocalRotation.w", 1,
                    newUnityRotation.w);

                W3xPhase3CAnimationExportCore.Report report;
                var exported = W3xPhase3CAnimationExportCore.BuildDocument(
                    original, doc, imported, edited, metadata, out report);
                Assert(report.ChangedChannels == 2 &&
                       report.ChangedFrames == 2 &&
                       report.UnsupportedPreserved == 1,
                    "Exporter must patch exactly one scalar frame " +
                    "and one quaternion frame.");
                Assert(XNode.DeepEquals(original, snapshot),
                    "Exporter must not mutate source document.");

                var outChannels = exported.Root.Elements().Single(e =>
                    e.Name.LocalName == "W3DAnimation")
                    .Elements().Single(e => e.Name.LocalName == "Channels")
                    .Elements().ToArray();
                var srcChannels = xmlAnimation.Elements().Single(e =>
                    e.Name.LocalName == "Channels").Elements().ToArray();
                Assert(Mathf.Abs(float.Parse(
                    outChannels[0].Elements().ElementAt(1).Value,
                    System.Globalization.CultureInfo.InvariantCulture)
                    - 1.25f) < .001f,
                    "Unity local position must subtract the rest pose.");
                Assert(XNode.DeepEquals(srcChannels[1], outChannels[1]),
                    "Zero-only source channel must remain byte-equivalent XML.");
                Assert(XNode.DeepEquals(srcChannels[3], outChannels[3]),
                    "Visibility channel must remain completely untouched.");
                Assert(XNode.DeepEquals(original.Root.Elements().Last(),
                    exported.Root.Elements().Last()),
                    "Unrelated W3X declarations must remain unchanged.");
                var quaternionFrame = outChannels[2].Elements().ElementAt(1);
                Assert(Mathf.Abs(float.Parse(
                    (string)quaternionFrame.Attribute("Z"),
                    System.Globalization.CultureInfo.InvariantCulture) -
                    Mathf.Sin(35f * Mathf.Deg2Rad / 2)) < .002f,
                    "Quaternion inverse coordinate mapping is incorrect.");
                Assert((string)exported.Root.Elements().Single(e =>
                    e.Name.LocalName == "W3DAnimation")
                    .Attribute("NumFrames") == "3",
                    "NumFrames must never be rewritten.");
                Assert(XNode.DeepEquals(srcChannels[0].Elements().First(),
                    outChannels[0].Elements().First()) &&
                    XNode.DeepEquals(srcChannels[0].Elements().Last(),
                    outChannels[0].Elements().Last()),
                    "Unedited source frames must remain unchanged.");

                // A brand new EYE position Y is W3X ZTranslation, even
                // though this pivot had no source translation channel.
                string eyePath = path + "/EYE";
                Modify(edited, eyePath, "m_LocalPosition.y", 1, .75f);
                SetQuaternionTrack(edited, eyePath, 30f);
                W3xPhase3CAnimationExportCore.Report expandedReport;
                var expanded = W3xPhase3CAnimationExportCore.BuildDocument(
                    original, doc, imported, edited, metadata,
                    out expandedReport);
                Assert(expandedReport.ChangedChannels == 2 &&
                       expandedReport.AddedChannels == 2 &&
                       expandedReport.AddedFrames == 6 &&
                       expandedReport.UnsupportedPreserved == 1,
                    "Expected two existing changes and two new EYE " +
                    "translation/quaternion channels.");
                var expandedChannels = expanded.Root.Elements()
                    .Single(e => e.Name.LocalName == "W3DAnimation")
                    .Elements().Single(e => e.Name.LocalName == "Channels")
                    .Elements().ToArray();
                Assert(expandedChannels.Length == srcChannels.Length + 2,
                    "New channel count must be two.");
                for (int i = 0; i < srcChannels.Length; i++)
                    Assert(XNode.DeepEquals(outChannels[i],
                        expandedChannels[i]),
                        "Appending a channel changed an unrelated XML node.");
                var newPos = expandedChannels[4];
                var newRot = expandedChannels[5];
                Assert((string)newPos.Attribute("Pivot") == "2" &&
                       (string)newPos.Attribute("Type") == "ZTranslation" &&
                       (string)newPos.Attribute("FirstFrame") == "0" &&
                       newPos.Elements().Count() == 3,
                    "New Unity Y must create SAGE pivot2 ZTranslation.");
                Assert(Mathf.Abs(float.Parse(
                    newPos.Elements().ElementAt(1).Value,
                    System.Globalization.CultureInfo.InvariantCulture) -
                    .75f) < .001f,
                    "New EYE translation channel reverse conversion failed.");
                Assert(newRot.Name.LocalName == "ChannelQuaternion" &&
                       (string)newRot.Attribute("Pivot") == "2" &&
                       (string)newRot.Attribute("Type") == "Orientation" &&
                       newRot.Elements().Count() == 3,
                    "New EYE rotation must emit ChannelQuaternion.");
                var frame1 = newRot.Elements().ElementAt(1);
                Assert(Mathf.Abs(float.Parse(
                    (string)frame1.Attribute("Z"),
                    System.Globalization.CultureInfo.InvariantCulture) -
                    Mathf.Sin(15f * Mathf.Deg2Rad)) < .002f,
                    "New quaternion inverse handedness is incorrect.");
                Assert(XNode.DeepEquals(original, snapshot),
                    "Adding W3X channels mutated the original source XML.");
                var expandedAnim = expanded.Root.Elements().Single(
                    e => e.Name.LocalName == "W3DAnimation");
                var newWarnings = new System.Collections.Generic.List<string>();
                var redecoded = W3xAnimationReader.Read(expandedAnim,
                    newWarnings);
                Assert(redecoded != null && redecoded.Channels.Count == 5,
                    "Exporter-created translation/quaternion channels " +
                    "must be accepted by the real W3X animation reader.");

                // Reject unsupported Unity Transform properties and
                // paths that do not belong to the original SAGE rig.
                Modify(edited, eyePath, "m_LocalScale.y", 1, 2f);
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "New scale tracks must be rejected.");
                Remove(edited, eyePath, "m_LocalScale.y");

                Modify(edited, eyePath + "/UNKNOWN",
                    "m_LocalPosition.x", 1, 1f);
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "New pivot paths not in source rig must be rejected.");
                Remove(edited, eyePath + "/UNKNOWN", "m_LocalPosition.x");

                // Incomplete quaternion channels must fail rather
                // than creating malformed SAGE rotations.
                Remove(edited, eyePath, "m_LocalRotation.w");
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "Incomplete new quaternion must be rejected.");
                SetQuaternionTrack(edited, eyePath, 30f);

                // Unity's Animation window can author Euler curves.
                // Three matching localEulerAnglesRaw axes should also
                // be exportable as one W3X Orientation channel.
                foreach (string axis in new[] { "x", "y", "z", "w" })
                    Remove(edited, eyePath, "m_LocalRotation." + axis);
                SetEulerTrack(edited, eyePath, 25f);
                W3xPhase3CAnimationExportCore.Report eulerReport;
                var eulerW3x = W3xPhase3CAnimationExportCore.BuildDocument(
                    original, doc, imported, edited, metadata,
                    out eulerReport);
                Assert(eulerReport.AddedChannels == 2,
                    "Three local Euler channels must yield one " +
                    "new SAGE quaternion plus EYE position.");
                var eulerChannel = eulerW3x.Root.Elements().Single(e =>
                    e.Name.LocalName == "W3DAnimation")
                    .Elements().Single(e => e.Name.LocalName == "Channels")
                    .Elements().Last();
                Assert((string)eulerChannel.Attribute("Type") ==
                       "Orientation" &&
                       Mathf.Abs(float.Parse((string)eulerChannel
                           .Elements().ElementAt(1).Attribute("Z"),
                           System.Globalization.CultureInfo
                               .InvariantCulture) -
                           Mathf.Sin(12.5f * Mathf.Deg2Rad)) < .003f,
                    "Unity Euler rotation did not map to SAGE Z axis.");
                foreach (string axis in new[] { "x", "y", "z" })
                    Remove(edited, eyePath, "localEulerAnglesRaw." + axis);

                // Even when user adds a position curve, if it equals
                // the source rest pose in every frame it has no effect
                // and must not invent a zero-only channel.
                Remove(edited, eyePath, "m_LocalPosition.y");
                AnimationUtility.SetEditorCurve(edited,
                    EditorCurveBinding.FloatCurve(eyePath,
                        typeof(Transform), "m_LocalPosition.y"),
                    new AnimationCurve(
                        new Keyframe(0f, 0f),
                        new Keyframe(2f / 30f, 0f)));
                W3xPhase3CAnimationExportCore.Report noopReport;
                var noop = W3xPhase3CAnimationExportCore.BuildDocument(
                    original, doc, imported, edited, metadata,
                    out noopReport);
                Assert(noopReport.AddedChannels == 0 &&
                       noopReport.ChangedChannels == 2 &&
                       XNode.DeepEquals(noop, exported),
                    "Constant rest-pose position must not create XML.");
                edited.frameRate = 60f;
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "Changed source FPS must be rejected.");

                // A stale/replaced W3X imported source clip must be
                // rejected even when the binding signatures still match.
                edited.frameRate = 30f;
                Modify(imported, path, "m_LocalPosition.x", 1, 20f);
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "Mismatched source XML vs imported baseline must fail.");

                Debug.Log("[C&C SAGE Phase 3C] Animation export self-test " +
                    "PASS: zero-change byte-safe XML, changed X-axis/rest " +
                    "delta and quaternion frames, immutable unsupported " +
                    "visibility/metadata, new EYE translation/quaternion " +
                    "channels, Euler rotation, invalid pivot/scale, " +
                    "no-op, FPS and source-drift safety gates.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3C] Animation export self-test FAILED: " +
                    e);
            }
            finally
            {
                if (metadata != null)
                    UnityEngine.Object.DestroyImmediate(metadata);
                if (edited != null)
                    UnityEngine.Object.DestroyImmediate(edited);
                if (imported != null)
                    UnityEngine.Object.DestroyImmediate(imported);
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Modify(AnimationClip clip, string path,
            string prop, int frame, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(
                path, typeof(Transform), prop);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null)
            {
                curve = new AnimationCurve(
                    new Keyframe(0, 0), new Keyframe(1f / 30f, 0),
                    new Keyframe(2f / 30f, 0));
            }
            var keys = curve.keys;
            keys[frame].value = value;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void Remove(AnimationClip clip, string path,
            string property)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform),
                    property), null);
        }

        private static void SetQuaternionTrack(AnimationClip clip,
            string path, float sourceZDegrees)
        {
            var angle = Quaternion.Euler(0f, -sourceZDegrees, 0f);
            var samples = new[]
            {
                Quaternion.identity, angle, Quaternion.identity
            };
            var components = new[] { "x", "y", "z", "w" };
            for (int i = 0; i < components.Length; i++)
            {
                var values = new Keyframe[3];
                for (int frame = 0; frame < 3; frame++)
                {
                    var q = samples[frame];
                    float value = i == 0 ? q.x :
                        i == 1 ? q.y : i == 2 ? q.z : q.w;
                    values[frame] = new Keyframe(frame / 30f, value);
                }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path,
                        typeof(Transform),
                        "m_LocalRotation." + components[i]),
                    new AnimationCurve(values));
            }
        }

        private static void SetEulerTrack(AnimationClip clip,
            string path, float sourceZDegrees)
        {
            foreach (var axis in new[] { "x", "y", "z" })
            {
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path,
                        typeof(Transform), "localEulerAnglesRaw." + axis),
                    new AnimationCurve(
                        new Keyframe(0f, 0f),
                        new Keyframe(1f / 30f,
                            axis == "y" ? -sourceZDegrees : 0f),
                        new Keyframe(2f / 30f, 0f)));
            }
        }

        private static bool Throws(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return true; }
            return false;
        }

        private static void Assert(bool ok, string message)
        {
            if (!ok)
                throw new InvalidDataException(message);
        }
    }
}
