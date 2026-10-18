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

        private const string NewMeshMenuPath =
            "Tools/C&C SAGE/Export Selected W3X (Including New Static Meshes)...";
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

        [MenuItem(NewMeshMenuPath)]
        private static void ExportNewMeshes()
        {
            if (!EditorUtility.DisplayDialog("C&C SAGE Phase 2C",
                "Export new static W3DMesh declarations and container " +
                "bindings. This is a preview feature: only one submesh, " +
                "existing SAGE pivots and original shader templates are " +
                "supported. No new skinned meshes or animation. " +
                "The original W3X is never overwritten.",
                "Export new W3X copy", "Cancel"))
                return;
            ExportWithOptions(new W3xExportCore.ExportOptions
            {
                ExportNewStaticMeshes = true
            }, "_unity_meshes");
        }

        [MenuItem(NewMeshMenuPath, true)]
        private static bool ValidateNewMeshExport()
        {
            return ValidateSelection();
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
                ExportMaterialTextures = true,
                ExportNewStaticMeshes = true
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
                    "Select an imported W3X model or linked editable W3X " +
                    "prefab in the Project window.", "OK");
                return;
            }

            if (!ConfirmUnsupportedNewMeshes(assetPath, model, options))
                return;

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

                // The W3X importer currently locates texture files beside
                // the output W3X, so only those are safe to reference.
                options.OutputTextureDirectory =
                    Path.GetDirectoryName(destination);
                W3xExportCore.Report report;
                XDocument exported = W3xExportCore.BuildDocument(
                    original, model, options, out report);

                // Ensure all nonmesh declarations (including includes, material
                // references, hierarchies, collisions and animations) survive.
                W3xExportCore.ValidateMetadataPreservation(original, exported,
                    options.ExportPivotTransforms, options.ExportNewStaticMeshes);

                // Serialize to a unique sidecar on the same filesystem and
                // reload/validate before making any destination changes.
                // Never delete a validated export after an I/O commit failure.
                string temp = destination + "." +
                    Guid.NewGuid().ToString("N") + ".sage-export-tmp";
                bool stagedValidated = false;
                bool savedAsCopy = false;
                string actualDestination = destination;
                try
                {
                    W3xExportCore.Save(exported, temp);
                    XDocument reloaded = W3xExportCore.LoadSource(temp);
                    W3xExportCore.ValidateMetadataPreservation(original, reloaded,
                        options.ExportPivotTransforms, options.ExportNewStaticMeshes);
                    stagedValidated = true;
                    actualDestination = W3xSafeDestination.Commit(
                        temp, destination, out savedAsCopy);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        if (stagedValidated)
                            Debug.LogError(
                                "[C&C SAGE Phase 2] Validated W3X retained " +
                                "after an unsuccessful file commit: " + temp +
                                "\nThe existing destination was not intentionally " +
                                "modified. Inspect the staged file before retrying.");
                        else
                            File.Delete(temp);
                    }
                }

                if (savedAsCopy)
                {
                    report.Notes.Add(
                        "Windows could not replace the existing output. " +
                        "Validated W3X saved as a separate copy: " +
                        actualDestination);
                    Debug.LogWarning(
                        "[C&C SAGE Phase 2] Existing W3X could not be " +
                        "replaced (possibly in use or read-only). " +
                        "Saved a validated NEW copy instead: " +
                        actualDestination + "\nOriginal output: " + destination);
                }

                // Project assets are imported by Unity when generated under Assets.
                AssetDatabase.Refresh();
                Debug.Log("[C&C SAGE Phase 2] W3X exported: " +
                    actualDestination + "\n" + report.Describe());
                EditorUtility.DisplayDialog("C&C SAGE Export",
                    (savedAsCopy ?
                        "Existing W3X could not be replaced. " +
                        "Saved a separate validated file instead:\n" +
                        actualDestination + "\n\n" :
                        "Exported W3X:\n" +
                        actualDestination + "\n\n") +
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

        private static bool ConfirmUnsupportedNewMeshes(
            string sourcePath, GameObject edited,
            W3xExportCore.ExportOptions options)
        {
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(
                sourcePath);
            if (original == null || edited == null ||
                original == edited)
                return true;

            var sourceIds = new System.Collections.Generic.HashSet<string>(
                original.GetComponentsInChildren<CncSageMeshIdentity>(true)
                    .Select(x => x.SageIdentifier)
                    .Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.OrdinalIgnoreCase);

            var newIds = edited.GetComponentsInChildren<
                    CncSageMeshIdentity>(true)
                .Where(x => !string.IsNullOrEmpty(x.SageIdentifier) &&
                    !sourceIds.Contains(x.SageIdentifier) &&
                    (x.GetComponent<MeshFilter>()?.sharedMesh != null ||
                     x.GetComponent<SkinnedMeshRenderer>()?.sharedMesh != null))
                .Select(x => x.SageIdentifier).Distinct(
                    StringComparer.OrdinalIgnoreCase).ToArray();

            var untagged = edited.GetComponentsInChildren<MeshFilter>(true)
                .Where(x => x.sharedMesh != null &&
                    x.GetComponent<CncSageMeshIdentity>() == null)
                .Select(x => x.name)
                .Concat(edited.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true)
                    .Where(x => x.sharedMesh != null &&
                        x.GetComponent<CncSageMeshIdentity>() == null)
                    .Select(x => x.name))
                .Distinct().ToArray();

            // Phase 2C supports new MeshFilter/MeshRenderer parts.
            // Warn only about omitted new parts when using an older mode,
            // or about unsupported new SKIN and untagged Mesh objects.
            var skinnedNewIds = edited.GetComponentsInChildren<
                CncSageMeshIdentity>(true)
                .Where(x => !string.IsNullOrEmpty(x.SageIdentifier) &&
                    !sourceIds.Contains(x.SageIdentifier) &&
                    x.GetComponent<SkinnedMeshRenderer>() != null)
                .Select(x => x.SageIdentifier).Distinct(
                    StringComparer.OrdinalIgnoreCase).ToArray();
            if (options.ExportNewStaticMeshes &&
                skinnedNewIds.Length > 0)
            {
                EditorUtility.DisplayDialog(
                    "C&C SAGE: New Skin Unsupported",
                    "New skinned meshes cannot be exported in Phase 2C:\n" +
                    string.Join("\n", skinnedNewIds), "OK");
                return false;
            }
            if (options.ExportNewStaticMeshes && untagged.Length > 0)
            {
                EditorUtility.DisplayDialog("C&C SAGE: Untagged Meshes",
                    "Give each new rendered mesh a unique " +
                    "CncSageMeshIdentity before exporting:\n" +
                    string.Join("\n", untagged), "OK");
                return false;
            }
            if (options.ExportNewStaticMeshes)
                return true;

            if (newIds.Length == 0 && untagged.Length == 0)
                return true;

            string message = "WARNING: The selected editable prefab has " +
                "meshes that Phase 2B cannot export.\n\n" +
                (newIds.Length == 0 ? "" :
                    "New mesh identities (not in original W3X):\n" +
                    string.Join("\n", newIds.Take(20).ToArray()) + "\n\n") +
                (untagged.Length == 0 ? "" :
                    "Mesh objects without CncSageMeshIdentity:\n" +
                    string.Join("\n", untagged.Take(20).ToArray()) + "\n\n") +
                "These meshes will NOT be present in the output W3X. " +
                "Adding CncSageMeshIdentity alone does not implement " +
                "new W3DMesh export. Cancel is recommended until Phase 2C.";
            Debug.LogWarning("[C&C SAGE Phase 2] " +
                "Unexportable new meshes detected.\n" + message);
            // The first button is intentionally the SAFE default.
            bool cancel = EditorUtility.DisplayDialog(
                "C&C SAGE: Missing Mesh Risk",
                message, "Cancel Export", "Continue Without New Meshes");
            return !cancel;
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSelection()
        {
            string path;
            GameObject model;
            return TryGetSelectedModel(out path, out model);
        }

        internal static bool TryGetSelectedModel(
            out string sourcePath, out GameObject root)
        {
            sourcePath = null;
            root = null;

            var selected = Selection.activeObject as GameObject;
            if (selected == null)
                selected = Selection.activeGameObject;
            if (selected == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(assetPath))
            {
                // Backwards compatibility for older scene-instance workflow.
                var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(
                    selected);
                if (prefabRoot == null)
                    return false;
                root = prefabRoot;
                assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    prefabRoot);
            }
            else
            {
                root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
            if (root == null || string.IsNullOrEmpty(assetPath))
                return false;

            string extension = Path.GetExtension(assetPath);
            if (string.Equals(extension, ".w3x",
                StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = assetPath;
                return true;
            }
            if (!string.Equals(extension, ".prefab",
                StringComparison.OrdinalIgnoreCase))
                return false;

            var link = root.GetComponent<CncSageEditableSource>();
            if (link == null || string.IsNullOrWhiteSpace(link.SourceGuid))
                return false;

            // GUID lookup works even when the original W3X is moved within
            // Assets. Never treat an arbitrary prefab pathname as source XML.
            string resolved = AssetDatabase.GUIDToAssetPath(link.SourceGuid);
            if (string.IsNullOrEmpty(resolved) ||
                !string.Equals(Path.GetExtension(resolved), ".w3x",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(AssetDatabase.AssetPathToGUID(resolved),
                    link.SourceGuid, StringComparison.OrdinalIgnoreCase))
                return false;

            sourcePath = resolved;
            return true;
        }
    }
}
