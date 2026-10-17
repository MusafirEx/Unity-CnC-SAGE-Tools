using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Make an editable scene instance with independent Mesh/Material assets.
    /// The imported W3X file and imported sub-assets are not modified.
    /// </summary>
    internal static class W3xEditableInstance
    {
        private const string MenuPath =
            "Tools/C&C SAGE/Create Editable W3X Scene Instance";

        [MenuItem(MenuPath)]
        private static void Create()
        {
            string path = GetSelectedSourcePath();
            if (string.IsNullOrEmpty(path))
                return;

            if (!EditorUtility.DisplayDialog("C&C SAGE Editable Instance",
                "Create a linked scene instance and save independent copies " +
                "of its meshes and materials under Assets/CncSageEditable? " +
                "Original W3X files will not be modified.",
                "Create editable copy", "Cancel"))
                return;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
                return;

            string folder = "Assets/CncSageEditable";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "CncSageEditable");
            var createdAssets = new List<string>();
            GameObject instance = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException(
                        "Cannot instantiate selected W3X model prefab.");

                instance.name = Path.GetFileNameWithoutExtension(path) +
                    "_Editable";
                Undo.RegisterCreatedObjectUndo(instance,
                    "Create editable C&C SAGE W3X model");

                var copies = new Dictionary<Mesh, Mesh>();
                int meshesCopied = 0;
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    filter.sharedMesh = CopyMesh(filter.sharedMesh, copies,
                        folder, instance.name, ref meshesCopied,
                        createdAssets);
                }
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null)
                        continue;
                    skin.sharedMesh = CopyMesh(skin.sharedMesh, copies,
                        folder, instance.name, ref meshesCopied,
                        createdAssets);
                }

                var materialCopies = new Dictionary<Material, Material>();
                int materialsCopied = 0;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var old = materials[i];
                        if (old == null)
                            continue;
                        Material duplicate;
                        if (!materialCopies.TryGetValue(old, out duplicate))
                        {
                            duplicate = new Material(old);
                            string output = AssetDatabase.GenerateUniqueAssetPath(
                                folder + "/" + instance.name + "_Material_" +
                                materialsCopied + ".mat");
                            AssetDatabase.CreateAsset(duplicate, output);
                            createdAssets.Add(output);
                            materialCopies.Add(old, duplicate);
                            materialsCopied++;
                        }
                        materials[i] = duplicate;
                        changed = true;
                    }
                    if (changed)
                        renderer.sharedMaterials = materials;
                }

                AssetDatabase.SaveAssets();
                Selection.activeGameObject = instance;
                Debug.Log("[C&C SAGE Phase 2B] Editable instance created: " +
                    instance.name + " | mesh assets=" + meshesCopied +
                    " | material assets=" + materialsCopied +
                    " | folder=" + folder +
                    ". Export the scene instance, not the imported source asset.");
            }
            catch (Exception ex)
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                for (int i = createdAssets.Count - 1; i >= 0; i--)
                    AssetDatabase.DeleteAsset(createdAssets[i]);
                Debug.LogError("[C&C SAGE Phase 2B] Cannot create editable copy: " +
                    ex);
            }
        }

        private static Mesh CopyMesh(Mesh source,
            Dictionary<Mesh, Mesh> copies,
            string folder, string prefix, ref int count,
            List<string> createdAssets)
        {
            Mesh result;
            if (copies.TryGetValue(source, out result))
                return result;

            result = UnityEngine.Object.Instantiate(source);
            result.name = source.name + "_Editable";
            string path = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + prefix + "_Mesh_" + count + ".asset");
            AssetDatabase.CreateAsset(result, path);
            copies.Add(source, result);
            createdAssets.Add(path);
            count++;
            return result;
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreate()
        {
            return !string.IsNullOrEmpty(GetSelectedSourcePath());
        }

        private static string GetSelectedSourcePath()
        {
            if (Selection.activeGameObject == null)
                return null;
            string path = AssetDatabase.GetAssetPath(Selection.activeGameObject);
            if (string.IsNullOrEmpty(path))
                path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    Selection.activeGameObject);
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase) ?
                path : null;
        }
    }
}
