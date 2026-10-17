using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Explicit Phase 2B authoring subset. Never generates missing SAGE
    /// declarations or rewrites source bind matrices automatically.
    /// </summary>
    internal static class W3xExportAdvanced
    {
        private const float PositionEpsilon = 0.001f;
        private const float WeightEpsilon = 0.0001f;

        internal static void PatchLocalPivots(
            XDocument output, GameObject model, W3xExportCore.Report report)
        {
            var hierarchyNodes = output.Root.Elements()
                .Where(e => e.Name.LocalName == "W3DHierarchy").ToArray();
            var pivotComponents = model
                .GetComponentsInChildren<CncSagePivotInfo>(true);
            if (pivotComponents.Length == 0)
                return;

            if (hierarchyNodes.Length == 0)
                throw new InvalidDataException(
                    "Pivots are defined in an external _SKL.W3X file. " +
                    "Select and export that hierarchy asset separately. " +
                    "The model W3X must not silently lose bone edits.");
            if (hierarchyNodes.Length != 1)
                throw new InvalidDataException(
                    "Multiple local W3DHierarchy declarations are ambiguous.");

            var pivots = W3xExportCore.Children(hierarchyNodes[0], "Pivot").ToArray();
            if (pivots.Length != pivotComponents.Length)
                throw new InvalidDataException(
                    "Source/Unity pivot counts differ. Bone additions/removals " +
                    "are not supported yet.");

            var byIndex = new Dictionary<int, CncSagePivotInfo>();
            foreach (var pivot in pivotComponents)
            {
                if (byIndex.ContainsKey(pivot.PivotIndex))
                    throw new InvalidDataException(
                        "Duplicate SAGE pivot index " + pivot.PivotIndex);
                byIndex.Add(pivot.PivotIndex, pivot);
            }

            for (int i = 0; i < pivots.Length; i++)
            {
                CncSagePivotInfo info;
                if (!byIndex.TryGetValue(i, out info))
                    throw new InvalidDataException(
                        "Unity pivot index " + i + " is missing.");

                XElement source = pivots[i];
                int sourceParent = SourceInt((string)source.Attribute("Parent"), -1);
                int sourceNameId = SourceInt((string)source.Attribute("NameID"), i);
                if (!string.Equals((string)source.Attribute("Name"),
                    info.name, StringComparison.Ordinal) ||
                    sourceNameId != info.NameId ||
                    sourceParent != info.ParentIndex)
                    throw new InvalidDataException(
                        "W3DHierarchy pivot identity or parent changed at " +
                        i + " (" + info.name + "). Export halted.");

                var parentInfo = info.transform.parent == null ? null :
                    info.transform.parent.GetComponent<CncSagePivotInfo>();
                int actualParent = parentInfo == null ? -1 : parentInfo.PivotIndex;
                if (actualParent != sourceParent)
                    throw new InvalidDataException(
                        "Unity pivot parent was changed: " + info.name +
                        ". Reparenting requires a full skeleton exporter.");

                if ((info.transform.localScale - Vector3.one).sqrMagnitude >
                    0.00000001f)
                    throw new InvalidDataException(
                        "Scaled SAGE pivot is unsupported: " + info.name);

                var translationNode = W3xExportCore.Child(source, "Translation");
                var rotationNode = W3xExportCore.Child(source, "Rotation");
                Vector3 sourcePosition = translationNode == null ? Vector3.zero :
                    W3xExportCore.ReadSourceVector3(translationNode);
                Vector3 editedPosition = W3xExportCore.ToSage(info.transform.localPosition);
                Quaternion sourceRotation = rotationNode == null ?
                    Quaternion.identity : SourceQuaternion(rotationNode);
                Quaternion editedRotation = ToSage(info.transform.localRotation);
                bool positionChanged = !W3xExportCore.Close(
                    sourcePosition, editedPosition);
                bool rotationChanged = Mathf.Abs(Quaternion.Dot(
                    sourceRotation.normalized, editedRotation.normalized)) <
                    0.999999f;

                if (!positionChanged && !rotationChanged)
                    continue;
                // These matrices are required by SAGE to keep skin rest bind
                // transforms coherent. Preserving a stale matrix is unsafe.
                if (W3xExportCore.Child(source, "FixupMatrix") != null)
                    throw new InvalidDataException(
                        "Pivot " + info.name + " uses FixupMatrix. " +
                        "Editing its transform needs fixup matrix recalculation " +
                        "and is not available in Phase 2B.");

                XNamespace ns = source.Name.Namespace;
                if (positionChanged)
                {
                    if (translationNode == null)
                    {
                        translationNode = new XElement(ns + "Translation");
                        source.Add(translationNode);
                    }
                    W3xExportCore.WriteVector3(
                        translationNode, editedPosition);
                }
                if (rotationChanged)
                {
                    if (rotationNode == null)
                    {
                        rotationNode = new XElement(ns + "Rotation");
                        source.Add(rotationNode);
                    }
                    WriteQuaternion(rotationNode, editedRotation);
                }
                report.ChangedPivots++;
            }
        }

        internal static void ValidatePivotMetadata(
            XElement before, XElement after)
        {
            var oldClone = new XElement(before);
            var newClone = new XElement(after);
            // Only local translation and rotation nodes may differ. All pivot
            // attributes, parent links, fixups and hierarchy structure remain.
            foreach (var e in new[] { oldClone, newClone })
            foreach (var pivot in W3xExportCore.Children(e, "Pivot"))
            {
                var descendants = pivot.Elements().Where(x =>
                    x.Name.LocalName == "Translation" ||
                    x.Name.LocalName == "Rotation").ToArray();
                foreach (var child in descendants)
                    child.Remove();
            }
            if (!XNode.DeepEquals(oldClone, newClone))
                throw new InvalidDataException(
                    "W3DHierarchy metadata other than translation/rotation changed.");
        }

        internal static void PatchShaderTextures(
            XElement mesh, GameObject part, W3xExportCore.Report report)
        {
            var shaders = W3xExportCore.Children(mesh, "FXShader").ToArray();
            if (shaders.Length == 0)
                return;
            if (shaders.Length > 1)
                throw new InvalidDataException(
                    (string)mesh.Attribute("id") +
                    " has multiple FXShader passes. Shader pass mapping " +
                    "is not supported for advanced export.");

            var renderer = part.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null)
                return;
            var material = renderer.sharedMaterial;

            var constants = W3xExportCore.Child(shaders[0], "Constants");
            if (constants == null)
                return;

            var diffuse = GetTexture(material, "_BaseMap", "_MainTex");
            var normal = GetTexture(material, "_BumpMap", "_NormalMap");
            var specular = GetTexture(material, "_SpecGlossMap");
            PatchTextureConstant(constants, "DiffuseTexture", diffuse, report);
            PatchTextureConstant(constants, "Texture_0", diffuse, report);
            PatchTextureConstant(constants, "NormalMap", normal, report);
            PatchTextureConstant(constants, "SpecMap", specular, report);
        }

        private static Texture GetTexture(Material material, params string[] names)
        {
            foreach (string name in names)
            {
                if (!material.HasProperty(name))
                    continue;
                var tex = material.GetTexture(name);
                if (tex != null)
                    return tex;
            }
            return null;
        }

        private static void PatchTextureConstant(
            XElement constants, string name, Texture texture,
            W3xExportCore.Report report)
        {
            if (texture == null)
                return;
            var entry = constants.Elements().FirstOrDefault(e =>
                string.Equals((string)e.Attribute("Name"), name,
                    StringComparison.OrdinalIgnoreCase));
            if (entry == null || entry.Name.LocalName != "Texture")
                return;

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Changed " + name + " texture is not a project asset. " +
                    "Export it into Assets first.");

            var value = W3xExportCore.Child(entry, "Value");
            if (value == null)
                throw new InvalidDataException(
                    "Missing FXShader constant Value for " + name);
            string newName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidDataException(
                    "Cannot resolve W3X texture name: " + path);
            string oldName = Path.GetFileNameWithoutExtension(value.Value.Trim()
                .Replace('\\', '/'));
            if (string.Equals(oldName, newName,
                StringComparison.OrdinalIgnoreCase))
                return;
            value.Value = newName;
            report.ChangedShaderTextures++;
        }

        internal static void PatchSkin(
            XElement source, GameObject part, W3xExportCore.Report report)
        {
            string id = (string)source.Attribute("id") ?? "(unknown)";
            var skinned = part.GetComponent<SkinnedMeshRenderer>();
            if (skinned == null || skinned.sharedMesh == null)
                throw new InvalidDataException(id +
                    " cannot export skinned edits without SkinnedMeshRenderer.");

            var mesh = skinned.sharedMesh;
            var bones = skinned.bones;
            var verticesNode = W3xExportCore.Child(source, "Vertices");
            var trianglesNode = W3xExportCore.Child(source, "Triangles");
            if (verticesNode == null || trianglesNode == null)
                throw new InvalidDataException(id +
                    " lacks source mesh vertices or triangles.");
            var originalVertexNodes = W3xExportCore.Children(
                verticesNode, "V").ToArray();
            if (originalVertexNodes.Length != mesh.vertexCount ||
                mesh.subMeshCount != 1)
                throw new InvalidDataException(id +
                    " vertex/submesh topology is different from source.");
            var triangles = W3xExportCore.Children(trianglesNode, "T").ToArray();
            var indices = mesh.GetTriangles(0);
            if (indices.Length != triangles.Length * 3)
                throw new InvalidDataException(id +
                    " triangle count differs from source.");
            for (int i = 0; i < triangles.Length; i++)
            {
                var old = W3xExportCore.Children(triangles[i], "V")
                    .Select(x => W3xExportCore.ParseIndex(x, id)).ToArray();
                if (old.Length != 3 || old[0] != indices[i * 3] ||
                    old[2] != indices[i * 3 + 1] ||
                    old[1] != indices[i * 3 + 2])
                    throw new InvalidDataException(id +
                        " topology/winding changed. Skin export aborted.");
            }

            var influenceGroups = W3xExportCore.Children(
                source, "BoneInfluences").ToArray();
            if (influenceGroups.Length < 1 || influenceGroups.Length > 2)
                throw new InvalidDataException(id +
                    " requires one or two source BoneInfluences groups.");
            var primary = W3xExportCore.Children(influenceGroups[0], "I").ToArray();
            var secondary = influenceGroups.Length == 2 ?
                W3xExportCore.Children(influenceGroups[1], "I").ToArray() : null;
            if (primary.Length != mesh.vertexCount ||
                secondary != null && secondary.Length != mesh.vertexCount)
                throw new InvalidDataException(id +
                    " original influence counts do not match Unity vertices.");

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length != mesh.vertexCount ||
                bones == null || bones.Length == 0 ||
                mesh.bindposes.Length != bones.Length)
                throw new InvalidDataException(id +
                    " needs complete Unity bones, bindposes and bone weights.");

            for (int i = 0; i < bones.Length; i++)
            {
                var info = bones[i] == null ? null :
                    bones[i].GetComponent<CncSagePivotInfo>();
                if (info == null || info.PivotIndex != i)
                    throw new InvalidDataException(id +
                        " bone order differs from SAGE pivot indices.");
                if ((bones[i].lossyScale - Vector3.one).sqrMagnitude >
                    0.000001f)
                    throw new InvalidDataException(id +
                        " non-unit bone scales require validated matrix handling.");
            }

            Vector3[] sourceVertices = originalVertexNodes.Select(
                W3xExportCore.ReadSourceVector3).ToArray();
            var authoredVertices = mesh.vertices;
            var authoredNormals = mesh.normals;
            var originalNormals = W3xExportCore.Child(source, "Normals");
            var sourceNormalNodes = originalNormals == null ?
                new XElement[0] :
                W3xExportCore.Children(originalNormals, "N").ToArray();
            bool haveNormals = sourceNormalNodes.Length == mesh.vertexCount &&
                authoredNormals.Length == mesh.vertexCount;
            var outputVertices = new Vector3[mesh.vertexCount];
            var outputNormals = haveNormals ?
                new Vector3[mesh.vertexCount] : null;
            var outputWeights0 = new float[mesh.vertexCount];
            var outputWeights1 = new float[mesh.vertexCount];
            int changedVertices = 0;
            int changedWeights = 0;

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                BoneWeight w = weights[i];
                if (w.weight2 > WeightEpsilon || w.weight3 > WeightEpsilon ||
                    w.weight0 < 0 || w.weight1 < 0)
                    throw new InvalidDataException(id +
                        " supports up to two nonnegative bone influences.");
                if (w.boneIndex0 < 0 || w.boneIndex0 >= bones.Length ||
                    w.weight1 > WeightEpsilon &&
                        (w.boneIndex1 < 0 || w.boneIndex1 >= bones.Length))
                    throw new InvalidDataException(id +
                        " has out-of-range Unity bone index at vertex " + i);
                int sourceIndex0 = SourceInt((string)primary[i].Attribute("Bone"), -1);
                int sourceIndex1 = secondary == null ? 0 :
                    SourceInt((string)secondary[i].Attribute("Bone"), 0);
                if (w.boneIndex0 != sourceIndex0 ||
                    secondary != null && w.boneIndex1 != sourceIndex1 &&
                        w.weight1 > WeightEpsilon)
                    throw new InvalidDataException(id +
                        " changing bone assignments is unsupported; " +
                        "only weights on existing bones may be edited.");
                if (secondary == null && w.weight1 > WeightEpsilon)
                    throw new InvalidDataException(id +
                        " requires an existing second BoneInfluences group.");

                float total = w.weight0 + w.weight1;
                if (total < 0.00001f || float.IsNaN(total) ||
                    float.IsInfinity(total))
                    throw new InvalidDataException(id +
                        " invalid bone weight sum at vertex " + i);
                float weight0 = w.weight0 / total;
                float weight1 = w.weight1 / total;
                float old0 = W3xExportCore.ParseFloat(
                    (string)primary[i].Attribute("Weight"));
                float old1 = secondary == null ? 0f :
                    W3xExportCore.ParseFloat(
                        (string)secondary[i].Attribute("Weight"));
                float oldTotal = old0 + old1;
                if (oldTotal <= 0)
                {
                    old0 = 1;
                    old1 = 0;
                    oldTotal = 1;
                }
                if (Mathf.Abs(weight0 - old0 / oldTotal) > WeightEpsilon ||
                    Mathf.Abs(weight1 - old1 / oldTotal) > WeightEpsilon)
                    changedWeights++;

                // Undo the Phase 1B rest-pose bake using a weighted affine
                // combination of the current bone-to-mesh transforms. Unlike
                // simply exporting mesh.vertices, this retains the visible
                // rest geometry when existing weights are edited.
                Matrix4x4 m0 = part.transform.worldToLocalMatrix *
                    bones[w.boneIndex0].localToWorldMatrix;
                Matrix4x4 weighted = m0;
                if (weight1 > 0)
                {
                    Matrix4x4 m1 = part.transform.worldToLocalMatrix *
                        bones[w.boneIndex1].localToWorldMatrix;
                    for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 4; col++)
                        weighted[row, col] = m0[row, col] * weight0 +
                            m1[row, col] * weight1;
                }
                if (Mathf.Abs(weighted.determinant) < 0.000001f)
                    throw new InvalidDataException(id +
                        " weighted rest matrix is not invertible at vertex " + i);
                Vector3 sourceUnity = weighted.inverse.MultiplyPoint3x4(
                    authoredVertices[i]);
                Vector3 sourceSage = W3xExportCore.ToSage(sourceUnity);
                outputVertices[i] = sourceSage;
                if (Vector3.Distance(sourceVertices[i], sourceSage) > PositionEpsilon)
                    changedVertices++;

                if (haveNormals)
                {
                    // Phase 1B TransformDirection ignores bone scale, which
                    // is disallowed above. Normalize inverse-weighted normal.
                    var local = weighted.inverse.MultiplyVector(
                        authoredNormals[i]);
                    if (local.sqrMagnitude < 0.0000001f)
                        throw new InvalidDataException(id +
                            " degenerate normal at vertex " + i);
                    outputNormals[i] = W3xExportCore.ToSage(
                        local.normalized);
                }
                outputWeights0[i] = weight0;
                outputWeights1[i] = weight1;
            }

            if (changedVertices > 0 && W3xExportCore.Child(source, "AABTree") != null)
                throw new InvalidDataException(id +
                    " source AABTree cannot be rebuilt after skin vertex edits.");

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                if (changedVertices > 0)
                    W3xExportCore.WriteVector3(originalVertexNodes[i],
                        outputVertices[i]);
                if (changedWeights > 0)
                {
                    primary[i].SetAttributeValue("Weight",
                        W3xExportCore.Num(outputWeights0[i]));
                    if (secondary != null)
                        secondary[i].SetAttributeValue("Weight",
                            W3xExportCore.Num(outputWeights1[i]));
                }
            }

            if (changedVertices > 0)
            {
                W3xExportCore.UpdateBounds(source, outputVertices);
                W3xExportCore.UpdateTrianglePlanes(triangles,
                    sourceVertices, outputVertices);
                report.ChangedSkinVertices += changedVertices;
            }

            if (haveNormals && (changedWeights > 0 || changedVertices > 0))
                for (int i = 0; i < mesh.vertexCount; i++)
                    W3xExportCore.WriteVector3(sourceNormalNodes[i],
                        outputNormals[i]);

            var uv = W3xExportCore.Children(source, "TexCoords")
                .Take(2).ToArray();
            if (uv.Length > 0)
                W3xExportCore.PatchUv(
                    uv[0], mesh.uv, mesh.vertexCount, id, report);
            if (uv.Length > 1)
                W3xExportCore.PatchUv(
                    uv[1], mesh.uv2, mesh.vertexCount, id, report);

            report.ChangedSkinWeights += changedWeights;
            report.Notes.Add("SKIN " + id +
                ": advanced export kept original bone indices; " +
                "recomputed source-space rest vertices.");
        }

        private static int SourceInt(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text))
                return fallback;
            int result;
            if (!int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException("Invalid source integer: " + text);
            return result;
        }

        private static Quaternion SourceQuaternion(XElement node)
        {
            return new Quaternion(
                W3xExportCore.ParseFloat((string)node.Attribute("X")),
                W3xExportCore.ParseFloat((string)node.Attribute("Y")),
                W3xExportCore.ParseFloat((string)node.Attribute("Z")),
                W3xExportCore.ParseFloat((string)node.Attribute("W")));
        }

        private static Quaternion ToSage(Quaternion unity)
        {
            // Same reflection twice is the inverse of importer ToUnity().
            return new Quaternion(-unity.x, -unity.z, -unity.y, unity.w)
                .normalized;
        }

        private static void WriteQuaternion(XElement node, Quaternion q)
        {
            node.SetAttributeValue("W", W3xExportCore.Num(q.w));
            node.SetAttributeValue("X", W3xExportCore.Num(q.x));
            node.SetAttributeValue("Y", W3xExportCore.Num(q.y));
            node.SetAttributeValue("Z", W3xExportCore.Num(q.z));
        }
    }
}
