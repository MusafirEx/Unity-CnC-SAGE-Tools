using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Resolves FXShader texture names across categorized extracted folders:
    /// WBData/W3X/<model>.w3x and WBData/DDS/<texture>.dds.
    /// Indexes filenames only once per folder, and never preloads all DDS files.
    /// </summary>
    internal static class Cnc3WbDataTextureLocator
    {
        private static readonly string[] Extensions =
            { ".dds", ".tga", ".png", ".jpg", ".jpeg", ".bmp" };

        private static readonly Dictionary<string, Dictionary<string, string>> Cache =
            new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        static Cnc3WbDataTextureLocator()
        {
            EditorApplication.projectChanged += Clear;
        }

        private static void Clear()
        {
            Cache.Clear();
        }

        internal static Texture2D Find(AssetImportContext context, string source)
        {
            if (context == null) return null;
            string path = ResolveAssetPath(context.assetPath, source);
            if (string.IsNullOrEmpty(path)) return null;

            // Register the dependency BEFORE loading. During a large import
            // batch the DDS may not yet have its Unity Texture2D ready, but
            // it is still a valid source-asset dependency for a later rebuild.
            context.DependsOnSourceAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Read-only resolver shared by imports and the selected W3X diagnosis
        /// menu. Returns an actual existing Unity project asset path (not
        /// a texture object) and never traverses outside the project.
        /// </summary>
        internal static string ResolveAssetPath(string w3xAssetPath, string source)
        {
            if (string.IsNullOrWhiteSpace(w3xAssetPath) ||
                string.IsNullOrWhiteSpace(source))
                return null;
            source = source.Trim().Trim('"', '\'').Replace('\\', '/');
            if (source.StartsWith("ART:", StringComparison.OrdinalIgnoreCase))
                source = source.Substring(4);
            if (source.IndexOf(':') >= 0 || Path.IsPathRooted(source) ||
                source.StartsWith("../", StringComparison.Ordinal) ||
                source.Contains("/../") || source.StartsWith("/", StringComparison.Ordinal))
                return null;

            string name = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrWhiteSpace(name)) return null;

            string modelFolder = (Path.GetDirectoryName(w3xAssetPath) ?? "Assets")
                .Replace('\\', '/');
            string path = FindSourcePath(modelFolder, name);
            if (!string.IsNullOrEmpty(path)) return path;

            string candidate = modelFolder;
            for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(candidate);
                 depth++)
            {
                if (string.Equals(Path.GetFileName(candidate), "WBData",
                    StringComparison.OrdinalIgnoreCase))
                {
                    path = FindSourcePath((candidate + "/DDS").Replace('\\', '/'), name);
                    if (!string.IsNullOrEmpty(path)) return path;
                    // Keep support for original flat WBData imports.
                    return FindSourcePath(candidate, name);
                }
                string parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(candidate, parent, StringComparison.Ordinal))
                    break;
                candidate = parent.Replace('\\', '/');
            }
            return null;
        }

        private static string FindSourcePath(string folder, string name)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;
            Dictionary<string, string> index;
            if (!Cache.TryGetValue(folder, out index))
            {
                index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                // Prefer .dds; never load all textures just to build this index.
                foreach (string extension in Extensions)
                foreach (string file in files)
                {
                    if (!string.Equals(Path.GetExtension(file), extension,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (!index.ContainsKey(stem))
                        index.Add(stem, file.Replace('\\', '/'));
                }
                Cache[folder] = index;
            }

            string path;
            return index.TryGetValue(name, out path) && File.Exists(path)
                ? path : null;
        }

    }
}
