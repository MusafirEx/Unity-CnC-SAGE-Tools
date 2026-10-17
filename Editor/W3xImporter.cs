using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Prototype importer for editable W3X XML geometry.
    /// W3D is a different binary format and is intentionally not registered.
    /// </summary>
    [ScriptedImporter(1, "w3x")]
    public sealed class W3xImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var document = W3xReader.Read(context.assetPath);

            foreach (var warning in document.Warnings)
                Debug.LogWarning("[C&C SAGE] " + warning);

            // Only dependencies inside the Unity project can be registered.
            foreach (var source in document.SourcePaths)
            {
                var normalized = source.Replace('\\', '/');
                var projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
                if (!normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = normalized.Substring(projectRoot.Length);
                if (relative != context.assetPath.Replace('\\', '/'))
                    context.DependsOnSourceAsset(relative);
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            context.AddObjectToAsset("root", root);
            context.SetMainObject(root);

            for (int i = 0; i < document.Meshes.Count; i++)
            {
                var data = document.Meshes[i];
                var child = new GameObject(data.Identifier);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<CncSageMeshIdentity>().SetSageIdentifier(data.Identifier);

                var filter = child.AddComponent<MeshFilter>();
                child.AddComponent<MeshRenderer>();

                var mesh = new Mesh { name = data.Identifier };
                if (data.Positions.Length > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;

                var vertices = new Vector3[data.Positions.Length];
                for (int v = 0; v < vertices.Length; v++)
                    vertices[v] = ToUnity(data.Positions[v]);
                mesh.vertices = vertices;

                // Swapping Y/Z changes handedness. Reverse winding to keep front faces.
                var triangles = new int[data.TriangleIndices.Length];
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    triangles[t] = data.TriangleIndices[t];
                    triangles[t + 1] = data.TriangleIndices[t + 2];
                    triangles[t + 2] = data.TriangleIndices[t + 1];
                }
                mesh.triangles = triangles;

                if (data.Normals.Length == vertices.Length)
                {
                    var normals = new Vector3[vertices.Length];
                    for (int v = 0; v < vertices.Length; v++)
                        normals[v] = ToUnity(data.Normals[v]).normalized;
                    mesh.normals = normals;
                }
                else
                {
                    mesh.RecalculateNormals();
                    if (data.Normals.Length > 0)
                        Debug.LogWarning("[C&C SAGE] Incorrect normals count in " + data.Identifier);
                }

                if (data.Uvs.Length == vertices.Length)
                    mesh.uv = data.Uvs;
                else if (data.Uvs.Length > 0)
                    Debug.LogWarning("[C&C SAGE] Incorrect UV count in " + data.Identifier);

                mesh.RecalculateBounds();
                context.AddObjectToAsset("mesh_" + i, mesh);
                filter.sharedMesh = mesh;

                if (!string.Equals(data.GeometryType, "Normal", StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning("[C&C SAGE] " + data.Identifier +
                        " uses geometry type " + data.GeometryType +
                        "; special rendering/skinning is not implemented.");
            }
        }

        // Preliminary Z-up to Unity Y-up mapping. Verify against a real SAGE model.
        private static Vector3 ToUnity(Vector3 value)
        {
            return new Vector3(value.x, value.z, value.y);
        }
    }
}
