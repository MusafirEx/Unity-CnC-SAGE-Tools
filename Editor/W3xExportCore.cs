using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 2A, source-backed W3X XML exporter. It deliberately preserves
    /// unknown SAGE declarations, hierarchy, FXShader and animation XML.
    /// Topology, skinning and pivot authoring are not implemented by this exporter.
    /// </summary>
    internal static class W3xExportCore
    {
        internal sealed class ExportOptions
        {
            // Default values preserve all Phase 2A behavior.
            public bool ExportPivotTransforms;
            public bool ExportSkinWeightsAndVertices;
            public bool ExportMaterialTextures;
            public bool ExportNewStaticMeshes;
            // Only the new-mesh mode resolves images: textures must be
            // siblings of the emitted W3X to survive Unity reimport.
            public string OutputTextureDirectory;
        }

        internal sealed class Report
        {
            public int SourceMeshes;
            public int StaticMeshesExported;
            public int SkinnedMeshesPreserved;
            public int SkinnedMeshesExported;
            public int ChangedVertices;
            public int ChangedUvs;
            public int ChangedPivots;
            public int ChangedShaderTextures;
            public int ChangedSkinWeights;
            public int ChangedSkinVertices;
            public int NewStaticMeshesExported;
            public int NewContainerBindings;
            public readonly List<string> Notes = new List<string>();

            public string Describe()
            {
                return "Source meshes: " + SourceMeshes +
                    "\nStatic meshes exported: " + StaticMeshesExported +
                    "\nSkin meshes preserved as source XML: " + SkinnedMeshesPreserved +
                    "\nSkin meshes advanced-exported: " + SkinnedMeshesExported +
                    "\nChanged vertex coordinates: " + ChangedVertices +
                    "\nChanged UV coordinates: " + ChangedUvs +
                    "\nChanged pivot transforms: " + ChangedPivots +
                    "\nChanged FXShader texture references: " + ChangedShaderTextures +
                    "\nChanged skin weights: " + ChangedSkinWeights +
                    "\nChanged skin source vertices: " + ChangedSkinVertices +
                    "\nNew static meshes exported: " + NewStaticMeshesExported +
                    "\nNew container bindings: " + NewContainerBindings +
                    (Notes.Count == 0 ? "" : "\n" + string.Join("\n", Notes.ToArray()));
            }
        }

        internal static XDocument BuildDocument(
            XDocument original, GameObject selectedModel, out Report report)
        {
            return BuildDocument(original, selectedModel, new ExportOptions(),
                out report);
        }

        internal static XDocument BuildDocument(
            XDocument original, GameObject selectedModel,
            ExportOptions options, out Report report)
        {
            if (options == null)
                options = new ExportOptions();
            if (original == null || original.Root == null ||
                original.Root.Name.LocalName != "AssetDeclaration")
                throw new InvalidDataException("The source is not a W3X AssetDeclaration.");
            if (selectedModel == null)
                throw new InvalidDataException("No selected Unity model.");

            // Make a structural copy. All unrelated nodes and attributes remain
            // intact, including unsupported SAGE formats and comments.
            var output = new XDocument(original);
            report = new Report();
            var parts = new Dictionary<string, GameObject>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var identity in selectedModel
                         .GetComponentsInChildren<CncSageMeshIdentity>(true))
            {
                if (string.IsNullOrEmpty(identity.SageIdentifier))
                    continue;
                if (parts.ContainsKey(identity.SageIdentifier))
                    throw new InvalidDataException(
                        "Duplicate mesh identifier in selected Unity model: " +
                        identity.SageIdentifier);
                parts.Add(identity.SageIdentifier, identity.gameObject);
            }

            foreach (var sourceMesh in output.Root.Elements()
                         .Where(x => x.Name.LocalName == "W3DMesh"))
            {
                report.SourceMeshes++;
                string id = (string)sourceMesh.Attribute("id");
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Source W3DMesh has no id.");

                GameObject part;
                if (!parts.TryGetValue(id, out part))
                    throw new InvalidDataException(
                        "Imported Unity model is missing source mesh " + id +
                        ". Export aborted rather than dropping source geometry.");

                var filter = part.GetComponent<MeshFilter>();
                var skinned = part.GetComponent<SkinnedMeshRenderer>();
                Mesh mesh = skinned != null ? skinned.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    throw new InvalidDataException("No Mesh is attached to " + id);

                bool isSkin = string.Equals(
                    (string)sourceMesh.Attribute("GeometryType"), "Skin",
                    StringComparison.OrdinalIgnoreCase) || skinned != null;

                if (isSkin)
                {
                    // Phase 1B transforms SAGE skin vertices to rest-pose space.
                    // Re-exporting them directly would corrupt their source
                    // coordinates. Preserve their original complete XML block.
                    if (options.ExportSkinWeightsAndVertices)
                    {
                        W3xExportAdvanced.PatchSkin(sourceMesh, part, report);
                        report.SkinnedMeshesExported++;
                    }
                    else
                    {
                        report.SkinnedMeshesPreserved++;
                        report.Notes.Add("SKIN " + id +
                            ": preserved original W3X geometry, bone influences and FXShader.");
                    }
                }
                else
                {
                    PatchStaticMesh(sourceMesh, mesh, id, report);
                    report.StaticMeshesExported++;
                }

                if (options.ExportMaterialTextures)
                    W3xExportAdvanced.PatchShaderTextures(sourceMesh, part, report);
            }

            if (options.ExportNewStaticMeshes)
                W3xNewMeshExporter.AppendNewStaticMeshes(
                    output, selectedModel, parts,
                    options.OutputTextureDirectory, report);

            if (options.ExportPivotTransforms)
                W3xExportAdvanced.PatchLocalPivots(output, selectedModel, report);

            report.Notes.Add("New topology, collision tree rebuild, animation " +
                "tracks and external ART dependencies are not exported.");
            report.Notes.Add("External ART: include dependencies are unchanged.");
            return output;
        }

        internal static void PatchStaticMesh(
            XElement source, Mesh unityMesh, string id, Report report)
        {
            if (unityMesh.subMeshCount != 1)
                throw new InvalidDataException(id +
                    ": only one Unity submesh is supported for source-backed export.");

            var verticesNode = Child(source, "Vertices");
            var trianglesNode = Child(source, "Triangles");
            if (verticesNode == null || trianglesNode == null)
                throw new InvalidDataException(id +
                    ": missing Vertices or Triangles in source.");

            var vertexElements = Children(verticesNode, "V").ToArray();
            Vector3[] positions = unityMesh.vertices
                .Select(ToSage).ToArray();
            if (vertexElements.Length != positions.Length)
                throw new InvalidDataException(id +
                    ": vertex count changed (" + vertexElements.Length +
                    " source / " + positions.Length +
                    " Unity). Changing topology is not supported.");

            var triangles = Children(trianglesNode, "T").ToArray();
            int[] unityTriangles = unityMesh.GetTriangles(0);
            if (triangles.Length * 3 != unityTriangles.Length)
                throw new InvalidDataException(id +
                    ": triangle count changed. Export aborted.");
            for (int i = 0; i < triangles.Length; i++)
            {
                var indices = Children(triangles[i], "V").ToArray();
                if (indices.Length != 3)
                    throw new InvalidDataException(id +
                        ": source triangle has an invalid index count.");
                int a = ParseIndex(indices[0], id);
                int b = ParseIndex(indices[1], id);
                int c = ParseIndex(indices[2], id);
                // Importer swaps triangle winding because ToUnity swaps Y/Z.
                if (unityTriangles[i * 3] != a ||
                    unityTriangles[i * 3 + 1] != c ||
                    unityTriangles[i * 3 + 2] != b)
                    throw new InvalidDataException(id +
                        ": topology or winding was edited at triangle " + i +
                        ". Source triangle metadata cannot be safely preserved.");
            }

            var originalPositions = vertexElements.Select(ReadSourceVector3).ToArray();
            int changed = 0;
            for (int i = 0; i < positions.Length; i++)
                if (!Close(originalPositions[i], positions[i]))
                    changed++;

            // Spatial AABTree nodes are compiled for the original geometry.
            // Do not knowingly emit outdated spatial data after vertex edits.
            if (changed > 0 && Child(source, "AABTree") != null)
                throw new InvalidDataException(id +
                    ": vertex positions changed but the source contains AABTree. " +
                    "Rebuilding the spatial tree is not supported yet.");

            if (changed > 0)
            {
                for (int i = 0; i < positions.Length; i++)
                    WriteVector3(vertexElements[i], positions[i]);
                UpdateBounds(source, positions);
                UpdateTrianglePlanes(triangles, originalPositions, positions);
                report.ChangedVertices += changed;
            }

            var sourceNormals = Child(source, "Normals");
            var normals = unityMesh.normals;
            if (sourceNormals != null && normals != null &&
                normals.Length == positions.Length)
            {
                var normalEntries = Children(sourceNormals, "N").ToArray();
                if (normalEntries.Length == normals.Length)
                    for (int i = 0; i < normals.Length; i++)
                    {
                        var normal = ToSage(normals[i]).normalized;
                        if (!Close(ReadSourceVector3(normalEntries[i]), normal))
                            WriteVector3(normalEntries[i], normal);
                    }
            }

            var uvSets = Children(source, "TexCoords").Take(2).ToArray();
            if (uvSets.Length > 0)
                PatchUv(uvSets[0], unityMesh.uv, positions.Length, id, report);
            if (uvSets.Length > 1)
                PatchUv(uvSets[1], unityMesh.uv2, positions.Length, id, report);
            // Never remove an unknown third UV set or fabricate shader stages.
        }

        internal static void PatchUv(
            XElement node, Vector2[] values, int vertices,
            string id, Report report)
        {
            var entries = Children(node, "T").ToArray();
            if (entries.Length != vertices ||
                values == null || values.Length != vertices)
                throw new InvalidDataException(id +
                    ": source and Unity UV channel counts must match vertex count.");

            for (int i = 0; i < values.Length; i++)
            {
                var element = entries[i];
                float oldU = ParseFloat((string)element.Attribute("X"));
                float oldV = ParseFloat((string)element.Attribute("Y"));
                if (Mathf.Abs(values[i].x - oldU) <= 0.00001f &&
                    Mathf.Abs(values[i].y - oldV) <= 0.00001f)
                    continue;
                element.SetAttributeValue("X", Num(values[i].x));
                element.SetAttributeValue("Y", Num(values[i].y));
                report.ChangedUvs++;
            }
        }

        internal static void UpdateBounds(XElement mesh, Vector3[] vertices)
        {
            if (vertices.Length == 0)
                return;
            var min = vertices[0];
            var max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            var box = Child(mesh, "BoundingBox");
            if (box != null)
            {
                var minNode = Child(box, "Min");
                var maxNode = Child(box, "Max");
                if (minNode != null) WriteVector3(minNode, min);
                if (maxNode != null) WriteVector3(maxNode, max);
            }

            var sphere = Child(mesh, "BoundingSphere");
            if (sphere != null)
            {
                Vector3 center = (min + max) * 0.5f;
                float radius = 0;
                foreach (var vertex in vertices)
                    radius = Mathf.Max(radius, (vertex - center).magnitude);
                var centerNode = Child(sphere, "Center");
                if (centerNode != null) WriteVector3(centerNode, center);
                sphere.SetAttributeValue("Radius", Num(radius));
            }
        }

        internal static void UpdateTrianglePlanes(
            XElement[] triangleNodes,
            Vector3[] originals, Vector3[] positions)
        {
            for (int i = 0; i < triangleNodes.Length; i++)
            {
                var triangle = triangleNodes[i];
                int[] idx = Children(triangle, "V")
                    .Select(x => ParseIndex(x, "triangle")).ToArray();
                var plane = Child(triangle, "Nrm");
                var distance = Child(triangle, "Dist");
                if (plane == null || distance == null)
                    throw new InvalidDataException(
                        "Edited vertices require Nrm and Dist triangle fields.");

                Vector3 oldNormal = ReadSourceVector3(plane);
                var normal = Vector3.Cross(
                    positions[idx[1]] - positions[idx[0]],
                    positions[idx[2]] - positions[idx[0]]);
                if (normal.sqrMagnitude <= 0.00000001f)
                    throw new InvalidDataException(
                        "Degenerate triangle after vertex edit; export aborted.");
                normal.Normalize();
                if (Vector3.Dot(normal, oldNormal) < 0)
                    normal = -normal;

                // Determine source plane convention using original Nrm/Dist:
                // Some tools store the signed plane offset rather than dot(N,P).
                float oldDistance = ParseFloat(distance.Value);
                float oldDot = Vector3.Dot(oldNormal, originals[idx[0]]);
                float sign = Mathf.Abs(oldDistance + oldDot) <
                    Mathf.Abs(oldDistance - oldDot) ? -1f : 1f;
                WriteVector3(plane, normal);
                distance.Value = Num(sign * Vector3.Dot(normal, positions[idx[0]]));
            }
        }

        internal static int ParseIndex(XElement node, string id)
        {
            int value;
            if (!int.TryParse(node.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException(id + ": invalid triangle vertex index.");
            return value;
        }

        internal static float ParseFloat(string value)
        {
            float result;
            if (!float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out result) ||
                float.IsNaN(result) || float.IsInfinity(result))
                throw new InvalidDataException("Invalid source numeric value: " + value);
            return result;
        }

        internal static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Unity mesh contains an invalid number.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static Vector3 ReadSourceVector3(XElement node)
        {
            return new Vector3(
                ParseFloat((string)node.Attribute("X")),
                ParseFloat((string)node.Attribute("Y")),
                ParseFloat((string)node.Attribute("Z")));
        }

        internal static void WriteVector3(XElement node, Vector3 value)
        {
            node.SetAttributeValue("X", Num(value.x));
            node.SetAttributeValue("Y", Num(value.y));
            node.SetAttributeValue("Z", Num(value.z));
        }

        internal static bool Close(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= 0.0000000001f;
        }

        internal static Vector3 ToSage(Vector3 unity)
        {
            return new Vector3(unity.x, unity.z, unity.y);
        }

        internal static XElement Child(XElement parent, string name)
        {
            return parent.Elements().FirstOrDefault(x => x.Name.LocalName == name);
        }

        internal static IEnumerable<XElement> Children(XElement parent, string name)
        {
            return parent.Elements().Where(x => x.Name.LocalName == name);
        }

        internal static XDocument LoadSource(string path)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(path, settings))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        internal static void Save(XDocument doc, string path)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None
            };
            using (var writer = XmlWriter.Create(path, settings))
                doc.Save(writer);
        }

        internal static void ValidateMetadataPreservation(
            XDocument original, XDocument exported)
        {
            ValidateMetadataPreservation(original, exported, false, false);
        }

        internal static void ValidateMetadataPreservation(
            XDocument original, XDocument exported, bool allowPivotTransforms)
        {
            ValidateMetadataPreservation(original, exported,
                allowPivotTransforms, false);
        }

        internal static void ValidateMetadataPreservation(
            XDocument original, XDocument exported, bool allowPivotTransforms,
            bool allowNewStaticMeshes)
        {
            if (allowNewStaticMeshes)
                W3xNewMeshExporter.ValidateNewMeshDeclarations(
                    original, exported);
            var left = original.Root.Elements()
                .Where(x => x.Name.LocalName != "W3DMesh").ToArray();
            var right = exported.Root.Elements()
                .Where(x => x.Name.LocalName != "W3DMesh").ToArray();
            if (left.Length != right.Length)
                throw new InvalidDataException(
                    "Non-mesh declaration count changed during export.");
            for (int i = 0; i < left.Length; i++)
            {
                if (allowPivotTransforms &&
                    left[i].Name.LocalName == "W3DHierarchy")
                    W3xExportAdvanced.ValidatePivotMetadata(left[i], right[i]);
                else if (allowNewStaticMeshes &&
                    left[i].Name.LocalName == "W3DContainer")
                    W3xNewMeshExporter.ValidateContainerAppend(
                        left[i], right[i], original, exported);
                else if (!XNode.DeepEquals(left[i], right[i]))
                    throw new InvalidDataException(
                        "Source declaration was unexpectedly changed: " +
                        left[i].Name.LocalName);
            }
        }
    }
}
