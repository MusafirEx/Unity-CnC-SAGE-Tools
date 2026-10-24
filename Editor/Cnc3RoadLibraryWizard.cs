using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Generate a road-template asset with the exact road type names in the
    /// selected source .map. The widths are explicitly APPROXIMATE until
    /// real source definitions are supplied; no textures are fabricated.
    /// </summary>
    internal static class Cnc3RoadLibraryWizard
    {
        [MenuItem("Assets/C&C SAGE/Create Road Library From Map")]
        private static void CreateSelected()
        {
            UnityEngine.Object selected = Selection.activeObject;
            string selectedPath = AssetDatabase.GetAssetPath(selected);
            CreateFromMap(selectedPath);
        }

        [MenuItem("Assets/C&C SAGE/Create Road Library From Map", true)]
        private static bool CanCreateSelected()
        {
            return HasSourceMap(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        internal static bool HasSourceMap(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".map", StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        internal static void CreateFromMap(string mapPath)
        {
            if (!HasSourceMap(mapPath))
            {
                Debug.LogWarning("[C&C SAGE ROADS] Select a .map source asset.");
                return;
            }
            var loaded = AssetDatabase.LoadAllAssetsAtPath(mapPath);
            Cnc3MapManifest mapInfo = null;
            foreach (UnityEngine.Object asset in loaded)
                if (asset is Cnc3MapManifest info)
                {
                    mapInfo = info;
                    break;
                }
            if (mapInfo == null || mapInfo.placements == null)
            {
                Debug.LogWarning("[C&C SAGE ROADS] The source .map has no imported Cnc3MapManifest.");
                return;
            }
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Cnc3MapPlacement placement in mapInfo.placements)
                if ((placement.roadFlags & (2u | 4u)) != 0 &&
                    !string.IsNullOrWhiteSpace(placement.typeName))
                    names.Add(placement.typeName);
            if (names.Count == 0)
            {
                Debug.LogWarning("[C&C SAGE ROADS] No road control-point types in this map.");
                return;
            }
            var library = ScriptableObject.CreateInstance<Cnc3RoadLibrary>();
            foreach (string name in names)
                library.overrides.Add(new Cnc3RoadLibrary.Template
                {
                    roadType = name,
                    roadWidth = Cnc3MapRoadBuilder.GuessSourceWidth(name),
                    roadWidthInTexture = 1f,
                    sourceTextureName = "",
                    texture = null
                });

            string parent = Path.GetDirectoryName(mapPath).Replace('\\', '/');
            string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                parent + "/" + Path.GetFileNameWithoutExtension(mapPath) +
                "_RoadLibrary.asset");
            AssetDatabase.CreateAsset(library, outputPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(library);
            Selection.activeObject = library;
            Debug.Log("[C&C SAGE ROADS] Created " + outputPath + " with " +
                names.Count + " source road types. Widths are previews; " +
                "set original RoadWidth/RoadWidthInTexture and link extracted " +
                "road atlas textures before expecting in-game fidelity.");
        }
    }
}
