using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Resolves road texture atlases using exact source filenames, never by
    /// guessing that two road assets with similar names are interchangeable.
    /// Discovers the WBData sibling of Maps automatically and retains a list
    /// of inspected paths for the MapInfo diagnostic report.
    /// </summary>
    internal sealed class Cnc3RoadTextureCatalog
    {
        private readonly Dictionary<string, Texture2D> _byName =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _folders = new List<string>();
        private int _indexed;

        internal int IndexedCount { get { return _indexed; } }
        internal string SearchFolders { get { return string.Join("; ", _folders.ToArray()); } }

        internal static Cnc3RoadTextureCatalog Create(string mapOrLibraryPath,
            Cnc3RoadLibrary library, DefaultAsset requestedFolder)
        {
            var result = new Cnc3RoadTextureCatalog();
            string explicitPath = requestedFolder != null
                ? AssetDatabase.GetAssetPath(requestedFolder) : null;
            result.AddFolder(explicitPath);

            string firstFolder = Path.GetDirectoryName(mapOrLibraryPath ?? "");
            // Typical: Assets/CnC3KW/Maps/SomeMap/SomeMap.map =>
            //          Assets/CnC3KW/WBData.
            string searchParent = firstFolder;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(searchParent);
                 depth++)
            {
                result.AddFolder(Path.Combine(searchParent, "WBData"));
                string parent = Path.GetDirectoryName(searchParent);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, searchParent, StringComparison.Ordinal))
                    break;
                searchParent = parent;
            }

            if (library != null && library.sourceDefinitions != null)
            {
                string definitionsPath = AssetDatabase.GetAssetPath(library.sourceDefinitions);
                result.AddFolder(Path.GetDirectoryName(definitionsPath ?? ""));
            }

            result.AddFolder(firstFolder);
            result.Scan();
            return result;
        }

        internal bool TryResolve(string roadType, string declaredTexture,
            out Texture2D texture, out string matchSource)
        {
            texture = null;
            matchSource = "Not found";
            string declared = TextureKey(declaredTexture);
            if (!string.IsNullOrEmpty(declared) && _byName.TryGetValue(declared, out texture))
            {
                matchSource = "Original texture name";
                return true;
            }

            // A filename equal to source MapObject.TypeName is a *candidate*.
            // Explicit RoadTemplate.Texture always takes priority.
            string byType = TextureKey(roadType);
            if (!string.IsNullOrEmpty(byType) && _byName.TryGetValue(byType, out texture))
            {
                matchSource = "Exact road type filename (candidate)";
                return true;
            }

            if (!string.IsNullOrEmpty(declared))
                matchSource = "Missing declared texture: " + declared;
            else
                matchSource = "No exact DDS/TGA filename for " + roadType;
            return false;
        }

        private static string TextureKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string cleaned = name.Trim().Trim('"', '\'').Replace('\\', '/');
            int separator = cleaned.LastIndexOf('/');
            if (separator >= 0) cleaned = cleaned.Substring(separator + 1);
            cleaned = Path.GetFileNameWithoutExtension(cleaned).Trim();
            return cleaned;
        }

        private void AddFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase))
                return;
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (string existing in _folders)
                if (string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase))
                    return;
            _folders.Add(folder);
        }

        private void Scan()
        {
            foreach (string folder in _folders)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                    new[] { folder });
                var candidates = new List<string>(guids.Length);
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath))
                        candidates.Add(assetPath);
                }
                candidates.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string assetPath in candidates)
                {
                    string extension = Path.GetExtension(assetPath);
                    if (!extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string key = TextureKey(assetPath);
                    if (string.IsNullOrEmpty(key) || _byName.ContainsKey(key))
                        continue;
                    Texture2D image = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (image == null) continue;
                    _byName.Add(key, image);
                    _indexed++;
                }
            }
        }
    }
}
