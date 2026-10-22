using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// In-memory smoke tests for advanced W3X export. No disk writes and no
    /// Unity Test Runner dependency.
    /// </summary>
    internal static class W3xPhase2BSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 2B Advanced Export Self-Test")]
        private static void Run()
        {
            GameObject root = null;
            Mesh mesh = null;
            Material material = null;
            Texture2D runtimeTexture = null;
            try
            {
                XDocument source = CreateSource();
                root = new GameObject("MODEL");
                var holder = new GameObject("SAGE_Pivots");
                holder.transform.SetParent(root.transform, false);
                var rootPivot = new GameObject("ROOT");
                rootPivot.transform.SetParent(holder.transform, false);
                rootPivot.AddComponent<CncSagePivotInfo>()
                    .SetSource(0, 0, -1, "");
                var wheel = new GameObject("WHEEL");
                wheel.transform.SetParent(rootPivot.transform, false);
                wheel.transform.localPosition = new Vector3(1, 0, 0);
                wheel.AddComponent<CncSagePivotInfo>()
                    .SetSource(1, 1, 0, "");

                var part = new GameObject("TEST.SKIN");
                part.transform.SetParent(root.transform, false);
                part.AddComponent<CncSageMeshIdentity>()
                    .SetSageIdentifier("TEST.SKIN");
                var skinned = part.AddComponent<SkinnedMeshRenderer>();
                mesh = new Mesh();
                mesh.vertices = new[] {
                    Vector3.zero, new Vector3(1.5f, 0, 0),
                    new Vector3(1, 0, 1) };
                mesh.triangles = new[] { 0, 2, 1 };
                mesh.normals = new[] {
                    Vector3.up, Vector3.up, Vector3.up };
                mesh.uv = new[] { Vector2.zero,
                    Vector2.right, Vector2.up };
                mesh.boneWeights = new[] {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 0, weight0 = .5f,
                        boneIndex1 = 1, weight1 = .5f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 0,
                        boneIndex1 = 1, weight1 = 1 }
                };
                var bones = new[] { rootPivot.transform, wheel.transform };
                mesh.bindposes = bones.Select(b =>
                    b.worldToLocalMatrix * part.transform.localToWorldMatrix)
                    .ToArray();
                skinned.bones = bones;
                skinned.rootBone = bones[0];
                skinned.sharedMesh = mesh;

                var exportSkin = new W3xExportCore.ExportOptions {
                    ExportSkinWeightsAndVertices = true
                };
                W3xExportCore.Report report;
                var first = W3xExportCore.BuildDocument(
                    source, root, exportSkin, out report);
                Check(report.ChangedSkinWeights == 0 &&
                    report.ChangedSkinVertices == 0,
                    "Baseline skin must be unchanged.");
                W3xExportCore.ValidateMetadataPreservation(source, first);

                var authored = mesh.vertices;
                authored[1] = new Vector3(1.6f, 0, 0);
                mesh.vertices = authored;
                var weights = mesh.boneWeights;
                weights[1] = new BoneWeight {
                    boneIndex0 = 0, weight0 = .25f,
                    boneIndex1 = 1, weight1 = .75f
                };
                mesh.boneWeights = weights;
                var edited = W3xExportCore.BuildDocument(
                    source, root, exportSkin, out report);
                Check(report.ChangedSkinWeights == 1 &&
                    report.ChangedSkinVertices == 1,
                    "Edited skin vertex and weights were not exported.");
                var outputSkin = edited.Root.Elements().First(e =>
                    e.Name.LocalName == "W3DMesh");
                var exportedVertex = W3xExportCore.Children(
                    W3xExportCore.Child(outputSkin, "Vertices"), "V")
                    .Skip(1).First();
                float sourceX = W3xExportCore.ParseFloat(
                    (string)exportedVertex.Attribute("X"));
                // 0.85 source + (0.25 * 0) + (0.75 * 1)
                // = 1.60 visible Unity rest coordinate.
                Check(Mathf.Abs(sourceX + .75f - 1.6f) < 0.0001f,
                    "New skin data must preserve the authored rest pose.");

                wheel.transform.localPosition = new Vector3(1.25f, 0, 0);
                var exportPivots = new W3xExportCore.ExportOptions {
                    ExportPivotTransforms = true
                };
                var withPivots = W3xExportCore.BuildDocument(
                    source, root, exportPivots, out report);
                Check(report.ChangedPivots == 1,
                    "Expected one edited local pivot.");
                W3xExportCore.ValidateMetadataPreservation(
                    source, withPivots, true);

                var fixupSource = new XDocument(source);
                var hierarchy = fixupSource.Root.Elements().First(x =>
                    x.Name.LocalName == "W3DHierarchy");
                var wheelNode = W3xExportCore.Children(
                    hierarchy, "Pivot").Skip(1).First();
                wheelNode.Add(new XElement(wheelNode.Name.Namespace +
                    "FixupMatrix"));
                bool fixupRejected = false;
                try
                {
                    W3xExportCore.BuildDocument(
                        fixupSource, root, exportPivots, out report);
                }
                catch (InvalidDataException)
                {
                    fixupRejected = true;
                }
                Check(fixupRejected,
                    "Pivot edit with FixupMatrix must be blocked.");

                var shader = Shader.Find("Unlit/Texture") ??
                    Shader.Find("Standard");
                if (shader != null)
                {
                    material = new Material(shader);
                    runtimeTexture = new Texture2D(2, 2);
                    if (material.HasProperty("_MainTex"))
                        material.SetTexture("_MainTex", runtimeTexture);
                    else if (material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", runtimeTexture);
                    skinned.sharedMaterial = material;
                    var exportMaterial = new W3xExportCore.ExportOptions {
                        ExportMaterialTextures = true
                    };
                    bool unsavedTextureRejected = false;
                    try
                    {
                        W3xExportCore.BuildDocument(
                            source, root, exportMaterial, out report);
                    }
                    catch (InvalidDataException)
                    {
                        unsavedTextureRejected = true;
                    }
                    Check(unsavedTextureRejected,
                        "Runtime-only texture substitution must be blocked.");
                }

                Debug.Log("[C&C SAGE Phase 2B] Advanced self-test PASS: " +
                    "skin rest-pose inversion, weight changes, pivot export, " +
                    "FixupMatrix guard and texture-asset guard.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE Phase 2B] Advanced self-test FAILED: " +
                    ex);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
                if (runtimeTexture != null)
                    UnityEngine.Object.DestroyImmediate(runtimeTexture);
            }
        }

        private static XDocument CreateSource()
        {
            return XDocument.Parse(
                "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                "<W3DHierarchy id='TEST_RIG'>" +
                "<Pivot Name='ROOT' NameID='0' Parent='-1'>" +
                "<Translation X='0' Y='0' Z='0' />" +
                "<Rotation X='0' Y='0' Z='0' W='1' />" +
                "</Pivot><Pivot Name='WHEEL' NameID='1' Parent='0'>" +
                "<Translation X='1' Y='0' Z='0' />" +
                "<Rotation X='0' Y='0' Z='0' W='1' /></Pivot>" +
                "</W3DHierarchy>" +
                "<W3DMesh id='TEST.SKIN' GeometryType='Skin'>" +
                "<Vertices><V X='0' Y='0' Z='0'/>" +
                "<V X='1' Y='0' Z='0'/><V X='0' Y='1' Z='0'/>" +
                "</Vertices><Normals>" +
                "<N X='0' Y='0' Z='1'/><N X='0' Y='0' Z='1'/>" +
                "<N X='0' Y='0' Z='1'/></Normals>" +
                "<TexCoords><T X='0' Y='0'/><T X='1' Y='0'/>" +
                "<T X='0' Y='1'/></TexCoords>" +
                "<BoneInfluences>" +
                "<I Bone='0' Weight='1'/>" +
                "<I Bone='0' Weight='0.5'/>" +
                "<I Bone='0' Weight='0'/></BoneInfluences>" +
                "<BoneInfluences>" +
                "<I Bone='1' Weight='0'/>" +
                "<I Bone='1' Weight='0.5'/>" +
                "<I Bone='1' Weight='1'/></BoneInfluences>" +
                "<Triangles><T><V>0</V><V>1</V><V>2</V>" +
                "<Nrm X='0' Y='0' Z='1'/><Dist>0</Dist>" +
                "</T></Triangles>" +
                "<FXShader ShaderName='TestFX'><Constants>" +
                "<Texture Name='DiffuseTexture'><Value>ORIGINAL_TEXTURE</Value>" +
                "</Texture></Constants></FXShader>" +
                "</W3DMesh></AssetDeclaration>");
        }

        private static void Check(bool success, string message)
        {
            if (!success)
                throw new InvalidDataException(message);
        }
    }
}
