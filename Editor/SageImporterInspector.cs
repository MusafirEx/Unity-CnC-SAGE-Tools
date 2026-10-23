using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Shared read-only import inspection UI, intentionally separate from W3X/W3D
    /// parsing and export code. Uses only imported Unity assets and sub-assets.
    /// </summary>
    public abstract class SageImporterInspectorBase : ScriptedImporterEditor
    {
        private static readonly string[] TabNames =
            { "Model", "Rig", "Animation", "Material" };

        private const int MaxDisplayedPivots = 150;
        private const int MaxDisplayedMeshes = 80;

        private int selectedTab;
        private bool showPivotNames;
        private string cachedPath;
        private GameObject root;
        private Mesh[] meshes = Array.Empty<Mesh>();
        private Material[] materials = Array.Empty<Material>();
        private AnimationClip[] clips = Array.Empty<AnimationClip>();
        private Renderer[] renderers = Array.Empty<Renderer>();
        private Transform pivotRoot;
        private string[] declarations = Array.Empty<string>();


        public override void OnEnable()
        {
            base.OnEnable();
            Refresh();
        }

        private void Refresh()
        {
            var importer = target as AssetImporter;
            cachedPath = importer != null ? importer.assetPath : null;
            root = null;
            meshes = Array.Empty<Mesh>();
            materials = Array.Empty<Material>();
            clips = Array.Empty<AnimationClip>();
            renderers = Array.Empty<Renderer>();
            pivotRoot = null;
            declarations = Array.Empty<string>();

            if (string.IsNullOrEmpty(cachedPath))
                return;

            var objects = AssetDatabase.LoadAllAssetsAtPath(cachedPath);
            if (objects == null)
                return;

            root = objects.OfType<GameObject>().FirstOrDefault();
            meshes = objects.OfType<Mesh>().ToArray();
            materials = objects.OfType<Material>().ToArray();
            clips = objects.OfType<AnimationClip>().ToArray();

            if (root != null)
            {
                renderers = root.GetComponentsInChildren<Renderer>(true);
                pivotRoot = root.transform.Find("SAGE_Pivots");
                var info = root.GetComponent<CncSageSourceInfo>();
                if (info != null && info.Declarations != null)
                    declarations = info.Declarations;

                // In case an importer didn't register certain references as sub-assets,
                // still inspect actual attached mesh/material/animation objects.
                var allMeshes = new List<Mesh>(meshes);
                var allMaterials = new List<Material>(materials);

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    var skinned = renderer as SkinnedMeshRenderer;
                    if (skinned != null && skinned.sharedMesh != null &&
                        !allMeshes.Contains(skinned.sharedMesh))
                        allMeshes.Add(skinned.sharedMesh);

                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null &&
                        !allMeshes.Contains(filter.sharedMesh))
                        allMeshes.Add(filter.sharedMesh);

                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && !allMaterials.Contains(material))
                            allMaterials.Add(material);
                }

                meshes = allMeshes.ToArray();
                materials = allMaterials.ToArray();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var importer = target as AssetImporter;
            string currentPath = importer != null ? importer.assetPath : null;
            if (!string.Equals(cachedPath, currentPath, StringComparison.Ordinal))
                Refresh();

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("C&C SAGE Import Inspector", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(64)))
                    Refresh();
            }

            EditorGUILayout.LabelField("Format", target is W3dImporter
                ? "Binary W3D  |  Generals / Zero Hour"
                : "Source W3X  |  Tiberium Wars / Kane's Wrath");
            if (!string.IsNullOrEmpty(cachedPath))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Source", cachedPath);
            }

            GUILayout.Space(6f);
            selectedTab = GUILayout.Toolbar(selectedTab, TabNames, EditorStyles.toolbarButton,
                GUILayout.MinHeight(25f));
            GUILayout.Space(6f);

            switch (selectedTab)
            {
                case 0: DrawModel(); break;
                case 1: DrawRig(); break;
                case 2: DrawAnimation(); break;
                case 3: DrawMaterial(); break;
            }

            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Import settings take effect after Apply. Original W3D/W3X sources " +
                "and W3X export commands are not modified.", MessageType.None);
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }

        private void DrawModel()
        {
            Section("Model Import Settings");
            Prop("importMeshes", "Import Meshes");
            Prop("unitsToMeters", "Scale Factor");
            EditorGUILayout.Space();
            Section("Model Summary");
            if (root == null)
            {
                MissingAsset();
                return;
            }

            int vertexCount = 0;
            int triangleCount = 0;
            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;
                vertexCount += mesh.vertexCount;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                        triangleCount += (int)(mesh.GetIndexCount(sub) / 3);
                }
            }

            Row("Main Object", root.name);
            Row("Imported Meshes", meshes.Length.ToString());
            Row("Renderers", renderers.Length.ToString());
            Row("Vertices", vertexCount.ToString("N0"));
            Row("Triangles", triangleCount.ToString("N0"));
            Row("Skinned Renderers", renderers.OfType<SkinnedMeshRenderer>().Count().ToString());

            if (meshes.Length == 0)
                EditorGUILayout.HelpBox(
                    "No renderable meshes were imported. This may be a skeleton-only, " +
                    "animation-only or unsupported asset.", MessageType.Info);

            if (meshes.Length > 0)
            {
                Section("Imported Meshes");
                int displayed = Math.Min(meshes.Length, MaxDisplayedMeshes);
                for (int i = 0; i < displayed; i++)
                {
                    var mesh = meshes[i];
                    if (mesh == null) continue;
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(mesh.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("Select", GUILayout.Width(58)))
                                SelectAsset(mesh);
                        }
                        Row("Vertices", mesh.vertexCount.ToString());
                        Row("Submeshes", mesh.subMeshCount.ToString());
                        Row("UV0", mesh.uv != null && mesh.uv.Length == mesh.vertexCount
                            ? "Available" : "Unavailable / incomplete");
                        Row("UV1", mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount
                            ? "Available" : "Unavailable / incomplete");
                    }
                }
                if (meshes.Length > displayed)
                    EditorGUILayout.HelpBox("Displaying first " + displayed +
                        " meshes. Select sub-assets in Project for the others.", MessageType.Info);
            }

            if (declarations.Length > 0)
            {
                Section("W3X Source Declarations");
                Row("Declarations", declarations.Length.ToString());
                foreach (string item in declarations.Take(12))
                    EditorGUILayout.LabelField(item, EditorStyles.miniLabel);
                if (declarations.Length > 12)
                    EditorGUILayout.LabelField("... and " +
                        (declarations.Length - 12) + " more", EditorStyles.miniLabel);
            }
        }

        private void DrawRig()
        {
            Section("Rig Import Settings");
            Prop("rigType", "Animation Type");
            SerializedProperty kind = Option("rigType");
            if (kind != null && !kind.hasMultipleDifferentValues)
            {
                SageRigType type = (SageRigType)kind.enumValueIndex;
                if (type == SageRigType.Generic)
                {
                    Prop("avatarDefinition", "Avatar Definition");
                    SerializedProperty definition = Option("avatarDefinition");
                    if (definition != null && (SageAvatarDefinition)definition.enumValueIndex ==
                        SageAvatarDefinition.CopyFromOtherAvatar)
                        Prop("sourceAvatar", "Source Avatar");
                    else
                        Prop("genericRootBone", "Root Motion Bone");
                    EditorGUILayout.HelpBox("Generic creates/reuses a Unity Generic Avatar. " +
                        "Source bone paths remain intact.", MessageType.Info);
                }
                else if (type == SageRigType.Legacy)
                    EditorGUILayout.HelpBox("Legacy adds the Animation component when " +
                        "clips exist; no Avatar is generated.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("None keeps imported pivots/clips without an " +
                        "Animation, Animator or Avatar.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Humanoid retargeting is not yet supported. " +
                "SAGE unit models typically use non-humanoid hierarchies.", MessageType.None);
            EditorGUILayout.Space();
            Section("Skeleton / Pivot Hierarchy");
            if (pivotRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "No SAGE_Pivots hierarchy was imported. An animation may require a " +
                    "matching *_SKL.w3d / *_SKL.w3x file beside the source.",
                    MessageType.Info);
                return;
            }

            Transform[] transforms = pivotRoot.GetComponentsInChildren<Transform>(true);
            int pivotCount = Math.Max(0, transforms.Length - 1);
            Row("Rig Type", "SAGE pivot hierarchy (not Unity Humanoid)");
            Row("Pivot Count", pivotCount.ToString());
            Row("Pivot Root", pivotRoot.name);
            Row("Top-Level Bones", pivotRoot.childCount.ToString());

            int skinnedCount = renderers.OfType<SkinnedMeshRenderer>().Count();
            Row("Skinned Meshes", skinnedCount.ToString());
            showPivotNames = EditorGUILayout.Foldout(showPivotNames, "Show Pivot Names", true);
            if (!showPivotNames)
                return;

            int displayed = Math.Min(pivotCount, MaxDisplayedPivots);
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 1; i <= displayed; i++)
                {
                    Transform node = transforms[i];
                    EditorGUILayout.LabelField(
                        RelativePath(node, pivotRoot), EditorStyles.miniLabel);
                }
            }
            if (pivotCount > displayed)
                EditorGUILayout.HelpBox("Showing first " + displayed + " pivots of " +
                    pivotCount + ".", MessageType.Info);
        }

        private void DrawAnimation()
        {
            Section("Animation Import Settings");
            Prop("importAnimations", "Import Animations");
            EditorGUILayout.HelpBox("W3D supports uncompressed tracks. W3X uses existing " +
                "source animation conversion and preserves frame rates.", MessageType.None);
            EditorGUILayout.Space();
            Section("Imported Animation Clips");
            Row("Clip Count", clips.Length.ToString());
            if (clips.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No AnimationClip sub-assets were imported. This may be a model-only " +
                    "file, an unresolved skeleton, or an unsupported W3D compressed " +
                    "animation/channel. See the Console for importer warnings.",
                    MessageType.Info);
                return;
            }

            foreach (var clip in clips)
            {
                if (clip == null) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(clip.name, EditorStyles.boldLabel);
                        if (GUILayout.Button("Select", GUILayout.Width(58)))
                            SelectAsset(clip);
                    }
                    Row("Length", clip.length.ToString("F3") + " s");
                    Row("Frame Rate", clip.frameRate.ToString("F2") + " fps");
                    Row("Approx. Frames",
                        (Mathf.RoundToInt(clip.length * clip.frameRate) + 1).ToString());
                    Row("Unity Clip Type", clip.legacy ? "Legacy" : "Mecanim");
                    Row("Looping", clip.isLooping ? "Yes" : "No");
                }
            }

            EditorGUILayout.HelpBox(
                "W3D clips are generated from supported uncompressed channels. " +
                "W3X source clips follow the existing W3X animation workflow. " +
                "To edit W3X animations, continue using the Phase 3B editable workspace.",
                MessageType.None);
        }

        private void DrawMaterial()
        {
            Section("Material Import Settings");
            Prop("importMaterials", "Import Materials");
            Prop("materialShaderOverride", "Generated Material Shader");
            DrawMaterialOverrides();
            EditorGUILayout.Space();
            Section("Imported Materials");
            Row("Material Count", materials.Length.ToString());
            if (materials.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No material sub-assets are available. Skeleton and animation " +
                    "files typically have no materials.", MessageType.Info);
                return;
            }

            foreach (var material in materials)
            {
                if (material == null) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(material.name, EditorStyles.boldLabel);
                        if (GUILayout.Button("Select", GUILayout.Width(58)))
                            SelectAsset(material);
                    }
                    Row("Shader", material.shader != null
                        ? material.shader.name : "Missing");
                    Texture texture = material.mainTexture;
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField("Main Texture", texture,
                            typeof(Texture), false);

                    if (texture == null)
                        EditorGUILayout.LabelField(
                            "Texture not assigned (may require extracted source textures).",
                            EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.HelpBox(
                "Materials displayed here are import previews. Selecting a material " +
                "opens its usual Unity Inspector. The source W3D/W3X file is not " +
                "rewritten by this tab.", MessageType.None);
        }

        private static string RelativePath(Transform node, Transform root)
        {
            var parts = new List<string>();
            Transform cursor = node;
            while (cursor != null && cursor != root)
            {
                parts.Add(cursor.name);
                cursor = cursor.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void Section(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
        }

        private static void Row(string label, string value)
        {
            EditorGUILayout.LabelField(label, value ?? "");
        }

        private static void SelectAsset(UnityEngine.Object asset)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void MissingAsset()
        {
            EditorGUILayout.HelpBox(
                "Imported GameObject not available. Select a valid .w3d or .w3x " +
                "source file and check the Unity Console for import errors.",
                MessageType.Warning);
        }
    }

    [CustomEditor(typeof(W3xImporter))]
    public sealed class W3xImporterInspector : SageImporterInspectorBase
    {
    }

    [CustomEditor(typeof(W3dImporter))]
    public sealed class W3dImporterInspector : SageImporterInspectorBase
    {
    }
}
