using System;
using System.Collections.Generic;
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
    /// Read-only inspector for the actual selected W3X animation source.
    /// Does not rely on the clip importer succeeding and never writes assets.
    /// </summary>
    internal static class W3xAnimationDiagnostics
    {
        private const string Menu =
            "Tools/C&C SAGE/Diagnose Selected W3X Animation";

        [MenuItem(Menu)]
        private static void Run()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetExtension(path), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[C&C SAGE Phase 3A] Select a W3X file " +
                    "(for example GUPitbull_IDLA.W3X) in the Project window.");
                return;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                XDocument original;
                using (var reader = XmlReader.Create(path, settings))
                    original = XDocument.Load(reader);
                if (original.Root == null ||
                    original.Root.Name.LocalName != "AssetDeclaration")
                    throw new InvalidDataException(
                        "Not a W3X AssetDeclaration document.");

                var animNodes = original.Root.Elements()
                    .Where(e => e.Name.LocalName == "W3DAnimation")
                    .ToArray();
                var current = W3xReader.Read(path);
                var storedClips = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>().ToArray();
                var log = new StringBuilder();
                log.AppendLine("[C&C SAGE Phase 3A] W3X Animation Diagnosis");
                log.AppendLine("Source: " + path);
                log.AppendLine("W3DAnimation declarations: " +
                    animNodes.Length);
                log.AppendLine("Parsed animations: " +
                    current.Animations.Count);
                log.AppendLine("Resolved hierarchy declarations: " +
                    current.Hierarchies.Count);
                foreach (var h in current.Hierarchies.Take(8))
                    log.AppendLine("  hierarchy=" + h.Identifier +
                        " pivots=" + h.Pivots.Count);
                log.AppendLine("Generated AnimationClip sub-assets: " +
                    storedClips.Length);
                foreach (var clip in storedClips.Take(10))
                    log.AppendLine("  clip=" + clip.name + " length=" +
                        clip.length + " FPS=" + clip.frameRate);

                foreach (var node in animNodes.Take(24))
                {
                    string id = (string)node.Attribute("id") ?? "(no id)";
                    string hierarchy = (string)node.Attribute("Hierarchy")
                        ?? "(no hierarchy)";
                    var channels = node.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "Channels");
                    var all = channels == null ? new XElement[0] :
                        channels.Elements().ToArray();
                    var recognized = current.Animations.FirstOrDefault(a =>
                        string.Equals(a.Identifier, id,
                            StringComparison.OrdinalIgnoreCase));
                    log.AppendLine("  W3DAnimation=" + id +
                        " hierarchy=" + hierarchy +
                        " NumFrames=" + ((string)node.Attribute("NumFrames") ?? "?") +
                        " FrameRate=" + ((string)node.Attribute("FrameRate") ?? "?") +
                        " rawChannels=" + all.Length +
                        " parsedChannels=" +
                        (recognized == null ? 0 : recognized.Channels.Count));
                    foreach (var g in all.GroupBy(ch =>
                        ch.Name.LocalName + " Type=" +
                        ((string)ch.Attribute("Type") ?? "(none)"))
                        .Take(32))
                        log.AppendLine("    " + g.Key + ": " + g.Count());
                    foreach (var channel in all.Take(12))
                        log.AppendLine("    pivot=" +
                            ((string)channel.Attribute("Pivot") ?? "?") +
                            " first=" +
                            ((string)channel.Attribute("FirstFrame") ?? "?") +
                            " type=" +
                            ((string)channel.Attribute("Type") ?? "?") +
                            " keys=" + channel.Elements().Count(c =>
                                c.Name.LocalName == "Frame"));
                }

                if (current.Warnings.Count > 0)
                {
                    log.AppendLine("Warnings (" +
                        current.Warnings.Count + "):");
                    foreach (var warning in current.Warnings.Take(40))
                        log.AppendLine("  " + warning);
                }

                if (animNodes.Length == 0)
                    log.AppendLine("NO W3DAnimation element in selected " +
                        "source. Inspect the file or choose an animation W3X.");
                else if (storedClips.Length == 0)
                    log.AppendLine("NO CLIPS: inspect unsupported Type labels, " +
                        "invalid channel schema, and Hierarchy match above.");
                Debug.Log(log.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3A] W3X animation diagnosis failed: " +
                    e);
            }
        }

        [MenuItem(Menu, true)]
        private static bool Validate()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                string.Equals(Path.GetExtension(path), ".w3x",
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
