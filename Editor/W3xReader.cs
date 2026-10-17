using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    internal sealed class W3xMeshData
    {
        public string Identifier;
        public string GeometryType;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] TriangleIndices;
    }

    internal sealed class W3xDocument
    {
        public readonly List<W3xMeshData> Meshes = new List<W3xMeshData>();
        // Mesh, hierarchy and animation declarations share the W3X extension.
        public readonly List<string> Declarations = new List<string>();
        public readonly List<string> SourcePaths = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Original, deliberately limited XML reader for uncompiled W3X sources.
    /// It does not parse binary W3D, material shaders, animation, or HLOD bindings.
    /// </summary>
    internal static class W3xReader
    {
        private const int MaxIncludeDepth = 24;

        public static W3xDocument Read(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("W3X path is empty.", nameof(assetPath));

            var result = new W3xDocument();
            var fullPath = Path.GetFullPath(assetPath);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadFile(fullPath, result, visited, 0, true);

            // Valid SAGE W3X sources can contain no meshes (for example _SKL
            // hierarchies and _IDLA / _ATKA animations). Keep a placeholder.
            if (result.Declarations.Count == 0)
                throw new InvalidDataException(
                    "No supported W3X declarations found. Expected W3DMesh, " +
                    "W3DHierarchy, W3DAnimation, W3DContainer, W3DCollisionBox or Texture.");

            return result;
        }

        private static void ReadFile(
            string fullPath, W3xDocument result, HashSet<string> visited,
            int depth, bool isMain)
        {
            if (depth > MaxIncludeDepth)
                throw new InvalidDataException("Maximum W3X include depth exceeded.");

            if (!visited.Add(fullPath))
                return;

            if (!File.Exists(fullPath))
            {
                if (isMain)
                    throw new FileNotFoundException("W3X file not found.", fullPath);
                result.Warnings.Add("Missing referenced file: " + fullPath);
                return;
            }

            XDocument document;
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using (var reader = XmlReader.Create(fullPath, settings))
                document = XDocument.Load(reader);

            if (document.Root == null || document.Root.Name.LocalName != "AssetDeclaration")
            {
                if (isMain)
                    throw new InvalidDataException("Expected W3X AssetDeclaration in " + fullPath);
                // Some ART: references are XML metadata rather than W3X sources.
                result.Warnings.Add("Skipped non-W3X include: " + fullPath);
                return;
            }

            result.SourcePaths.Add(fullPath);
            foreach (var node in document.Root.Elements())
            {
                string kind = node.Name.LocalName;
                if (kind == "W3DMesh")
                {
                    var mesh = ParseMesh(node);
                    result.Meshes.Add(mesh);
                    result.Declarations.Add("W3DMesh: " + mesh.Identifier);
                }
                else if (kind == "W3DHierarchy" || kind == "W3DAnimation" ||
                         kind == "W3DContainer" || kind == "W3DCollisionBox" ||
                         kind == "Texture")
                {
                    // Recognition only. Do not misrepresent these as decoded
                    // skeletons, animation clips, containers or materials.
                    string identifier = (string)node.Attribute("id");
                    result.Declarations.Add(kind +
                        (string.IsNullOrWhiteSpace(identifier) ? "" : ": " + identifier));
                }
                else if (kind == "Includes")
                {
                    foreach (var include in node.Elements()
                                                .Where(e => e.Name.LocalName == "Include"))
                    {
                        var source = (string)include.Attribute("source");
                        if (string.IsNullOrWhiteSpace(source))
                            continue;

                        if (source.StartsWith("ART:", StringComparison.OrdinalIgnoreCase))
                            source = source.Substring(4);

                        // W3X includes normally refer to neighboring art assets.
                        // Ignore URI-like and absolute paths; do not fetch remote content.
                        if (source.IndexOf(':') >= 0 || Path.IsPathRooted(source))
                        {
                            result.Warnings.Add("Unsupported include path: " + source);
                            continue;
                        }

                        var includePath = Path.GetFullPath(
                            Path.Combine(Path.GetDirectoryName(fullPath) ?? "", source.Replace('\\', '/')));
                        ReadFile(includePath, result, visited, depth + 1, false);
                    }
                }
            }
        }

        private static W3xMeshData ParseMesh(XElement node)
        {
            string id = (string)node.Attribute("id");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("W3DMesh is missing its id attribute.");

            var positionsNode = FirstChild(node, "Vertices");
            if (positionsNode == null)
                throw new InvalidDataException(id + " has no Vertices element.");

            var positions = positionsNode.Elements()
                .Where(e => e.Name.LocalName == "V")
                .Select(e => ReadVector3(e, id)).ToArray();

            var trianglesNode = FirstChild(node, "Triangles");
            if (trianglesNode == null)
                throw new InvalidDataException(id + " has no Triangles element.");

            var indices = new List<int>();
            foreach (var triangle in trianglesNode.Elements()
                                                  .Where(e => e.Name.LocalName == "T"))
            {
                var triangleVertices = triangle.Elements()
                    .Where(e => e.Name.LocalName == "V")
                    .Select(e => ParseInt(e.Value, id))
                    .ToArray();

                if (triangleVertices.Length != 3)
                    throw new InvalidDataException(id + " has a triangle without exactly 3 indices.");

                foreach (var index in triangleVertices)
                {
                    if (index < 0 || index >= positions.Length)
                        throw new InvalidDataException(id + " contains an out-of-range triangle index.");
                    indices.Add(index);
                }
            }

            var normalsElement = FirstChild(node, "Normals");
            var normals = normalsElement == null ? new Vector3[0] :
                normalsElement.Elements().Where(e => e.Name.LocalName == "N")
                    .Select(e => ReadVector3(e, id)).ToArray();

            // This first version imports only the first UV set.
            var uvElement = FirstChild(node, "TexCoords");
            var uvs = uvElement == null ? new Vector2[0] :
                uvElement.Elements().Where(e => e.Name.LocalName == "T")
                    .Select(e => ReadVector2(e, id)).ToArray();

            return new W3xMeshData
            {
                Identifier = id,
                GeometryType = (string)node.Attribute("GeometryType") ?? "Normal",
                Positions = positions,
                Normals = normals,
                Uvs = uvs,
                TriangleIndices = indices.ToArray()
            };
        }

        private static XElement FirstChild(XElement parent, string name)
        {
            return parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        }

        private static float Number(XElement node, string attr, string context)
        {
            var raw = (string)node.Attribute(attr);
            float number;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || float.IsNaN(number) || float.IsInfinity(number))
                throw new InvalidDataException(context + ": invalid " + attr + " coordinate.");
            return number;
        }

        private static int ParseInt(string value, string context)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException(context + ": invalid triangle index.");
            return result;
        }

        private static Vector3 ReadVector3(XElement node, string context)
        {
            return new Vector3(Number(node, "X", context), Number(node, "Y", context),
                               Number(node, "Z", context));
        }

        private static Vector2 ReadVector2(XElement node, string context)
        {
            return new Vector2(Number(node, "X", context), Number(node, "Y", context));
        }
    }
}
