using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>Phase 2A explicit non-destructive W3X source-backed export.</summary>
    internal static class W3xExportMenu
    {
        private const string MenuPath =
            "Tools/C&C SAGE/Export Selected W3X (Safe Roundtrip)...";

        private const string SkinMenuPath =
            "Tools/C&C SAGE/Export Selected W3X (Skin & Shader Textures)...";
        private const string PivotMenuPath =
            "Tools/C&C SAGE/Export Selected W3X (Local Pivots)...";

        [MenuItem(MenuPath)]
        private static void ExportSelected()
        {
            ExportWithOptions(new W3xExportCore.ExportOptions(),
                "_unity_roundtrip");
        }

        [MenuItem(SkinMenuPath)]
        private static void ExportAdvancedSkin()
        {
            if (!EditorUtility.DisplayDialog("C&C SAGE Phase 2B",
                "Experimental: export edited skin vertices/weights and existing " +
                "FXShader texture references. Bone indices and mesh topology " +
                "must be unchanged. Source files are never overwritten. " +
                "Verify results against Blender before game use.",
                "Export copy", "Cancel"))
                return;
            ExportWithOptions(new W3xExportCore.ExportOptions
            {
                ExportSkinWeightsAndVertices = true,
                ExportMaterialTextures = true
            }, "_unity_skin");
        }

        [MenuItem(PivotMenuPath)]
        private static void ExportAdvancedPivots()
        {
            if (!EditorUtility.DisplayDialog("C&C SAGE Phase 2B",
                "Experimental: export edited LOCAL W3DHierarchy pivots. " +
                "For models that reference a separate _SKL.W3X, select " +
                "the _SKL asset instead. Pivots using FixupMatrix cannot " +
                "be edited by this exporter.",
                "Export copy", "Cancel"))
                return;
            ExportWithOptions(new W3xExportCore.ExportOptions
            {
                ExportPivotTransforms = true
            }, "_unity_pivots");
        }

        [MenuItem(SkinMenuPath, true)]
        private static bool ValidateAdvancedSkin()
        {
            return ValidateSelection();
        }

        [MenuItem(PivotMenuPath, true)]
        private static bool ValidateAdvancedPivots()
        {
            return ValidateSelection();
        }

        private static void ExportWithOptions(
            W3xExportCore.ExportOptions options, string suffix)
        {
            string assetPath;
            GameObject model;
            if (!TryGetSelectedModel(out assetPath, out model))
            {
                EditorUtility.DisplayDialog("C&C SAGE Export",
                    "Select a W3X model asset in the Project window or " +
                    "an instance of a W3X model in the scene.", "OK");
                return;
            }

            string originalPath = Path.GetFullPath(assetPath);
            string directory = Path.GetDirectoryName(originalPath);
            string defaultName = Path.GetFileNameWithoutExtension(assetPath) +
                suffix;

            string destination = EditorUtility.SaveFilePanel(
                "Export W3X source-preserving copy", directory,
                defaultName, "w3x");
            if (string.IsNullOrEmpty(destination))
                return;

            destination = Path.GetFullPath(destination);
            if (string.Equals(originalPath, destination,
                StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("C&C SAGE Export",
                    "Export refused: never overwrite the original W3X file. " +
                    "Choose a new filename.", "OK");
                return;
            }

            try
            {
                XDocument original = W3xExportCore.LoadSource(originalPath);

                bool hasIncludes = original.Root != null &&
                    original.Root.Elements().Any(e => e.Name.LocalName == "Includes"
                        && e.Elements().Any(x => x.Name.LocalName == "Include"));
                if (hasIncludes && !string.Equals(
                    Path.GetDirectoryName(destination),
                    Path.GetDirectoryName(originalPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    EditorUtility.DisplayDialog("C&C SAGE Export",
                        "This W3X has ART: include dependencies. Save the copy " +
                        "beside the original so relative asset paths remain valid. " +
                        "Export to another directory is blocked for now.", "OK");
                    return;
                }

                W3xExportCore.Report report;
                XDocument exported = W3xExportCore.BuildDocument(
                    original, model, options, out report);

                // Ensure all nonmesh declarations (including includes, material
                // references, hierarchies, collisions and animations) survive.
                W3xExportCore.ValidateMetadataPreservation(original, exported,
                    options.ExportPivotTransforms);

                // Write to a staging file, validate after serialization, then
                // move to destination. Failure does not damage existing W3X.
                string temp = destination + ".sage-export-tmp";
                if (File.Exists(temp))
                    throw new IOException("Temporary path already exists: " + temp);
                try
                {
                    W3xExportCore.Save(exported, temp);
                    XDocument reloaded = W3xExportCore.LoadSource(temp);
                    W3xExportCore.ValidateMetadataPreservation(original, reloaded,
                        options.ExportPivotTransforms);
                    // Replace atomically on the same filesystem, preserving
                    // an existing export if the replacement operation fails.
                    if (File.Exists(destination))
                        File.Replace(temp, destination, null);
                    else
                        File.Move(temp, destination);
                }
                finally
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }

                // Project assets are imported by Unity when generated under Assets.
                AssetDatabase.Refresh();
                Debug.Log("[C&C SAGE Phase 2] W3X exported: " +
                    destination + "\n" + report.Describe());
                EditorUtility.DisplayDialog("C&C SAGE Export",
                    "Exported a new W3X XML file.\n\n" +
                    report.Describe() +
                    "\n\nOnly explicitly selected export modes can modify " +
                    "skinning, pivot or texture references. This is not yet " +
                    "a Kane's Wrath-compatible asset build.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE Phase 2] Export aborted. " + ex);
                EditorUtility.DisplayDialog("C&C SAGE Export failed",
                    ex.Message + "\n\nOriginal W3X has not been modified.", "OK");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSelection()
        {
            string path;
            GameObject model;
            return TryGetSelectedModel(out path, out model);
        }

        private static bool TryGetSelectedModel(
            out string sourcePath, out GameObject root)
        {
            sourcePath = null;
            root = null;

            var selected = Selection.activeGameObject;
            if (selected == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
                root = PrefabUtility.GetNearestPrefabInstanceRoot(selected);
            }
            else
            {
                root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }

            if (root == null ||
                string.IsNullOrEmpty(assetPath) ||
                !string.Equals(Path.GetExtension(assetPath), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            sourcePath = assetPath;
            return true;
        }
    }
}
