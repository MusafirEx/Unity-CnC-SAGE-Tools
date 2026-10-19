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
                    "</Pivot></W3DHierarchy>" +
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
                int generated = W3xAnimationClipBuilder.Build(
                    doc, root, hierarchy,
                    new[] { rootPivot.transform, bodyPivot.transform },
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

                // Unsupported new curves are a hard error (no silent
                // dropping, no invented new W3X channel declarations).
                Modify(edited, path, "m_LocalPosition.y", 1, 5f);
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "New unbacked position channels must be rejected.");
                AnimationUtility.SetEditorCurve(edited,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        "m_LocalPosition.y"), null);

                edited.frameRate = 60f;
                Assert(Throws(() =>
                {
                    W3xPhase3CAnimationExportCore.Report ignored;
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        original, doc, imported, edited, metadata, out ignored);
                }), "Changed source FPS must be rejected.");

                Debug.Log("[C&C SAGE Phase 3C] Animation export self-test " +
                    "PASS: zero-change byte-safe XML, changed X-axis/rest " +
                    "delta and quaternion frames, immutable unsupported " +
                    "visibility/metadata, FPS preservation and safety gates.");
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
