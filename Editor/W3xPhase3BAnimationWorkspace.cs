using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3B: create independent editable .anim clips and a disposable
    /// preview prefab/controller without writing to source W3X or model.
    /// Animation export is available through the separate Phase 3C menu.
    /// </summary>
    internal static class W3xPhase3BAnimationWorkspace
    {
        private const string Menu =
            "Tools/C&C SAGE/Create Phase 3B Editable Animation Workspace";
        private const string OutputFolder =
            "Assets/CncSageEditable/Animations";
        private const int MaximumSourceClips = 32;

        private sealed class Source
        {
            internal AnimationClip Clip;
            internal string Path;
            internal W3xAnimationData Animation;
            internal W3xDocument Document;
        }

        [MenuItem(Menu)]
        private static void Create()
        {
            string reason;
            GameObject model;
            string modelPath;
            List<Source> sources;
            if (!TryGetSelection(out model, out modelPath,
                    out sources, out reason))
            {
                EditorUtility.DisplayDialog("C&C SAGE Phase 3B",
                    reason, "OK");
                return;
            }

            int totalCurves = sources.Sum(s =>
                AnimationUtility.GetCurveBindings(s.Clip).Length);
            if (!EditorUtility.DisplayDialog(
                "C&C SAGE Phase 3B Animation Workspace",
                "Create an independent AnimationClip copy for each of " +
                sources.Count + " selected W3X animation(s), a preview " +
                "Animator Controller and a separate prefab with the " +
                "same pivot hierarchy?\n\n" +
                "Model: " + model.name + "\n" +
                "Source curves: " + totalCurves + "\n\n" +
                "Original W3X XML and existing prefab assets will not " +
                "be modified. Edited .anim files can be exported using " +
                "Tools > C&C SAGE > Export Animation to W3X. " +
                "A new W3X XML copy is created beside its source.",
                "Create Workspace", "Cancel"))
                return;

            string workspace = null;
            GameObject preview = null;
            Scene previewScene = default(Scene);
            bool sceneCreated = false;
            bool createdSuccessfully = false;
            try
            {
                EnsureFolder();
                string desired = OutputFolder + "/" +
                    SafeFileName(model.name) + "_Phase3B";
                string unique = AssetDatabase.GenerateUniqueAssetPath(
                    desired);
                string guid = AssetDatabase.CreateFolder(OutputFolder,
                    Path.GetFileName(unique));
                if (string.IsNullOrEmpty(guid))
                    throw new IOException(
                        "Unity failed to create animation workspace folder.");
                workspace = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(workspace))
                    throw new IOException(
                        "Cannot resolve the new animation workspace.");

                string modelGuid = AssetDatabase.AssetPathToGUID(modelPath);
                var editable = new List<AnimationClip>();
                foreach (Source source in sources)
                {
                    var clone = CloneForEditing(source.Clip);
                    string name = SafeFileName(source.Clip.name);
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        workspace + "/" + name + "_Editable.anim");
                    AssetDatabase.CreateAsset(clone, assetPath);
                    editable.Add(clone);

                    var metadata = ScriptableObject.CreateInstance<
                        W3xEditableAnimationSource>();
                    metadata.name = name + "_Source";
                    metadata.SetSource(
                        AssetDatabase.AssetPathToGUID(source.Path),
                        source.Path,
                        source.Animation.Identifier,
                        source.Animation.HierarchyName,
                        source.Animation.NumFrames,
                        source.Animation.FrameRate,
                        modelGuid,
                        clone,
                        GetFloatBindingSignatures(source.Clip),
                        GetObjectBindingSignatures(source.Clip));
                    string metadataPath = AssetDatabase.GenerateUniqueAssetPath(
                        workspace + "/" + name + "_Source.asset");
                    AssetDatabase.CreateAsset(metadata, metadataPath);
                }

                string controllerPath = AssetDatabase.GenerateUniqueAssetPath(
                    workspace + "/" + SafeFileName(model.name) +
                    "_Preview.controller");
                var controller = AnimatorController
                    .CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null || controller.layers.Length == 0)
                    throw new InvalidDataException(
                        "Cannot initialize preview Animator Controller.");
                AnimatorState first = null;
                foreach (AnimationClip clip in editable)
                {
                    AnimatorState state = controller.AddMotion(clip);
                    state.name = clip.name;
                    if (first == null)
                        first = state;
                }
                if (first != null)
                    controller.layers[0].stateMachine.defaultState = first;

                // We use a preview scene to avoid touching the currently
                // opened RTS scene or the original imported model asset.
                previewScene = EditorSceneManager.NewPreviewScene();
                sceneCreated = true;
                preview = PrefabUtility.InstantiatePrefab(model,
                    previewScene) as GameObject;
                if (preview == null)
                    throw new IOException(
                        "Could not instantiate the selected model for preview.");
                if (PrefabUtility.IsAnyPrefabInstanceRoot(preview))
                    PrefabUtility.UnpackPrefabInstance(preview,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                preview.name = model.name + "_AnimationPreview";

                Animator animator = preview.GetComponent<Animator>();
                if (animator == null)
                    animator = preview.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                    workspace + "/" + SafeFileName(preview.name) + ".prefab");
                bool saved;
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    preview, prefabPath, out saved);
                if (!saved || prefab == null)
                    throw new IOException(
                        "Unable to save the Phase 3B preview prefab.");

                AssetDatabase.SaveAssets();
                var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(
                    prefabPath);
                if (loaded == null ||
                    loaded.GetComponent<Animator>() == null ||
                    loaded.GetComponent<Animator>()
                        .runtimeAnimatorController == null)
                    throw new InvalidDataException(
                        "Saved preview prefab lost the Animator Controller.");

                Selection.activeObject = loaded;
                EditorGUIUtility.PingObject(loaded);
                createdSuccessfully = true;
                Debug.Log("[C&C SAGE Phase 3B] Workspace CREATED: " +
                    workspace + " | editable clips=" + editable.Count +
                    " | Animator Controller=" + controllerPath +
                    " | preview prefab=" + prefabPath +
                    ". Source W3X and model were not modified. Open the " +
                    "preview prefab in Prefab Mode and Window > Animation > " +
                    "Animation to edit the independent clips.");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3B] Workspace creation failed: " + ex);
            }
            finally
            {
                // Release all temporary references BEFORE deleting any
                // partially created controller, clips or preview prefab.
                if (preview != null)
                    UnityEngine.Object.DestroyImmediate(preview);
                if (sceneCreated)
                    EditorSceneManager.ClosePreviewScene(previewScene);
                if (!createdSuccessfully &&
                    !string.IsNullOrEmpty(workspace) &&
                    AssetDatabase.IsValidFolder(workspace))
                {
                    // This unique directory is created exclusively by
                    // this invocation. No pre-existing assets are deleted.
                    AssetDatabase.DeleteAsset(workspace);
                    Debug.LogWarning(
                        "[C&C SAGE Phase 3B] Incomplete workspace removed.");
                }
            }
        }

        /// <summary>
        /// Copy *all* Unity AnimationClip serialization, not just known
        /// Transform curves. Changes to clone never write to the source.
        /// </summary>
        internal static AnimationClip CloneForEditing(AnimationClip original)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            var clone = UnityEngine.Object.Instantiate(original);
            clone.name = original.name + "_Editable";
            clone.hideFlags = HideFlags.None;
            // AnimatorController playback requires a non-legacy clip.
            clone.legacy = false;
            return clone;
        }

        internal static string[] GetFloatBindingSignatures(
            AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip).Select(b =>
                b.path + "\n" +
                (b.type == null ? "" : b.type.FullName) + "\n" +
                b.propertyName).OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetObjectBindingSignatures(
            AnimationClip clip)
        {
            return AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .Select(b => b.path + "\n" +
                    (b.type == null ? "" : b.type.FullName) + "\n" +
                    b.propertyName)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        internal static string GetCompatibilityError(
            AnimationClip clip, GameObject target,
            W3xDocument document, W3xAnimationData animation)
        {
            if (clip == null || target == null || document == null ||
                animation == null)
                return "Missing source animation or selected model.";
            var hierarchy = document.Hierarchies.FirstOrDefault(h =>
                string.Equals(h.Identifier, animation.HierarchyName,
                    StringComparison.OrdinalIgnoreCase));
            if (hierarchy == null)
                return "No source SAGE hierarchy named '" +
                    animation.HierarchyName + "'.";
            var targetPivots =
                target.GetComponentsInChildren<CncSagePivotInfo>(true);
            if (targetPivots.Length == 0)
                return "Model has no CncSagePivotInfo pivots.";
            if (targetPivots.GroupBy(p => p.PivotIndex).Any(g =>
                g.Count() > 1))
                return "Model contains duplicate SAGE pivot indices; " +
                    "resolve its hierarchy before creating a workspace.";
            var pivots = targetPivots.ToDictionary(p => p.PivotIndex, p => p);
            foreach (var idx in animation.Channels.Select(c => c.PivotIndex)
                .Distinct())
            {
                if (idx < 0 || idx >= hierarchy.Pivots.Count)
                    return "Source animation pivot index " + idx +
                        " is outside the original hierarchy.";
                CncSagePivotInfo targetPivot;
                if (!pivots.TryGetValue(idx, out targetPivot))
                    return "Target model lacks original pivot index " + idx;
                string expected = hierarchy.Pivots[idx].Name;
                if (!string.Equals(targetPivot.name, expected,
                    StringComparison.OrdinalIgnoreCase))
                    return "Pivot index " + idx + " name differs: source '" +
                        expected + "', target '" + targetPivot.name + "'.";
            }
            var curves = AnimationUtility.GetCurveBindings(clip);
            if (curves.Length == 0)
                return "Imported animation has no float curves to edit.";
            int missing = W3xAnimationBindingAudit.CountMissingTransformPaths(
                clip, target.transform);
            if (missing > 0)
                return missing + " animation curve(s) do not resolve " +
                    "against the selected prefab's Transform paths.";
            return null;
        }

        private static bool TryGetSelection(out GameObject model,
            out string modelPath, out List<Source> selectedSources,
            out string reason)
        {
            model = null;
            modelPath = null;
            selectedSources = new List<Source>();
            reason = null;

            var selected = Selection.objects ?? new UnityEngine.Object[0];
            var candidates = new List<GameObject>();
            var clips = new HashSet<AnimationClip>();
            var w3xPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in selected)
            {
                if (entry == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(entry);
                if (string.IsNullOrEmpty(path))
                    continue;
                var obj = entry as GameObject;
                if (obj != null &&
                    (string.Equals(Path.GetExtension(path), ".w3x",
                        StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Path.GetExtension(path), ".prefab",
                        StringComparison.OrdinalIgnoreCase)) &&
                    obj.GetComponentsInChildren<CncSagePivotInfo>(true)
                        .Length > 0 &&
                    obj.GetComponentsInChildren<Renderer>(true).Length > 0)
                    candidates.Add(obj);
                if (string.Equals(Path.GetExtension(path), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
                    w3xPaths.Add(path);
                var clip = entry as AnimationClip;
                if (clip != null)
                    clips.Add(clip);
            }

            candidates = candidates.Distinct().ToList();
            if (candidates.Count != 1)
            {
                reason = "Select exactly one SAGE model prefab (or imported " +
                    "W3X model) that contains meshes and SAGE pivots, " +
                    "together with 1–32 original W3X animation clips " +
                    "or animation W3X assets in the Project window. " +
                    "Do not select an old standalone .anim as the source.";
                return false;
            }

            model = candidates[0];
            modelPath = AssetDatabase.GetAssetPath(model);
            foreach (string path in w3xPaths)
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>())
                    clips.Add(clip);

            if (clips.Count == 0 || clips.Count > MaximumSourceClips)
            {
                reason = "Select 1–" + MaximumSourceClips +
                    " original AnimationClip sub-assets from W3X, or " +
                    "their source W3X animation assets.";
                return false;
            }

            var cache = new Dictionary<string, W3xDocument>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var clip in clips.OrderBy(c =>
                AssetDatabase.GetAssetPath(c), StringComparer.Ordinal)
                .ThenBy(c => c.name, StringComparer.Ordinal))
            {
                string source = AssetDatabase.GetAssetPath(clip);
                if (!string.Equals(Path.GetExtension(source), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Clip '" + clip.name +
                        "' is not an original W3X imported clip. " +
                        "Select its .w3x sub-asset, not an older .anim copy.";
                    return false;
                }
                W3xDocument document;
                if (!cache.TryGetValue(source, out document))
                {
                    try
                    {
                        document = W3xReader.Read(source);
                    }
                    catch (Exception ex)
                    {
                        reason = "Cannot read source W3X " + source +
                            ": " + ex.Message;
                        return false;
                    }
                    cache.Add(source, document);
                }
                var animation = document.Animations.FirstOrDefault(a =>
                    string.Equals(a.Identifier, clip.name,
                        StringComparison.OrdinalIgnoreCase));
                if (animation == null)
                {
                    reason = "Clip '" + clip.name +
                        "' was not found in its original W3X XML.";
                    return false;
                }

                string error = GetCompatibilityError(
                    clip, model, document, animation);
                if (!string.IsNullOrEmpty(error))
                {
                    reason = "Incompatible clip " + clip.name + ":\n" +
                        error + "\n\nChoose the matching SAGE model " +
                        "or correct source pivots before editing.";
                    return false;
                }
                selectedSources.Add(new Source
                {
                    Clip = clip,
                    Path = source,
                    Document = document,
                    Animation = animation
                });
            }
            return true;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CncSageEditable"))
                AssetDatabase.CreateFolder("Assets", "CncSageEditable");
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/CncSageEditable",
                    "Animations");
        }

        private static string SafeFileName(string name)
        {
            var safe = new string((name ?? "SAGE").Select(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
                .ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "SAGE" : safe;
        }

        [MenuItem(Menu, true)]
        private static bool ValidateMenu()
        {
            // Keep the menu discoverable for mixed selections; the
            // full input validation runs before any assets are written.
            return Selection.objects != null &&
                Selection.objects.Length > 0;
        }
    }
}
