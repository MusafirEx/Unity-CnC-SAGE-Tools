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
    /// No-write regression for the reported GPPITBULL.NEWLabel case: one
    /// new static mesh with 24 vertices and source container attachment.
    /// </summary>
    internal static class W3xPhase2CNewMeshTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 2C New Mesh Self-Test")]
        private static void Run()
        {
            Scene preview = default(Scene);
            bool opened = false;
            GameObject root = null;
            Mesh sourceMesh = null, extraMesh = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                opened = true;
                root = new GameObject("GPPitbull_Editable_Phase2CTest");
                SceneManager.MoveGameObjectToScene(root, preview);

                var rig = new GameObject("ROOTTRANSFORM");
                rig.transform.SetParent(root.transform, false);
                rig.AddComponent<CncSagePivotInfo>()
                    .SetSource(0, 0, -1, "");

                var existing = new GameObject("GPPITBULL.ORIGINAL");
                existing.transform.SetParent(root.transform, false);
                existing.AddComponent<CncSageMeshIdentity>()
                    .SetSageIdentifier("GPPITBULL.ORIGINAL");
                sourceMesh = new Mesh();
                sourceMesh.vertices = new[] {
                    Vector3.zero, Vector3.right, Vector3.up
                };
                sourceMesh.normals = new[] {
                    Vector3.forward, Vector3.forward, Vector3.forward
                };
                sourceMesh.uv = new[] {
                    Vector2.zero, Vector2.right, Vector2.up
                };
                sourceMesh.triangles = new[] { 0, 2, 1 };
                existing.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
                existing.AddComponent<MeshRenderer>();

                var part = new GameObject("GPPITBULL.NEWLabel");
                part.transform.SetParent(rig.transform, false);
                part.transform.localPosition = Vector3.right;
                part.AddComponent<CncSageMeshIdentity>()
                    .SetSageIdentifier("GPPITBULL.NEWLabel");
                extraMesh = new Mesh();
                var vertices = new Vector3[24];
                var normals = new Vector3[24];
                var uv = new Vector2[24];
                var triangles = new int[24];
                for (int t = 0; t < 8; t++)
                {
                    int i = t * 3;
                    float x = t * 0.4f;
                    vertices[i] = new Vector3(x, 0, 0);
                    vertices[i + 1] = new Vector3(x + .2f, 0, 0);
                    vertices[i + 2] = new Vector3(x, .2f, 0);
                    normals[i] = normals[i + 1] =
                        normals[i + 2] = Vector3.forward;
                    uv[i] = Vector2.zero;
                    uv[i + 1] = Vector2.right;
                    uv[i + 2] = Vector2.up;
                    triangles[i] = i;
                    triangles[i + 1] = i + 1;
                    triangles[i + 2] = i + 2;
                }
                extraMesh.vertices = vertices;
                extraMesh.normals = normals;
                extraMesh.uv = uv;
                extraMesh.triangles = triangles;
                part.AddComponent<MeshFilter>().sharedMesh = extraMesh;
                part.AddComponent<MeshRenderer>();

                XDocument original = CreateSource();
                var options = new W3xExportCore.ExportOptions
                {
                    ExportNewStaticMeshes = true
                };
                W3xExportCore.Report report;
                var result = W3xExportCore.BuildDocument(
                    original, root, options, out report);
                Check(report.NewStaticMeshesExported == 1 &&
                    report.NewContainerBindings == 1,
                    "Expected one new mesh and W3DContainer binding.");
                Check(report.SourceMeshes == 1 &&
                    report.StaticMeshesExported == 1,
                    "Original mesh should also survive export.");

                var newMesh = result.Root.Elements().FirstOrDefault(x =>
                    x.Name.LocalName == "W3DMesh" &&
                    string.Equals((string)x.Attribute("id"),
                        "GPPITBULL.NEWLabel", StringComparison.Ordinal));
                Check(newMesh != null, "NEWLabel W3DMesh not generated.");
                Check(W3xExportCore.Children(
                    W3xExportCore.Child(newMesh, "Vertices"),
                    "V").Count() == 24,
                    "Expected 24 W3D vertices.");
                Check(W3xExportCore.Children(
                    W3xExportCore.Child(newMesh, "Triangles"),
                    "T").Count() == 8,
                    "Expected 8 W3D triangles.");
                Check(W3xExportCore.Children(
                    W3xExportCore.Child(newMesh, "TexCoords"),
                    "T").Count() == 24,
                    "Expected 24 UV0 coordinates.");

                var sourceFirst = W3xExportCore.Children(
                    W3xExportCore.Child(newMesh, "Vertices"),
                    "V").First();
                float exportedX = W3xExportCore.ParseFloat(
                    (string)sourceFirst.Attribute("X"));
                Check(Mathf.Abs(exportedX - 1f) < 0.0001f,
                    "Pivot-relative vertex transform not baked.");
                Check(W3xExportCore.Child(newMesh, "FXShader") != null,
                    "Original SAGE FXShader template was not preserved.");

                var identity = part.AddComponent<CncSageMaterialIdentity>();
                identity.SetSource("GPPITBULL.ORIGINAL",
                    useAssignedMaterial: false);
                var identified = W3xExportCore.BuildDocument(
                    original, root, options, out report);
                var materialMesh = identified.Root.Elements().First(x =>
                    x.Name.LocalName == "W3DMesh" &&
                    (string)x.Attribute("id") == "GPPITBULL.NEWLabel");
                Check((string)W3xExportCore.Child(materialMesh, "FXShader")
                    .Attribute("ShaderName") == "KnownSourceShader",
                    "Material Identity did not select the source shader.");

                identity.SetSource("GPPITBULL.DOES_NOT_EXIST");
                bool unknownTemplateRejected = false;
                try
                {
                    W3xExportCore.BuildDocument(original, root,
                        options, out report);
                }
                catch (InvalidDataException)
                {
                    unknownTemplateRejected = true;
                }
                Check(unknownTemplateRejected,
                    "Unknown shader template mesh id must fail export.");

                var transientTexture = new Texture2D(2, 2);
                try
                {
                    identity.SetSource("GPPITBULL.ORIGINAL",
                        diffuse: transientTexture);
                    bool transientRejected = false;
                    try
                    {
                        W3xExportCore.BuildDocument(original, root,
                            options, out report);
                    }
                    catch (InvalidDataException)
                    {
                        transientRejected = true;
                    }
                    Check(transientRejected,
                        "Non-file texture overrides must fail export.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(transientTexture);
                }

                var normalAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/RTS Engine/Demo/Menu/Pitbull/GUPitbull_NRM.tga");
                if (normalAsset != null)
                {
                    identity.SetSource("GPPITBULL.ORIGINAL",
                        normal: normalAsset, useAssignedMaterial: false);
                    options.OutputTextureDirectory = Path.GetFullPath(
                        "Assets/RTS Engine/Demo/Menu/Pitbull");
                    var materialResult = W3xExportCore.BuildDocument(
                        original, root, options, out report);
                    var emitted = materialResult.Root.Elements().First(x =>
                        x.Name.LocalName == "W3DMesh" &&
                        (string)x.Attribute("id") == "GPPITBULL.NEWLabel");
                    var constants = W3xExportCore.Child(
                        W3xExportCore.Child(emitted, "FXShader"), "Constants");
                    var normalSlot = constants.Elements().First(x =>
                        (string)x.Attribute("Name") == "NormalMap");
                    Check(W3xExportCore.Child(normalSlot, "Value").Value ==
                        "GUPitbull_NRM" &&
                        report.ChangedShaderTextures == 1,
                        "Existing normal texture was not mapped.");

                    identity.SetSource("GPPITBULL.ORIGINAL",
                        normalName: "GUPitbull_NRM",
                        useAssignedMaterial: false);
                    var namedResult = W3xExportCore.BuildDocument(
                        original, root, options, out report);
                    var namedMesh = namedResult.Root.Elements().First(x =>
                        x.Name.LocalName == "W3DMesh" &&
                        (string)x.Attribute("id") == "GPPITBULL.NEWLabel");
                    var namedConstants = W3xExportCore.Child(
                        W3xExportCore.Child(namedMesh, "FXShader"), "Constants");
                    var namedSlot = namedConstants.Elements().First(x =>
                        (string)x.Attribute("Name") == "NormalMap");
                    Check(W3xExportCore.Child(namedSlot, "Value").Value ==
                        "GUPitbull_NRM",
                        "Name-based available image lookup was not applied.");

                    identity.SetSource("GPPITBULL.ORIGINAL",
                        normalName: "NONEXISTENT_TEXTURE_NAME",
                        useAssignedMaterial: false);
                    var absent = W3xExportCore.BuildDocument(
                        original, root, options, out report);
                    var absentMesh = absent.Root.Elements().First(x =>
                        x.Name.LocalName == "W3DMesh" &&
                        (string)x.Attribute("id") == "GPPITBULL.NEWLabel");
                    var absentConstants = W3xExportCore.Child(
                        W3xExportCore.Child(absentMesh, "FXShader"),
                        "Constants");
                    var absentSlot = absentConstants.Elements().First(x =>
                        (string)x.Attribute("Name") == "NormalMap");
                    Check(W3xExportCore.Child(absentSlot, "Value").Value ==
                        "OLD_NRM",
                        "Missing named image should leave source constant untouched.");
                }
                identity.SetSource("GPPITBULL.ORIGINAL",
                    useAssignedMaterial: false);
                options.OutputTextureDirectory = null;

                var container = result.Root.Elements().First(x =>
                    x.Name.LocalName == "W3DContainer");
                var binding = W3xExportCore.Children(
                    container, "SubObject").Last();
                Check((string)binding.Attribute("BoneIndex") == "0" &&
                    (string)binding.Attribute("SubObjectID") == "NEWLabel" &&
                    W3xExportCore.Child(
                        W3xExportCore.Child(binding, "RenderObject"),
                        "Mesh").Value == "GPPITBULL.NEWLabel",
                    "Expected NEWLabel to attach to SAGE pivot 0.");

                W3xExportCore.ValidateMetadataPreservation(
                    original, result, false, true);
                var serialized = XDocument.Parse(
                    result.ToString(SaveOptions.DisableFormatting));
                W3xExportCore.ValidateMetadataPreservation(
                    original, serialized, false, true);

                // Verify legacy safe mode has no implicit geometry addition.
                var safe = W3xExportCore.BuildDocument(
                    original, root, out report);
                Check(report.NewStaticMeshesExported == 0 &&
                    safe.Root.Elements().Count(x =>
                        x.Name.LocalName == "W3DMesh") == 1,
                    "Default safe export should keep prior Phase 2B behavior.");

                var skin = part.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = extraMesh;
                bool skinRejected = false;
                try
                {
                    W3xExportCore.BuildDocument(
                        original, root, options, out report);
                }
                catch (InvalidDataException)
                {
                    skinRejected = true;
                }
                Check(skinRejected,
                    "Phase 2C must reject creation of new skinned meshes.");

                Debug.Log("[C&C SAGE Phase 2C] New mesh self-test PASS: " +
                    "NEWLabel mesh, shader identity, texture mapping, " +
                    "guards and serializer validation.");
            }
            catch (Exception e)
            {
                Debug.LogError("[C&C SAGE Phase 2C] New mesh self-test FAILED: " +
                    e);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                if (sourceMesh != null)
                    UnityEngine.Object.DestroyImmediate(sourceMesh);
                if (extraMesh != null)
                    UnityEngine.Object.DestroyImmediate(extraMesh);
                if (opened)
                    EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static XDocument CreateSource()
        {
            return XDocument.Parse(
                "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" +
                "<W3DHierarchy id='GPPITBULL_SKL'>" +
                "<Pivot Name='ROOTTRANSFORM' Parent='-1'>" +
                "<Translation X='0' Y='0' Z='0'/>" +
                "<Rotation X='0' Y='0' Z='0' W='1'/>" +
                "</Pivot></W3DHierarchy>" +
                "<W3DMesh id='GPPITBULL.ORIGINAL' GeometryType='Normal'>" +
                "<Vertices><V X='0' Y='0' Z='0'/>" +
                "<V X='1' Y='0' Z='0'/><V X='0' Y='0' Z='1'/>" +
                "</Vertices><Normals>" +
                "<N X='0' Y='1' Z='0'/><N X='0' Y='1' Z='0'/>" +
                "<N X='0' Y='1' Z='0'/></Normals>" +
                "<TexCoords><T X='0' Y='0'/><T X='1' Y='0'/>" +
                "<T X='0' Y='1'/></TexCoords>" +
                "<Triangles><T><V>0</V><V>1</V><V>2</V>" +
                "<Nrm X='0' Y='1' Z='0'/><Dist>0</Dist></T></Triangles>" +
                "<FXShader ShaderName='KnownSourceShader' TechniqueIndex='0'>" +
                "<Constants><Texture Name='DiffuseTexture'>" +
                "<Value>GUPitbull</Value></Texture>" +
                "<Texture Name='NormalMap'><Value>OLD_NRM</Value></Texture>" +
                "</Constants></FXShader>" +
                "</W3DMesh>" +
                "<W3DContainer id='GPPITBULL' Hierarchy='GPPITBULL_SKL'>" +
                "<SubObject SubObjectID='ORIGINAL' BoneIndex='0'>" +
                "<RenderObject><Mesh>GPPITBULL.ORIGINAL</Mesh></RenderObject>" +
                "</SubObject></W3DContainer></AssetDeclaration>");
        }

        private static void Check(bool pass, string description)
        {
            if (!pass)
                throw new InvalidDataException(description);
        }
    }
}
