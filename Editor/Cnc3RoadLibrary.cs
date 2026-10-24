using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Optional user-supplied road template values and textures.
    /// C&C3/KW map.bin stores road placements, not a complete road texture pack.
    /// Road definitions may be populated manually, or loaded from a text
    /// extraction of Roads.ini / RoadTemplate XML. Unknown fields are ignored.
    /// </summary>
    [CreateAssetMenu(fileName = "Cnc3RoadLibrary",
        menuName = "MusafirEx/C&C SAGE/Road Template Library")]
    public sealed class Cnc3RoadLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Template
        {
            public string roadType;
            public string sourceTextureName;
            [Min(0.01f)] public float roadWidth = 35f;
            [Range(0.05f, 1f)] public float roadWidthInTexture = 0.9f;
            public Texture2D texture;
        }

        [Tooltip("Optional text extraction containing Road ... End definitions, or RoadTemplate XML. You must supply the source file.")]
        public TextAsset sourceDefinitions;

        [Tooltip("Explicit overrides and optional texture links. These take precedence over the definitions text.")]
        public List<Template> overrides = new List<Template>();

        internal Dictionary<string, Template> ReadTemplates(List<string> warnings)
        {
            var templates = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);
            if (sourceDefinitions != null)
            {
                string content = sourceDefinitions.text;
                if (content.Length > 4 * 1024 * 1024)
                    warnings.Add("Road template file exceeds the 4 MB safety limit.");
                else
                {
                    try
                    {
                        if (content.TrimStart().StartsWith("<", StringComparison.Ordinal))
                            ReadXml(content, templates, warnings);
                        else
                            ReadIni(content, templates, warnings);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("Road template parse failed: " + ex.Message);
                    }
                }
            }
            foreach (Template road in overrides)
            {
                if (road == null || string.IsNullOrWhiteSpace(road.roadType))
                    continue;
                if (!Valid(road))
                {
                    warnings.Add("Invalid road definition " + road.roadType);
                    continue;
                }
                templates[road.roadType.Trim()] = road;
            }
            return templates;
        }

        private static bool Valid(Template road)
        {
            return road.roadWidth > 0f && road.roadWidth <= 1000f &&
                road.roadWidthInTexture > 0f && road.roadWidthInTexture <= 2f &&
                !float.IsNaN(road.roadWidth) && !float.IsInfinity(road.roadWidth) &&
                !float.IsNaN(road.roadWidthInTexture) &&
                !float.IsInfinity(road.roadWidthInTexture);
        }

        private static void ReadIni(string content, Dictionary<string, Template> output,
            List<string> warnings)
        {
            using (var input = new StringReader(content))
            {
                Template current = null;
                string line;
                int lineNo = 0;
                while ((line = input.ReadLine()) != null)
                {
                    lineNo++;
                    if (lineNo > 100000) break;
                    int comment = line.IndexOf(';');
                    if (comment >= 0) line = line.Substring(0, comment);
                    comment = line.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) line = line.Substring(0, comment);
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    if (current == null)
                    {
                        Match start = Regex.Match(line,
                            @"^(?:Road|RoadTemplate)\s+([A-Za-z0-9_./-]+)\s*$",
                            RegexOptions.IgnoreCase);
                        if (start.Success)
                            current = new Template { roadType = start.Groups[1].Value };
                        continue;
                    }
                    if (string.Equals(line, "End", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Valid(current))
                            output[current.roadType] = current;
                        else
                            warnings.Add("Skipped invalid Road " + current.roadType);
                        current = null;
                        continue;
                    }
                    int equal = line.IndexOf('=');
                    if (equal < 0) continue;
                    string key = line.Substring(0, equal).Trim();
                    string value = line.Substring(equal + 1).Trim().Trim('"');
                    Assign(current, key, value);
                }
            }
        }

        private static void ReadXml(string content, Dictionary<string, Template> output,
            List<string> warnings)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024,
                IgnoreComments = true
            };
            using (var reader = XmlReader.Create(new StringReader(content), settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element ||
                        !(reader.LocalName.Equals("Road", StringComparison.OrdinalIgnoreCase) ||
                          reader.LocalName.Equals("RoadTemplate", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    string roadName = reader.GetAttribute("id") ??
                        reader.GetAttribute("name") ?? reader.GetAttribute("Name");
                    if (string.IsNullOrWhiteSpace(roadName)) continue;
                    var record = new Template { roadType = roadName.Trim() };
                    Assign(record, "Texture",
                        reader.GetAttribute("Texture") ?? reader.GetAttribute("texture"));
                    Assign(record, "RoadWidth",
                        reader.GetAttribute("RoadWidth") ?? reader.GetAttribute("roadWidth"));
                    Assign(record, "RoadWidthInTexture",
                        reader.GetAttribute("RoadWidthInTexture") ??
                        reader.GetAttribute("roadWidthInTexture"));
                    if (Valid(record)) output[record.roadType] = record;
                    else warnings.Add("Skipped invalid road XML record " + roadName);
                }
            }
        }

        private static void Assign(Template item, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (key.Equals("Texture", StringComparison.OrdinalIgnoreCase))
            {
                item.sourceTextureName = value.Trim('"');
                return;
            }
            float number;
            if (!float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number)) return;
            if (key.Equals("RoadWidth", StringComparison.OrdinalIgnoreCase))
                item.roadWidth = number;
            else if (key.Equals("RoadWidthInTexture", StringComparison.OrdinalIgnoreCase))
                item.roadWidthInTexture = number;
        }
    }
}
