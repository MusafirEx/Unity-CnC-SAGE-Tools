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
            public string normalTextureName;
            public float priority;
            [Min(0.01f)] public float roadWidth = 35f;
            [Range(0.05f, 2f)] public float roadWidthInTexture = 0.9f;
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
                // EA's Roads.xml begins with a UTF-8 BOM. Unity TextAsset
                // can preserve it as U+FEFF, which otherwise breaks XML
                // detection and XmlReader when reading from a StringReader.
                string content = sourceDefinitions.text.TrimStart(
                    '\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
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
                string typeName = road.roadType.Trim();
                Template source;
                if (!templates.TryGetValue(typeName, out source))
                {
                    templates[typeName] = road;
                    continue;
                }

                // Phase 5A's initial "Create Road Library From Map" produced
                // guesses with no Texture name, WidthInTexture=1, and the
                // GuessSourceWidth value. These were not original definitions.
                // Once a real Roads.xml is supplied, upgrade those guesses
                // in-memory while retaining any manually linked Texture2D.
                // Never mutate serialized user assets during a .map import.
                bool generatedGuess = string.IsNullOrWhiteSpace(road.sourceTextureName) &&
                    Mathf.Approximately(road.roadWidthInTexture, 1f) &&
                    Mathf.Approximately(road.roadWidth,
                        Cnc3MapRoadBuilder.GuessSourceWidth(typeName));

                templates[typeName] = new Template
                {
                    roadType = typeName,
                    sourceTextureName = string.IsNullOrWhiteSpace(road.sourceTextureName)
                        ? source.sourceTextureName : road.sourceTextureName,
                    normalTextureName = string.IsNullOrWhiteSpace(road.normalTextureName)
                        ? source.normalTextureName : road.normalTextureName,
                    roadWidth = generatedGuess ? source.roadWidth : road.roadWidth,
                    roadWidthInTexture = generatedGuess
                        ? source.roadWidthInTexture : road.roadWidthInTexture,
                    priority = road.priority != 0 ? road.priority : source.priority,
                    texture = road.texture != null ? road.texture : source.texture
                };
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
                            current = new Template {
                                roadType = start.Groups[1].Value,
                                roadWidth = 0f,
                                roadWidthInTexture = 0f
                            };
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
                    var record = new Template {
                        roadType = roadName.Trim(),
                        roadWidth = 0f,
                        roadWidthInTexture = 0f
                    };
                    Assign(record, "Texture",
                        reader.GetAttribute("Texture") ?? reader.GetAttribute("texture"));
                    Assign(record, "RoadWidth",
                        reader.GetAttribute("RoadWidth") ?? reader.GetAttribute("roadWidth"));
                    Assign(record, "RoadWidthInTexture",
                        reader.GetAttribute("RoadWidthInTexture") ??
                        reader.GetAttribute("roadWidthInTexture"));
                    Assign(record, "NormalTexture",
                        reader.GetAttribute("NormalTexture") ??
                        reader.GetAttribute("normalTexture"));
                    Assign(record, "Priority",
                        reader.GetAttribute("Priority") ?? reader.GetAttribute("priority"));
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
            if (key.Equals("NormalTexture", StringComparison.OrdinalIgnoreCase))
            {
                item.normalTextureName = value.Trim('"');
                return;
            }
            float number;
            if (!float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out number)) return;
            if (key.Equals("RoadWidth", StringComparison.OrdinalIgnoreCase))
                item.roadWidth = number;
            else if (key.Equals("RoadWidthInTexture", StringComparison.OrdinalIgnoreCase))
                item.roadWidthInTexture = number;
            else if (key.Equals("Priority", StringComparison.OrdinalIgnoreCase))
                item.priority = number;
        }
    }
}
