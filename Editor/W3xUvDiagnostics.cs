using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Read-only diagnostic for W3X source TexCoords and generated Unity UV channels.
    /// No game assets or project files are modified.
    /// </summary>
    public static class W3xUvDiagnostics
    {
        [MenuItem("Tools/C&C SAGE/Inspect Selected W3X UV Channels")]
        private static void Inspect()
        {
            string path = FindSelectionPath();
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".w3x", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("C&C SAGE UV Inspector",
                    "Select an imported .w3x asset in the Project window or an instance of one in the scene.",
                    "OK");
                return;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };

                XDocument document;
                using (var reader = XmlReader.Create(path, settings))
                    document = XDocument.Load(reader);

                if (document.Root == null ||
                    document.Root.Name.LocalName != "AssetDeclaration")
                    throw new InvalidDataException("Expected a W3X AssetDeclaration root.");

                var report = new StringBuilder();
                report.AppendLine("[C&C SAGE UV Inspector] " + path);

                var meshes = document.Root.Descendants()
                    .Where(e => e.Name.LocalName == "W3DMesh").ToArray();

                report.AppendLine("W3DMesh declarations: " + meshes.Length);

                foreach (var mesh in meshes)
                {
                    string id = (string)mesh.Attribute("id") ?? "(unnamed)";
                    var positions = mesh.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "Vertices");
                    int vertexCount = positions == null ? 0 :
                        positions.Elements().Count(e => e.Name.LocalName == "V");

                    var uvSets = mesh.Elements()
                        .Where(e => e.Name.LocalName == "TexCoords").ToArray();
                    report.AppendLine("  Mesh: " + id +
                        " | Vertices: " + vertexCount +
                        " | W3X TexCoords blocks: " + uvSets.Length);

                    for (int channel = 0; channel < uvSets.Length; channel++)
                    {
                        var coords = uvSets[channel].Elements()
                            .Where(e => e.Name.LocalName == "T").ToArray();
                        float minU = float.PositiveInfinity;
                        float maxU = float.NegativeInfinity;
                        float minV = float.PositiveInfinity;
                        float maxV = float.NegativeInfinity;
                        int valid = 0;

                        foreach (var point in coords)
                        {
                            float u, v;
                            if (!TryFloat((string)point.Attribute("X"), out u) ||
                                !TryFloat((string)point.Attribute("Y"), out v))
                                continue;
                            valid++;
                            minU = Mathf.Min(minU, u);
                            maxU = Mathf.Max(maxU, u);
                            minV = Mathf.Min(minV, v);
                            maxV = Mathf.Max(maxV, v);
                        }

                        string bounds = valid == 0 ? "no valid coordinates" :
                            "U[" + minU.ToString("0.###", CultureInfo.InvariantCulture) +
                            ", " + maxU.ToString("0.###", CultureInfo.InvariantCulture) +
                            "], V[" + minV.ToString("0.###", CultureInfo.InvariantCulture) +
                            ", " + maxV.ToString("0.###", CultureInfo.InvariantCulture) + "]";

                        report.AppendLine("    UV" + channel +
                            ": entries=" + coords.Length +
                            ", valid=" + valid +
                            (coords.Length == vertexCount ? "" : " [NOT ONE PER VERTEX]") +
                            ", " + bounds);
                    }

                    var fxShaders = mesh.Elements()
                        .Where(e => e.Name.LocalName == "FXShader").ToArray();
                    report.AppendLine("    FXShader declarations: " + fxShaders.Length);

                    foreach (var fx in fxShaders)
                    {
                        report.AppendLine("      Shader: " +
                            ((string)fx.Attribute("ShaderName") ?? "(unnamed)"));
                        foreach (var property in fx.Descendants())
                        {
                            string propName = (string)property.Attribute("Name");
                            if (string.IsNullOrEmpty(propName))
                                continue;

                            if (propName.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) < 0 &&
                                propName.IndexOf("Map", StringComparison.OrdinalIgnoreCase) < 0 &&
                                propName.IndexOf("TexCoord", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            string[] values = property.Elements()
                                .Where(e => e.Name.LocalName == "Value")
                                .Select(e => e.Value).ToArray();
                            if (values.Length > 0)
                                report.AppendLine("        " + propName + " = " +
                                    string.Join(", ", values));
                        }
                    }
                }

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    report.AppendLine("No generated Unity GameObject asset was found.");
                }
                else
                {
                    var filters = asset.GetComponentsInChildren<MeshFilter>(true);
                    report.AppendLine("Generated Unity meshes: " + filters.Length);
                    foreach (var filter in filters)
                    {
                        var unityMesh = filter.sharedMesh;
                        if (unityMesh == null)
                            continue;
                        report.AppendLine("  " + unityMesh.name +
                            " | vertexCount=" + unityMesh.vertexCount +
                            " | Mesh.uv (UV0)=" + unityMesh.uv.Length +
                            " | Mesh.uv2 (UV1)=" + unityMesh.uv2.Length);
                    }
                }

                Debug.Log(report.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogError("[C&C SAGE UV Inspector] Cannot inspect " + path +
                    ": " + exception.Message);
            }
        }

        private static string FindSelectionPath()
        {
            if (Selection.activeObject == null)
                return null;

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(path))
                return path;

            var selectedGameObject = Selection.activeGameObject;
            return selectedGameObject == null ? null :
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedGameObject);
        }

        private static bool TryFloat(string value, out float number)
        {
            if (!float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number))
                return false;
            return !float.IsNaN(number) && !float.IsInfinity(number);
        }
    }
}
