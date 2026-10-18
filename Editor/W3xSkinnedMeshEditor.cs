using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Non-destructive first-pass vertex/UV/existing-weight editor for W3X
    /// skinned meshes. Requires the standalone editable Prefab + Mesh asset.
    /// It does not create vertices, bones, topology or new weight influences.
    /// </summary>
    internal sealed class W3xSkinnedMeshEditor : EditorWindow
    {
        private const string MenuPath =
            "Tools/C&C SAGE/Phase 2D Skinned Mesh Editor";
        private SkinnedMeshRenderer targetSkin;
        private int vertexIndex;
        private bool showVertexDots = true;
        private bool showPositionHandle = true;
        private string sourceStatus = "";
        private bool sourceHasSecondInfluence;
        private bool sourceHasAabTree;
        private int[] sourceBone0;
        private int[] sourceBone1;
        private string lastMeshPath;
        private string lastEditWarning = "";

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<W3xSkinnedMeshEditor>(
                "SAGE Skinned Mesh");
            window.minSize = new Vector2(340, 360);
            window.BindSelected();
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            Repaint();
        }

        private void BindSelected()
        {
            targetSkin = Selection.activeGameObject == null ? null :
                Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
            vertexIndex = 0;
            RefreshSource();
        }

        private bool TryGetEditableMesh(out Mesh mesh, out string warning)
        {
            mesh = null;
            warning = "";
            if (targetSkin == null || targetSkin.sharedMesh == null)
            {
                warning = "Select a GameObject with SkinnedMeshRenderer.";
                return false;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null ||
                !targetSkin.transform.IsChildOf(
                    stage.prefabContentsRoot.transform))
            {
                warning = "Open an editable C&C SAGE prefab in Prefab Mode, " +
                    "then select its SkinnedMeshRenderer child.";
                return false;
            }
            var link = stage.prefabContentsRoot.GetComponent<
                CncSageEditableSource>();
            if (link == null || string.IsNullOrWhiteSpace(link.SourceGuid))
            {
                warning = "The prefab has no CncSageEditableSource link.";
                return false;
            }
            string path = AssetDatabase.GetAssetPath(targetSkin.sharedMesh);
            if (string.IsNullOrEmpty(path) ||
                !path.StartsWith("Assets/CncSageEditable/",
                    StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".asset",
                    StringComparison.OrdinalIgnoreCase))
            {
                warning = "Cannot edit a W3X imported mesh or shared external " +
                    "asset. Use Create Editable W3X Project Prefab first.";
                return false;
            }
            mesh = targetSkin.sharedMesh;
            if (!mesh.isReadable)
            {
                warning = "Selected Mesh is not readable.";
                return false;
            }
            if (mesh.boneWeights.Length != mesh.vertexCount ||
                mesh.bindposes.Length != targetSkin.bones.Length)
            {
                warning = "Bone weights or bindposes are inconsistent.";
                return false;
            }
            if (targetSkin.GetComponent<CncSageMeshIdentity>() == null)
            {
                warning = "Skinned mesh has no CncSageMeshIdentity.";
                return false;
            }
            return true;
        }

        private void RefreshSource()
        {
            sourceStatus = "";
            sourceHasSecondInfluence = false;
            sourceHasAabTree = false;
            sourceBone0 = null;
            sourceBone1 = null;
            lastMeshPath = "";

            Mesh mesh;
            string message;
            if (!TryGetEditableMesh(out mesh, out message))
            {
                sourceStatus = message;
                return;
            }

            try
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                var link = stage.prefabContentsRoot.GetComponent<
                    CncSageEditableSource>();
                string originalPath = AssetDatabase.GUIDToAssetPath(
                    link.SourceGuid);
                if (!originalPath.EndsWith(".w3x",
                    StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(originalPath))
                    throw new InvalidDataException(
                        "Original source W3X could not be resolved.");
                var id = targetSkin.GetComponent<CncSageMeshIdentity>()
                    .SageIdentifier;
                var doc = XDocument.Load(originalPath);
                var original = doc.Root.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "W3DMesh" &&
                    string.Equals((string)e.Attribute("id"),
                        id, StringComparison.OrdinalIgnoreCase));
                if (original == null)
                    throw new InvalidDataException(
                        "Original source has no skinned mesh " + id + ".");
                if (!string.Equals(
                    (string)original.Attribute("GeometryType"), "Skin",
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Original mesh is not GeometryType=Skin.");
                var groups = original.Elements()
                    .Where(x => x.Name.LocalName == "BoneInfluences").ToArray();
                if (groups.Length != 1 && groups.Length != 2)
                    throw new InvalidDataException(
                        "Expected one or two original influence groups.");
                var p = groups[0].Elements().Where(x =>
                    x.Name.LocalName == "I").ToArray();
                var q = groups.Length == 2 ? groups[1].Elements().Where(
                    x => x.Name.LocalName == "I").ToArray() : null;
                if (p.Length != mesh.vertexCount ||
                    q != null && q.Length != mesh.vertexCount)
                    throw new InvalidDataException(
                        "Source influence vertex count differs from mesh.");

                sourceBone0 = p.Select(x => int.Parse(
                    (string)x.Attribute("Bone") ?? "0")).ToArray();
                sourceBone1 = q == null ? null : q.Select(x => int.Parse(
                    (string)x.Attribute("Bone") ?? "0")).ToArray();
                sourceHasSecondInfluence = sourceBone1 != null;
                sourceHasAabTree = original.Elements().Any(
                    x => x.Name.LocalName == "AABTree");
                lastMeshPath = AssetDatabase.GetAssetPath(mesh);
                sourceStatus = "Ready: " + id +
                    (sourceHasAabTree ? " | AABTree: position locked" :
                        " | position editing allowed") +
                    (sourceHasSecondInfluence ? " | two influence channels" :
                        " | one influence channel");
            }
            catch (Exception e)
            {
                sourceStatus = "Source validation failed: " + e.Message;
                sourceBone0 = null;
                sourceBone1 = null;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phase 2D | Existing Skin Mesh",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit existing rest-pose vertices on the independent Mesh " +
                "asset saved by Create Editable W3X Project Prefab. " +
                "Open Prefab Mode first. Avoid posing bones while editing. " +
                "New topology, new bone indices and blend-shape sculpting " +
                "are not supported.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                var selectedSkin =
                    (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                        "Skinned Renderer", targetSkin,
                        typeof(SkinnedMeshRenderer), true);
                if (selectedSkin != targetSkin)
                {
                    targetSkin = selectedSkin;
                    vertexIndex = 0;
                    RefreshSource();
                }
                if (GUILayout.Button("Use Selected", GUILayout.Width(105)))
                    BindSelected();
            }
            if (GUILayout.Button("Refresh Source Validation"))
                RefreshSource();

            Mesh mesh;
            string warning;
            if (!TryGetEditableMesh(out mesh, out warning))
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
                return;
            }
            if (AssetDatabase.GetAssetPath(mesh) != lastMeshPath)
                RefreshSource();
            if (sourceBone0 == null)
            {
                EditorGUILayout.HelpBox(sourceStatus, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(sourceStatus, MessageType.None);
            if (!string.IsNullOrEmpty(lastEditWarning))
                EditorGUILayout.HelpBox(lastEditWarning, MessageType.Warning);
            EditorGUILayout.LabelField("Independent Mesh",
                AssetDatabase.GetAssetPath(mesh));
            EditorGUILayout.LabelField("Vertices",
                mesh.vertexCount.ToString());
            vertexIndex = EditorGUILayout.IntSlider("Vertex", vertexIndex, 0,
                Mathf.Max(0, mesh.vertexCount - 1));
            showVertexDots = EditorGUILayout.Toggle("Show vertex dots",
                showVertexDots);
            showPositionHandle = EditorGUILayout.Toggle(
                "Scene position handle", showPositionHandle);

            var positions = mesh.vertices;
            if (vertexIndex < 0 || vertexIndex >= positions.Length)
                return;

            using (new EditorGUI.DisabledScope(sourceHasAabTree))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 pos = EditorGUILayout.Vector3Field(
                    "Local Rest Position", positions[vertexIndex]);
                if (EditorGUI.EndChangeCheck())
                    ApplyPosition(mesh, vertexIndex, pos);
            }
            if (sourceHasAabTree)
                EditorGUILayout.HelpBox(
                    "The original W3X has AABTree; exported skin positions " +
                    "cannot change until the tree can be rebuilt.",
                    MessageType.Warning);

            var uv0 = mesh.uv;
            if (uv0.Length == mesh.vertexCount)
            {
                EditorGUI.BeginChangeCheck();
                var uv = EditorGUILayout.Vector2Field(
                    "UV0", uv0[vertexIndex]);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mesh, "Edit SAGE skinned UV0");
                    uv0[vertexIndex] = uv;
                    mesh.uv = uv0;
                    Persist(mesh);
                }
            }

            var uv1 = mesh.uv2;
            if (uv1.Length == mesh.vertexCount)
            {
                EditorGUI.BeginChangeCheck();
                var uv = EditorGUILayout.Vector2Field(
                    "UV1", uv1[vertexIndex]);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mesh, "Edit SAGE skinned UV1");
                    uv1[vertexIndex] = uv;
                    mesh.uv2 = uv1;
                    Persist(mesh);
                }
            }

            var bw = mesh.boneWeights[vertexIndex];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Original SAGE Bone Indices",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Primary",
                BoneLabel(bw.boneIndex0));
            if (sourceHasSecondInfluence)
                EditorGUILayout.LabelField("Secondary",
                    BoneLabel(bw.boneIndex1));

            bool canEditWeights = !sourceHasAabTree &&
                sourceHasSecondInfluence &&
                sourceBone0[vertexIndex] == bw.boneIndex0 &&
                sourceBone1[vertexIndex] == bw.boneIndex1 &&
                bw.boneIndex0 != bw.boneIndex1;
            using (new EditorGUI.DisabledScope(!canEditWeights))
            {
                EditorGUI.BeginChangeCheck();
                float weight = EditorGUILayout.Slider(
                    "Primary weight", bw.weight0, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    ApplyWeight(mesh, vertexIndex, weight);
            }
            if (!canEditWeights)
                EditorGUILayout.HelpBox(
                    "Weight painting requires two existing source " +
                    "influences with unchanged bone indices. Single-bone " +
                    "vertices remain fixed. AABTree also blocks " +
                    "weight editing because inverse skin export could " +
                    "change source-space positions.", MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Mesh Asset"))
            {
                // Importing an asset synchronously inside OnGUI can dispose
                // IMGUI/UIElements state while still processing GUI events.
                // Save on the next Editor update, outside the GUI callback.
                string path = AssetDatabase.GetAssetPath(mesh);
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("[C&C SAGE Phase 2D] Saved editable skinned mesh: " +
                        path);
                };
            }
            EditorGUILayout.HelpBox(
                "After editing, select the editable prefab in Project and " +
                "use Export Selected W3X (Skin & Shader Textures)... " +
                "Do not use Safe Roundtrip for edited skin vertices.",
                MessageType.Info);
        }

        private string BoneLabel(int index)
        {
            var bones = targetSkin == null ? null : targetSkin.bones;
            if (bones == null || index < 0 || index >= bones.Length)
                return index + " (invalid)";
            return index + ": " +
                (bones[index] == null ? "(missing bone)" :
                    bones[index].name);
        }

        internal static void ModifyVertex(Mesh mesh, int index,
            Vector3 localPosition)
        {
            var v = mesh.vertices;
            if (index < 0 || index >= v.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (float.IsNaN(localPosition.x) ||
                float.IsNaN(localPosition.y) ||
                float.IsNaN(localPosition.z) ||
                float.IsInfinity(localPosition.x) ||
                float.IsInfinity(localPosition.y) ||
                float.IsInfinity(localPosition.z))
                throw new InvalidDataException(
                    "Vertex position must contain finite numbers.");

            var candidate = (Vector3[])v.Clone();
            candidate[index] = localPosition;
            ValidateVertexPosition(mesh, index, candidate);
            v[index] = localPosition;
            mesh.vertices = v;
            mesh.RecalculateBounds();
        }

        private static void ValidateVertexPosition(
            Mesh mesh, int index, Vector3[] vertices)
        {
            var triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1],
                    c = triangles[t + 2];
                if (a != index && b != index && c != index)
                    continue;
                if (Vector3.Cross(vertices[b] - vertices[a],
                    vertices[c] - vertices[a]).sqrMagnitude <= 0.00000001f)
                    throw new InvalidDataException(
                        "Vertex " + index + " would collapse triangle " +
                        (t / 3) + " (" + a + "," + b + "," + c +
                        "). Position was not changed.");
            }
        }

        internal static void ModifyExistingWeight(Mesh mesh,
            int index, float primary)
        {
            var weights = mesh.boneWeights;
            if (index < 0 || index >= weights.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            BoneWeight w = weights[index];
            if (w.boneIndex0 == w.boneIndex1)
                throw new InvalidDataException(
                    "Cannot paint two identical bone indices.");
            primary = Mathf.Clamp01(primary);
            w.weight0 = primary;
            w.weight1 = 1f - primary;
            w.weight2 = 0;
            w.weight3 = 0;
            weights[index] = w;
            mesh.boneWeights = weights;
        }

        private void ApplyPosition(Mesh mesh, int index, Vector3 local)
        {
            if (sourceHasAabTree || sourceBone0 == null)
                return;
            try
            {
                // Validate the proposal first, before creating Undo records.
                // The actual mutation below repeats this safe validation.
                var vertices = mesh.vertices;
                var candidate = (Vector3[])vertices.Clone();
                candidate[index] = local;
                ValidateVertexPosition(mesh, index, candidate);
                Undo.RecordObject(mesh, "Edit SAGE skinned vertex");
                Undo.RecordObject(targetSkin, "Update skinned bounds");
                ModifyVertex(mesh, index, local);
                targetSkin.localBounds = mesh.bounds;
                lastEditWarning = "";
                Persist(mesh);
            }
            catch (InvalidDataException e)
            {
                lastEditWarning = e.Message;
                Repaint();
            }
        }

        private void ApplyWeight(Mesh mesh, int index, float value)
        {
            if (sourceHasAabTree || !sourceHasSecondInfluence ||
                sourceBone0 == null || sourceBone1 == null)
                return;
            BoneWeight bw = mesh.boneWeights[index];
            if (bw.boneIndex0 != sourceBone0[index] ||
                bw.boneIndex1 != sourceBone1[index] ||
                bw.boneIndex0 == bw.boneIndex1)
                return;
            Undo.RecordObject(mesh, "Edit SAGE existing bone weights");
            ModifyExistingWeight(mesh, index, value);
            Persist(mesh);
        }

        private static void Persist(Mesh mesh)
        {
            // SaveAssets during every OnGUI/Handles drag can cause repeated
            // asset reimports and deleted GUIStateObj warnings.
            EditorUtility.SetDirty(mesh);
            SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView view)
        {
            Mesh mesh;
            string warning;
            if (!TryGetEditableMesh(out mesh, out warning) ||
                sourceBone0 == null ||
                (!showPositionHandle && !showVertexDots))
                return;

            var vertices = mesh.vertices;
            if (vertices.Length == 0)
                return;
            vertexIndex = Mathf.Clamp(vertexIndex, 0, vertices.Length - 1);
            var transform = targetSkin.transform;
            Vector3 position = transform.TransformPoint(
                vertices[vertexIndex]);
            Handles.color = Color.yellow;
            Handles.Label(position,
                "SAGE v" + vertexIndex + " (rest pose)");

            if (showPositionHandle)
            {
                using (new EditorGUI.DisabledScope(sourceHasAabTree))
                {
                    EditorGUI.BeginChangeCheck();
                    var moved = Handles.PositionHandle(position,
                        Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && !sourceHasAabTree)
                    {
                        ApplyPosition(mesh, vertexIndex,
                            transform.InverseTransformPoint(moved));
                        Repaint();
                    }
                }
            }

            if (!showVertexDots)
                return;
            // Throttle scene UI complexity for large skinned meshes.
            const int maxDots = 400;
            int start = Mathf.Clamp(vertexIndex - maxDots / 2, 0,
                Mathf.Max(0, vertices.Length - maxDots));
            int end = Mathf.Min(vertices.Length, start + maxDots);
            Handles.color = new Color(0.35f, 0.9f, 1f, 0.8f);
            for (int i = start; i < end; i++)
            {
                if (i == vertexIndex)
                    continue;
                Vector3 world = transform.TransformPoint(vertices[i]);
                float size = HandleUtility.GetHandleSize(world) * 0.035f;
                if (Handles.Button(world, Quaternion.identity, size,
                    size * 1.25f, Handles.DotHandleCap))
                {
                    vertexIndex = i;
                    Repaint();
                    view.Repaint();
                }
            }
        }
    }
}
