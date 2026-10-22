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
    /// Phase 3D in-memory roundtrip: Phase3A → Phase3B edit →
    /// Phase3C export → Phase3A reimport → Phase3D sampled comparison.
    /// No Project files or currently open scene assets are written.
    /// </summary>
    internal static class W3xPhase3DRoundTripSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 3D Round-Trip Self-Test")]
        private static void Run()
        {
            GameObject model = null;
            AnimationClip original = null;
            AnimationClip edited = null;
            AnimationClip regenerated = null;
            W3xEditableAnimationSource metadata = null;
            try
            {
                // Real-data discovery without touching imported assets.
                // Only exports derived from the original W3X basename
                // may be auto-selected. The selected original itself
                // and unrelated model/animation files never qualify.
                Assert(W3xPhase3DRoundTripValidator
                        .IsGeneratedExportName("AURazDro_RUNA",
                            "AURazDro_RUNA_unity_anim") &&
                       W3xPhase3DRoundTripValidator
                        .IsGeneratedExportName("AURazDro_RUNA",
                            "AURazDro_RUNA_unity_anim (phase3D) 1") &&
                       !W3xPhase3DRoundTripValidator
                        .IsGeneratedExportName("AURazDro_RUNA",
                            "AURazDro_RUNA") &&
                       !W3xPhase3DRoundTripValidator
                        .IsGeneratedExportName("AURazDro_RUNA",
                            "AURazDro_RUNB_unity_anim") &&
                       !W3xPhase3DRoundTripValidator
                        .IsGeneratedExportName("AURazDro_RUNA",
                            "AURazDro_RUNA_unity_animation"),
                    "Phase 3D real-export name filter misidentified " +
                    "original/unrelated or generated W3X filenames.");

                const string xml =
                    "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                    "<W3DHierarchy id='ROUND_RIG'>" +
                    "<Pivot Name='ROOTTRANSFORM' NameID='0' Parent='-1'/>" +
                    "<Pivot Name='BODY' NameID='1' Parent='0'>" +
                    "<Translation X='1' Y='2' Z='3'/>" +
                    "<Rotation X='0' Y='0' Z='0' W='1'/>" +
                    "</Pivot>" +
                    "<Pivot Name='EYE' NameID='2' Parent='1'/>" +
                    "</W3DHierarchy>" +
                    "<W3DAnimation id='TEST_RUNA' Hierarchy='ROUND_RIG' " +
                    "NumFrames='3' FrameRate='30'><Channels>" +
                    "<ChannelScalar Pivot='1' Type='XTranslation' " +
                    "FirstFrame='0'><Frame>0</Frame><Frame>1</Frame>" +
                    "<Frame>2</Frame></ChannelScalar>" +
                    "<ChannelScalar Pivot='1' Type='YTranslation' " +
                    "FirstFrame='0'><Frame>0</Frame><Frame>0</Frame>" +
                    "<Frame>0</Frame></ChannelScalar>" +
                    "<ChannelQuaternion Pivot='1' Type='Orientation' " +
                    "FirstFrame='0'><Frame X='0' Y='0' Z='0' W='1'/>" +
                    "<Frame X='0' Y='0' Z='0' W='1'/>" +
                    "<Frame X='0' Y='0' Z='0' W='1'/>" +
                    "</ChannelQuaternion>" +
                    "<ChannelScalar Pivot='1' Type='Visibility' " +
                    "FirstFrame='0'><Frame>1</Frame><Frame>1</Frame>" +
                    "<Frame>0</Frame></ChannelScalar>" +
                    "</Channels></W3DAnimation>" +
                    "<FXShader id='UNCHANGED'/>" +
                    "</AssetDeclaration>";
                var source = XDocument.Parse(xml,
                    LoadOptions.PreserveWhitespace);
                var rig = new W3xHierarchyData { Identifier = "ROUND_RIG" };
                rig.Pivots.Add(new W3xPivotData
                {
                    Name = "ROOTTRANSFORM", Parent = -1,
                    Rotation = Quaternion.identity
                });
                rig.Pivots.Add(new W3xPivotData
                {
                    Name = "BODY", Parent = 0,
                    Translation = new Vector3(1, 2, 3),
                    Rotation = Quaternion.identity
                });
                rig.Pivots.Add(new W3xPivotData
                {
                    Name = "EYE", Parent = 1,
                    Translation = Vector3.zero,
                    Rotation = Quaternion.identity
                });
                var doc = new W3xDocument();
                doc.Hierarchies.Add(rig);
                var originalNode = source.Root.Elements()
                    .Single(x => x.Name.LocalName == "W3DAnimation");
                var warnings = new List<string>();
                var anim = W3xAnimationReader.Read(
                    originalNode, warnings);
                Assert(anim != null && anim.Channels.Count == 3,
                    "Synthetic source animation parse failed.");
                doc.Animations.Add(anim);
                model = new GameObject("PHASE_3D_TEST_MODEL");
                var holder = new GameObject("SAGE_Pivots");
                holder.transform.SetParent(model.transform, false);
                var root = new GameObject("ROOTTRANSFORM");
                root.transform.SetParent(holder.transform, false);
                var body = new GameObject("BODY");
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(1, 3, 2);
                var eye = new GameObject("EYE");
                eye.transform.SetParent(body.transform, false);
                var pivotTransforms = new[]
                {
                    root.transform, body.transform, eye.transform
                };
                int firstPass = W3xAnimationClipBuilder.Build(
                    doc, model, rig, pivotTransforms,
                    (name, clip) => { original = clip; });
                Assert(firstPass == 1 && original != null,
                    "Phase 3A failed to create original test clip.");
                edited = W3xPhase3BAnimationWorkspace
                    .CloneForEditing(original);
                metadata = ScriptableObject.CreateInstance<
                    W3xEditableAnimationSource>();
                metadata.SetSource("test-guid", "Assets/Sample.w3x",
                    "TEST_RUNA", "ROUND_RIG", 3, 30,
                    "model-guid", edited,
                    W3xPhase3BAnimationWorkspace
                        .GetFloatBindingSignatures(original),
                    new string[0]);

                // Change one existing BODY X key, then add the exact
                // class of EYE Unity Y / W3X ZTranslation encountered
                // in the real RazorDrones export.
                const string bodyPath =
                    "SAGE_Pivots/ROOTTRANSFORM/BODY";
                const string eyePath =
                    "SAGE_Pivots/ROOTTRANSFORM/BODY/EYE";
                var bodyBinding = EditorCurveBinding.FloatCurve(
                    bodyPath, typeof(Transform), "m_LocalPosition.x");
                var modified = AnimationUtility.GetEditorCurve(
                    edited, bodyBinding);
                Assert(modified != null, "BODY X curve is missing.");
                var keys = modified.keys;
                keys[1].value += 0.35f;
                modified.keys = keys;
                AnimationUtility.SetEditorCurve(
                    edited, bodyBinding, modified);
                var eyeBinding = EditorCurveBinding.FloatCurve(
                    eyePath, typeof(Transform), "m_LocalPosition.y");
                AnimationUtility.SetEditorCurve(edited, eyeBinding,
                    new AnimationCurve(
                        new Keyframe(0, 0),
                        new Keyframe(1f / 30f, 0.75f),
                        new Keyframe(2f / 30f, 0)));

                W3xPhase3CAnimationExportCore.Report exportReport;
                var exported = W3xPhase3CAnimationExportCore.BuildDocument(
                    source, doc, original, edited, metadata,
                    out exportReport);
                Assert(exportReport.AddedChannels == 1 &&
                       exportReport.ChangedChannels == 1,
                    "Expected exactly one new EYE channel and one " +
                    "modified existing BODY XTranslation channel; " +
                    "zero-only YTranslation must stay unchanged. " +
                    "Actual new channels=" + exportReport.AddedChannels +
                    ", changed channels=" + exportReport.ChangedChannels +
                    ", changed frames=" + exportReport.ChangedFrames +
                    ". Details:\n" + exportReport.Describe());

                var roundtripNode = exported.Root.Elements()
                    .Single(x => x.Name.LocalName == "W3DAnimation");
                var roundtrip = W3xAnimationReader.Read(roundtripNode,
                    new List<string>());
                Assert(roundtrip != null && roundtrip.Channels.Count == 4,
                    "Phase 3A cannot decode the exported new channel.");
                var docAfter = new W3xDocument();
                docAfter.Hierarchies.Add(rig);
                docAfter.Animations.Add(roundtrip);
                int lastPass = W3xAnimationClipBuilder.Build(
                    docAfter, model, rig, pivotTransforms,
                    (name, clip) => { regenerated = clip; });
                Assert(lastPass == 1 && regenerated != null,
                    "Phase 3A reimport failed after export.");

                var report = W3xPhase3DRoundTripValidator.Compare(
                    source, exported, doc, edited, regenerated, metadata);
                Assert(report.Passed && report.AddedChannels == 1 &&
                       report.ComparedPositions > 0 &&
                       report.ComparedRotations > 0,
                    "Roundtrip sample or XML comparison failed:\n" +
                    report.Describe());

                // A changed reimported value must be caught, even if
                // the W3X XML nodes passed structural verification.
                var regeneratedX = AnimationUtility.GetEditorCurve(
                    regenerated, bodyBinding);
                Assert(regeneratedX != null,
                    "Reimported BODY X curve missing.");
                var wrong = regeneratedX.keys;
                wrong[1].value += 0.05f;
                regeneratedX.keys = wrong;
                AnimationUtility.SetEditorCurve(
                    regenerated, bodyBinding, regeneratedX);
                var drift = W3xPhase3DRoundTripValidator.Compare(
                    source, exported, doc, edited, regenerated, metadata);
                Assert(!drift.Passed && drift.MaximumPositionError > .01f,
                    "Deliberate position drift was not detected.");

                // Unknown XML declaration modification must be rejected
                // even when animation values are unchanged.
                var damaged = new XDocument(exported);
                damaged.Root.Elements().Last()
                    .SetAttributeValue("id", "CHANGED");
                var xmlDrift = W3xPhase3DRoundTripValidator.Compare(
                    source, damaged, doc, edited, regenerated, metadata);
                Assert(!xmlDrift.Passed && xmlDrift.Messages.Any(m =>
                    m.Contains("Unrelated W3X XML")),
                    "Unexpected XML modification went undetected.");

                Debug.Log(
                    "[C&C SAGE Phase 3D] Round-trip self-test PASS: " +
                    "source import, independent edit, source-backed export, " +
                    "new EYE ZTranslation, reimported frame samples, " +
                    "zero-only translation preservation, intentional " +
                    "drift detection, real-export discovery filter, " +
                    "and unknown XML preservation.");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3D] Round-trip self-test FAILED: " +
                    ex);
            }
            finally
            {
                if (metadata != null)
                    UnityEngine.Object.DestroyImmediate(metadata);
                if (regenerated != null)
                    UnityEngine.Object.DestroyImmediate(regenerated);
                if (edited != null)
                    UnityEngine.Object.DestroyImmediate(edited);
                if (original != null)
                    UnityEngine.Object.DestroyImmediate(original);
                if (model != null)
                    UnityEngine.Object.DestroyImmediate(model);
            }
        }

        private static void Assert(bool ok, string message)
        {
            if (!ok)
                throw new InvalidDataException(message);
        }
    }
}
