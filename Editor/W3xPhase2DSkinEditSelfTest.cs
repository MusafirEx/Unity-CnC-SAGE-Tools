using System;
using System.IO;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Synthetic regression for the non-destructive Phase 2D skin authoring
    /// operations. Mesh lives in memory and never touches a Project asset.
    /// </summary>
    internal static class W3xPhase2DSkinEditSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 2D Skin Editing Self-Test")]
        private static void Run()
        {
            Mesh mesh = null;
            try
            {
                mesh = new Mesh();
                mesh.name = "Temporary SAGE Skin Editor Test";
                mesh.vertices = new[] {
                    Vector3.zero, Vector3.right, Vector3.up
                };
                mesh.triangles = new[] { 0, 2, 1 };
                mesh.uv = new[] {
                    Vector2.zero, Vector2.right, Vector2.up
                };
                mesh.bindposes = new[] {
                    Matrix4x4.identity,
                    Matrix4x4.Translate(new Vector3(-1, 0, 0))
                };
                mesh.boneWeights = new[] {
                    new BoneWeight {
                        boneIndex0 = 0, boneIndex1 = 1,
                        weight0 = .6f, weight1 = .4f
                    },
                    new BoneWeight {
                        boneIndex0 = 0, boneIndex1 = 1,
                        weight0 = .6f, weight1 = .4f
                    },
                    new BoneWeight {
                        boneIndex0 = 0, boneIndex1 = 1,
                        weight0 = .6f, weight1 = .4f
                    }
                };

                var originalTriangles = mesh.triangles;
                var originalBindposes = mesh.bindposes;
                var originalOtherWeights = mesh.boneWeights[1];
                W3xSkinnedMeshEditor.ModifyVertex(mesh, 0,
                    new Vector3(.125f, .25f, .375f));
                W3xSkinnedMeshEditor.ModifyExistingWeight(mesh, 0, .25f);

                if (Vector3.Distance(mesh.vertices[0],
                        new Vector3(.125f, .25f, .375f)) > .00001f)
                    throw new InvalidDataException(
                        "Edited rest vertex did not persist.");
                var weight = mesh.boneWeights[0];
                if (weight.boneIndex0 != 0 || weight.boneIndex1 != 1 ||
                    Mathf.Abs(weight.weight0 - .25f) > .00001f ||
                    Mathf.Abs(weight.weight1 - .75f) > .00001f ||
                    weight.weight2 != 0 || weight.weight3 != 0)
                    throw new InvalidDataException(
                        "Existing skin indices or weight normalization changed.");
                if (mesh.vertexCount != 3 ||
                    mesh.triangles.Length != originalTriangles.Length ||
                    mesh.triangles[0] != originalTriangles[0] ||
                    mesh.triangles[1] != originalTriangles[1] ||
                    mesh.triangles[2] != originalTriangles[2] ||
                    mesh.bindposes.Length != originalBindposes.Length ||
                    mesh.bindposes[1] != originalBindposes[1] ||
                    Mathf.Abs(mesh.boneWeights[1].weight0 -
                        originalOtherWeights.weight0) > .00001f)
                    throw new InvalidDataException(
                        "Mesh topology, bindposes or unrelated weights changed.");

                // The editor must reject a change that overlaps two
                // vertices before mutating the saved Mesh asset.
                Vector3 previousVertex = mesh.vertices[0];
                bool collapsedEditRejected = false;
                try
                {
                    W3xSkinnedMeshEditor.ModifyVertex(mesh, 0,
                        mesh.vertices[1]);
                }
                catch (InvalidDataException e)
                {
                    collapsedEditRejected =
                        e.Message.Contains("triangle 0");
                }
                if (!collapsedEditRejected ||
                    Vector3.Distance(mesh.vertices[0], previousVertex) >
                    .000001f)
                    throw new InvalidDataException(
                        "Collapsed triangle edit was not rejected safely.");

                // Exporter must preserve an untouched source-degenerate
                // triangle Nrm/Dist even if a different triangle changes.
                var sourcePlanes = XDocument.Parse(
                    "<Root><T><V>0</V><V>1</V><V>2</V>" +
                    "<Nrm X='0' Y='0' Z='1'/><Dist>0</Dist></T>" +
                    "<T><V>3</V><V>4</V><V>5</V>" +
                    "<Nrm X='0' Y='0' Z='1'/><Dist>7</Dist></T></Root>");
                var trianglesXml = sourcePlanes.Root.Elements().ToArray();
                var originalPositions = new[] {
                    Vector3.zero, Vector3.right, Vector3.up,
                    Vector3.zero, Vector3.right, Vector3.right * 2f
                };
                var editedPositions = (Vector3[])originalPositions.Clone();
                editedPositions[0] = new Vector3(0, 0, .5f);
                var marked = new[] {
                    true, false, false, false, false, false
                };
                string untouchedBefore = trianglesXml[1].ToString();
                W3xExportCore.UpdateTrianglePlanes(trianglesXml,
                    originalPositions, editedPositions, marked);
                if (trianglesXml[1].ToString() != untouchedBefore)
                    throw new InvalidDataException(
                        "Unchanged source-degenerate plane was rewritten.");

                editedPositions = (Vector3[])originalPositions.Clone();
                editedPositions[5] = new Vector3(.5f, 0, 0);
                marked = new[] {
                    false, false, false, false, false, true
                };
                bool modifiedDegenerateRejected = false;
                try
                {
                    W3xExportCore.UpdateTrianglePlanes(
                        trianglesXml, originalPositions,
                        editedPositions, marked);
                }
                catch (InvalidDataException e)
                {
                    modifiedDegenerateRejected =
                        e.Message.Contains("triangle 1") &&
                        e.Message.Contains("3,4,5");
                }
                if (!modifiedDegenerateRejected)
                    throw new InvalidDataException(
                        "Modified degenerate triangle needs vertex diagnostics.");

                var singleBone = mesh.boneWeights;
                singleBone[2].boneIndex1 = singleBone[2].boneIndex0;
                mesh.boneWeights = singleBone;
                bool protectedIndex = false;
                try
                {
                    W3xSkinnedMeshEditor.ModifyExistingWeight(
                        mesh, 2, .5f);
                }
                catch (InvalidDataException)
                {
                    protectedIndex = true;
                }
                if (!protectedIndex)
                    throw new InvalidDataException(
                        "Identical bone influence indices must be rejected.");

                Debug.Log(
                    "[C&C SAGE Phase 2D] Skinned mesh edit self-test PASS: " +
                    "rest vertex, normalized existing weight pair, " +
                    "unchanged topology/bindposes, safe degenerate " +
                    "triangle preservation and rejected collapsed edits.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 2D] Skinned mesh edit self-test FAILED: " +
                    e);
            }
            finally
            {
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
