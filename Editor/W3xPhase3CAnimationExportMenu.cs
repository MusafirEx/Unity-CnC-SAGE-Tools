using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Explicit opt-in export of ONE edited Phase 3B AnimationClip to a
    /// fresh, validated, same-folder W3X copy. Source and existing outputs
    /// are never overwritten. No binary compilation/game install.
    /// </summary>
    internal static class W3xPhase3CAnimationExportMenu
    {
        private const string Menu =
            "Tools/C&C SAGE/Export Animation to W3X...";

        [MenuItem(Menu)]
        private static void Run()
        {
            W3xEditableAnimationSource provenance;
            AnimationClip edited;
            string sourcePath;
            string reason;
            if (!TryResolveSelection(out edited, out provenance,
                    out sourcePath, out reason))
            {
                EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                    reason, "OK");
                return;
            }
            AnimationClip original = AssetDatabase
                .LoadAllAssetsAtPath(sourcePath)
                .OfType<AnimationClip>()
                .SingleOrDefault(c => string.Equals(c.name,
                    provenance.OriginalAnimationId,
                    StringComparison.OrdinalIgnoreCase));
            if (original == null)
            {
                EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                    "The source W3X no longer imports exactly one matching " +
                    "AnimationClip. Reimport the original W3X and retry.",
                    "OK");
                return;
            }
            string sourceFullPath = Path.GetFullPath(sourcePath);
            if (!EditorUtility.DisplayDialog(
                "C&C SAGE Phase 3C: Export W3X Copy",
                "Export edited clip '" + edited.name +
                "' as a separate W3X XML copy?\n\n" +
                "Source: " + sourcePath + "\n" +
                "Original animation ID: " +
                    provenance.OriginalAnimationId + "\n\n" +
                "Existing X/Y/Z translation and quaternion channels " +
                "can be edited. New translation or rotation channels " +
                "can be added on EXISTING SAGE pivots only. Unknown " +
                "channels and original W3X XML are preserved.\n\n" +
                "Output must be in the SAME directory as the source, " +
                "with a NEW filename. Existing files are never overwritten." +
                "\n\nRequires external WrathEd/game-toolchain validation " +
                "before use in Kane's Wrath.",
                "Export W3X Copy", "Cancel"))
                return;

            string fileName = Path.GetFileNameWithoutExtension(sourcePath) +
                "_unity_anim";
            string destination = EditorUtility.SaveFilePanel(
                "Export edited W3X animation copy",
                Path.GetDirectoryName(sourceFullPath),
                fileName, "w3x");
            if (string.IsNullOrWhiteSpace(destination))
                return;
            destination = Path.GetFullPath(destination);
            if (!string.Equals(Path.GetExtension(destination), ".w3x",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(destination),
                    Path.GetDirectoryName(sourceFullPath),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(destination, sourceFullPath,
                    StringComparison.OrdinalIgnoreCase) ||
                File.Exists(destination))
            {
                EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                    "Choose a NEW .w3x filename beside the original W3X. " +
                    "Existing output files and the original source are " +
                    "never overwritten.", "OK");
                return;
            }

            try
            {
                var source = W3xExportCore.LoadSource(sourceFullPath);
                var decoded = W3xReader.Read(sourceFullPath);
                W3xPhase3CAnimationExportCore.Report report;
                XDocument output =
                    W3xPhase3CAnimationExportCore.BuildDocument(
                        source, decoded, original, edited, provenance,
                        out report);
                if (report.ChangedChannels == 0 && report.AddedChannels == 0)
                {
                    EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                        "No changed or new SAGE channel frame values " +
                        "were detected. No W3X export was created.\n\n" +
                        "Changes only to tangents or unkeyed subframes " +
                        "cannot be represented by this exporter.", "OK");
                    return;
                }

                // Preserve relative Include/art references by keeping the
                // new output in the same folder. Stage/verify XML before
                // moving to a non-existing destination.
                string temporary = destination + "." +
                    Guid.NewGuid().ToString("N") + ".sage3c-tmp";
                bool stagedValidated = false;
                bool committed = false;
                try
                {
                    W3xExportCore.Save(output, temporary);
                    var parsedBack = W3xExportCore.LoadSource(temporary);
                    if (!XNode.DeepEquals(output, parsedBack))
                        throw new InvalidDataException(
                            "Staged W3X was not reproduced exactly after " +
                            "serialization. Export aborted.");
                    // The actual importer must be able to decode every
                    // added channel before the new file is committed.
                    var stagedAnimation = parsedBack.Root.Elements()
                        .Single(e => e.Name.LocalName == "W3DAnimation" &&
                            string.Equals((string)e.Attribute("id"),
                                provenance.OriginalAnimationId,
                                StringComparison.OrdinalIgnoreCase));
                    var warnings = new System.Collections.Generic.List<string>();
                    var parsedAnimation = W3xAnimationReader.Read(
                        stagedAnimation, warnings);
                    var originalAnimation = decoded.Animations
                        .Single(a => string.Equals(a.Identifier,
                            provenance.OriginalAnimationId,
                            StringComparison.OrdinalIgnoreCase));
                    if (parsedAnimation == null ||
                        parsedAnimation.Channels.Count !=
                            originalAnimation.Channels.Count +
                            report.AddedChannels)
                        throw new InvalidDataException(
                            "Staged W3X contains animation channels that " +
                            "the Phase 3A reader cannot decode correctly.");
                    stagedValidated = true;
                    // File.Move never replaces existing files on the
                    // supported .NET/Unity deployment.
                    if (File.Exists(destination))
                        throw new IOException(
                            "Destination appeared during export; " +
                            "existing W3X not overwritten.");
                    File.Move(temporary, destination);
                    committed = true;
                }
                finally
                {
                    if (!committed && File.Exists(temporary))
                    {
                        if (stagedValidated)
                            Debug.LogError(
                                "[C&C SAGE Phase 3C] Validated temporary " +
                                "W3X retained after failed commit: " +
                                temporary);
                        else
                            File.Delete(temporary);
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log("[C&C SAGE Phase 3C] Export SUCCESS: " +
                    destination + "\n" + report.Describe() +
                    "\nOriginal source: " + sourcePath +
                    " (unchanged). This is W3X XML, not a compiled game mod.");
                EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                    "Exported a validated new W3X file:\n" +
                    destination + "\n\n" + report.Describe() +
                    "\n\nAnimation exporter remains experimental. " +
                    "Reimport the copy in Unity and compare curves before " +
                    "testing with the game toolchain.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3C] Export FAILED; original source " +
                    "untouched. " + e);
                EditorUtility.DisplayDialog("C&C SAGE Phase 3C",
                    "Export refused:\n" + e.Message +
                    "\n\nOriginal W3X is unchanged.", "OK");
            }
        }

        internal static bool TryResolveSelection(
            out AnimationClip edited, out W3xEditableAnimationSource metadata,
            out string originalSource, out string reason)
        {
            edited = Selection.activeObject as AnimationClip;
            metadata = Selection.activeObject as W3xEditableAnimationSource;
            originalSource = null;
            reason = null;
            if (metadata != null)
                edited = metadata.EditableClip;
            if (edited == null)
            {
                reason = "In the Unity Project window, select ONE " +
                    "editable animation asset created by Phase 3B: " +
                    "<animation>_Editable.anim or the matching " +
                    "<animation>_Source.asset. Then choose Tools > " +
                    "C&C SAGE > Export Animation to W3X... again. " +
                    "Do not select a SAGE_Pivots transform, a model " +
                    "prefab, or a W3X-imported clip sub-asset.";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(edited);
            if (!string.Equals(Path.GetExtension(path), ".anim",
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Select a Phase 3B independent .anim copy, " +
                    "not a W3X imported AnimationClip sub-asset.";
                return false;
            }
            string folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                reason = "The Phase 3B animation workspace folder is missing.";
                return false;
            }
            // C# forbids capturing an out parameter in a LINQ lambda
            // (CS1628). Copy it to an ordinary local first.
            AnimationClip selectedClip = edited;
            var matches = Directory.GetFiles(folder)
                .Where(p => string.Equals(Path.GetExtension(p), ".asset",
                    StringComparison.OrdinalIgnoreCase))
                .Select(p => AssetDatabase.LoadAssetAtPath<
                    W3xEditableAnimationSource>(p.Replace('\\', '/')))
                .Where(p => p != null && p.EditableClip == selectedClip)
                .ToArray();
            if (matches.Length != 1 ||
                metadata != null && metadata != matches[0])
            {
                reason = "Expected exactly one Phase 3B metadata sidecar " +
                    "that references this editable clip.";
                return false;
            }
            metadata = matches[0];
            if (string.IsNullOrWhiteSpace(metadata.SourceW3xGuid) ||
                string.IsNullOrWhiteSpace(metadata.OriginalAnimationId) ||
                string.IsNullOrWhiteSpace(metadata.OriginalHierarchyId))
            {
                reason = "Phase 3B animation metadata is incomplete.";
                return false;
            }
            originalSource = AssetDatabase.GUIDToAssetPath(
                metadata.SourceW3xGuid);
            if (string.IsNullOrWhiteSpace(originalSource) ||
                !string.Equals(Path.GetExtension(originalSource), ".w3x",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(AssetDatabase.AssetPathToGUID(
                        originalSource), metadata.SourceW3xGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Original W3X GUID is no longer resolvable. " +
                    "The exporter will not guess a different source file.";
                return false;
            }
            return true;
        }

        // Intentionally no MenuItem validation: keep Export Animation
        // visible even when a model, prefab or GameObject is selected.
        // Run() provides a useful selection guide before any file I/O.
    }
}
