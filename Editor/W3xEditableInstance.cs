using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Creates an editable Project prefab and independent Mesh/Material assets.
    /// Uses a temporary preview scene; nothing is added to the active scene.
    /// </summary>
    internal static class W3xEditableInstance
    {
        private const string MenuPath =
            "Tools/C&C SAGE/Create Editable W3X Project Prefab";
        private const string OutputFolder = "Assets/CncSageEditable";

        [MenuItem(MenuPath)]
        private static void Create()
        {
            string sourcePath = GetSelectedW3xPath();
            if (string.IsNullOrEmpty(sourcePath))
                return;

            if (!EditorUtility.DisplayDialog("C&C SAGE Project Prefab",
                "Create an editable Project prefab and independent Mesh/Material " +
                "assets in Assets/CncSageEditable? The original W3X and active " +
                "scene will not be modified.",
                "Create Project prefab", "Cancel"))
                return;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogError("[C&C SAGE] Selected W3X has no imported GameObject.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets", "CncSageEditable");

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(sourceGuid))
            {
                Debug.LogError("[C&C SAGE] Could not resolve the W3X source GUID.");
                return;
            }

            var createdPaths = new List<string>();
            Scene previewScene = default(Scene);
            bool previewOpened = false;
            GameObject temporary = null;
            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                previewOpened = true;
                temporary = PrefabUtility.InstantiatePrefab(source,
                    previewScene) as GameObject;
                if (temporary == null)
                    throw new InvalidOperationException(
                        "Could not instantiate the source in a preview scene.");

                // The saved editable prefab must not require its nested
                // imported W3X prefab for mesh/transform overrides.
                if (PrefabUtility.IsAnyPrefabInstanceRoot(temporary))
                    PrefabUtility.UnpackPrefabInstance(temporary,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);

                string prefix = Path.GetFileNameWithoutExtension(sourcePath) +
                    "_Editable";
                temporary.name = prefix;

                var link = temporary.GetComponent<CncSageEditableSource>();
                if (link == null)
                    link = temporary.AddComponent<CncSageEditableSource>();
                link.SetSource(sourceGuid, sourcePath);

                int meshesCopied = 0;
                var meshCopies = new Dictionary<Mesh, Mesh>();
                foreach (var filter in temporary
                             .GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null)
                        filter.sharedMesh = CopyMesh(filter.sharedMesh,
                            meshCopies, prefix, ref meshesCopied, createdPaths);
                }
                foreach (var skin in temporary
                             .GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh != null)
                        skin.sharedMesh = CopyMesh(skin.sharedMesh,
                            meshCopies, prefix, ref meshesCopied, createdPaths);
                }

                int materialsCopied = 0;
                var materialCopies = new Dictionary<Material, Material>();
                foreach (var renderer in temporary
                             .GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material original = materials[i];
                        if (original == null)
                            continue;
                        Material copy;
                        if (!materialCopies.TryGetValue(original, out copy))
                        {
                            copy = new Material(original);
                            string materialPath =
                                AssetDatabase.GenerateUniqueAssetPath(
                                    OutputFolder + "/" + prefix + "_Material_" +
                                    materialsCopied + ".mat");
                            AssetDatabase.CreateAsset(copy, materialPath);
                            createdPaths.Add(materialPath);
                            materialCopies.Add(original, copy);
                            materialsCopied++;
                        }
                        materials[i] = copy;
                    }
                    renderer.sharedMaterials = materials;
                }

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                    OutputFolder + "/" + prefix + ".prefab");
                // Track the path before SaveAsPrefabAsset in case Unity
                // creates a partial file and then reports an error.
                createdPaths.Add(prefabPath);
                bool success;
                var saved = PrefabUtility.SaveAsPrefabAsset(
                    temporary, prefabPath, out success);
                if (!success || saved == null)
                    throw new IOException("Unity could not save the editable prefab.");

                AssetDatabase.SaveAssets();
                var editableAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    prefabPath);
                if (editableAsset == null ||
                    editableAsset.GetComponent<CncSageEditableSource>() == null)
                    throw new InvalidDataException(
                        "Saved prefab lost the original W3X source link.");

                Selection.activeObject = editableAsset;
                EditorGUIUtility.PingObject(editableAsset);
                Debug.Log("[C&C SAGE Phase 2B] Editable Project prefab ready: " +
                    prefabPath + " | copied meshes=" + meshesCopied +
                    " | copied materials=" + materialsCopied +
                    " | source W3X=" + sourcePath +
                    " | active scene not modified.");
            }
            catch (Exception e)
            {
                for (int i = createdPaths.Count - 1; i >= 0; i--)
                    AssetDatabase.DeleteAsset(createdPaths[i]);
                Debug.LogError(
                    "[C&C SAGE Phase 2B] Project prefab creation failed: " + e);
            }
            finally
            {
                if (temporary != null)
                    UnityEngine.Object.DestroyImmediate(temporary);
                if (previewOpened)
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static Mesh CopyMesh(Mesh original,
            Dictionary<Mesh, Mesh> copies, string prefix,
            ref int index, List<string> createdPaths)
        {
            Mesh copy;
            if (copies.TryGetValue(original, out copy))
                return copy;
            copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name + "_Editable";
            string path = AssetDatabase.GenerateUniqueAssetPath(
                OutputFolder + "/" + prefix + "_Mesh_" + index + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            createdPaths.Add(path);
            copies.Add(original, copy);
            index++;
            return copy;
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            return !string.IsNullOrEmpty(GetSelectedW3xPath());
        }

        private static string GetSelectedW3xPath()
        {
            var selected = Selection.activeObject;
            if (selected == null)
                return null;
            var path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
                return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null ?
                path : null;
        }
    }
}
