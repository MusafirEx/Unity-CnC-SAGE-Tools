using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// In-memory Phase 3A checks. No assets or scene objects are saved.
    /// Does not claim compatibility with extracted game animations.
    /// </summary>
    internal static class W3xPhase3AAnimationSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 3A Animation Import Self-Test")]
        private static void Run()
        {
            GameObject root = null;
            var imported = new List<AnimationClip>();
            try
            {
                var document = XDocument.Parse(
                    "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                    "<W3DAnimation id='TEST_IDLE' Hierarchy='TEST_RIG' " +
                    "NumFrames='3' FrameRate='30'><Channels>" +
                    "<ChannelScalar Pivot='0' Type='XTranslation' " +
                    "FirstFrame='0'><Frame>0</Frame><Frame>0.5</Frame>" +
                    "<Frame>1</Frame></ChannelScalar>" +
                    "<ChannelScalar Pivot='0' Type='YTranslation' " +
                    "FirstFrame='1'><Frame>0</Frame><Frame>3</Frame>" +
                    "</ChannelScalar>" +
                    "<ChannelScalar Pivot='0' Type='Visibility' " +
                    "FirstFrame='0'><Frame>1</Frame></ChannelScalar>" +
                    "<ChannelQuaternion Pivot='0' Type='Orientation' " +
                    "FirstFrame='0'>" +
                    "<Frame X='0' Y='0' Z='0' W='1'/>" +
                    "<Frame X='0' Y='0' Z='0.7071068' W='0.7071068'/>" +
                    "<Frame X='0' Y='0' Z='0.7071068' W='0.7071068'/>" +
                    "</ChannelQuaternion></Channels></W3DAnimation>" +
                    "</AssetDeclaration>");

                var data = new W3xDocument();
                var warnings = data.Warnings;
                var a = W3xAnimationReader.Read(
                    document.Root.Elements().First(), warnings);
                Assert(a != null && a.Channels.Count == 3,
                    "Valid scalar and quaternion channels should parse.");
                Assert(warnings.Any(w => w.Contains("Visibility")),
                    "Visibility channel must be explicitly skipped.");
                data.Animations.Add(a);
                var hierarchy = new W3xHierarchyData
                {
                    Identifier = "TEST_RIG"
                };
                hierarchy.Pivots.Add(new W3xPivotData
                {
                    Name = "ROOT",
                    Parent = -1,
                    Translation = new Vector3(2, 3, 4),
                    Rotation = Quaternion.identity
                });
                data.Hierarchies.Add(hierarchy);
                root = new GameObject("W3X");
                var holder = new GameObject("SAGE_Pivots");
                holder.transform.SetParent(root.transform, false);
                var node = new GameObject("ROOT");
                node.transform.SetParent(holder.transform, false);
                node.transform.localPosition = new Vector3(2, 4, 3);
                var count = W3xAnimationClipBuilder.Build(data, root,
                    hierarchy, new[] { node.transform }, (name, clip) =>
                    {
                        imported.Add(clip);
                    });
                Assert(count == 1 && imported.Count == 1,
                    "Exactly one valid clip must be created.");
                var output = imported[0];
                Assert(Mathf.Abs(output.frameRate - 30f) < .001f,
                    "W3X frame rate must be retained.");
                Assert(Mathf.Abs(output.length - 2f / 30f) < .001f,
                    "Frames must map to frameIndex/frameRate.");
                string path = "SAGE_Pivots/ROOT";
                Assert(Near(output, path, "m_LocalPosition.x",
                    2f / 30f, 3f),
                    "XTranslation plus source rest position wrong.");
                Assert(Near(output, path, "m_LocalPosition.y",
                    2f / 30f, 4f),
                    "Source Z-to-Unity Y axis conversion wrong.");
                Assert(Near(output, path, "m_LocalPosition.z",
                    2f / 30f, 6f),
                    "YTranslation plus rest pose / Z axis wrong.");
                Assert(Near(output, path, "m_LocalRotation.y",
                    1f / 30f, -0.7071068f),
                    "Quaternion coordinate conversion wrong.");

                // Clip curve paths must resolve against the imported SAGE
                // model's root. The artificial holder is not a source pivot.
                Assert(W3xAnimationBindingAudit.CountMissingTransformPaths(
                    output, root.transform) == 0,
                    "Generated clip has unresolved source pivot bindings.");
                Assert(AnimationUtility.GetCurveBindings(output)
                    .All(binding => binding.path != "SAGE_Pivots"),
                    "The generated SAGE_Pivots holder must not be animated.");
                var wrongRoot = new GameObject("WRONG_RIG");
                wrongRoot.transform.SetParent(root.transform, false);
                Assert(W3xAnimationBindingAudit.CountMissingTransformPaths(
                    output, wrongRoot.transform) > 0,
                    "Binding validator must detect missing target pivots.");

                // Real W3X authoring tools label quaternion channels
                // Type="Orientation". Ensure animation clips can also be
                // generated without any translation channels.
                var rotationOnly = XDocument.Parse(
                    "<W3DAnimation id='ROTATION_ONLY' Hierarchy='TEST_RIG' " +
                    "NumFrames='2' FrameRate='30'><Channels>" +
                    "<ChannelQuaternion Pivot='0' Type='Orientation' " +
                    "FirstFrame='0'><Frame X='0' Y='0' Z='0' W='1'/>" +
                    "<Frame X='0' Y='0' Z='0.7071068' W='0.7071068'/>" +
                    "</ChannelQuaternion></Channels></W3DAnimation>");
                var rotationData = W3xAnimationReader.Read(
                    rotationOnly.Root, warnings);
                Assert(rotationData != null &&
                    rotationData.Channels.Count == 1 &&
                    rotationData.Channels[0].Kind == "Quaternion",
                    "Orientation-only source must parse as quaternion.");
                var rotationDocument = new W3xDocument();
                rotationDocument.Animations.Add(rotationData);
                int rotationCount = W3xAnimationClipBuilder.Build(
                    rotationDocument, root, hierarchy,
                    new[] { node.transform }, (name, clip) =>
                    {
                        imported.Add(clip);
                    });
                Assert(rotationCount == 1 && imported.Count == 2 &&
                    Near(imported[1], path, "m_LocalRotation.y",
                        1f / 30f, -0.7071068f),
                    "Orientation-only source must generate valid rotation clip.");

                // Safety: must not create clips for a mismatched hierarchy.
                int missing = W3xAnimationClipBuilder.Build(data, root,
                    new W3xHierarchyData { Identifier = "WRONG" },
                    new[] { node.transform }, (name, clip) =>
                    {
                        imported.Add(clip);
                    });
                Assert(missing == 0 && imported.Count == 2,
                    "Mismatched hierarchy must not create invalid clips.");

                // End-to-end XML reader companion test: standalone _IDLA-
                // style animation resolves its explicitly named _SKL sibling.
                string folder = Path.Combine(Path.GetTempPath(),
                    "CncSagePhase3A_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(folder);
                    string animationPath = Path.Combine(
                        folder, "TEST_IDLE_IDLA.W3X");
                    string hierarchyPath = Path.Combine(
                        folder, "TEST_RIG_SKL.W3X");
                    File.WriteAllText(animationPath,
                        "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                        "<W3DAnimation id='TEST_IDLE' Hierarchy='TEST_RIG' " +
                        "NumFrames='2' FrameRate='30'><Channels>" +
                        "<ChannelScalar Pivot='0' Type='XTranslation' " +
                        "FirstFrame='0'><Frame>0</Frame><Frame>1</Frame>" +
                        "</ChannelScalar></Channels></W3DAnimation>" +
                        "</AssetDeclaration>");
                    File.WriteAllText(hierarchyPath,
                        "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                        "<W3DHierarchy id='TEST_RIG'>" +
                        "<Pivot Name='ROOT' NameID='0' Parent='-1'/>" +
                        "</W3DHierarchy></AssetDeclaration>");
                    var roundTrip = W3xReader.Read(animationPath);
                    Assert(roundTrip.Animations.Count == 1 &&
                        roundTrip.Hierarchies.Count == 1 &&
                        roundTrip.SourcePaths.Count == 2 &&
                        roundTrip.Hierarchies[0].Pivots.Count == 1,
                        "Animation-only W3X did not resolve matching _SKL.");
                }
                finally
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }

                Debug.Log("[C&C SAGE Phase 3A] Animation import self-test PASS: " +
                    "scalar channels, quaternion, frame rate, rest pose, " +
                    "Y/Z conversion, Orientation-only clips, pivot paths, " +
                    "root binding verification, missing rig, " +
                    "_SKL companion resolution and warnings.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3A] Animation import self-test FAILED: " +
                    e);
            }
            finally
            {
                foreach (var clip in imported)
                    if (clip != null)
                        UnityEngine.Object.DestroyImmediate(clip);
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool Near(AnimationClip clip, string path,
            string property, float time, float expected)
        {
            var binding = EditorCurveBinding.FloatCurve(
                path, typeof(Transform), property);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            return curve != null &&
                Mathf.Abs(curve.Evaluate(time) - expected) < .003f;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }
    }
}
