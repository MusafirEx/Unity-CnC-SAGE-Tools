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
            if (context == null || string.IsNullOrWhiteSpace(source))
                return null;
            source = source.Trim().Trim('"').Replace('\\', '/');
            if (source.StartsWith("ART:", StringComparison.OrdinalIgnoreCase))
                source = source.Substring(4);
            if (source.IndexOf(':') >= 0 || Path.IsPathRooted(source) ||
                source.StartsWith("../", StringComparison.Ordinal) ||
                source.Contains("/../"))
                return null;
            string name = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrWhiteSpace(name)) return null;

            string modelFolder = (Path.GetDirectoryName(context.assetPath) ?? "Assets")
                .Replace('\\', '/');
            // A standalone W3X remains compatible with textures beside it.
            Texture2D local = FromFolder(context, modelFolder, name);
            if (local != null) return local;

            // Find the WBData root through the W3X folder. Up to six levels
            // supports nested W3X variants without scanning the entire project.
            string candidate = modelFolder;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(candidate);
                 depth++)
            {
                if (string.Equals(Path.GetFileName(candidate), "WBData",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Texture2D sibling = FromFolder(context,
                        (candidate + "/DDS").Replace('\\', '/'), name);
                    if (sibling != null) return sibling;

                    // Legacy layout: WBData/<model>.w3x + WBData/<name>.dds.
                    return FromFolder(context, candidate, name);
                }
                string parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(candidate, parent, StringComparison.Ordinal))
                    break;
                candidate = parent.Replace('\\', '/');
            }
            return null;
        }

        private static Texture2D FromFolder(AssetImportContext context,
            string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return null;
            Dictionary<string, string> index;
            if (!Cache.TryGetValue(folder, out index))
            {
                index = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                // Unity-imported .meta is ignored. Only direct children of
                // this folder are examined, not thousands of nested W3X.
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
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
            if (!index.TryGetValue(name, out path) || !File.Exists(path))
                return null;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return null;
            context.DependsOnSourceAsset(path);
            return texture;
        }
    }
}
