using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Targeted W3X DDS reimport and diagnostics. Useful after moving
    /// thousands of extracted DDS files into WBData/DDS; never bulk-imports
    /// the entire WBData/W3X directory.
    /// </summary>
    internal static class Cnc3W3xTextureTools
    {
        private static readonly string[] TextureKeys =
        {
            "DiffuseTexture", "Texture_0", "NormalMap", "SpecMap"
        };

        [MenuItem("Assets/C&C SAGE/Diagnose Selected W3X Textures")]
        private static void DiagnoseSelected()
        {
            string[] selected = SelectedW3xPaths();
            foreach (string path in selected) Diagnose(path);
            if (selected.Length == 0)
                Debug.LogWarning("[C&C SAGE W3X DDS] Select a .w3x asset in Project.");
        }

        [MenuItem("Assets/C&C SAGE/Diagnose Selected W3X Textures", true)]
        private static bool HasW3xToDiagnose()
        {
            return SelectedW3xPaths().Length > 0;
        }

        [MenuItem("Assets/C&C SAGE/Reimport Selected W3X Materials")]
        private static void ReimportSelected()
        {
            string[] paths = SelectedW3xPaths();
            if (paths.Length == 0) return;
            // Only these W3X assets. AssetDatabase.Refresh() and recursive
            // folder reimports would unnecessarily process all 12k resources.
            foreach (string path in paths)
            {
                Debug.Log("[C&C SAGE W3X DDS] Reimporting " + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                Diagnose(path);
            }
        }

        [MenuItem("Assets/C&C SAGE/Reimport Selected W3X Materials", true)]
        private static bool HasW3xToReimport()
        {
            return SelectedW3xPaths().Length > 0;
        }

        private static string[] SelectedW3xPaths()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase) &&
                    AssetImporter.GetAtPath(path) is W3xImporter)
                    found.Add(path);
            }
            return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void Diagnose(string path)
        {
            try
            {
                var src = W3xReader.Read(path);
                int references = 0, found = 0, missing = 0, unityPending = 0;
                var lines = new List<string>();
                foreach (var mesh in src.Meshes)
                {
                    if (mesh.Shaders == null || mesh.Shaders.Length == 0)
                    {
                        lines.Add(mesh.Identifier +
                            ": no FXShader data in source W3X (no material is generated).");
                        continue;
                    }
                    foreach (var shader in mesh.Shaders)
                    foreach (string key in TextureKeys)
                    {
                        string requested = shader.Get(key);
                        if (string.IsNullOrWhiteSpace(requested)) continue;
                        references++;
                        string match = Cnc3WbDataTextureLocator.ResolveAssetPath(
                            path, requested);
                        if (string.IsNullOrEmpty(match))
                        {
                            missing++;
                            lines.Add(mesh.Identifier + " / " + key + ": " +
                                requested + " -> NOT FOUND under W3X or WBData/DDS");
                            continue;
                        }
                        Texture2D texture =
                            AssetDatabase.LoadAssetAtPath<Texture2D>(match);
                        if (texture == null)
                        {
                            unityPending++;
                            lines.Add(mesh.Identifier + " / " + key + ": " +
                                requested + " -> file exists but Unity Texture2D " +
                                "is not imported: " + match);
                            continue;
                        }
                        found++;
                        lines.Add(mesh.Identifier + " / " + key + ": " +
                            requested + " -> " + match);
                    }
                }
                var materials = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<Material>().ToArray();
                int texturedMaterials = 0;
                foreach (Material mat in materials)
                {
                    Texture tex = mat.HasProperty("_BaseMap")
                        ? mat.GetTexture("_BaseMap") : null;
                    if (tex == null && mat.HasProperty("_MainTex"))
                        tex = mat.GetTexture("_MainTex");
                    if (tex != null) texturedMaterials++;
                }
                int count = Math.Min(lines.Count, 80);
                Debug.Log("[C&C SAGE W3X DDS] " + path + "\n" +
                    "Mesh count: " + src.Meshes.Count +
                    "; source texture references: " + references +
                    "; DDS resolved: " + found +
                    "; missing: " + missing +
                    "; DDS not imported by Unity: " + unityPending +
                    "; generated materials: " + materials.Length +
                    "; materials with albedo assigned: " +
                    texturedMaterials + "\n" +
                    string.Join("\n", lines.Take(count).ToArray()) +
                    (lines.Count > count
                        ? "\n... " + (lines.Count - count) + " more lines." : ""));
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE W3X DDS] Diagnosis failed for " +
                    path + ": " + ex);
            }
        }
    }
}
