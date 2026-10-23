using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 1B reconstruction: W3X pivots, simple skinning and basic FX materials.
    /// This is not a full SAGE renderer or animation importer.
    /// </summary>
    internal static class W3xModelBuilder
    {
        private static readonly string[] TextureExtensions =
            { ".tga", ".dds", ".png", ".jpg", ".jpeg", ".bmp" };

        internal static void Build(AssetImportContext context, W3xDocument document,
            GameObject root, SageImportOptions options,
            List<AnimationClip> importedClips)
        {
            W3xHierarchyData hierarchy = ChooseHierarchy(document);
            Transform[] pivots = BuildPivots(root.transform, hierarchy);

            for (int i = 0; i < (options == null || options.importMeshes ?
                document.Meshes.Count : 0); i++)
            {
                W3xMeshData data = document.Meshes[i];
                var part = new GameObject(data.Identifier);
                part.transform.SetParent(root.transform, false);
                part.AddComponent<CncSageMeshIdentity>().SetSageIdentifier(data.Identifier);

                var mesh = new Mesh { name = data.Identifier };
                if (data.Positions.Length > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;

                var vertices = data.Positions.Select(ToUnity).ToArray();
                int[] triangles = new int[data.TriangleIndices.Length];
                for (int j = 0; j < triangles.Length; j += 3)
                {
                    triangles[j] = data.TriangleIndices[j];
                    triangles[j + 1] = data.TriangleIndices[j + 2];
                    triangles[j + 2] = data.TriangleIndices[j + 1];
                }

                bool wantsSkin = string.Equals(data.GeometryType, "Skin",
                    StringComparison.OrdinalIgnoreCase);
                BoneWeight[] weights = null;
                bool canSkin = wantsSkin && TryGetWeights(data, pivots, out weights);

                // SAGE skin vertices are authored in their influencing pivot space.
                // Bake source rest-pivot transforms, as Blender does for this format.
                // This is still provisional pending validated in-game rest-pose samples.
                if (canSkin)
                {
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var weight = weights[v];
                        var source = vertices[v];
                        var world0 = root.transform.InverseTransformPoint(
                            pivots[weight.boneIndex0].TransformPoint(source));
                        if (weight.weight1 <= 0)
                            vertices[v] = world0;
                        else
                        {
                            var world1 = root.transform.InverseTransformPoint(
                                pivots[weight.boneIndex1].TransformPoint(source));
                            vertices[v] = world0 * weight.weight0 +
                                world1 * weight.weight1;
                        }
                    }
                }

                mesh.vertices = vertices;
                mesh.triangles = triangles;
                if (data.Normals.Length == vertices.Length)
                {
                    var normals = data.Normals.Select(ToUnity).ToArray();
                    if (canSkin)
                    {
                        for (int v = 0; v < normals.Length; v++)
                        {
                            var weight = weights[v];
                            var first = root.transform.InverseTransformDirection(
                                pivots[weight.boneIndex0].TransformDirection(normals[v]));
                            if (weight.weight1 <= 0)
                                normals[v] = first.normalized;
                            else
                            {
                                var second = root.transform.InverseTransformDirection(
                                    pivots[weight.boneIndex1].TransformDirection(normals[v]));
                                normals[v] = (first * weight.weight0 +
                                    second * weight.weight1).normalized;
                            }
                        }
                    }
                    mesh.normals = normals;
                }
                else
                {
                    mesh.RecalculateNormals();
                }

                if (data.Uvs.Length == vertices.Length)
                    mesh.uv = data.Uvs;
                else if (data.Uvs.Length != 0)
                    Debug.LogWarning("[C&C SAGE] UV0 count mismatch: " + data.Identifier);

                if (data.Uvs2.Length == vertices.Length)
                    mesh.uv2 = data.Uvs2;
                else if (data.Uvs2.Length != 0)
                    Debug.LogWarning("[C&C SAGE] UV1 count mismatch: " + data.Identifier);

                if (data.UvSetCount > 2)
                    Debug.LogWarning("[C&C SAGE] Only the first 2 UV sets are supported: " +
                        data.Identifier);

                mesh.RecalculateBounds();

                Renderer renderer;
                if (canSkin)
                {
                    var skinned = part.AddComponent<SkinnedMeshRenderer>();
                    mesh.boneWeights = weights;
                    var bindposes = new Matrix4x4[pivots.Length];
                    for (int j = 0; j < pivots.Length; j++)
                        bindposes[j] = pivots[j].worldToLocalMatrix *
                            part.transform.localToWorldMatrix;
                    mesh.bindposes = bindposes;
                    skinned.bones = pivots;
                    skinned.rootBone = pivots[0];
                    skinned.sharedMesh = mesh;
                    skinned.localBounds = mesh.bounds;
                    renderer = skinned;
                }
                else
                {
                    var filter = part.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    renderer = part.AddComponent<MeshRenderer>();
                    if (wantsSkin)
                        Debug.LogWarning("[C&C SAGE] " + data.Identifier +
                            " is Skin but has no valid skeleton + influences. " +
                            "Imported static mesh instead.");
                }

                if (!canSkin)
                    AttachStaticSubObject(part.transform, data.Identifier,
                        document.Containers, pivots);

                var materials = MakeMaterials(context, data, i);
                if (materials.Length > 0)
                {
                    renderer.sharedMaterial = materials[0];
                    // Materials beyond the first are preserved as sub-assets,
                    // but triangle-to-shader slot mapping is not yet implemented.
                    if (materials.Length > 1)
                        Debug.LogWarning("[C&C SAGE] " + data.Identifier +
                            " has multiple FXShader passes; assigning first pass only.");
                }
                context.AddObjectToAsset("mesh_" + i, mesh);
            }

            // Phase 3A: store AnimationClips as independent W3X import
            // sub-assets. No Animation/Animator runtime component or
            // controller is generated; Phase 3B will add authoring tools.
            if (options == null || options.importAnimations)
                W3xAnimationClipBuilder.Build(document, root, hierarchy, pivots,
                    (key, clip) =>
                    {
                        context.AddObjectToAsset(key, clip);
                        importedClips.Add(clip);
                    });
        }

        private static W3xHierarchyData ChooseHierarchy(W3xDocument document)
        {
            if (document.Hierarchies.Count == 0)
                return null;
            foreach (var container in document.Containers)
            {
                var match = document.Hierarchies.FirstOrDefault(h =>
                    string.Equals(h.Identifier, container.HierarchyName,
                        StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }
            if (document.Hierarchies.Count > 1)
                Debug.LogWarning("[C&C SAGE] Multiple hierarchies without " +
                    "an unambiguous container association; using the first.");
            return document.Hierarchies[0];
        }

        private static Transform[] BuildPivots(Transform root, W3xHierarchyData hierarchy)
        {
            if (hierarchy == null || hierarchy.Pivots.Count == 0)
                return new Transform[0];

            var holder = new GameObject("SAGE_Pivots");
            holder.transform.SetParent(root, false);
            var result = new Transform[hierarchy.Pivots.Count];

            // Allocate all transforms before assigning parents to allow arbitrary
            // W3X pivot index order.
            for (int i = 0; i < result.Length; i++)
            {
                var go = new GameObject(hierarchy.Pivots[i].Name);
                go.transform.SetParent(holder.transform, false);
                result[i] = go.transform;
            }

            for (int i = 0; i < result.Length; i++)
            {
                var pivot = hierarchy.Pivots[i];
                bool validParent = pivot.Parent >= 0 &&
                    pivot.Parent < result.Length && pivot.Parent != i;
                // Prevent malformed cyclic hierarchies.
                if (validParent)
                {
                    var parent = pivot.Parent;
                    var seen = new HashSet<int> { i };
                    while (parent >= 0 && parent < result.Length)
                    {
                        if (!seen.Add(parent))
                        {
                            validParent = false;
                            break;
                        }
                        parent = hierarchy.Pivots[parent].Parent;
                    }
                }

                result[i].SetParent(validParent ? result[pivot.Parent] :
                    holder.transform, false);
                result[i].localPosition = ToUnity(pivot.Translation);
                result[i].localRotation = ToUnity(pivot.Rotation);
                result[i].gameObject.AddComponent<CncSagePivotInfo>()
                    .SetSource(pivot.NameId, i, pivot.Parent, pivot.FixupXml);
            }

            return result;
        }

        private static bool TryGetWeights(W3xMeshData data,
            Transform[] pivots, out BoneWeight[] result)
        {
            result = null;
            int count = data.Positions.Length;
            if (pivots.Length == 0 || data.BoneInfluences == null ||
                data.BoneInfluences.Length != count)
                return false;
            var secondary = data.SecondaryBoneInfluences;
            if (secondary != null && secondary.Length > 0 &&
                secondary.Length != count)
                return false;

            result = new BoneWeight[count];
            for (int v = 0; v < count; v++)
            {
                var p = data.BoneInfluences[v];
                var s = secondary != null && secondary.Length > 0 ?
                    secondary[v] : new W3xBoneInfluence();
                if (p.Bone < 0 || p.Bone >= pivots.Length ||
                    s.Weight > 0 && (s.Bone < 0 || s.Bone >= pivots.Length))
                {
                    result = null;
                    return false;
                }
                float w0 = Mathf.Max(0f, p.Weight);
                float w1 = Mathf.Max(0f, s.Weight);
                float sum = w0 + w1;
                if (sum < 0.0001f)
                {
                    w0 = 1f;
                    w1 = 0f;
                    sum = 1f;
                }
                result[v] = new BoneWeight
                {
                    boneIndex0 = p.Bone,
                    boneIndex1 = w1 > 0 ? s.Bone : 0,
                    weight0 = w0 / sum,
                    weight1 = w1 / sum
                };
            }
            return true;
        }

        private static void AttachStaticSubObject(Transform part, string identifier,
            List<W3xContainerData> containers, Transform[] pivots)
        {
            if (pivots.Length == 0)
                return;
            string partName = identifier.Contains(".") ?
                identifier.Substring(identifier.IndexOf('.') + 1) : identifier;

            foreach (var container in containers)
            foreach (var subObject in container.SubObjects)
            {
                bool matches = string.Equals(subObject.Identifier, identifier,
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(subObject.Identifier, partName,
                        StringComparison.OrdinalIgnoreCase);
                if (!matches || subObject.BoneIndex < 0 ||
                    subObject.BoneIndex >= pivots.Length)
                    continue;

                part.SetParent(pivots[subObject.BoneIndex], false);
                return;
            }
        }

        private static Material[] MakeMaterials(AssetImportContext context,
            W3xMeshData mesh, int index)
        {
            if (mesh.Shaders == null || mesh.Shaders.Length == 0)
                return new Material[0];

            var result = new Material[mesh.Shaders.Length];
            Shader shader = Shader.Find("Standard") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Unlit/Texture") ??
                Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("[C&C SAGE] No compatible fallback Unity shader found.");
                return new Material[0];
            }

            for (int i = 0; i < mesh.Shaders.Length; i++)
            {
                var fx = mesh.Shaders[i];
                var material = new Material(shader)
                {
                    name = mesh.Identifier + "." + fx.Name + "." + i
                };

                string diffuse = FirstNotEmpty(fx.Get("DiffuseTexture"),
                    fx.Get("Texture_0"));
                var tex = FindTexture(context, diffuse);
                if (tex != null)
                {
                    if (material.HasProperty("_MainTex"))
                        material.SetTexture("_MainTex", tex);
                    if (material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", tex);
                }

                string normalName = fx.Get("NormalMap");
                var normal = FindTexture(context, normalName);
                if (normal != null)
                {
                    if (material.HasProperty("_BumpMap"))
                        material.SetTexture("_BumpMap", normal);
                    if (material.HasProperty("_NormalMap"))
                        material.SetTexture("_NormalMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }

                string specName = fx.Get("SpecMap");
                var spec = FindTexture(context, specName);
                if (spec != null && material.HasProperty("_SpecGlossMap"))
                    material.SetTexture("_SpecGlossMap", spec);

                context.AddObjectToAsset("material_" + index + "_" + i, material);
                var metadata = ScriptableObject.CreateInstance<CncSageMaterialMetadata>();
                metadata.name = material.name + ".SAGE_Source";
                metadata.SetSource(fx.Name, fx.Properties);
                context.AddObjectToAsset("materialmeta_" + index + "_" + i, metadata);
                result[i] = material;
            }
            return result;
        }

        private static string FirstNotEmpty(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) ? a : b;
        }

        private static Texture2D FindTexture(AssetImportContext context, string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            source = source.Replace('\\', '/');
            if (source.StartsWith("ART:", StringComparison.OrdinalIgnoreCase))
                source = source.Substring(4);
            // Do not resolve arbitrary absolute paths or remote names.
            if (source.IndexOf(':') >= 0 || Path.IsPathRooted(source))
                return null;

            string assetDirectory = (Path.GetDirectoryName(context.assetPath) ?? "Assets")
                .Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrWhiteSpace(baseName))
                return null;

            // Match case-insensitively even on filesystems where a source W3X
            // uses a different case from its texture asset.
            if (!Directory.Exists(assetDirectory))
                return null;
            foreach (var file in Directory.GetFiles(assetDirectory))
            {
                if (!TextureExtensions.Any(ext =>
                    file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(file),
                    baseName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string path = file.Replace('\\', '/');
                context.DependsOnSourceAsset(path);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return null;
        }

        private static Vector3 ToUnity(Vector3 p)
        {
            return new Vector3(p.x, p.z, p.y);
        }

        private static Quaternion ToUnity(Quaternion q)
        {
            // P * R * P^-1, where P swaps source Y / Z (determinant -1).
            return new Quaternion(-q.x, -q.z, -q.y, q.w).normalized;
        }
    }
}
