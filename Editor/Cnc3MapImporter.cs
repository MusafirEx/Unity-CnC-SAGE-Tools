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
    [ScriptedImporter(8, "map")]
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

        [Tooltip("Blend secondary and three-way terrain texture layers using SAGE blend directions. Requires the compiled diffuse atlas and bundled SAGE blend shader.")]
        public bool renderTerrainBlending = true;

        [Header("Mode 4 - Cliffs / Roads / SAGE Lighting")]
        [Tooltip("Experimental source cliff overlay mapping. Enables only when a source map contains active cliff entries.")]
        public bool previewCliffTextures;

        [Tooltip("Use the map's terrain sun, ambient, and accent lighting in the atlas shader.")]
        public bool useSageTerrainLighting;

        [Tooltip("0 uses the map's original time of day; 1/2/3/4 override Morning/Afternoon/Evening/Night.")]
        [Range(0, 4)]
        public int lightingTimeOverride;

        [Tooltip("Blend from the original unlit terrain (0) to the source SAGE lighting (1).")]
        [Range(0f, 1f)]
        public float terrainLightingStrength = 0.8f;

        [Tooltip("Show heightfield-draped guide strips from consecutive RoadType Start/End map objects. These are NOT original textured SAGE roads.")]
        public bool importRoadGuides;

        [Header("Phase 5A - Connected Road Surfaces")]
        [Tooltip("Create real connected road topology and textured surfaces when user-supplied road definitions and textures are available. Off by default.")]
        public bool importRoadSurfaces;

        [Tooltip("Optional original road definitions (.ini/.xml as TextAsset) and road material/width overrides.")]
        public Cnc3RoadLibrary roadLibrary;

        [Tooltip("Optional Assets folder containing original road TGA/DDS textures, matching RoadTemplate.Texture filenames.")]
        public DefaultAsset roadTextureFolder;


        [Tooltip("Global width multiplier for approximate road guide previews; exact widths require external SAGE Road assets.")]
        [Range(0.1f, 4f)]
        public float roadGuideWidthMultiplier = 1f;

        [Tooltip("Lift road guides a small distance above terrain to avoid z-fighting.")]
        [Range(0f, 2f)]
        public float roadGuideSurfaceOffset = 0.08f;

        [Tooltip("Limit generated preview road segments in Editor.")]
        [Range(0, 20000)]
        public int maximumRoadGuideSegments = 5000;

        [Header("Mode 3 - Standing Water Preview")]
        [Tooltip("Create Unity water surfaces from StandingWaterAreas in the source .map.")]
        public bool importStandingWater = true;

        [Tooltip("Base color and transparency for preview water; original SAGE shaders are not reproduced.")]
        public Color waterTint = new Color(0.12f, 0.35f, 0.46f, 0.62f);

        [Tooltip("Multiplier applied to the original water UV scroll speed.")]
        [Min(0f)]
        public float waterScrollMultiplier = 1f;

        [Tooltip("Optional custom water material; cloned per imported source water area.")]
        public Material waterPreviewMaterial;

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
            report.standingWaterCount = map.StandingWaters.Count;
            report.globalWaterReflectionEnabled = map.ReflectionEnabled;
            report.globalWaterReflectionHeight = map.ReflectionPlaneZ;
            foreach (Cnc3MapReader.WaterArea area in map.StandingWaters)
            {
                report.standingWaters.Add(new Cnc3StandingWaterEntry
                {
                    uniqueId = area.UniqueId,
                    name = area.Name,
                    layerName = area.LayerName,
                    sageHeight = area.WaterHeight,
                    pointCount = area.Points == null ? 0 : area.Points.Length,
                    uvScrollSpeed = area.UvScrollSpeed,
                    additive = area.Additive,
                    bumpMap = area.BumpMap,
                    skyTexture = area.SkyTexture,
                    fxShader = area.FxShader,
                    depthColors = area.DepthColors
                });
            }
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
            if (importStandingWater)
                BuildStandingWater(context, root, map, report);
            if (importRoadGuides)
                BuildRoadGuides(context, root, map, report);
            if (importRoadSurfaces)
                BuildRoadSurfaces(context, root, map, report);

            foreach (string warning in map.Warnings)
                Debug.LogWarning("[C&C SAGE MAP] " + context.assetPath + ": " + warning);
            Debug.Log("[C&C SAGE MAP] Imported " + context.assetPath + " " +
                map.Width + "x" + map.Height + " points, " + report.tileCount +
                " terrain meshes, " + map.Objects.Count +
                " source object placements (" + (importObjects ? "created" : "metadata only") + ").");
        }





        private void BuildRoadSurfaces(AssetImportContext context, GameObject root,
            Cnc3MapReader.Data map, Cnc3MapManifest report)
        {
            var warnings = new List<string>();
            var definitions = roadLibrary != null
                ? roadLibrary.ReadTemplates(warnings)
                : new Dictionary<string, Cnc3RoadLibrary.Template>(
                    StringComparer.OrdinalIgnoreCase);
            if (roadLibrary != null)
            {
                string libraryPath = AssetDatabase.GetAssetPath(roadLibrary);
                if (!string.IsNullOrEmpty(libraryPath) &&
                    libraryPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    context.DependsOnSourceAsset(libraryPath);
                if (roadLibrary.sourceDefinitions != null)
                {
                    string definitionsPath = AssetDatabase.GetAssetPath(
                        roadLibrary.sourceDefinitions);
                    if (!string.IsNullOrEmpty(definitionsPath) &&
                        definitionsPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        context.DependsOnSourceAsset(definitionsPath);
                }
            }

            var roads = Cnc3RoadSurfaceBuilder.Build(map, definitions,
                Mathf.Clamp(horizontalScale, 0.001f, 100000f),
                Mathf.Clamp(verticalScale, 0.00001f, 10000f),
                flipMapNorthSouth,
                roadGuideWidthMultiplier, roadGuideSurfaceOffset,
                maximumRoadGuideSegments);
            report.roadSurfaceSegments = roads.Segments;
            report.roadSurfaceConnectedJunctions = roads.TwoWayJunctions;
            report.roadSurfaceCrossings = roads.Crossings;
            report.roadSurfaceFourWayCrossings = roads.FourWayCrossings;
            report.roadSurfaceUnresolvedTypes = roads.UnresolvedTypes;
            report.roadSurfaceDuplicateSegments = roads.DuplicateSegments;
            if (roads.Surfaces.Count == 0)
            {
                report.roadSurfaceStatus = "No valid road segments found.";
                return;
            }

            const string roadShaderPath =
                "Packages/com.musafirex.cnc-sage-tools/Shaders/Cnc3RoadAtlas.shader";
            Shader roadShader = AssetDatabase.LoadAssetAtPath<Shader>(roadShaderPath);
            if (roadShader == null)
                roadShader = Shader.Find("MusafirEx/SAGE Road Atlas");
            bool hasShader = roadShader != null && roadShader.isSupported;
            if (hasShader) context.DependsOnSourceAsset(roadShaderPath);
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            Shader previewShader = srp
                ? (Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("HDRP/Unlit") ??
                    Shader.Find("Unlit/Color"))
                : (Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            if (!hasShader && (previewShader == null || !previewShader.isSupported))
            {
                report.roadSurfaceStatus = "No compatible road surface shader found.";
                return;
            }

            Dictionary<string, Texture2D> imageIndex =
                IndexRoadTextures(context, context.assetPath);
            var holder = new GameObject("SAGE_RoadSurfaces_Phase5A").transform;
            holder.SetParent(root.transform, false);
            int counter = 0;
            foreach (var road in roads.Surfaces)
            {
                Texture2D texture = road.RoadTexture;
                string rawName = road.SourceTextureName;
                if (texture == null && !string.IsNullOrWhiteSpace(rawName))
                    imageIndex.TryGetValue(
                        Path.GetFileNameWithoutExtension(rawName).ToLowerInvariant(),
                        out texture);
                if (texture != null)
                {
                    string texPath = AssetDatabase.GetAssetPath(texture);
                    if (!string.IsNullOrEmpty(texPath) &&
                        texPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        context.DependsOnSourceAsset(texPath);
                }

                var material = new Material(hasShader ? roadShader : previewShader)
                {
                    name = "SAGE_RoadSurface_" + Cnc3MapRoadBuilder.Sanitize(road.TypeName)
                };
                Color tint = RoadPreviewTint(road.TypeName);
                if (hasShader)
                {
                    material.SetFloat("_UseRoadTexture", texture != null ? 1f : 0f);
                    material.SetFloat("_WidthInTexture", road.WidthInTexture);
                    material.SetColor("_Color", texture != null ? Color.white : tint);
                    if (texture != null)
                        material.SetTexture("_MainTex", texture);
                }
                else
                {
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", tint);
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", tint);
                }

                context.AddObjectToAsset("SAGE_RoadSurfaceMesh_" + counter, road.Mesh);
                context.AddObjectToAsset("SAGE_RoadSurfaceMaterial_" + counter, material);
                var go = new GameObject("RoadSurface_" +
                    Cnc3MapRoadBuilder.Sanitize(road.TypeName));
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = road.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;

                report.roadSurfaceTypes.Add(new Cnc3RoadTypeEntry
                {
                    roadType = road.TypeName,
                    sourceTextureName = rawName,
                    roadWidth = road.RoadWidth,
                    roadWidthInTexture = road.WidthInTexture,
                    segmentCount = road.Segments,
                    crossingCount = road.Crossings,
                    foundRoadDefinition = road.HasDefinition,
                    hasTexture = hasShader && texture != null,
                    mesh = road.Mesh,
                    material = material
                });
                if (road.HasDefinition) report.roadSurfaceResolvedTypes++;
                if (hasShader && texture != null) report.roadSurfaceTexturedTypes++;
                counter++;
            }
            report.roadSurfaceMeshes = counter;
            report.roadSurfaceStatus = counter + " network road surfaces, " +
                roads.Segments + " segments, " + roads.TwoWayJunctions +
                " degree-2 junctions and " + roads.Crossings +
                " crossings; " + report.roadSurfaceResolvedTypes +
                " styles defined, " + report.roadSurfaceTexturedTypes +
                " styles textured. Three-/four-way pads and curve geometry " +
                "are preliminary; final SAGE road intersections are Phase 5B.";
            foreach (string warning in warnings)
                Debug.LogWarning("[C&C SAGE ROADS] " + warning);
            Debug.Log("[C&C SAGE ROADS] " + context.assetPath + ": " +
                report.roadSurfaceStatus);
        }

        private Dictionary<string, Texture2D> IndexRoadTextures(
            AssetImportContext context, string mapPath)
        {
            var output = new Dictionary<string, Texture2D>(
                StringComparer.OrdinalIgnoreCase);
            var searchFolders = new List<string>();
            string mapFolder = Path.GetDirectoryName(mapPath);
            if (!string.IsNullOrEmpty(mapFolder) &&
                AssetDatabase.IsValidFolder(mapFolder))
                searchFolders.Add(mapFolder);
            if (roadLibrary != null && roadLibrary.sourceDefinitions != null)
            {
                string defFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(
                    roadLibrary.sourceDefinitions));
                if (!string.IsNullOrEmpty(defFolder) &&
                    AssetDatabase.IsValidFolder(defFolder) &&
                    !searchFolders.Contains(defFolder))
                    searchFolders.Add(defFolder);
            }
            if (roadTextureFolder != null)
            {
                string candidate = AssetDatabase.GetAssetPath(roadTextureFolder);
                if (!string.IsNullOrEmpty(candidate) &&
                    AssetDatabase.IsValidFolder(candidate) &&
                    !searchFolders.Contains(candidate))
                    searchFolders.Add(candidate);
            }
            foreach (string folder in searchFolders)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                    new[] { folder });
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    string key = Path.GetFileNameWithoutExtension(assetPath);
                    if (string.IsNullOrEmpty(key) || output.ContainsKey(key)) continue;
                    Texture2D image = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (image != null) output.Add(key, image);
                }
            }
            return output;
        }

        private static Color RoadPreviewTint(string name)
        {
            if (name.IndexOf("Marking", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.91f, 0.83f, 0.42f, 1f);
            if (name.IndexOf("Sidewalk", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.53f, 0.53f, 0.51f, 1f);
            if (name.IndexOf("Dirt", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.42f, 0.32f, 0.24f, 1f);
            return new Color(0.29f, 0.31f, 0.34f, 1f);
        }

        private void BuildRoadGuides(AssetImportContext context, GameObject root,
            Cnc3MapReader.Data map, Cnc3MapManifest report)
        {
            report.roadSourceControlCount = 0;
            foreach (var obj in map.Objects)
                if ((obj.RoadFlags & (2u | 4u)) != 0) report.roadSourceControlCount++;
            if (maximumRoadGuideSegments <= 0)
            {
                report.roadGuideStatus = "Disabled by maximumRoadGuideSegments = 0.";
                return;
            }
            float hScale = Mathf.Clamp(horizontalScale, 0.001f, 100000f);
            float vScale = Mathf.Clamp(verticalScale, 0.00001f, 10000f);
            if (map.HeightmapVersion < 5 && Mathf.Approximately(vScale, 0.0390625f))
                vScale = 0.625f;
            var groups = Cnc3MapRoadBuilder.Build(map, hScale, vScale,
                flipMapNorthSouth, roadGuideWidthMultiplier, roadGuideSurfaceOffset,
                maximumRoadGuideSegments);
            if (groups.Count == 0)
            {
                report.roadGuideStatus = "No consecutive source RoadType Start/End pairs.";
                return;
            }
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = srp
                ? (Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("HDRP/Unlit") ??
                    Shader.Find("Unlit/Color"))
                : (Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            if (shader != null && !shader.isSupported)
                shader = null;
            if (shader == null)
            {
                report.roadGuideStatus = "No road preview material shader found.";
                return;
            }
            Transform holder = new GameObject("SAGE_RoadGuides_PreviewOnly").transform;
            holder.SetParent(root.transform, false);
            int counter = 0;
            foreach (var group in groups)
            {
                var material = new Material(shader) { name = "SAGE_RoadGuide_" + group.TypeName };
                bool marking = group.TypeName.IndexOf("Marking",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool sidewalk = group.TypeName.IndexOf("Sidewalk",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool dirt = group.TypeName.IndexOf("Dirt",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                Color tint = marking ? new Color(0.91f, 0.83f, 0.42f, 1f) :
                    sidewalk ? new Color(0.53f, 0.53f, 0.51f, 1f) :
                    dirt ? new Color(0.42f, 0.32f, 0.24f, 1f) :
                    new Color(0.29f, 0.31f, 0.34f, 1f);
                if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                context.AddObjectToAsset("SAGE_RoadGuideMesh_" + counter, group.Mesh);
                context.AddObjectToAsset("SAGE_RoadGuideMat_" + counter, material);
                var child = new GameObject("RoadGuide_" +
                    Cnc3MapRoadBuilder.Sanitize(group.TypeName));
                child.transform.SetParent(holder, false);
                child.AddComponent<MeshFilter>().sharedMesh = group.Mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
                report.roadGuideSegments += group.Segments;
                report.curvedRoadGuideSegments += group.SourceCurves;
                counter++;
            }
            report.roadGuideMeshCount = counter;
            report.roadGuideStatus = counter + " guide meshes; " +
                report.roadGuideSegments + " control-point segments, including " +
                report.curvedRoadGuideSegments + " flagged curved/angled segments. " +
                "Geometry and material are approximate; external Road assets required.";
        }

        private void BuildStandingWater(AssetImportContext context,
            GameObject root, Cnc3MapReader.Data map, Cnc3MapManifest report)
        {
            if (map.StandingWaters.Count == 0) return;
            const string waterShaderPath =
                "Packages/com.musafirex.cnc-sage-tools/Shaders/Cnc3WaterPreview.shader";
            Shader waterShader = AssetDatabase.LoadAssetAtPath<Shader>(waterShaderPath);
            if (waterShader != null)
                context.DependsOnSourceAsset(waterShaderPath);
            if (waterShader == null || !waterShader.isSupported)
                waterShader = Shader.Find("MusafirEx/SAGE Water Preview");
            if (waterShader == null || !waterShader.isSupported)
                waterShader = Shader.Find("Unlit/Transparent") ??
                    Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Standard");
            if (waterShader == null)
            {
                Debug.LogWarning("[C&C SAGE WATER] No supported water preview shader.");
                return;
            }
            if (waterPreviewMaterial != null)
            {
                string materialPath = AssetDatabase.GetAssetPath(waterPreviewMaterial);
                if (!string.IsNullOrEmpty(materialPath) &&
                    materialPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    context.DependsOnSourceAsset(materialPath);
            }

            Transform group = new GameObject("SAGE_WaterAreas").transform;
            group.SetParent(root.transform, false);
            float hScale = Mathf.Clamp(horizontalScale, 0.001f, 100000f);
            float vScale = Mathf.Clamp(verticalScale, 0.00001f, 10000f);
            float speedMultiplier = Mathf.Clamp(waterScrollMultiplier, 0f, 100f);
            for (int i = 0; i < map.StandingWaters.Count; i++)
            {
                var water = map.StandingWaters[i];
                Mesh mesh;
                string error;
                if (!Cnc3MapWaterBuilder.TryBuild(water, map.Height,
                    hScale, vScale, flipMapNorthSouth, out mesh, out error))
                {
                    Debug.LogWarning("[C&C SAGE WATER] Skipping " + water.Name +
                        " #" + water.UniqueId + ": " + error);
                    continue;
                }

                var material = waterPreviewMaterial != null
                    ? new Material(waterPreviewMaterial)
                    : new Material(waterShader);
                material.name = "CNC3_Water_Material_" + i;
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", waterTint);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", waterTint);
                if (material.HasProperty("_Scroll"))
                    material.SetFloat("_Scroll", water.UvScrollSpeed * speedMultiplier);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", water.Additive ?
                        (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
                material.renderQueue = (int)RenderQueue.Transparent;
                context.AddObjectToAsset("CNC3_WaterMesh_" + i, mesh);
                context.AddObjectToAsset("CNC3_WaterMaterial_" + i, material);

                string areaName = string.IsNullOrEmpty(water.Name)
                    ? "Unnamed" : SafeHierarchyName(water.Name);
                var child = new GameObject("Water_" + water.UniqueId + "_" + areaName);
                child.transform.SetParent(group, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;

                var entry = report.standingWaters[i];
                entry.generated = true;
                entry.waterMesh = mesh;
                entry.previewMaterial = material;
                report.standingWaterGenerated++;
            }
            Debug.Log("[C&C SAGE WATER] Imported " + report.standingWaterGenerated +
                "/" + report.standingWaterCount + " standing-water polygon(s).");
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
                report.compiledStreamId = compiled.SignatureRead;
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

        private static Color SageRgb(Vector3 value)
        {
            return new Color(value.x, value.y, value.z, 1f);
        }

        private static Vector4 SageDirection(Vector3 source)
        {
            Vector3 direction = new Vector3(source.x, source.z, source.y);
            if (direction.sqrMagnitude < 0.00001f) direction = Vector3.down;
            direction.Normalize();
            return new Vector4(direction.x, direction.y, direction.z, 0f);
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

            // Definite assignment: TryMap() is short-circuited when atlas mode
            // is disabled, so the out argument may never execute.
            Cnc3TerrainAtlasUv.Mapping atlasMapping = null;
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

            report.terrainBlendDescriptionCount = map.BlendDescriptions.Count;
            if (map.Blends != null && map.ThreeWayBlends != null)
            {
                for (int i = 0; i < map.Blends.Length; i++)
                {
                    if (map.Blends[i] != 0) report.terrainFirstBlendCells++;
                    if (map.ThreeWayBlends[i] != 0) report.terrainThreeWayBlendCells++;
                }
            }
            report.sourceTimeOfDay = map.SourceTimeOfDay;
            int chosenTime = lightingTimeOverride >= 1 && lightingTimeOverride <= 4
                ? lightingTimeOverride : map.SourceTimeOfDay;
            report.displayedTimeOfDay = chosenTime;
            bool lightingAvailable = chosenTime >= 1 && chosenTime <= 4 &&
                map.TerrainLighting[chosenTime - 1] != null;
            report.cliffMappingCount = map.CliffMappings.Count;
            if (map.CliffTextureIndices != null)
                foreach (uint value in map.CliffTextureIndices)
                    if (value != 0) report.cliffOverlayCellCount++;
            bool requestLighting = useAtlas && useSageTerrainLighting && lightingAvailable;
            bool requestCliffs = useAtlas && previewCliffTextures &&
                report.cliffOverlayCellCount > 0 && report.cliffMappingCount > 0;

            Shader blendShader = null;
            if (useAtlas && ((renderTerrainBlending &&
                report.terrainBlendDescriptionCount > 0) ||
                requestLighting || requestCliffs))
            {
                const string packageShader = "Packages/com.musafirex.cnc-sage-tools/Shaders/Cnc3TerrainAtlasBlend.shader";
                blendShader = AssetDatabase.LoadAssetAtPath<Shader>(packageShader);
                if (blendShader == null)
                    blendShader = Shader.Find("MusafirEx/SAGE Terrain Atlas Blend");
                if (blendShader != null)
                    context.DependsOnSourceAsset(packageShader);
            }
            bool shaderReady = blendShader != null && blendShader.isSupported;
            bool useBlending = shaderReady &&
                useAtlas && renderTerrainBlending && map.Blends != null &&
                map.ThreeWayBlends != null && map.BlendDescriptions.Count > 0;
            bool useLighting = shaderReady && requestLighting;
            bool useCliffs = shaderReady && requestCliffs;
            bool useCustomShader = useBlending || useLighting || useCliffs;
            report.cliffPreviewEnabled = useCliffs;
            report.terrainSageLightingApplied = useLighting;
            report.terrainBlendingApplied = useBlending;
            report.terrainBlendStatus = useBlending
                ? "Two-way and three-way blending active in SAGE shader."
                : (!renderTerrainBlending ? "Blending disabled in Inspector."
                : (!useAtlas ? "Compiled base atlas not available."
                : (map.BlendDescriptions.Count == 0
                    ? "Map has no blend descriptions."
                    : "SAGE blend shader missing or unsupported by this render pipeline.")));
            if (!useBlending && useAtlas && renderTerrainBlending &&
                map.BlendDescriptions.Count > 0)
                Debug.LogWarning("[C&C SAGE TERRAIN] " + report.terrainBlendStatus);

            Material displayMat = terrainMaterial;
            if (useAtlas && displayMat != null)
            {
                // A user-provided .mat must never be changed in place.
                displayMat = new Material(displayMat)
                {
                    name = useCustomShader ? "CNC3_Terrain_AtlasBlend" :
                        "CNC3_Atlas_Terrain_Preview"
                };
                if (useCustomShader)
                    displayMat.shader = blendShader;
                context.AddObjectToAsset("CNC3_Terrain_Atlas_Material", displayMat);
            }
            if (displayMat == null)
            {
                // Prefer an unlit shader compatible with the current render
                // pipeline. The Built-In unlit shader can appear pink in URP.
                bool srp = GraphicsSettings.currentRenderPipeline != null;
                Shader shader = useCustomShader ? blendShader : useAtlas
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

            if (useCustomShader)
            {
                displayMat.SetFloat("_SageUseCliffs", useCliffs ? 1f : 0f);
                displayMat.SetFloat("_SageLightingStrength",
                    useLighting ? Mathf.Clamp01(terrainLightingStrength) : 0f);
                if (useLighting)
                {
                    var source = map.TerrainLighting[chosenTime - 1];
                    displayMat.SetColor("_SageAmbient", SageRgb(source.Sun.Ambient));
                    displayMat.SetColor("_SageSunColor", SageRgb(source.Sun.Color));
                    displayMat.SetColor("_SageAccent1Color", SageRgb(source.Accent1.Color));
                    displayMat.SetColor("_SageAccent2Color", SageRgb(source.Accent2.Color));
                    displayMat.SetVector("_SageSunDirection", SageDirection(source.Sun.Direction));
                    displayMat.SetVector("_SageAccent1Direction", SageDirection(source.Accent1.Direction));
                    displayMat.SetVector("_SageAccent2Direction", SageDirection(source.Accent2.Direction));
                }
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
                        flipMapNorthSouth, useBlending, useCliffs);
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
                "Two-way/three-way terrain blending and standing-water polygon previews " +
                "are supported. Cliff overlays, roads, reflections and compiled object " +
                "meshes are not yet reconstructed.", MessageType.Info);
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("renderTerrainBlending"));
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Mode 4 — Cliffs, Roads & Lighting", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("previewCliffTextures"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useSageTerrainLighting"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lightingTimeOverride"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainLightingStrength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importRoadGuides"));
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Phase 5A — Connected Road Surfaces", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importRoadSurfaces"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadLibrary"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadTextureFolder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadGuideWidthMultiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadGuideSurfaceOffset"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumRoadGuideSegments"));
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Mode 3 — Standing Water Preview", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("importStandingWater"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("waterTint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("waterScrollMultiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("waterPreviewMaterial"));

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
                    EditorGUILayout.LabelField("Cliff Mapping Records", report.cliffMappingCount.ToString("N0"));
                    EditorGUILayout.LabelField("Active Cliff Cells", report.cliffOverlayCellCount.ToString("N0"));
                    EditorGUILayout.LabelField("Cliff Texture Preview", report.cliffPreviewEnabled ? "Active (experimental)" : "Off");
                    EditorGUILayout.LabelField("Source Time of Day", report.sourceTimeOfDay.ToString());
                    EditorGUILayout.LabelField("Lighting Preset Used", report.displayedTimeOfDay.ToString());
                    EditorGUILayout.LabelField("SAGE Terrain Lighting", report.terrainSageLightingApplied ? "Active" : "Off");
                    EditorGUILayout.LabelField("Road Control Points", report.roadSourceControlCount.ToString("N0"));
                    EditorGUILayout.LabelField("Road Guide Segments", report.roadGuideSegments.ToString("N0"));
                    EditorGUILayout.LabelField("Road Preview Meshes", report.roadGuideMeshCount.ToString("N0"));
                    EditorGUILayout.LabelField("Road Surface Segments",
                        report.roadSurfaceSegments.ToString("N0"));
                    EditorGUILayout.LabelField("Road Surface Meshes",
                        report.roadSurfaceMeshes.ToString("N0"));
                    EditorGUILayout.LabelField("Two-Way Road Junctions",
                        report.roadSurfaceConnectedJunctions.ToString("N0"));
                    EditorGUILayout.LabelField("Road Crossings",
                        report.roadSurfaceCrossings.ToString("N0"));
                    EditorGUILayout.LabelField("Road Templates Matched",
                        report.roadSurfaceResolvedTypes.ToString("N0"));
                    EditorGUILayout.LabelField("Road Types Textured",
                        report.roadSurfaceTexturedTypes.ToString("N0"));
                    if (!string.IsNullOrEmpty(report.roadSurfaceStatus))
                        EditorGUILayout.HelpBox(report.roadSurfaceStatus, MessageType.Info);
                    if (!string.IsNullOrEmpty(report.roadGuideStatus))
                        EditorGUILayout.HelpBox(report.roadGuideStatus, MessageType.Info);
                    EditorGUILayout.LabelField("Standing Water Areas", report.standingWaterCount.ToString());
                    EditorGUILayout.LabelField("Water Meshes Created", report.standingWaterGenerated.ToString());
                    EditorGUILayout.LabelField("Global Water Reflection",
                        report.globalWaterReflectionEnabled ? "Enabled" : "Disabled");
                    for (int w = 0; w < report.standingWaters.Count; w++)
                    {
                        var area = report.standingWaters[w];
                        if (area == null) continue;
                        EditorGUILayout.LabelField("Water #" + area.uniqueId,
                            area.pointCount + " points, Z=" + area.sageHeight +
                            (area.generated ? " (mesh)" : " (source only)"));
                    }

                    EditorGUILayout.LabelField("Atlas Applied To Terrain",
                        report.terrainAtlasApplied ? "Yes — diffuse atlas" : "No");
                    EditorGUILayout.LabelField("Texture Blending",
                        report.terrainBlendingApplied ? "Active (two-way + three-way)" : "Off");
                    EditorGUILayout.LabelField("First Blend Cells", report.terrainFirstBlendCells.ToString("N0"));
                    EditorGUILayout.LabelField("Three-Way Blend Cells", report.terrainThreeWayBlendCells.ToString("N0"));
                    EditorGUILayout.LabelField("Blend Descriptions", report.terrainBlendDescriptionCount.ToString("N0"));
                    if (!string.IsNullOrEmpty(report.terrainBlendStatus))
                        EditorGUILayout.HelpBox(report.terrainBlendStatus, MessageType.Info);
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
                    EditorGUILayout.LabelField("Compiled Stream ID", report.hasCompiledBin
                        ? "0x" + report.compiledStreamId.ToString("X8") : "Not available");
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
