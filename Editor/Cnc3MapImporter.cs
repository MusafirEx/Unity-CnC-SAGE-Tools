using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// First-pass C&C 3 / Kane's Wrath World Builder .map importer.
    /// Source map remains untouched. Mode 1: terrain and source object placeholders.
    /// </summary>
    [ScriptedImporter(4, "map")]
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

        [Tooltip("Create named, transform-only GameObject markers for ObjectsList.")]
        public bool importObjects = true;

        [Tooltip("Group imported markers by their C&C3 object type names.")]
        public bool groupObjectsByType = true;

        [Tooltip("Safety limit for generated GameObjects. All placements remain in the MapInfo subasset.")]
        [Min(0)]
        public int maxObjectPlaceholders = 20000;

        [Tooltip("Add MeshColliders to the generated terrain tiles.")]
        public bool addMeshColliders;

        [Tooltip("Optional material for preview terrain. This does not resolve game textures.")]
        public Material terrainMaterial;

        [Header("Mode 2 - Optional Compiled Map Assets")]
        [Tooltip("Decode supported DDS atlases from map.bin in the same folder. Does not load compiled models.")]
        public bool importCompiledAtlases = true;

        [Tooltip("Validate map.imp, map.relo and map.manifest against map.bin without interpreting opaque asset bodies.")]
        public bool inspectCompiledIndexes = true;

        [Tooltip("Apply compiled map.bin base-color atlas to terrain cells using exact SAGE texture hashes and BlendTileData tile UVs. Does not implement secondary/three-way blends.")]
        public bool applyAtlasToTerrain = true;

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
            report.objectsGenerated = importObjects;
            report.objectCount = map.Objects.Count;
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in map.Objects)
            {
                types.Add(item.TypeName);
                report.placements.Add(new Cnc3MapPlacement
                {
                    typeName = item.TypeName,
                    sagePosition = new Vector3(item.X, item.Y, item.Z),
                    sageAngleRadians = item.Angle,
                    roadFlags = item.RoadFlags,
                    recordVersion = item.Version
                });
            }
            report.distinctObjectTypes = types.Count;
            context.AddObjectToAsset("CNC3_Map_Info", report);

            // Load compiled textures and atlas descriptors before building
            // the ground, otherwise the terrain mesh/material cannot bind them.
            if (importCompiledAtlases || inspectCompiledIndexes || applyAtlasToTerrain)
                ImportCompiledAssets(context, report);
            if (importTerrain)
                BuildTerrain(context, root, map, report);
            if (importObjects)
                report.objectMarkerCount = BuildObjectPlaceholders(root, map);

            foreach (string warning in map.Warnings)
                Debug.LogWarning("[C&C SAGE MAP] " + context.assetPath + ": " + warning);
            Debug.Log("[C&C SAGE MAP] Imported " + context.assetPath + " " +
                map.Width + "x" + map.Height + " points, " + report.tileCount +
                " terrain meshes, " + map.Objects.Count +
                " source object placements (" + (importObjects ? "created" : "metadata only") + ").");
        }


        private void ImportCompiledAssets(AssetImportContext context,
            Cnc3MapManifest report)
        {
            string folder = Path.GetDirectoryName(context.assetPath);
            if (string.IsNullOrEmpty(folder)) return;
            string compiledPath = Path.Combine(folder, "map.bin").Replace('\\', '/');
            if (!File.Exists(compiledPath))
            {
                report.compiledImportNote = "No map.bin beside source .map (optional).";
                return;
            }
            context.DependsOnSourceAsset(compiledPath);

            try
            {
                Cnc3CompiledMapReader.Result compiled =
                    Cnc3CompiledMapReader.Read(compiledPath,
                        importCompiledAtlases || applyAtlasToTerrain);
                report.hasCompiledBin = true;
                report.compiledBinSize = compiled.BinarySize;
                report.compiledMetadataBytes = compiled.MetadataBytes;
                report.compiledTextureCount = compiled.Images.Count;
                for (int i = 0; i < compiled.Images.Count; i++)
                {
                    var image = compiled.Images[i];
                    if (image.Texture != null)
                        context.AddObjectToAsset("CNC3_CompiledAtlas_" + i, image.Texture);
                    report.compiledTextures.Add(new Cnc3CompiledTextureEntry
                    {
                        name = image.Texture != null ? image.Texture.name
                            : "Atlas_" + image.Offset.ToString("X8"),
                        format = image.Format,
                        width = image.Width,
                        height = image.Height,
                        mipCount = image.Mips,
                        offset = image.Offset,
                        length = image.Length,
                        texture = image.Texture
                    });
                }

                if (inspectCompiledIndexes || applyAtlasToTerrain)
                {
                    string[] companions = { "map.imp", "map.relo", "map.manifest" };
                    foreach (string filename in companions)
                    {
                        string sibling = Path.Combine(folder, filename).Replace('\\', '/');
                        if (File.Exists(sibling))
                            context.DependsOnSourceAsset(sibling);
                    }
                    var linked = Cnc3CompiledMapBundle.Inspect(folder, compiled.BinarySize);
                    report.compiledImportAddresses = linked.ImportAddresses;
                    report.compiledRelocationAddresses = linked.RelocationAddresses;
                    report.manifestGameObjectNames = linked.ManifestGameObjects;
                    report.manifestTextureNames = linked.ManifestTextures;
                    report.compiledManifestMatchesBin = linked.ManifestLinksBinary;
                    report.manifestAssetCount = linked.ManifestAssetCount;
                    report.manifestAssetsWithData = linked.ManifestAssetsWithData;
                    report.compiledAtlasAssetName = linked.TerrainAtlasName;
                    report.compiledAtlasLayoutVerified = linked.TerrainAtlasValidated;
                    report.compiledAtlasTileCount = linked.AtlasTiles.Count;
                    foreach (var tile in linked.AtlasTiles)
                        report.compiledAtlasTiles.Add(new Cnc3AtlasTileEntry
                        {
                            textureId = tile.TextureId,
                            x0 = tile.X0, y0 = tile.Y0,
                            x1 = tile.X1, y1 = tile.Y1
                        });
                    foreach (var asset in linked.Assets)
                    {
                        report.compiledAssets.Add(new Cnc3CompiledAssetEntry
                        {
                            assetName = asset.Name,
                            sourceFile = asset.Source,
                            typeId = asset.TypeId,
                            instanceId = asset.InstanceId,
                            binaryOffset = asset.BinaryOffset,
                            binaryLength = asset.BinaryLength,
                            importOffset = asset.ImportOffset,
                            importLength = asset.ImportLength,
                            relocationOffset = asset.RelocationOffset,
                            relocationLength = asset.RelocationLength,
                            referenceCount = asset.ReferenceCount
                        });
                    }
                    foreach (var atlas in report.compiledTextures)
                        atlas.ownerAsset = linked.FindOwner(atlas.offset);
                    foreach (string warning in linked.Warnings)
                        Debug.LogWarning("[C&C SAGE BIN] " + warning);
                }

                foreach (string warning in compiled.Warnings)
                    Debug.LogWarning("[C&C SAGE BIN] " + warning);
                report.compiledImportNote = "Compiled resources indexed. " +
                    compiled.Images.Count + " DDS atlas(es) found; " +
                    (importCompiledAtlases ? "textures decoded." : "texture creation disabled.");
                Debug.Log("[C&C SAGE BIN] " + context.assetPath + ": " +
                    compiled.Images.Count + " atlas(es), " +
                    report.compiledImportAddresses + " import addresses and " +
                    report.compiledRelocationAddresses + " relocation addresses.");
            }
            catch (Exception exception)
            {
                // Compiled resources remain optional. Never fail the source
                // terrain/placements when a sibling .bin is unsupported.
                report.compiledImportNote = "Compiled resource parsing failed: " +
                    exception.Message;
                Debug.LogWarning("[C&C SAGE BIN] " + context.assetPath +
                    ": " + report.compiledImportNote);
            }
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

            Cnc3TerrainAtlasUv.Mapping atlasMapping;
            string mappingProblem = "";
            bool useAtlas = applyAtlasToTerrain &&
                Cnc3TerrainAtlasUv.TryMap(map, report, out atlasMapping, out mappingProblem);
            if (!useAtlas)
            {
                atlasMapping = null;
                if (applyAtlasToTerrain && report.hasCompiledBin)
                    Debug.LogWarning("[C&C SAGE TERRAIN] Base atlas was not bound: " +
                        mappingProblem);
            }
            report.terrainAtlasApplied = useAtlas;
            report.terrainAtlasStatus = useAtlas
                ? "Base atlas applied: " + map.TerrainTextures.Count + " matched TextureIDs."
                : (applyAtlasToTerrain
                    ? (string.IsNullOrEmpty(mappingProblem)
                        ? "Missing compiled atlas, source textures or atlas tile table."
                        : mappingProblem)
                    : "Auto-apply disabled in Importer Inspector.");
            report.terrainTextureMatchCount = useAtlas ? map.TerrainTextures.Count : 0;
            report.terrainSourceTextureCount = map.TerrainTextures.Count;

            Material displayMat = terrainMaterial;
            if (useAtlas && displayMat != null)
            {
                // A user-provided .mat must never be changed in place.
                displayMat = new Material(displayMat)
                {
                    name = "CNC3_Atlas_Terrain_Preview"
                };
                context.AddObjectToAsset("CNC3_Terrain_Atlas_Material", displayMat);
            }
            if (displayMat == null)
            {
                // Prefer an unlit shader compatible with the current render
                // pipeline. The Built-In unlit shader can appear pink in URP.
                bool srp = GraphicsSettings.currentRenderPipeline != null;
                Shader shader = useAtlas
                    ? (srp
                        ? (Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("HDRP/Unlit") ??
                            Shader.Find("Unlit/Texture"))
                        : (Shader.Find("Unlit/Texture") ??
                            Shader.Find("Standard")))
                    : (srp
                        ? (Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("HDRP/Lit") ??
                            Shader.Find("Standard"))
                        : Shader.Find("Standard"));
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

            if (useAtlas)
            {
                Texture2D atlas = atlasMapping.BaseAtlas;
                if (displayMat.HasProperty("_MainTex"))
                    displayMat.SetTexture("_MainTex", atlas);
                if (displayMat.HasProperty("_BaseMap"))
                    displayMat.SetTexture("_BaseMap", atlas);
                displayMat.mainTexture = atlas;
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
                if (useAtlas)
                {
                    Mesh texturedMesh = Cnc3TerrainAtlasUv.BuildTile(map, atlasMapping,
                        lowerX, lowerY, tileX, tileY, hScale, vScale,
                        flipMapNorthSouth);
                    context.AddObjectToAsset("CNC3_Terrain_" + counter, texturedMesh);
                    var texturedTile = new GameObject(texturedMesh.name);
                    texturedTile.transform.SetParent(terrain, false);
                    texturedTile.transform.localPosition = new Vector3(
                        lowerX * hScale, 0, lowerY * hScale);
                    texturedTile.AddComponent<MeshFilter>().sharedMesh = texturedMesh;
                    texturedTile.AddComponent<MeshRenderer>().sharedMaterial = displayMat;
                    if (addMeshColliders)
                        texturedTile.AddComponent<MeshCollider>().sharedMesh = texturedMesh;
                    counter++;
                    continue;
                }

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

        private int BuildObjectPlaceholders(GameObject root, Cnc3MapReader.Data map)
        {
            if (map.Objects.Count == 0 || maxObjectPlaceholders <= 0) return 0;
            Transform holder = new GameObject("SAGE_ObjectPlacements").transform;
            holder.SetParent(root.transform, false);
            var groups = new Dictionary<string, Transform>(StringComparer.Ordinal);
            float xyScale = Mathf.Clamp(horizontalScale, 0.001f, 100000f) / 10f;
            float baseElevationScale = map.HeightmapVersion >= 5
                ? 0.0390625f : 0.625f;
            float vScale = Mathf.Clamp(verticalScale, 0.00001f, 10000f);
            if (map.HeightmapVersion < 5 && Mathf.Approximately(vScale, 0.0390625f))
                vScale = 0.625f;
            float heightRatio = vScale / baseElevationScale;

            int count = Math.Min(map.Objects.Count, maxObjectPlaceholders);
            if (count < map.Objects.Count)
                Debug.LogWarning("[C&C SAGE MAP] Object placeholders were limited to " +
                    count + " GameObjects for Editor performance; all decoded placements " +
                    "are preserved in Cnc3MapManifest.");
            for (int i = 0; i < count; i++)
            {
                Cnc3MapReader.ObjectData source = map.Objects[i];
                string typeName = string.IsNullOrWhiteSpace(source.TypeName)
                    ? "Unnamed" : source.TypeName;
                Transform parent = holder;
                if (groupObjectsByType)
                {
                    if (!groups.TryGetValue(typeName, out parent))
                    {
                        parent = new GameObject(SafeHierarchyName(typeName)).transform;
                        parent.SetParent(holder, false);
                        groups.Add(typeName, parent);
                    }
                }

                var marker = new GameObject(
                    i.ToString("D5") + "_" + SafeHierarchyName(typeName));
                marker.transform.SetParent(parent, false);
                float sourceY = flipMapNorthSouth
                    ? (map.Height - 1) * 10f - source.Y : source.Y;
                marker.transform.localPosition = new Vector3(
                    source.X * xyScale,
                    source.Z * heightRatio,
                    sourceY * xyScale);
                // SAGE X/Y ground → Unity X/Z ground changes handedness;
                // negate yaw to preserve the source map's object facing.
                marker.transform.localRotation = Quaternion.Euler(
                    0f, -source.Angle * Mathf.Rad2Deg, 0f);
            }
            return count;
        }

        private static string SafeHierarchyName(string text)
        {
            return text.Replace('/', '_').Replace('\\', '_')
                .Replace(':', '_').Replace('\n', '_');
        }

    }

    [CustomEditor(typeof(Cnc3MapImporter))]
    public sealed class Cnc3MapImporterInspector : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("C&C 3 World Builder Map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Mode 1 imports .map terrain and object markers. " +
                "Mode 2 optionally finds compiled map.bin / map.imp / map.relo / map.manifest " +
                "and maps 43 source terrain texture IDs into atlas UVs when enabled. " +
                "Secondary/three-way blending, roads and compiled object meshes remain unsupported.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importTerrain"),
                new GUIContent("Import Terrain"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("horizontalScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("verticalScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flipMapNorthSouth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeBorder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importObjects"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groupObjectsByType"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxObjectPlaceholders"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("addMeshColliders"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainMaterial"));
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Mode 2 — Compiled Map BIN", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importCompiledAtlases"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectCompiledIndexes"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyAtlasToTerrain"));

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
                    EditorGUILayout.LabelField("Atlas Applied To Terrain",
                        report.terrainAtlasApplied ? "Yes — base texture UVs" : "No");
                    EditorGUILayout.LabelField("Source Terrain Textures",
                        report.terrainSourceTextureCount.ToString());
                    EditorGUILayout.LabelField("Texture IDs Matched",
                        report.terrainTextureMatchCount.ToString());
                    if (!string.IsNullOrEmpty(report.terrainAtlasStatus))
                        EditorGUILayout.HelpBox(report.terrainAtlasStatus,
                            report.terrainAtlasApplied ? MessageType.Info : MessageType.Warning);
                    EditorGUILayout.LabelField("Object Placements", report.objectCount.ToString());
                    EditorGUILayout.LabelField("Generated Placeholders", report.objectMarkerCount.ToString());
                    EditorGUILayout.LabelField("Distinct Object Types", report.distinctObjectTypes.ToString());
                    EditorGUILayout.LabelField("Map Sections", report.sections == null
                        ? "Unknown" : report.sections.Length.ToString());
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.LabelField("Compiled BIN Data", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("map.bin", report.hasCompiledBin
                        ? report.compiledBinSize.ToString("N0") + " bytes" : "Not loaded");
                    EditorGUILayout.LabelField("Compiled Header Region",
                        report.compiledMetadataBytes.ToString("N0") + " bytes");
                    EditorGUILayout.LabelField("DDS Atlases",
                        report.compiledTextureCount.ToString());
                    EditorGUILayout.LabelField("Atlas Source Asset",
                        string.IsNullOrEmpty(report.compiledAtlasAssetName)
                            ? "Not identified" : report.compiledAtlasAssetName);
                    EditorGUILayout.LabelField("Atlas Tile Table",
                        report.compiledAtlasLayoutVerified
                            ? report.compiledAtlasTileCount + " tile rectangles validated"
                            : "Not validated");
                    EditorGUILayout.LabelField("Import Addresses",
                        report.compiledImportAddresses.ToString("N0"));
                    EditorGUILayout.LabelField("Relocation Addresses",
                        report.compiledRelocationAddresses.ToString("N0"));
                    EditorGUILayout.LabelField("Manifest Records",
                        report.manifestAssetCount.ToString("N0"));
                    EditorGUILayout.LabelField("Records With Binary Data",
                        report.manifestAssetsWithData.ToString("N0"));
                    EditorGUILayout.LabelField("Manifest / B-I-R Match",
                        report.compiledManifestMatchesBin ? "Validated" : "Not validated");
                    EditorGUILayout.LabelField("Manifest GameObject Names",
                        report.manifestGameObjectNames.ToString("N0"));
                    EditorGUILayout.LabelField("Manifest Texture Names",
                        report.manifestTextureNames.ToString("N0"));
                    foreach (var atlas in report.compiledTextures)
                    {
                        if (atlas == null) continue;
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField(atlas.name, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField("Source", "map.bin @ 0x" +
                                atlas.offset.ToString("X8") + " (" + atlas.length +
                                " bytes)");
                            EditorGUILayout.LabelField("Format", atlas.format);
                            if (!string.IsNullOrEmpty(atlas.ownerAsset))
                                EditorGUILayout.LabelField("Owner", atlas.ownerAsset);
                            EditorGUILayout.LabelField("Size", atlas.width + " × " +
                                atlas.height + ", " + atlas.mipCount + " source mips");
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField("Unity Texture", atlas.texture,
                                    typeof(Texture2D), false);
                        }
                    }
                    if (report.compiledAssets.Count > 0)
                    {
                        EditorGUILayout.LabelField("Compiled Assets Indexed",
                            report.compiledAssets.Count.ToString("N0"));
                        EditorGUILayout.HelpBox("Full per-asset names, IDs, source paths and " +
                            "B/I/R ranges are stored in the imported Cnc3MapManifest " +
                            "sub-asset. Compiled W3DMesh bodies may be external.", MessageType.None);
                    }
                    if (!string.IsNullOrEmpty(report.compiledImportNote))
                        EditorGUILayout.HelpBox(report.compiledImportNote, MessageType.None);
                    break;
                }
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
