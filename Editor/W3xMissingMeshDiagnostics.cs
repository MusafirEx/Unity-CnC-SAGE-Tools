using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Compares named assets in Project, never touching the active scene.
    /// Specifically intended to diagnose missing meshes after Phase 2 export.
    /// </summary>
    internal static class W3xMissingMeshDiagnostics
    {
        private const string MenuPath =
            "Tools/C&C SAGE/Compare Pitbull Editable vs Unity Skin Export";

        [MenuItem(MenuPath)]
        private static void ComparePitbull()
        {
            try
            {
                string editablePath = FindUniqueAsset(
                    "GPPitbull_Editable", ".prefab", "t:Prefab");
                string exportedPath = FindUniqueAsset(
                    "GPPitbull_unity_skin", ".w3x", "t:GameObject");
                var editable = AssetDatabase.LoadAssetAtPath<GameObject>(
                    editablePath);
                var exported = AssetDatabase.LoadAssetAtPath<GameObject>(
                    exportedPath);
                if (editable == null || exported == null)
                    throw new InvalidDataException(
                        "An editable prefab or exported W3X is not imported.");

                var link = editable.GetComponent<CncSageEditableSource>();
                if (link == null || string.IsNullOrWhiteSpace(link.SourceGuid))
                    throw new InvalidDataException(
                        "Editable prefab has no CncSageEditableSource GUID.");

                string originalPath = AssetDatabase.GUIDToAssetPath(
                    link.SourceGuid);
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(
                    originalPath);
                if (original == null ||
                    !string.Equals(Path.GetExtension(originalPath), ".w3x",
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Original W3X is missing or its GUID was not resolved.");

                var originalParts = Capture(original);
                var editedParts = Capture(editable);
                var exportedParts = Capture(exported);

                var result = new StringBuilder();
                result.AppendLine(
                    "[C&C SAGE Phase 2] Missing mesh analysis");
                result.AppendLine("Original: " + originalPath);
                result.AppendLine("Edited:   " + editablePath);
                result.AppendLine("Exported: " + exportedPath);
                result.AppendLine("Original mesh IDs: " +
                    originalParts.Count + " | edited: " + editedParts.Count +
                    " | exported: " + exportedParts.Count);

                int missing = 0;
                int added = 0;
                foreach (var pair in editedParts.OrderBy(x => x.Key,
                             StringComparer.OrdinalIgnoreCase))
                {
                    Part output;
                    if (!exportedParts.TryGetValue(pair.Key, out output))
                    {
                        missing++;
                        string cause;
                        if (!originalParts.ContainsKey(pair.Key))
                        {
                            added++;
                            cause = "NEW MESH: Phase 2B exporter cannot " +
                                "create W3DMesh declarations for added meshes.";
                        }
                        else
                            cause = "ORIGINAL MESH MISSING: inspect exporter " +
                                "includes and W3X reimport warnings.";
                        result.AppendLine("MISSING " + pair.Key +
                            " | " + pair.Value.Describe() +
                            " | " + cause);
                    }
                    else
                    {
                        if (pair.Value.VertexCount != output.VertexCount)
                            result.AppendLine("DIFFERENT " + pair.Key +
                                " vertex count edited=" +
                                pair.Value.VertexCount + " exported=" +
                                output.VertexCount);
                        if (pair.Value.IsSkinned != output.IsSkinned)
                            result.AppendLine("DIFFERENT " + pair.Key +
                                " renderer type edited=" +
                                pair.Value.IsSkinned + " exported=" +
                                output.IsSkinned);
                        if (!output.RendererEnabled)
                            result.AppendLine("HIDDEN " + pair.Key +
                                " | exported renderer disabled");
                    }
                }
                foreach (var pair in exportedParts.OrderBy(x => x.Key))
                    if (!editedParts.ContainsKey(pair.Key))
                        result.AppendLine("EXTRA EXPORT " + pair.Key);

                AppendUntaggedMeshReport(result, editable, "EDITED");
                AppendUntaggedMeshReport(result, exported, "EXPORTED");
                result.AppendLine("Missing mesh IDs: " + missing +
                    " | newly added among missing: " + added);
                result.AppendLine("Note: the comparison uses Project " +
                    "assets, not scene objects. Source-only meshes may " +
                    "also be filtered by external ART includes.");
                Debug.Log(result.ToString());
                EditorUtility.DisplayDialog("C&C SAGE Mesh Comparison",
                    "Missing mesh IDs: " + missing +
                    "\nNewly added IDs among missing: " + added +
                    "\n\nSee Unity Console for the full per-mesh report.",
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[C&C SAGE Phase 2] " +
                    "Missing mesh comparison FAILED: " + e);
                EditorUtility.DisplayDialog("C&C SAGE Mesh Comparison",
                    e.Message + "\n\nSelect Project assets with the " +
                    "expected names or check for duplicated filenames.",
                    "OK");
            }
        }

        private static string FindUniqueAsset(
            string name, string extension, string filter)
        {
            var paths = AssetDatabase.FindAssets(name + " " + filter,
                    new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(
                    Path.GetFileNameWithoutExtension(p), name,
                    StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetExtension(p), extension,
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length != 1)
                throw new InvalidDataException(
                    "Expected exactly one " + name + extension +
                    " in Assets/, found " + paths.Length +
                    (paths.Length == 0 ? "" :
                        "\n" + string.Join("\n", paths)));
            return paths[0];
        }

        private sealed class Part
        {
            public int VertexCount;
            public bool IsSkinned;
            public bool RendererEnabled;
            public string Path;

            public string Describe()
            {
                return (IsSkinned ? "SkinnedMeshRenderer" :
                    "MeshFilter/MeshRenderer") +
                    " vertices=" + VertexCount + " at " + Path;
            }
        }

        private static Dictionary<string, Part> Capture(GameObject root)
        {
            var parts = new Dictionary<string, Part>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var identity in root.GetComponentsInChildren<
                         CncSageMeshIdentity>(true))
            {
                string id = identity.SageIdentifier;
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException(
                        "A mesh has a blank CncSageMeshIdentity at " +
                        identity.name);
                if (parts.ContainsKey(id))
                    throw new InvalidDataException(
                        "Duplicate mesh identity " + id + " in " +
                        root.name);
                var skin = identity.GetComponent<SkinnedMeshRenderer>();
                var filter = identity.GetComponent<MeshFilter>();
                Mesh mesh = skin != null ? skin.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    throw new InvalidDataException(
                        "Mesh identity " + id +
                        " has no shared Mesh assigned.");
                var renderer = identity.GetComponent<Renderer>();
                parts.Add(id, new Part
                {
                    IsSkinned = skin != null,
                    VertexCount = mesh.vertexCount,
                    RendererEnabled = renderer != null && renderer.enabled,
                    Path = RelativePath(identity.transform, root.transform)
                });
            }
            return parts;
        }

        private static string RelativePath(Transform child, Transform root)
        {
            var items = new List<string>();
            Transform current = child;
            while (current != null && current != root)
            {
                items.Add(current.name);
                current = current.parent;
            }
            items.Reverse();
            return string.Join("/", items.ToArray());
        }

        private static void AppendUntaggedMeshReport(
            StringBuilder result, GameObject root, string label)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(x => x.sharedMesh != null)
                .Select(x => x.gameObject);
            var skinned = root.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true)
                .Where(x => x.sharedMesh != null)
                .Select(x => x.gameObject);
            foreach (var part in filters.Concat(skinned).Distinct())
                if (part.GetComponent<CncSageMeshIdentity>() == null)
                    result.AppendLine("UNTAGGED " + label + " " +
                        RelativePath(part.transform, root.transform) +
                        ": mesh has no CncSageMeshIdentity");
        }
    }
}
