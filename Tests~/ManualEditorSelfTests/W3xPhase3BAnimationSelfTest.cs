using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// In-memory Phase 3B smoke test: clips are independently editable,
    /// source curves remain untouched, and mismatched pivots are rejected.
    /// No Unity project assets or active scene objects are saved.
    /// </summary>
    internal static class W3xPhase3BAnimationSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 3B Animation Editing Self-Test")]
        private static void Run()
        {
            GameObject model = null;
            AnimationClip source = null;
            AnimationClip editable = null;
            W3xEditableAnimationSource metadata = null;
            try
            {
                model = new GameObject("PHASE3B_MODEL");
                var holder = new GameObject("SAGE_Pivots");
                holder.transform.SetParent(model.transform, false);
                var root = new GameObject("ROOTTRANSFORM");
                root.transform.SetParent(holder.transform, false);
                root.AddComponent<CncSagePivotInfo>()
                    .SetSource(0, 0, -1, "");
                var body = new GameObject("BODY");
                body.transform.SetParent(root.transform, false);
                body.AddComponent<CncSagePivotInfo>()
                    .SetSource(1, 1, 0, "");

                string path = "SAGE_Pivots/ROOTTRANSFORM/BODY";
                var positionBinding = EditorCurveBinding.FloatCurve(
                    path, typeof(Transform), "m_LocalPosition.x");
                var rotationBinding = EditorCurveBinding.FloatCurve(
                    path, typeof(Transform), "m_LocalRotation.z");

                source = new AnimationClip
                {
                    name = "TEST_RUNA",
                    frameRate = 30
                };
                AnimationUtility.SetEditorCurve(source, positionBinding,
                    AnimationCurve.Linear(0f, 0f, 1f / 30f, 0.75f));
                AnimationUtility.SetEditorCurve(source, rotationBinding,
                    AnimationCurve.Linear(0f, 0f, 1f / 30f, 0.5f));
                string[] originalBindings =
                    W3xPhase3BAnimationWorkspace.GetFloatBindingSignatures(
                        source);

                var doc = new W3xDocument();
                var rig = new W3xHierarchyData
                {
                    Identifier = "TEST_RIG"
                };
                rig.Pivots.Add(new W3xPivotData
                {
                    Name = "ROOTTRANSFORM",
                    Parent = -1
                });
                rig.Pivots.Add(new W3xPivotData
                {
                    Name = "BODY",
                    Parent = 0
                });
                doc.Hierarchies.Add(rig);
                var animation = new W3xAnimationData
                {
                    Identifier = source.name,
                    HierarchyName = rig.Identifier,
                    FrameRate = source.frameRate,
                    NumFrames = 2
                };
                animation.Channels.Add(new W3xAnimationChannel
                {
                    PivotIndex = 1,
                    Kind = "XTranslation",
                    FirstFrame = 0,
                    Scalars = new[] { 0f, .75f }
                });
                animation.Channels.Add(new W3xAnimationChannel
                {
                    PivotIndex = 1,
                    Kind = "Quaternion",
                    FirstFrame = 0,
                    Quaternions = new[] {
                        Quaternion.identity, Quaternion.identity }
                });
                doc.Animations.Add(animation);

                Assert(W3xPhase3BAnimationWorkspace.GetCompatibilityError(
                    source, model, doc, animation) == null,
                    "Matching source pivot indices and Transform paths " +
                    "must be accepted.");
                editable =
                    W3xPhase3BAnimationWorkspace.CloneForEditing(source);
                Assert(!ReferenceEquals(source, editable),
                    "Editable clip must not reuse the imported clip instance.");
                Assert(Mathf.Abs(editable.frameRate - 30f) < .001f,
                    "Editable clip must retain source frame rate.");
                Assert(originalBindings.SequenceEqual(
                    W3xPhase3BAnimationWorkspace.GetFloatBindingSignatures(
                        editable)),
                    "Editable clip must preserve all original bindings.");

                // Edit ONLY the cloned local position. The original
                // imported clip must remain unchanged.
                AnimationUtility.SetEditorCurve(editable, positionBinding,
                    AnimationCurve.Linear(0f, 0f, 1f / 30f, 1.25f));
                var newValue = AnimationUtility.GetEditorCurve(editable,
                    positionBinding).Evaluate(1f / 30f);
                var originalValue = AnimationUtility.GetEditorCurve(source,
                    positionBinding).Evaluate(1f / 30f);
                Assert(Mathf.Abs(newValue - 1.25f) < .001f &&
                       Mathf.Abs(originalValue - .75f) < .001f,
                    "Changing editable curves must not alter the original.");
                Assert(W3xAnimationBindingAudit.CountMissingTransformPaths(
                    editable, model.transform) == 0,
                    "Cloned clip must remain bound to its source rig.");

                body.name = "WRONG_BODY";
                Assert(!string.IsNullOrEmpty(
                    W3xPhase3BAnimationWorkspace.GetCompatibilityError(
                        source, model, doc, animation)),
                    "Wrong source pivot name must be rejected.");

                metadata = ScriptableObject.CreateInstance<
                    W3xEditableAnimationSource>();
                metadata.SetSource(
                    "fixture-guid", "Assets/Fixture/TEST_RUNA.w3x",
                    animation.Identifier, animation.HierarchyName,
                    animation.NumFrames, animation.FrameRate, "model-guid",
                    editable, originalBindings,
                    new string[0]);
                Assert(metadata.OriginalAnimationId == source.name &&
                       metadata.SourceW3xGuid == "fixture-guid" &&
                       metadata.OriginalHierarchyId == "TEST_RIG" &&
                       metadata.EditableClip == editable &&
                       metadata.SourceFloatBindings.SequenceEqual(
                           originalBindings),
                    "Source provenance and baseline bindings must be saved.");

                Debug.Log(
                    "[C&C SAGE Phase 3B] Animation editing self-test PASS: " +
                    "independent clip clone, unchanged source curves, " +
                    "preserved clip paths and FPS, valid/mismatched rig " +
                    "detection, and W3X source provenance.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3B] Animation editing self-test FAILED: " +
                    e);
            }
            finally
            {
                if (metadata != null)
                    UnityEngine.Object.DestroyImmediate(metadata);
                if (editable != null)
                    UnityEngine.Object.DestroyImmediate(editable);
                if (source != null)
                    UnityEngine.Object.DestroyImmediate(source);
                if (model != null)
                    UnityEngine.Object.DestroyImmediate(model);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }
    }
}
