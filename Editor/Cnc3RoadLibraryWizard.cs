using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;
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
        [MenuItem("Assets/C&C SAGE/Auto-Match Road Library Textures")]
        private static void AutoMatchSelectedLibrary()
        {
            var library = Selection.activeObject as Cnc3RoadLibrary;
            if (library == null) return;
            string assetPath = AssetDatabase.GetAssetPath(library);
            var catalog = Cnc3RoadTextureCatalog.Create(assetPath, library, null);
            var warnings = new List<string>();
            var definitions = library.ReadTemplates(warnings);
            int assigned = 0, preserved = 0, migrated = 0;
            bool changed = false;
            var unmatched = new List<string>();
            foreach (var entry in library.overrides)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.roadType))
                    continue;
                Cnc3RoadLibrary.Template source;
                bool hasDefinition = definitions.TryGetValue(entry.roadType, out source);
                if (hasDefinition)
                {
                    bool generatedGuess =
                        string.IsNullOrWhiteSpace(entry.sourceTextureName) &&
                        Mathf.Approximately(entry.roadWidthInTexture, 1f) &&
                        Mathf.Approximately(entry.roadWidth,
                            Cnc3MapRoadBuilder.GuessSourceWidth(entry.roadType));
                    if ((string.IsNullOrWhiteSpace(entry.sourceTextureName) &&
                         !string.IsNullOrWhiteSpace(source.sourceTextureName)) ||
                        generatedGuess)
                    {
                        if (!changed)
                        {
                            Undo.RecordObject(library,
                                "Match original C&C3 road template definitions");
                            changed = true;
                        }
                        if (string.IsNullOrWhiteSpace(entry.sourceTextureName))
                            entry.sourceTextureName = source.sourceTextureName;
                        if (generatedGuess)
                        {
                            entry.roadWidth = source.roadWidth;
                            entry.roadWidthInTexture = source.roadWidthInTexture;
                        }
                        if (string.IsNullOrWhiteSpace(entry.normalTextureName))
                            entry.normalTextureName = source.normalTextureName;
                        if (entry.priority == 0)
                            entry.priority = source.priority;
                        migrated++;
                    }
                }
                if (entry.texture != null)
                {
                    preserved++;
                    continue;
                }

                Texture2D matched;
                string matchMethod;
                string declaredName = !string.IsNullOrWhiteSpace(entry.sourceTextureName)
                    ? entry.sourceTextureName
                    : (hasDefinition ? source.sourceTextureName : "");
                if (catalog.TryResolve(entry.roadType, declaredName,
                    true, out matched, out matchMethod))
                {
                    if (!changed)
                    {
                        Undo.RecordObject(library,
                            "Match original C&C3 road textures");
                        changed = true;
                    }
                    entry.texture = matched;
                    assigned++;
                }
                else unmatched.Add(entry.roadType + " (" + declaredName + ")");
            }

            if (changed)
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            string details = "Road library matching: " + migrated +
                " template records enriched from supplied definitions, " +
                assigned + " textures matched, " + preserved +
                " manual Texture2D assignments preserved, " +
                unmatched.Count + " textures missing.\n" +
                "Searched folders: " + catalog.SearchFolders +
                (unmatched.Count > 0 ? "\nMissing: " +
                    string.Join(", ", unmatched.ToArray()) : "") +
                (warnings.Count > 0 ? "\nParser: " +
                    string.Join("; ", warnings.ToArray()) : "");
            Debug.Log("[C&C SAGE ROADS] " + details);
            EditorUtility.DisplayDialog("C&C SAGE Road Texture Matching",
                details, "OK");
        }

        [MenuItem("Assets/C&C SAGE/Auto-Match Road Library Textures", true)]
        private static bool CanAutoMatchSelectedLibrary()
        {
            return Selection.activeObject is Cnc3RoadLibrary;
        }

        private const string OfficialRoadXmlUrl =
            "https://raw.githubusercontent.com/electronicarts/CnC_Modding_Support/" +
            "main/Kanes%20Wrath/Xml/GlobalData/Roads.xml";
        private const string OfficialRoadXmlAssetPath =
            "Assets/CnC3KW/Definitions/Roads_KanesWrath.xml";

        [MenuItem("Tools/C&C SAGE/Install Official Kane's Wrath Road Definitions")]
        private static void InstallOfficialRoadDefinitions()
        {
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(OfficialRoadXmlAssetPath) != null)
            {
                AttachOfficialDefinitionsToLibraries();
                return;
            }
            var request = UnityWebRequest.Get(OfficialRoadXmlUrl);
            request.timeout = 45;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError("[C&C SAGE ROADS] Official EA XML download failed: " +
                            request.error + ". Manual source: " + OfficialRoadXmlUrl);
                        return;
                    }
                    string xml = request.downloadHandler.text;
                    if (xml.Length < 5000 || xml.IndexOf("<Road", 
                        StringComparison.OrdinalIgnoreCase) < 0 ||
                        xml.IndexOf("RoadWidthInTexture",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Debug.LogError("[C&C SAGE ROADS] Unexpected content returned " +
                            "from official EA Roads.xml. Nothing was written.");
                        return;
                    }
                    if (!AssetDatabase.IsValidFolder("Assets/CnC3KW"))
                        AssetDatabase.CreateFolder("Assets", "CnC3KW");
                    if (!AssetDatabase.IsValidFolder("Assets/CnC3KW/Definitions"))
                        AssetDatabase.CreateFolder("Assets/CnC3KW", "Definitions");

                    string physicalPath = Path.GetFullPath(OfficialRoadXmlAssetPath);
                    File.WriteAllText(physicalPath, xml,
                        new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(OfficialRoadXmlAssetPath,
                        ImportAssetOptions.ForceSynchronousImport);
                    AttachOfficialDefinitionsToLibraries();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[C&C SAGE ROADS] Failed to install " +
                        "EA Roads.xml: " + ex.Message);
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static void AttachOfficialDefinitionsToLibraries()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                OfficialRoadXmlAssetPath);
            if (textAsset == null)
            {
                Debug.LogError("[C&C SAGE ROADS] Roads.xml was not imported as a TextAsset. " +
                    OfficialRoadXmlAssetPath);
                return;
            }
            int attached = 0, preserved = 0, total = 0;
            string[] assetGuids = AssetDatabase.FindAssets(
                "RoadLibrary", new[] { "Assets/CnC3KW/Maps" });
            foreach (string guid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var library = AssetDatabase.LoadAssetAtPath<Cnc3RoadLibrary>(assetPath);
                if (library == null) continue;
                total++;
                if (library.sourceDefinitions != null)
                {
                    preserved++;
                    continue;
                }
                Undo.RecordObject(library, "Use official Kane's Wrath road definitions");
                library.sourceDefinitions = textAsset;
                EditorUtility.SetDirty(library);
                attached++;
            }
            if (attached > 0)
                AssetDatabase.SaveAssets();
            string result = "Official EA Roads.xml installed at " +
                OfficialRoadXmlAssetPath + ".\n" +
                attached + " of " + total + " map Road Libraries linked; " +
                preserved + " existing custom source definitions preserved.\n" +
                "Map importer automatically uses source definitions during " +
                "reimport, without modifying manual Texture2D assignments.\n" +
                "Select any *_RoadLibrary.asset then use Assets > C&C SAGE > " +
                "Auto-Match Road Library Textures to persist the original " +
                "RoadWidth, RoadWidthInTexture and DDS references.";
            Debug.Log("[C&C SAGE ROADS] " + result);
            EditorUtility.DisplayDialog("EA Kane's Wrath Roads.xml",
                result, "OK");
        }

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
            // Only valid consecutive Start/End pairs are roads.
            // Filtering by an isolated road flag previously included
            // map object types without drawable road geometry.
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < mapInfo.placements.Count; i++)
            {
                Cnc3MapPlacement start = mapInfo.placements[i];
                Cnc3MapPlacement end = mapInfo.placements[i + 1];
                if ((start.roadFlags & 2u) == 0 ||
                    (end.roadFlags & 4u) == 0 ||
                    string.IsNullOrWhiteSpace(start.typeName) ||
                    !string.Equals(start.typeName, end.typeName,
                        StringComparison.Ordinal))
                    continue;
                names.Add(start.typeName);
                i++;
            }
            if (names.Count == 0)
            {
                Debug.LogWarning("[C&C SAGE ROADS] No road control-point types in this map.");
                return;
            }
            var library = ScriptableObject.CreateInstance<Cnc3RoadLibrary>();
            library.sourceDefinitions = AssetDatabase.LoadAssetAtPath<TextAsset>(
                OfficialRoadXmlAssetPath);
            var warnings = new List<string>();
            var sourceTemplates = library.ReadTemplates(warnings);
            foreach (string name in names)
            {
                Cnc3RoadLibrary.Template known;
                bool found = sourceTemplates.TryGetValue(name, out known);
                library.overrides.Add(new Cnc3RoadLibrary.Template
                {
                    roadType = name,
                    roadWidth = found ? known.roadWidth :
                        Cnc3MapRoadBuilder.GuessSourceWidth(name),
                    roadWidthInTexture = found ? known.roadWidthInTexture : 1f,
                    sourceTextureName = found ? known.sourceTextureName : "",
                    normalTextureName = found ? known.normalTextureName : "",
                    priority = found ? known.priority : 0f,
                    texture = null
                });
            }

            string parent = Path.GetDirectoryName(mapPath).Replace('\\', '/');
            string outputPath = AssetDatabase.GenerateUniqueAssetPath(
                parent + "/" + Path.GetFileNameWithoutExtension(mapPath) +
                "_RoadLibrary.asset");
            AssetDatabase.CreateAsset(library, outputPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(library);
            Selection.activeObject = library;
            Debug.Log("[C&C SAGE ROADS] Created " + outputPath + " with " +
                names.Count + " valid road surface types. " +
                (library.sourceDefinitions != null
                    ? "Original EA RoadWidth/Texture fields loaded when available."
                    : "Install official EA Roads.xml for original widths/atlas names.") +
                " Missing styles and DDS assets require independent validation.");
        }
    }
}
