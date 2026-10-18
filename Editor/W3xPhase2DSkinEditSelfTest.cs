using System;
using System.IO;
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
                    "unchanged topology/bindposes, guard on invalid indices.");
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
