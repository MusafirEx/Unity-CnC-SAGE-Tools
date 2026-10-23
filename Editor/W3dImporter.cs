using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Preview-oriented binary W3D importer for Generals / Zero Hour.
    /// No changes to the W3X parser, export commands or original source files.
    /// </summary>
    [ScriptedImporter(1, "w3d")]
    public sealed class W3dImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var data = W3dBinaryReader.Read(context.assetPath);
            W3dBinaryReader.HierarchyData hierarchy = data.Hierarchy;
            if (hierarchy == null)
            {
                string hint = data.Animations.Count != 0 ?
                    data.Animations[0].HierarchyName : data.ModelHierarchyName;
                string companion = FindHierarchyCompanion(context.assetPath, hint);
                if (companion != null)
                {
                    context.DependsOnSourceAsset(companion);
                    hierarchy = W3dBinaryReader.Read(companion).Hierarchy;
                }
            }

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            context.AddObjectToAsset("W3D_Root", root);
            context.SetMainObject(root);

            Transform[] pivots = BuildHierarchy(root.transform, hierarchy);
            string assetFolder = Path.GetDirectoryName(context.assetPath).Replace('\\', '/');
            bool anySkin = false;
            for (int i = 0; i < data.Meshes.Count; i++)
            {
                var src = data.Meshes[i];
                if (src.Vertices.Count == 0 || src.Indices.Count == 0) continue;

                var mesh = new Mesh { name = string.IsNullOrEmpty(src.Name) ? "Mesh_" + i : src.Name };
                if (src.Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = src.Vertices.ToArray();
                int[] indices = src.Indices.ToArray();
                // Swapping the W3D Y/Z axes changes handedness: reverse triangle winding.
                for (int t = 0; t < indices.Length; t += 3)
                {
                    int swap = indices[t + 1];
                    indices[t + 1] = indices[t + 2];
                    indices[t + 2] = swap;
                }
                mesh.triangles = indices;
                if (src.Uvs.Count == src.Vertices.Count) mesh.uv = src.Uvs.ToArray();
                if (src.Normals.Count == src.Vertices.Count) mesh.normals = src.Normals.ToArray();
                else mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                context.AddObjectToAsset("W3D_Mesh_" + i, mesh);

                var material = MakeMaterial(src, assetFolder, context);
                material.name = mesh.name + "_W3D_Preview";
                context.AddObjectToAsset("W3D_Material_" + i, material);

                var node = new GameObject(mesh.name);
                var parent = ResolveNodeParent(data, src, pivots);
                node.transform.SetParent(parent == null ? root.transform : parent, false);
                bool canSkin = src.BoneIndices.Count == src.Vertices.Count &&
                               pivots != null && pivots.Length > 0 &&
                               src.BoneIndices.All(b => b < pivots.Length);
                if (canSkin)
                {
                    // Generals' simple 1-bone influences. Multi-weight skinning,
                    // mesh fixup matrices and full HLOD still need real-asset testing.
                    var weights = new BoneWeight[src.Vertices.Count];
                    for (int v = 0; v < weights.Length; v++)
                    {
                        weights[v] = new BoneWeight
                        {
                            boneIndex0 = src.BoneIndices[v],
                            weight0 = 1
                        };
                    }
                    var bindposes = new Matrix4x4[pivots.Length];
                    for (int b = 0; b < pivots.Length; b++)
                        bindposes[b] = pivots[b].worldToLocalMatrix *
                                       node.transform.localToWorldMatrix;
                    mesh.boneWeights = weights;
                    mesh.bindposes = bindposes;
                    var renderer = node.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.bones = pivots;
                    renderer.rootBone = pivots[0];
                    renderer.sharedMaterial = material;
                    renderer.updateWhenOffscreen = true;
                    anySkin = true;
                }
                else
                {
                    node.AddComponent<MeshFilter>().sharedMesh = mesh;
                    node.AddComponent<MeshRenderer>().sharedMaterial = material;
                    if (src.BoneIndices.Count != 0)
                        data.Warnings.Add(mesh.name + ": skin influences could not bind to a matching hierarchy.");
                }
            }

            Animation animationComponent = null;
            for (int i = 0; i < data.Animations.Count; i++)
            {
                var source = data.Animations[i];
                if (hierarchy == null || source.FrameCount <= 0 || source.FrameRate <= 0)
                {
                    data.Warnings.Add("Animation " + source.Name +
                                      " has no matching skeleton or valid timing; clip not generated.");
                    continue;
                }
                if (!string.IsNullOrEmpty(hierarchy.Name) &&
                    !string.IsNullOrEmpty(source.HierarchyName) &&
                    !string.Equals(hierarchy.Name, source.HierarchyName,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    data.Warnings.Add("Animation " + source.Name +
                                      " hierarchy differs: " + source.HierarchyName +
                                      " vs " + hierarchy.Name);
                    continue;
                }
                AnimationClip clip = BuildClip(source, hierarchy, data.Warnings);
                clip.name = string.IsNullOrEmpty(source.Name) ? "W3D_Animation_" + i : source.Name;
                context.AddObjectToAsset("W3D_Animation_" + i, clip);
                if (animationComponent == null)
                    animationComponent = root.AddComponent<Animation>();
                animationComponent.AddClip(clip, clip.name);
                if (animationComponent.clip == null)
                    animationComponent.clip = clip;
            }

            if (anySkin)
                data.Warnings.Add("W3D skin preview is experimental: fixup matrices and multi-weight skinning are not reconstructed.");
            if (data.Meshes.Count == 0 && data.Animations.Count == 0 && hierarchy == null)
                data.Warnings.Add("No supported W3D mesh, hierarchy or uncompressed animation found.");
            foreach (string warning in data.Warnings.Distinct())
                Debug.LogWarning("[C&C SAGE W3D] " + context.assetPath + ": " + warning);
        }

        private static Transform[] BuildHierarchy(Transform root, W3dBinaryReader.HierarchyData source)
        {
            if (source == null || source.Pivots.Count == 0) return null;
            var container = new GameObject("SAGE_Pivots").transform;
            container.SetParent(root, false);
            var transforms = new Transform[source.Pivots.Count];
            for (int i = 0; i < transforms.Length; i++)
            {
                var pivot = source.Pivots[i];
                transforms[i] = new GameObject(string.IsNullOrEmpty(pivot.Name)
                    ? "Pivot_" + i : pivot.Name).transform;
            }

            // Parent references may appear after child pivots in the source.
            for (int i = 0; i < transforms.Length; i++)
            {
                int parent = source.Pivots[i].ParentIndex;
                var visited = new HashSet<int> { i };
                int ancestor = parent;
                while (ancestor >= 0)
                {
                    if (ancestor >= transforms.Length || !visited.Add(ancestor))
                        throw new InvalidDataException("W3D hierarchy contains an invalid parent chain.");
                    ancestor = source.Pivots[ancestor].ParentIndex;
                }
                transforms[i].SetParent(parent < 0 ? container : transforms[parent], false);
                transforms[i].localPosition = source.Pivots[i].Position;
                Quaternion q = source.Pivots[i].Rotation;
                transforms[i].localRotation = q.x * q.x + q.y * q.y + q.z * q.z +
                                              q.w * q.w > 0.00001f ? q : Quaternion.identity;
            }
            return transforms;
        }

        private static Transform ResolveNodeParent(W3dBinaryReader.FileData data,
            W3dBinaryReader.MeshData mesh, Transform[] pivots)
        {
            if (pivots == null) return null;
            var node = data.ModelNodes.FirstOrDefault(n =>
                string.Equals(n.MeshName, mesh.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.MeshName, mesh.Container + "." + mesh.Name,
                              StringComparison.OrdinalIgnoreCase));
            if (node == null || node.PivotIndex < 0 || node.PivotIndex >= pivots.Length)
                return null;
            return pivots[node.PivotIndex];
        }

        private static Material MakeMaterial(W3dBinaryReader.MeshData mesh,
            string folder, AssetImportContext context)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader);
            if (mesh.Textures.Count == 0) return material;
            string filename = Path.GetFileName(mesh.Textures[0].Replace('\\', '/'));
            string basename = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrEmpty(basename)) return material;

            // Only bind extracted textures in the same Unity folder.
            // Never silently read from outside the Unity asset database.
            string[] guids = AssetDatabase.FindAssets(basename + " t:Texture2D",
                new[] { folder });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(assetPath),
                    basename, StringComparison.OrdinalIgnoreCase))
                    continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null) continue;
                material.mainTexture = texture;
                if (!string.Equals(assetPath, context.assetPath,
                    StringComparison.OrdinalIgnoreCase))
                    context.DependsOnSourceAsset(assetPath);
                break;
            }
            return material;
        }

        private static AnimationClip BuildClip(W3dBinaryReader.AnimationData source,
            W3dBinaryReader.HierarchyData hierarchy, List<string> warnings)
        {
            var clip = new AnimationClip { frameRate = source.FrameRate, legacy = true };
            foreach (var channel in source.Channels)
            {
                if (channel.Pivot < 0 || channel.Pivot >= hierarchy.Pivots.Count)
                {
                    warnings.Add(source.Name + ": animation channel targets a missing pivot.");
                    continue;
                }
                string path = PivotPath(hierarchy, channel.Pivot);
                string[] properties;
                int[] components;
                float[] signs;
                switch (channel.Type)
                {
                    case 0: properties = new[] { "m_LocalPosition.x" }; components = new[] { 0 }; signs = new[] { 1f }; break;
                    case 1: properties = new[] { "m_LocalPosition.z" }; components = new[] { 0 }; signs = new[] { 1f }; break;
                    case 2: properties = new[] { "m_LocalPosition.y" }; components = new[] { 0 }; signs = new[] { 1f }; break;
                    case 6:
                        properties = new[] { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                        components = new[] { 0, 2, 1, 3 };
                        signs = new[] { -1f, -1f, -1f, 1f };
                        break;
                    default:
                        warnings.Add(source.Name + ": unsupported rotation/Euler channel type " + channel.Type);
                        continue;
                }
                if ((channel.Type == 6 && channel.VectorLength != 4) ||
                    (channel.Type != 6 && channel.VectorLength != 1))
                {
                    warnings.Add(source.Name + ": unexpected vector width in animation channel.");
                    continue;
                }
                for (int axis = 0; axis < properties.Length; axis++)
                {
                    var keys = new Keyframe[channel.Last - channel.First + 1];
                    for (int frame = channel.First; frame <= channel.Last; frame++)
                    {
                        int index = (frame - channel.First) * channel.VectorLength + components[axis];
                        keys[frame - channel.First] = new Keyframe(
                            (float)frame / source.FrameRate, signs[axis] * channel.Values[index]);
                    }
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform), properties[axis]),
                        new AnimationCurve(keys));
                }
            }
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static string PivotPath(W3dBinaryReader.HierarchyData data, int index)
        {
            var segments = new List<string>();
            var visited = new HashSet<int>();
            while (index >= 0)
            {
                if (index >= data.Pivots.Count || !visited.Add(index))
                    throw new InvalidDataException("Invalid W3D animation hierarchy.");
                var p = data.Pivots[index];
                segments.Add(string.IsNullOrEmpty(p.Name) ? "Pivot_" + index : p.Name);
                index = p.ParentIndex;
            }
            segments.Reverse();
            return "SAGE_Pivots/" + string.Join("/", segments.ToArray());
        }

        private static string FindHierarchyCompanion(string assetPath, string name)
        {
            string folder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string sourceName = Path.GetFileNameWithoutExtension(assetPath);
            string modelBase = sourceName.EndsWith("_SKN", StringComparison.OrdinalIgnoreCase) ?
                sourceName.Substring(0, sourceName.Length - 4) : sourceName;
            string[] candidates = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".w3d",
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string path in candidates)
            {
                if (string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                string stem = Path.GetFileNameWithoutExtension(path);
                if ((!string.IsNullOrEmpty(name) &&
                    (string.Equals(stem, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(stem, name + "_SKL", StringComparison.OrdinalIgnoreCase))) ||
                    string.Equals(stem, modelBase + "_SKL", StringComparison.OrdinalIgnoreCase))
                    return path.Replace('\\', '/');
            }
            return null;
        }
    }
}
