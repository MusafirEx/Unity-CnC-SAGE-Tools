using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 2C: generate new source-backed W3DMesh XML for static Unity mesh
    /// parts that do not exist in the original W3X. No new Skin or LOD authoring.
    /// </summary>
    internal static class W3xNewMeshExporter
    {
        internal static void AppendNewStaticMeshes(
            XDocument output, GameObject root,
            IDictionary<string, GameObject> parts,
            W3xExportCore.Report report)
        {
            if (output == null || output.Root == null)
                throw new InvalidDataException("Missing W3X AssetDeclaration.");
            var existing = new HashSet<string>(
                output.Root.Elements()
                    .Where(x => x.Name.LocalName == "W3DMesh")
                    .Select(x => (string)x.Attribute("id")),
                StringComparer.OrdinalIgnoreCase);
            var extras = parts
                .Where(p => !existing.Contains(p.Key))
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Missing identities are a data-loss risk even when this mode
            // exports every tagged part.
            var allRendered = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(x => x.sharedMesh != null)
                .Select(x => x.gameObject)
                .Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(x => x.sharedMesh != null)
                    .Select(x => x.gameObject))
                .Distinct();
            foreach (var part in allRendered)
                if (part.GetComponent<CncSageMeshIdentity>() == null)
                    throw new InvalidDataException(
                        "Mesh " + part.name +
                        " has no CncSageMeshIdentity; cannot export it.");

            if (extras.Length == 0)
            {
                report.Notes.Add("Phase 2C: no new mesh identities found.");
                return;
            }

            var containerNodes = output.Root.Elements()
                .Where(e => e.Name.LocalName == "W3DContainer").ToArray();
            if (containerNodes.Length > 1)
                throw new InvalidDataException(
                    "Multiple W3DContainer declarations: new mesh " +
                    "attachment target is ambiguous. Export stopped.");
            var container = containerNodes.FirstOrDefault();
            var fxTemplates = output.Root.Elements()
                .Where(e => e.Name.LocalName == "W3DMesh")
                .SelectMany(e => e.Elements()
                    .Where(k => k.Name.LocalName == "FXShader")).ToArray();

            foreach (var extra in extras)
            {
                string id = extra.Key;
                GameObject part = extra.Value;
                ValidateIdentifier(id);
                if (part.GetComponent<SkinnedMeshRenderer>() != null)
                    throw new InvalidDataException(id +
                        ": creating new skinned W3DMesh declarations is not " +
                        "supported yet. New static MeshFilter required.");
                var filter = part.GetComponent<MeshFilter>();
                var renderer = part.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null ||
                    renderer == null)
                    throw new InvalidDataException(id +
                        " must contain MeshFilter, MeshRenderer and Mesh.");
                Mesh mesh = filter.sharedMesh;
                if (mesh.subMeshCount != 1 ||
                    renderer.sharedMaterials.Length > 1)
                    throw new InvalidDataException(id +
                        " requires exactly one mesh submesh/material slot. " +
                        "Split multi-material geometry into separate meshes.");
                if (mesh.vertexCount < 3 || mesh.triangles.Length < 3)
                    throw new InvalidDataException(id +
                        " has insufficient vertices or triangles.");

                int boneIndex = -1;
                Transform basis = root.transform;
                if (container != null)
                {
                    var pivot = ClosestSagePivot(part.transform.parent,
                        root.transform);
                    if (pivot == null)
                        pivot = root.GetComponentsInChildren<CncSagePivotInfo>(
                            true).FirstOrDefault(x => x.PivotIndex == 0);
                    if (pivot == null)
                        throw new InvalidDataException(id +
                            ": source has a W3DContainer but the editable " +
                            "prefab has no matching SAGE pivot. " +
                            "Cannot calculate a valid BoneIndex.");
                    boneIndex = pivot.PivotIndex;
                    if (boneIndex < 0)
                        throw new InvalidDataException(id +
                            ": invalid SAGE pivot index.");
                    basis = pivot.transform;
                }

                var transform = basis.worldToLocalMatrix *
                    part.transform.localToWorldMatrix;
                if (transform.determinant <= 0.000001f)
                    throw new InvalidDataException(id +
                        ": negative, zero or nearly singular Transform scale " +
                        "cannot be exported safely.");

                XElement xml = GenerateXml(
                    id, mesh, transform, fxTemplates, part, report);
                // Insert mesh declarations before container references.
                var firstContainer = output.Root.Elements()
                    .FirstOrDefault(x => x.Name.LocalName == "W3DContainer");
                if (firstContainer != null)
                    firstContainer.AddBeforeSelf(xml);
                else
                    output.Root.Add(xml);

                if (container != null)
                {
                    AddContainerBinding(container, id, boneIndex);
                    report.NewContainerBindings++;
                }
                else
                    report.Notes.Add("NEW " + id +
                        ": W3DMesh added without container binding " +
                        "(source has no W3DContainer).");

                existing.Add(id);
                report.NewStaticMeshesExported++;
                report.Notes.Add("NEW " + id + ": exported " +
                    mesh.vertexCount + " vertices and " +
                    (mesh.triangles.Length / 3) + " triangles" +
                    (boneIndex < 0 ? "" : ", pivot " + boneIndex) + ".");
            }
        }

        private static XElement GenerateXml(string id, Mesh mesh,
            Matrix4x4 toPivot, XElement[] fxTemplates, GameObject part,
            W3xExportCore.Report report)
        {
            XNamespace ns = "uri:ea.com:eala:asset";
            var result = new XElement(ns + "W3DMesh",
                new XAttribute("id", id),
                new XAttribute("GeometryType", "Normal"),
                new XAttribute("SortLevel", "0"));

            var vertices = mesh.vertices;
            var triangles = mesh.GetTriangles(0);
            if (triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidDataException(id +
                    ": triangle index count must be a positive multiple of 3.");
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var uv2 = mesh.uv2;
            if (uvs.Length != 0 && uvs.Length != vertices.Length ||
                uv2.Length != 0 && uv2.Length != vertices.Length)
                throw new InvalidDataException(id +
                    ": UV0/UV1 count must be zero or match vertex count.");
            if (uv2.Length > 0 && uvs.Length == 0)
                throw new InvalidDataException(id +
                    ": UV1 without UV0 cannot be represented by this importer.");
            if (normals.Length != 0 && normals.Length != vertices.Length)
                throw new InvalidDataException(id +
                    ": per-vertex normal count does not match vertices.");

            var positions = vertices.Select(v =>
                W3xExportCore.ToSage(toPivot.MultiplyPoint3x4(v))).ToArray();
            if (positions.Any(p => float.IsNaN(p.x) || float.IsNaN(p.y) ||
                float.IsNaN(p.z) || float.IsInfinity(p.x) ||
                float.IsInfinity(p.y) || float.IsInfinity(p.z)))
                throw new InvalidDataException(id +
                    ": vertex positions contain invalid floats.");

            var min = positions[0];
            var max = positions[0];
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            result.Add(new XElement(ns + "BoundingBox",
                VectorElement(ns, "Min", min),
                VectorElement(ns, "Max", max)));
            Vector3 center = (min + max) / 2f;
            float radius = positions.Max(p => (p - center).magnitude);
            result.Add(new XElement(ns + "BoundingSphere",
                new XAttribute("Radius", W3xExportCore.Num(radius)),
                VectorElement(ns, "Center", center)));

            result.Add(new XElement(ns + "Vertices",
                positions.Select(p => VectorElement(ns, "V", p))));

            // Inverse transpose handles rigid rotation and positive scaling.
            var normalMatrix = toPivot.inverse.transpose;
            var transformedNormals = normals.Length == vertices.Length ?
                normals.Select(n =>
                    W3xExportCore.ToSage(
                        normalMatrix.MultiplyVector(n).normalized)).ToArray()
                : new Vector3[0];
            if (transformedNormals.Length == 0)
            {
                transformedNormals = new Vector3[vertices.Length];
                var assigned = new bool[vertices.Length];
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1],
                        c = triangles[t + 2];
                    CheckIndices(a, b, c, positions.Length, id);
                    var normal = Vector3.Cross(
                        positions[c] - positions[a],
                        positions[b] - positions[a]).normalized;
                    transformedNormals[a] = normal;
                    transformedNormals[b] = normal;
                    transformedNormals[c] = normal;
                    assigned[a] = assigned[b] = assigned[c] = true;
                }
                for (int n = 0; n < transformedNormals.Length; n++)
                    if (!assigned[n])
                        transformedNormals[n] = Vector3.up;
            }
            result.Add(new XElement(ns + "Normals",
                transformedNormals.Select(n => VectorElement(ns, "N", n))));

            if (uvs.Length > 0)
                result.Add(new XElement(ns + "TexCoords",
                    uvs.Select(p => UvElement(ns, p))));
            if (uv2.Length > 0)
                result.Add(new XElement(ns + "TexCoords",
                    uv2.Select(p => UvElement(ns, p))));

            result.Add(new XElement(ns + "ShadeIndices",
                Enumerable.Repeat(0, positions.Length).Select(n =>
                    new XElement(ns + "I", n))));

            var tris = new XElement(ns + "Triangles");
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1],
                    c = triangles[i + 2];
                CheckIndices(a, b, c, positions.Length, id);
                // Source winding reverses the Y/Z swap made by importer.
                Vector3 plane = Vector3.Cross(
                    positions[c] - positions[a],
                    positions[b] - positions[a]);
                if (plane.sqrMagnitude < 0.0000000001f)
                    throw new InvalidDataException(id +
                        ": triangle " + (i / 3) + " is degenerate.");
                plane.Normalize();
                tris.Add(new XElement(ns + "T",
                    new XElement(ns + "V", a),
                    new XElement(ns + "V", c),
                    new XElement(ns + "V", b),
                    VectorElement(ns, "Nrm", plane),
                    new XElement(ns + "Dist",
                        W3xExportCore.Num(Vector3.Dot(
                            plane, positions[a])))));
            }
            result.Add(tris);

            if (fxTemplates.Length > 0)
            {
                // Copy a known SAGE pass, never invent a new ShaderName.
                result.Add(new XElement(fxTemplates[0]));
                W3xExportAdvanced.PatchShaderTextures(result, part, report);
                report.Notes.Add("NEW " + id +
                    ": FXShader template copied from original W3X. " +
                    "Review its shader technique and texture stages.");
            }
            else
                report.Notes.Add("NEW " + id +
                    ": source W3X has no FXShader template. " +
                    "No material declaration created.");

            return result;
        }

        private static void CheckIndices(
            int a, int b, int c, int count, string id)
        {
            if (a < 0 || b < 0 || c < 0 ||
                a >= count || b >= count || c >= count)
                throw new InvalidDataException(id +
                    ": triangle contains an out-of-range vertex index.");
        }

        private static XElement VectorElement(
            XNamespace ns, string tag, Vector3 p)
        {
            return new XElement(ns + tag,
                new XAttribute("X", W3xExportCore.Num(p.x)),
                new XAttribute("Y", W3xExportCore.Num(p.y)),
                new XAttribute("Z", W3xExportCore.Num(p.z)));
        }

        private static XElement UvElement(XNamespace ns, Vector2 p)
        {
            return new XElement(ns + "T",
                new XAttribute("X", W3xExportCore.Num(p.x)),
                new XAttribute("Y", W3xExportCore.Num(p.y)));
        }

        private static void ValidateIdentifier(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Trim() != id ||
                id.Any(char.IsWhiteSpace) || id.Contains("/") ||
                id.Contains("\\") || id.IndexOf('.') <= 0 ||
                id.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "New mesh identifier must have format MODEL.PART, " +
                    "for example GPPITBULL.NEWLabel. Got: " + id);
        }

        private static CncSagePivotInfo ClosestSagePivot(
            Transform start, Transform root)
        {
            var cursor = start;
            while (cursor != null)
            {
                var pivot = cursor.GetComponent<CncSagePivotInfo>();
                if (pivot != null)
                    return pivot;
                if (cursor == root)
                    break;
                cursor = cursor.parent;
            }
            return null;
        }

        private static void AddContainerBinding(
            XElement container, string id, int boneIndex)
        {
            var oldIds = container.Elements()
                .Where(x => x.Name.LocalName == "SubObject")
                .Select(x => (string)x.Attribute("SubObjectID")).ToArray();
            string suffix = id.Substring(id.IndexOf('.') + 1);
            if (oldIds.Any(x => string.Equals(x, suffix,
                StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(id +
                    ": SubObjectID " + suffix +
                    " already exists in W3DContainer.");
            XNamespace ns = container.Name.Namespace;
            container.Add(new XElement(ns + "SubObject",
                new XAttribute("SubObjectID", suffix),
                new XAttribute("BoneIndex", boneIndex),
                new XElement(ns + "RenderObject",
                    new XElement(ns + "Mesh", id))));
        }

        internal static void ValidateNewMeshDeclarations(
            XDocument original, XDocument exported)
        {
            var originalIds = original.Root.Elements()
                .Where(x => x.Name.LocalName == "W3DMesh")
                .Select(x => (string)x.Attribute("id")).ToArray();
            var emittedIds = exported.Root.Elements()
                .Where(x => x.Name.LocalName == "W3DMesh")
                .Select(x => (string)x.Attribute("id")).ToArray();
            if (emittedIds.Length < originalIds.Length ||
                emittedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                emittedIds.Length)
                throw new InvalidDataException(
                    "Exported W3DMesh identifiers are missing or duplicated.");
            foreach (var id in originalIds)
                if (!emittedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Original mesh was dropped during export: " + id);
            foreach (var element in exported.Root.Elements()
                         .Where(x => x.Name.LocalName == "W3DMesh" &&
                             !originalIds.Contains((string)x.Attribute("id"),
                                 StringComparer.OrdinalIgnoreCase)))
            {
                if ((string)element.Attribute("GeometryType") != "Normal" ||
                    W3xExportCore.Child(element, "Triangles") == null ||
                    W3xExportCore.Child(element, "Vertices") == null)
                    throw new InvalidDataException(
                        "New mesh declaration incomplete: " +
                        (string)element.Attribute("id"));
            }
        }

        internal static void ValidateContainerAppend(
            XElement oldContainer, XElement newContainer,
            XDocument original, XDocument exported)
        {
            var oldChildren = oldContainer.Elements().ToArray();
            var newChildren = newContainer.Elements().ToArray();
            if (newChildren.Length < oldChildren.Length)
                throw new InvalidDataException(
                    "Original W3DContainer subobjects were removed.");
            var scrub = new XElement(newContainer);
            foreach (var item in scrub.Elements()
                         .Skip(oldChildren.Length).ToArray())
            {
                if (item.Name.LocalName != "SubObject" ||
                    item.Elements().Count() != 1 ||
                    item.Element(item.Name.Namespace + "RenderObject") ==
                        null)
                    throw new InvalidDataException(
                        "Unexpected modified W3DContainer metadata.");
                var mesh = item.Element(item.Name.Namespace + "RenderObject")
                    .Element(item.Name.Namespace + "Mesh");
                string id = mesh == null ? null : mesh.Value;
                if (string.IsNullOrEmpty(id) ||
                    original.Root.Elements().Any(x =>
                        x.Name.LocalName == "W3DMesh" &&
                        string.Equals((string)x.Attribute("id"), id,
                            StringComparison.OrdinalIgnoreCase)) ||
                    !exported.Root.Elements().Any(x =>
                        x.Name.LocalName == "W3DMesh" &&
                        string.Equals((string)x.Attribute("id"), id,
                            StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(
                        "New container SubObject has an invalid mesh reference.");
                item.Remove();
            }
            if (!XNode.DeepEquals(oldContainer, scrub))
                throw new InvalidDataException(
                    "Original W3DContainer metadata changed unexpectedly.");
        }
    }
}
