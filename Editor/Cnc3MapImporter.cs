using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// First-pass C&C 3 / Kane's Wrath World Builder .map importer.
    /// Source map remains untouched. Current scope: terrain height preview only.
    /// </summary>
    [ScriptedImporter(1, "map")]
    public sealed class Cnc3MapImporter : ScriptedImporter
    {
        [Tooltip("Reconstruct HeightMapData as tiled, editable Unity mesh sub-assets.")]
        public bool importTerrain = true;

        [Tooltip("C&C SAGE horizontal grid spacing; usually 10 game units per sample.")]
        [Min(0.001f)]
        public float horizontalScale = 10f;

        [Tooltip("16-bit C&C 3 World Builder elevation multiplier.")]
        [Min(0.00001f)]
        public float verticalScale = 0.0390625f;

        [Tooltip("Visual correction for a terrain origin mismatch, if your map is inverted.")]
        public bool flipMapNorthSouth;

        [Tooltip("Include the unplayable border area of the map in the preview.")]
        public bool includeBorder = true;

        [Tooltip("Add MeshColliders to the generated terrain tiles.")]
        public bool addMeshColliders;

        [Tooltip("Optional material for preview terrain. This does not resolve game textures.")]
        public Material terrainMaterial;

        public override void OnImportAsset(AssetImportContext context)
        {
            Cnc3MapReader.Data map = Cnc3MapReader.Read(context.assetPath);
            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            context.AddObjectToAsset("CNC3_Map_Root", root);
            context.SetMainObject(root);

            var report = ScriptableObject.CreateInstance<Cnc3MapManifest>();
            report.name = root.name + "_MapInfo";
            report.width = map.Width;
            report.height = map.Height;
            report.border = map.Border;
            report.heightVersion = map.HeightmapVersion;
            report.wasCompressed = map.Compressed;
            report.sections = map.SectionNames.ToArray();
            report.terrainGenerated = importTerrain;
            context.AddObjectToAsset("CNC3_Map_Info", report);

            if (importTerrain)
                BuildTerrain(context, root, map, report);

            foreach (string warning in map.Warnings)
                Debug.LogWarning("[C&C SAGE MAP] " + context.assetPath + ": " + warning);
            Debug.Log("[C&C SAGE MAP] Imported " + context.assetPath + " " +
                map.Width + "x" + map.Height + " points, " + report.tileCount +
                " terrain meshes. Objects/material blending are not reconstructed.");
        }

        private void BuildTerrain(AssetImportContext context, GameObject root,
            Cnc3MapReader.Data map, Cnc3MapManifest report)
        {
            int startX = includeBorder ? 0 : map.Border;
            int startY = includeBorder ? 0 : map.Border;
            int endX = includeBorder ? map.Width - 1 : map.Width - map.Border - 1;
            int endY = includeBorder ? map.Height - 1 : map.Height - map.Border - 1;
            if (endX <= startX || endY <= startY)
                throw new InvalidDataException("Terrain playable bounds are empty.");

            float hScale = Mathf.Clamp(horizontalScale, 0.001f, 100000f);
            float vScale = Mathf.Clamp(verticalScale, 0.00001f, 10000f);
            if (map.HeightmapVersion < 5 &&
                Mathf.Approximately(vScale, 0.0390625f))
                vScale = 0.625f;

            Material displayMat = terrainMaterial;
            if (displayMat == null)
            {
                Shader shader = Shader.Find("Standard") ??
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Unlit/Color");
                if (shader == null)
                    throw new InvalidDataException("No suitable Unity terrain preview shader.");
                displayMat = new Material(shader) { name = "CNC3_Terrain_Preview" };
                context.AddObjectToAsset("CNC3_Terrain_Material", displayMat);
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(displayMat);
                if (!string.IsNullOrEmpty(assetPath) &&
                    assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    context.DependsOnSourceAsset(assetPath);
            }

            var terrain = new GameObject("SAGE_Terrain").transform;
            terrain.SetParent(root.transform, false);
            const int cellsPerTile = 64;
            int counter = 0;

            for (int lowerY = startY; lowerY < endY; lowerY += cellsPerTile)
            for (int lowerX = startX; lowerX < endX; lowerX += cellsPerTile)
            {
                int tileX = Math.Min(cellsPerTile, endX - lowerX);
                int tileY = Math.Min(cellsPerTile, endY - lowerY);
                int row = tileX + 1;
                var positions = new Vector3[row * (tileY + 1)];
                var uv = new Vector2[positions.Length];
                var triangles = new int[tileX * tileY * 6];

                for (int y = 0; y <= tileY; y++)
                for (int x = 0; x <= tileX; x++)
                {
                    int mapX = lowerX + x;
                    int mapY = lowerY + y;
                    int sampleY = flipMapNorthSouth ? map.Height - 1 - mapY : mapY;
                    float height = map.Elevations[sampleY * map.Width + mapX] * vScale;
                    int index = y * row + x;
                    positions[index] = new Vector3(
                        x * hScale,
                        height,
                        y * hScale);
                    uv[index] = new Vector2(
                        mapX / (float)(map.Width - 1),
                        mapY / (float)(map.Height - 1));
                }
                int indexBuffer = 0;
                for (int y = 0; y < tileY; y++)
                for (int x = 0; x < tileX; x++)
                {
                    int a = y * row + x;
                    int b = a + row;
                    triangles[indexBuffer++] = a;
                    triangles[indexBuffer++] = b;
                    triangles[indexBuffer++] = a + 1;
                    triangles[indexBuffer++] = a + 1;
                    triangles[indexBuffer++] = b;
                    triangles[indexBuffer++] = b + 1;
                }
                var mesh = new Mesh
                {
                    name = "Terrain_" + lowerX + "_" + lowerY,
                    indexFormat = IndexFormat.UInt16,
                    vertices = positions,
                    triangles = triangles,
                    uv = uv
                };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                context.AddObjectToAsset("CNC3_Terrain_" + counter, mesh);

                var tile = new GameObject(mesh.name);
                tile.transform.SetParent(terrain, false);
                tile.transform.localPosition = new Vector3(
                    lowerX * hScale, 0, lowerY * hScale);
                tile.AddComponent<MeshFilter>().sharedMesh = mesh;
                tile.AddComponent<MeshRenderer>().sharedMaterial = displayMat;
                if (addMeshColliders)
                    tile.AddComponent<MeshCollider>().sharedMesh = mesh;
                counter++;
            }
            report.tileCount = counter;
            report.horizontalScale = hScale;
            report.verticalScale = vScale;
            report.terrainWidth = (endX - startX) * hScale;
            report.terrainLength = (endY - startY) * hScale;
        }
    }

    /// <summary>Persistent map metadata, available as a Unity sub-asset.</summary>
    public sealed class Cnc3MapManifest : ScriptableObject
    {
        public int width;
        public int height;
        public int border;
        public int tileCount;
        public bool wasCompressed;
        public bool terrainGenerated;
        public ushort heightVersion;
        public float horizontalScale;
        public float verticalScale;
        public float terrainWidth;
        public float terrainLength;
        public string[] sections;
    }

    [CustomEditor(typeof(Cnc3MapImporter))]
    public sealed class Cnc3MapImporterInspector : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("C&C 3 World Builder Map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Terrain-only preview; objects, blend textures, roads, " +
                "water and scripts are not imported in this milestone.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importTerrain"),
                new GUIContent("Import Terrain"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("horizontalScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("verticalScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flipMapNorthSouth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeBorder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("addMeshColliders"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainMaterial"));

            var importer = target as Cnc3MapImporter;
            if (importer != null)
            {
                foreach (var item in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
                {
                    var report = item as Cnc3MapManifest;
                    if (report == null) continue;
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Imported Map Info", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Grid", report.width + " x " + report.height);
                    EditorGUILayout.LabelField("Border", report.border.ToString());
                    EditorGUILayout.LabelField("Heightmap Version", report.heightVersion.ToString());
                    EditorGUILayout.LabelField("Compression", report.wasCompressed ? "EAR / RefPack" : "CkMp");
                    EditorGUILayout.LabelField("Terrain Tiles", report.tileCount.ToString());
                    EditorGUILayout.LabelField("Map Sections", report.sections == null
                        ? "Unknown" : report.sections.Length.ToString());
                    break;
                }
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
