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
        public Vector2[] Uvs2;
        public int UvSetCount;
        public int[] TriangleIndices;
        public W3xBoneInfluence[] BoneInfluences;
        public W3xBoneInfluence[] SecondaryBoneInfluences;
        public W3xFxShaderData[] Shaders;
    }

    internal struct W3xBoneInfluence
    {
        public int Bone;
        public float Weight;
    }

    internal sealed class W3xFxShaderData
    {
        public string Name;
        public readonly Dictionary<string, string> Properties =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string property)
        {
            string result;
            return Properties.TryGetValue(property, out result) ? result : null;
        }
    }

    internal sealed class W3xPivotData
    {
        public string Name;
        public int Parent;
        public int NameId;
        public string FixupXml;
        public Vector3 Translation;
        public Quaternion Rotation;
    }

    internal sealed class W3xHierarchyData
    {
        public string Identifier;
        public readonly List<W3xPivotData> Pivots = new List<W3xPivotData>();
    }

    internal sealed class W3xSubObjectData
    {
        public string Identifier;
        public int BoneIndex;
    }

    internal sealed class W3xContainerData
    {
        public string Identifier;
        public string HierarchyName;
        public readonly List<W3xSubObjectData> SubObjects = new List<W3xSubObjectData>();
    }

    internal sealed class W3xDocument
    {
        public readonly List<W3xMeshData> Meshes = new List<W3xMeshData>();
        public readonly List<W3xHierarchyData> Hierarchies = new List<W3xHierarchyData>();
        public readonly List<W3xContainerData> Containers = new List<W3xContainerData>();
        // Mesh, hierarchy and animation declarations share the W3X extension.
        public readonly List<string> Declarations = new List<string>();
        public readonly List<string> SourcePaths = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Original, deliberately limited XML reader for uncompiled W3X sources.
    /// Does not decode binary W3D or animation tracks; phase 1B parses
    /// source pivots, vertex influences, FXShader properties, and container refs.
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
            ResolveCompanionHierarchy(fullPath, result, visited);

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
                else if (kind == "W3DHierarchy")
                {
                    var hierarchy = ParseHierarchy(node);
                    result.Hierarchies.Add(hierarchy);
                    result.Declarations.Add(kind + ": " + hierarchy.Identifier);
                }
                else if (kind == "W3DContainer")
                {
                    var container = ParseContainer(node);
                    result.Containers.Add(container);
                    result.Declarations.Add(kind + ": " + container.Identifier);
                }
                else if (kind == "W3DAnimation" || kind == "W3DCollisionBox" ||
                         kind == "Texture")
                {
                    // Animation / collision XML is recognized, not yet decoded.
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

            // W3X may contain multiple sibling TexCoords blocks. Keep the
            // first two channels in Unity UV0 / UV1, respectively.
            var uvElements = node.Elements()
                .Where(e => e.Name.LocalName == "TexCoords").ToArray();
            var uvs = uvElements.Length < 1 ? new Vector2[0] :
                uvElements[0].Elements().Where(e => e.Name.LocalName == "T")
                    .Select(e => ReadVector2(e, id)).ToArray();
            var uvs2 = uvElements.Length < 2 ? new Vector2[0] :
                uvElements[1].Elements().Where(e => e.Name.LocalName == "T")
                    .Select(e => ReadVector2(e, id)).ToArray();

            // SAGE supports a second influence list on some skinned meshes.
            var influences = node.Elements()
                .Where(e => e.Name.LocalName == "BoneInfluences").ToArray();
            var primary = influences.Length > 0 ? ParseInfluences(influences[0], id) :
                new W3xBoneInfluence[0];
            var secondary = influences.Length > 1 ? ParseInfluences(influences[1], id) :
                new W3xBoneInfluence[0];
            var shaders = node.Elements().Where(e => e.Name.LocalName == "FXShader")
                .Select(ParseFxShader).ToArray();

            return new W3xMeshData
            {
                Identifier = id,
                GeometryType = (string)node.Attribute("GeometryType") ?? "Normal",
                Positions = positions,
                Normals = normals,
                Uvs = uvs,
                Uvs2 = uvs2,
                UvSetCount = uvElements.Length,
                TriangleIndices = indices.ToArray(),
                BoneInfluences = primary,
                SecondaryBoneInfluences = secondary,
                Shaders = shaders
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

        private static float Number(XElement node, string attr, string context, float fallback)
        {
            if (node.Attribute(attr) == null)
                return fallback;
            return Number(node, attr, context);
        }


        private static W3xBoneInfluence[] ParseInfluences(XElement node, string name)
        {
            return node.Elements().Where(e => e.Name.LocalName == "I")
                .Select(e => new W3xBoneInfluence
                {
                    Bone = ParseInt((string)e.Attribute("Bone") ?? "0", name),
                    Weight = Number(e, "Weight", name)
                }).ToArray();
        }

        private static W3xFxShaderData ParseFxShader(XElement node)
        {
            var shader = new W3xFxShaderData
            {
                Name = (string)node.Attribute("ShaderName") ?? "SAGE_FXShader"
            };

            foreach (var constant in node.Elements()
                         .Where(e => e.Name.LocalName == "Constants")
                         .SelectMany(e => e.Elements()))
            {
                string name = (string)constant.Attribute("Name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var values = constant.Elements()
                    .Where(e => e.Name.LocalName == "Value")
                    .Select(e => e.Value.Trim()).ToArray();
                if (values.Length != 0)
                    shader.Properties[name] = string.Join(",", values);
            }
            return shader;
        }

        private static W3xHierarchyData ParseHierarchy(XElement node)
        {
            var hierarchy = new W3xHierarchyData
            {
                Identifier = (string)node.Attribute("id") ?? "SAGE_Hierarchy"
            };
            foreach (var pivot in node.Elements()
                         .Where(e => e.Name.LocalName == "Pivot"))
            {
                string name = (string)pivot.Attribute("Name");
                var translation = FirstChild(pivot, "Translation");
                var rotation = FirstChild(pivot, "Rotation");
                var source = new W3xPivotData
                {
                    Name = string.IsNullOrWhiteSpace(name) ?
                        "Pivot_" + hierarchy.Pivots.Count : name,
                    Parent = ParseInt((string)pivot.Attribute("Parent") ?? "-1", "Pivot.Parent"),
                    NameId = ParseInt((string)pivot.Attribute("NameID") ?? 
                        hierarchy.Pivots.Count.ToString(CultureInfo.InvariantCulture), "Pivot.NameID"),
                    FixupXml = FirstChild(pivot, "FixupMatrix") == null ? "" :
                        FirstChild(pivot, "FixupMatrix").ToString(SaveOptions.DisableFormatting),
                    Translation = translation == null ? Vector3.zero :
                        ReadVector3(translation, name),
                    Rotation = rotation == null ? Quaternion.identity :
                        ReadQuaternion(rotation, name)
                };
                hierarchy.Pivots.Add(source);
            }
            return hierarchy;
        }

        private static Quaternion ReadQuaternion(XElement node, string context)
        {
            // Source is W,X,Y,Z. Swapping the model's Y and Z axes changes
            // handedness; the converted Quaternion is applied by the builder.
            return new Quaternion(
                Number(node, "X", context),
                Number(node, "Y", context),
                Number(node, "Z", context),
                Number(node, "W", context));
        }

        private static W3xContainerData ParseContainer(XElement node)
        {
            var container = new W3xContainerData
            {
                Identifier = (string)node.Attribute("id") ?? "SAGE_Container",
                HierarchyName = (string)node.Attribute("Hierarchy")
            };

            foreach (var item in node.Elements()
                         .Where(e => e.Name.LocalName == "SubObject"))
            {
                var render = FirstChild(item, "RenderObject");
                var identifier = render == null ? null : render.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Mesh" ||
                                         e.Name.LocalName == "CollisionBox");
                if (identifier == null || string.IsNullOrWhiteSpace(identifier.Value))
                    continue;
                container.SubObjects.Add(new W3xSubObjectData
                {
                    Identifier = identifier.Value.Trim(),
                    BoneIndex = ParseInt((string)item.Attribute("BoneIndex") ?? "0",
                        container.Identifier)
                });
            }
            return container;
        }

        private static void ResolveCompanionHierarchy(
            string mainPath, W3xDocument result, HashSet<string> visited)
        {
            if (result.Hierarchies.Count > 0 ||
                (result.Meshes.Count == 0 && result.Containers.Count == 0))
                return;

            // Use the source container's explicit Hierarchy reference first.
            // Never use an unrelated _SKL simply because it is in this folder.
            var names = result.Containers
                .Select(c => c.HierarchyName)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(
                    StringComparer.OrdinalIgnoreCase).ToList();

            if (names.Count == 0 &&
                result.Meshes.Any(m => string.Equals(m.GeometryType, "Skin",
                    StringComparison.OrdinalIgnoreCase)))
            {
                var stem = Path.GetFileNameWithoutExtension(mainPath);
                int suffix = stem.IndexOf("_SKN", StringComparison.OrdinalIgnoreCase);
                if (suffix > 0)
                {
                    string baseName = stem.Substring(0, suffix);
                    names.Add(baseName);
                    // C&C damage/upgrade variant names may share a base skeleton.
                    if (baseName.EndsWith("R", StringComparison.OrdinalIgnoreCase) ||
                        baseName.EndsWith("D", StringComparison.OrdinalIgnoreCase))
                        names.Add(baseName.Substring(0, baseName.Length - 1));
                }
            }

            string directory = Path.GetDirectoryName(mainPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            foreach (var name in names)
            {
                var targets = new[] { name, name + "_SKL" };
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (!string.Equals(Path.GetExtension(file), ".w3x",
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!targets.Any(t => string.Equals(
                        Path.GetFileNameWithoutExtension(file), t,
                        StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (string.Equals(Path.GetFullPath(file), mainPath,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        // Probe the expected W3X by its actual declarations.
                        // Import no unrelated mesh data from a sibling skeleton.
                        var settings = new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null
                        };
                        XDocument xml;
                        using (var reader = XmlReader.Create(file, settings))
                            xml = XDocument.Load(reader);
                        if (xml.Root == null ||
                            xml.Root.Name.LocalName != "AssetDeclaration")
                            continue;
                        var candidates = xml.Root.Elements().Where(e =>
                            e.Name.LocalName == "W3DHierarchy").ToArray();
                        foreach (var entry in candidates)
                        {
                            string id = (string)entry.Attribute("id");
                            if (!string.IsNullOrEmpty(id) &&
                                !names.Any(n => string.Equals(n, id,
                                    StringComparison.OrdinalIgnoreCase)))
                                continue;
                            result.Hierarchies.Add(ParseHierarchy(entry));
                            result.Declarations.Add("W3DHierarchy: " + id);
                            if (visited.Add(Path.GetFullPath(file)))
                                result.SourcePaths.Add(Path.GetFullPath(file));
                        }
                        if (result.Hierarchies.Count > 0)
                            return;
                    }
                    catch (Exception ex) when (ex is IOException ||
                                             ex is XmlException)
                    {
                        result.Warnings.Add("Cannot read companion hierarchy " +
                            file + ": " + ex.Message);
                    }
                }
            }

            if (names.Count > 0)
                result.Warnings.Add("No matching companion hierarchy found for " +
                    string.Join(", ", names.ToArray()));
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
