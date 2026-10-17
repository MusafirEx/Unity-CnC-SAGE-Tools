using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Validate export of a REAL selected W3X prefab by cloning both the
    /// GameObject and its Mesh in memory. Does not write or change source files.
    /// </summary>
    internal static class W3xEditedMeshProbe
    {
        private const string Command =
            "Tools/C&C SAGE/Validate Selected W3X Mesh Edits (No Write)";

        [MenuItem(Command)]
        private static void Validate()
        {
            string path;
            GameObject model;
            if (!W3xExportMenu.TryGetSelectedModel(out path, out model))
            {
                EditorUtility.DisplayDialog("C&C SAGE Phase 2B",
                    "Select an imported W3X or linked editable W3X " +
                    "prefab in the Project window.", "OK");
                return;
            }

            GameObject clone = null;
            Mesh workingMesh = null;
            Scene previewScene = default(Scene);
            bool previewOpen = false;
            try
            {
                var source = W3xExportCore.LoadSource(Path.GetFullPath(path));
                previewScene = EditorSceneManager.NewPreviewScene();
                previewOpen = true;
                clone = PrefabUtility.InstantiatePrefab(model, previewScene)
                    as GameObject;
                if (clone == null)
                    throw new InvalidDataException(
                        "Cannot instantiate selected Project model for test.");
                clone.name = "SAGE_Phase2B_TemporaryTest";
                clone.hideFlags = HideFlags.HideAndDontSave;

                var sourceMeshes = source.Root.Elements()
                    .Where(x => x.Name.LocalName == "W3DMesh" &&
                        !string.Equals((string)x.Attribute("GeometryType"),
                            "Skin", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var target = clone.GetComponentsInChildren<CncSageMeshIdentity>(true)
                    .Select(x => new
                    {
                        identity = x,
                        filter = x.GetComponent<MeshFilter>(),
                        xml = sourceMeshes.FirstOrDefault(e =>
                            string.Equals((string)e.Attribute("id"),
                                x.SageIdentifier,
                                StringComparison.OrdinalIgnoreCase))
                    })
                    .Where(x =>
                        x.filter != null && x.filter.sharedMesh != null &&
                        x.xml != null &&
                        x.filter.sharedMesh.uv.Length ==
                            x.filter.sharedMesh.vertexCount &&
                        W3xExportCore.Children(x.xml, "TexCoords").Any())
                    .OrderBy(x => W3xExportCore.Child(x.xml, "AABTree") != null)
                    .FirstOrDefault();

                if (target == null)
                    throw new InvalidDataException(
                        "No static source-backed mesh with valid UV0 was found " +
                        "in the selected W3X. Choose another test asset.");

                Mesh original = target.filter.sharedMesh;
                workingMesh = UnityEngine.Object.Instantiate(original);
                target.filter.sharedMesh = workingMesh;
                string id = target.identity.SageIdentifier;

                W3xExportCore.Report baselineReport;
                var unchanged = W3xExportCore.BuildDocument(
                    source, clone, out baselineReport);
                W3xExportCore.ValidateMetadataPreservation(source, unchanged);
                // An editable Project prefab may already contain valid edits.
                // The test validates a further in-memory change, not zero
                // differences against the original W3X.
                Debug.Log("[C&C SAGE Phase 2B] Existing source-backed edits: " +
                    baselineReport.ChangedUvs + " UV, " +
                    baselineReport.ChangedVertices + " vertex.");

                var uv = workingMesh.uv;
                uv[0] = uv[0] + new Vector2(0.03125f, 0.0625f);
                workingMesh.uv = uv;

                bool hasSpatialTree =
                    W3xExportCore.Child(target.xml, "AABTree") != null;
                if (!hasSpatialTree)
                {
                    var vertices = workingMesh.vertices;
                    vertices[0] = vertices[0] +
                        new Vector3(0.0125f, 0.0075f, 0.001f);
                    workingMesh.vertices = vertices;
                    workingMesh.RecalculateBounds();
                }

                W3xExportCore.Report editedReport;
                var edited = W3xExportCore.BuildDocument(
                    source, clone, out editedReport);
                W3xExportCore.ValidateMetadataPreservation(source, edited);

                if (editedReport.ChangedUvs < 1)
                    throw new InvalidDataException(
                        "Edited UV0 did not propagate to W3X output.");
                if (!hasSpatialTree && editedReport.ChangedVertices < 1)
                    throw new InvalidDataException(
                        "Edited static vertex did not propagate to W3X output.");

                var outputMesh = edited.Root.Elements()
                    .First(x => x.Name.LocalName == "W3DMesh" &&
                        string.Equals((string)x.Attribute("id"),
                            id, StringComparison.OrdinalIgnoreCase));
                var outputUv = W3xExportCore.Children(
                    W3xExportCore.Child(outputMesh, "TexCoords"), "T").First();
                float u = W3xExportCore.ParseFloat(
                    (string)outputUv.Attribute("X"));
                float v = W3xExportCore.ParseFloat(
                    (string)outputUv.Attribute("Y"));
                if (Mathf.Abs(u - uv[0].x) > 0.00001f ||
                    Mathf.Abs(v - uv[0].y) > 0.00001f)
                    throw new InvalidDataException(
                        "Output XML UV values do not match the Unity edit.");

                // Prove that W3X output is valid serialized XML and all the
                // unmodified non-mesh declarations survive serialization.
                string serialized = edited.ToString(SaveOptions.DisableFormatting);
                var reloaded = XDocument.Parse(serialized,
                    LoadOptions.PreserveWhitespace);
                W3xExportCore.ValidateMetadataPreservation(
                    source, reloaded);

                if (target.filter.sharedMesh == original)
                    throw new InvalidDataException(
                        "Temporary test did not clone its working Mesh.");

                Debug.Log("[C&C SAGE Phase 2B] Edited W3X test PASS: " +
                    path + " / " + id +
                    " | UV edits=" + editedReport.ChangedUvs +
                    " | Vertex edits=" + editedReport.ChangedVertices +
                    " | AABTree=" + hasSpatialTree +
                    " | source files changed=0");
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE Phase 2B] Edited W3X test FAILED: " +
                    path + "\n" + ex);
            }
            finally
            {
                if (clone != null)
                    UnityEngine.Object.DestroyImmediate(clone);
                if (workingMesh != null)
                    UnityEngine.Object.DestroyImmediate(workingMesh);
                if (previewOpen)
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [MenuItem(Command, true)]
        private static bool HasSelectedW3x()
        {
            string sourcePath;
            GameObject model;
            return W3xExportMenu.TryGetSelectedModel(
                out sourcePath, out model);
        }
    }
}
