using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Small Editor-only smoke tests, no Unity Test Runner package dependency.
    /// Run from Tools / C&C SAGE / Run Phase 2 Exporter Self-Test.
    /// </summary>
    internal static class W3xExportSmokeTests
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 2 Exporter Self-Test")]
        private static void Run()
        {
            GameObject root = null;
            Mesh mesh = null;
            try
            {
                var original = XDocument.Parse(
                    "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                    "<W3DHierarchy id='RIG'><Pivot Name='ROOT' Parent='-1'/></W3DHierarchy>" +
                    "<W3DMesh id='TEST.BODY' GeometryType='Normal'>" +
                    "<Vertices><V X='0' Y='0' Z='0'/>" +
                    "<V X='1' Y='0' Z='0'/><V X='0' Y='1' Z='0'/></Vertices>" +
                    "<Normals><N X='0' Y='0' Z='1'/>" +
                    "<N X='0' Y='0' Z='1'/><N X='0' Y='0' Z='1'/></Normals>" +
                    "<TexCoords><T X='0' Y='0'/><T X='1' Y='0'/>" +
                    "<T X='0' Y='1'/></TexCoords>" +
                    "<Triangles><T><V>0</V><V>1</V><V>2</V>" +
                    "<Nrm X='0' Y='0' Z='1'/><Dist>0</Dist></T></Triangles>" +
                    "<FXShader ShaderName='KEEP_ME'><Constants>" +
                    "<Texture Name='DiffuseTexture'><Value>TEST_DIFF</Value>" +
                    "</Texture></Constants></FXShader></W3DMesh>" +
                    "<W3DAnimation id='ANIMATION_NOT_DECODED'/>" +
                    "</AssetDeclaration>");
                root = new GameObject("TEST");
                var part = new GameObject("TEST.BODY");
                part.transform.SetParent(root.transform, false);
                part.AddComponent<CncSageMeshIdentity>()
                    .SetSageIdentifier("TEST.BODY");
                mesh = new Mesh();
                mesh.vertices = new[] {
                    Vector3.zero, new Vector3(1, 0, 0),
                    new Vector3(0, 0, 1) };
                mesh.triangles = new[] { 0, 2, 1 };
                mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                part.AddComponent<MeshFilter>().sharedMesh = mesh;

                W3xExportCore.Report report;
                var result = W3xExportCore.BuildDocument(original, root, out report);
                W3xExportCore.ValidateMetadataPreservation(original, result);
                Check(report.SourceMeshes == 1 && report.StaticMeshesExported == 1,
                    "Expected one static source mesh.");
                Check(report.ChangedVertices == 0 && report.ChangedUvs == 0,
                    "Unchanged source must produce no geometry edits.");

                var newUvs = (Vector2[])mesh.uv.Clone();
                newUvs[0] = new Vector2(0.25f, 0.75f);
                mesh.uv = newUvs;
                result = W3xExportCore.BuildDocument(original, root, out report);
                Check(report.ChangedUvs == 1, "Expected exactly one UV edit.");
                Check((string)result.Descendants()
                    .First(x => x.Name.LocalName == "TexCoords")
                    .Elements().First().Attribute("X") == "0.25",
                    "Expected UV0 coordinate change in emitted W3X.");

                mesh.triangles = new[] { 0, 1, 2 };
                bool topologyRejected = false;
                try
                {
                    W3xExportCore.BuildDocument(original, root, out report);
                }
                catch (InvalidDataException)
                {
                    topologyRejected = true;
                }
                Check(topologyRejected, "Topology/winding edit must be rejected.");

                // Vertex movement is exported only if source spatial metadata
                // can be reconstructed without corrupting AABTree data.
                mesh.triangles = new[] { 0, 2, 1 };
                var positions = mesh.vertices;
                positions[0] = new Vector3(0.125f, 0f, 0f);
                mesh.vertices = positions;
                result = W3xExportCore.BuildDocument(original, root, out report);
                Check(report.ChangedVertices == 1,
                    "Expected one modified static vertex.");

                var treeBacked = new XDocument(original);
                var w3mesh = treeBacked.Descendants()
                    .First(x => x.Name.LocalName == "W3DMesh");
                w3mesh.Add(new XElement(w3mesh.Name.Namespace + "AABTree"));
                bool unsafeSpatialTreeRejected = false;
                try
                {
                    W3xExportCore.BuildDocument(treeBacked, root, out report);
                }
                catch (InvalidDataException)
                {
                    unsafeSpatialTreeRejected = true;
                }
                Check(unsafeSpatialTreeRejected,
                    "Edited vertices with source AABTree must be rejected.");

                var skinSource = new XDocument(original);
                var skinNode = skinSource.Descendants()
                    .First(x => x.Name.LocalName == "W3DMesh");
                skinNode.SetAttributeValue("GeometryType", "Skin");
                var skinResult = W3xExportCore.BuildDocument(
                    skinSource, root, out report);
                Check(report.SkinnedMeshesPreserved == 1 &&
                    report.StaticMeshesExported == 0,
                    "Source skin mesh must remain unchanged.");
                Check(XNode.DeepEquals(skinNode,
                    skinResult.Descendants().First(x => x.Name.LocalName == "W3DMesh")),
                    "Skinned W3X source XML must be byte-semantically preserved.");

                Debug.Log("[C&C SAGE Phase 2] Exporter self-test PASS: " +
                    "metadata, static UV/vertex edits, topology guard, " +
                    "AABTree guard, source-preserved skin.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE Phase 2] Exporter self-test FAILED: " + ex);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void Check(bool success, string message)
        {
            if (!success)
                throw new InvalidDataException(message);
        }
    }
}
