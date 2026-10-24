using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// BIG Browser UI: index one original archive, no bulk extraction or
    /// import of individual game assets. Binary bytes are loaded only when
    /// the user explicitly extracts one selected member.
    /// </summary>
    internal sealed class Cnc3BigBrowserWindow : EditorWindow
    {
        private Cnc3BigArchiveIndex _archive;
        private string _search = "";
        private Vector2 _scroll;
        private int _selected = -1;
        private int _visiblePage;
        private const int ItemsPerPage = 80;
        private string _filterInfo = "";

        [MenuItem("Tools/C&C SAGE/BIG Archive Browser")]
        private static void ShowBrowser()
        {
            var window = GetWindow<Cnc3BigBrowserWindow>("SAGE BIG Archive");
            window.minSize = new Vector2(650, 450);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EA SAGE .big Archive Browser",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "References a BIGF/BIG4 file directly, including archives outside " +
                "the Unity project. Only a lightweight index .asset is saved. " +
                "No source W3X, XML, or DDS files are imported until explicitly requested.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _archive = (Cnc3BigArchiveIndex)EditorGUILayout.ObjectField(
                    "Archive Index", _archive, typeof(Cnc3BigArchiveIndex), false);
                if (GUILayout.Button("Open BIG...", GUILayout.Width(120)))
                    BrowseNewArchive();
                GUI.enabled = _archive != null;
                if (GUILayout.Button("Rescan", GUILayout.Width(85)))
                    Rescan();
                GUI.enabled = true;
            }

            if (_archive == null)
            {
                EditorGUILayout.HelpBox("Choose Open BIG... to index a local " +
                    "WBData.big, StaticStream.big, Terrain.big or other SAGE archive.",
                    MessageType.None);
                return;
            }
            EditorGUILayout.LabelField("Source", _archive.archivePath,
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Archive",
                _archive.archiveFourCC + " / " + _archive.archiveLength.ToString("N0") +
                " bytes / " + (_archive.entries == null ? 0 :
                    _archive.entries.Count).ToString("N0") + " members");
            bool valid = _archive.IsCurrent;
            if (!valid)
                EditorGUILayout.HelpBox("The BIG file is missing or changed. " +
                    "Restore its location or choose Rescan before reading a member.",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    "Archive index is valid. Compressed members are decoded " +
                    "on demand with output limits. Compiled .bin resources " +
                    "need separate SAGE asset decoding before individual " +
                    "textures/models can be used.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                string nextSearch = EditorGUILayout.TextField(
                    "Find entry", _search);
                if (nextSearch != _search)
                {
                    _search = nextSearch;
                    _visiblePage = 0;
                    _scroll = Vector2.zero;
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    _search = "";
                    _visiblePage = 0;
                }
            }

            if (_archive.entries == null) return;
            int[] matches = FindMatches(_archive, _search);
            int pageCount = Math.Max(1,
                (matches.Length + ItemsPerPage - 1) / ItemsPerPage);
            _visiblePage = Mathf.Clamp(_visiblePage, 0, pageCount - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(matches.Length.ToString("N0") +
                    " matched files | Page " + (_visiblePage + 1) +
                    "/" + pageCount, GUILayout.MinWidth(230));
                GUILayout.FlexibleSpace();
                GUI.enabled = _visiblePage > 0;
                if (GUILayout.Button("Previous", GUILayout.Width(90)))
                    _visiblePage--;
                GUI.enabled = _visiblePage + 1 < pageCount;
                if (GUILayout.Button("Next", GUILayout.Width(70)))
                    _visiblePage++;
                GUI.enabled = true;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll,
                GUILayout.MinHeight(185));
            int begin = _visiblePage * ItemsPerPage;
            int end = Math.Min(matches.Length, begin + ItemsPerPage);
            for (int i = begin; i < end; i++)
            {
                int index = matches[i];
                Cnc3BigEntry member = _archive.entries[index];
                string display = member.name + "  (" +
                    member.storedLength.ToString("N0") + " bytes)";
                bool clicked = GUILayout.Toggle(_selected == index,
                    display, "Button", GUILayout.Height(22));
                if (clicked && _selected != index)
                    _selected = index;
            }
            EditorGUILayout.EndScrollView();

            if (_selected >= 0 && _selected < _archive.entries.Count)
            {
                var chosen = _archive.entries[_selected];
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Selected member", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(chosen.name,
                    EditorStyles.textField, GUILayout.Height(18));
                EditorGUILayout.LabelField("Stored bytes: " +
                    chosen.storedLength.ToString("N0") + " / offset 0x" +
                    chosen.offset.ToString("X8"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = valid;
                    if (GUILayout.Button("Extract only this entry..."))
                        ExportSelected(chosen);
                    GUI.enabled = true;
                }
            }
        }

        private static int[] FindMatches(Cnc3BigArchiveIndex archive,
            string text)
        {
            var selected = new System.Collections.Generic.List<int>();
            for (int i = 0; i < archive.entries.Count; i++)
            {
                if (string.IsNullOrEmpty(text) ||
                    archive.entries[i].name.IndexOf(
                        text, StringComparison.OrdinalIgnoreCase) >= 0)
                    selected.Add(i);
            }
            return selected.ToArray();
        }

        private void BrowseNewArchive()
        {
            string path = EditorUtility.OpenFilePanel(
                "Open original EA BIG archive", "", "big");
            if (string.IsNullOrEmpty(path)) return;
            var result = CreateInstance<Cnc3BigArchiveIndex>();
            try
            {
                Cnc3BigArchiveReader.Scan(path, result);
                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save lightweight BIG archive index",
                    Path.GetFileNameWithoutExtension(path) + "_Index",
                    "asset", "Save a lightweight reference to the external BIG file.");
                if (string.IsNullOrEmpty(assetPath))
                {
                    DestroyImmediate(result);
                    return;
                }
                AssetDatabase.CreateAsset(result, assetPath);
                AssetDatabase.SaveAssets();
                _archive = result;
                _selected = -1;
                _visiblePage = 0;
                Selection.activeObject = result;
                EditorGUIUtility.PingObject(result);
                Debug.Log("[C&C SAGE BIG] Indexed " + path + " as " + assetPath +
                    ", " + result.entries.Count + " archive members, no extraction.");
            }
            catch (Exception error)
            {
                if (result != null && !AssetDatabase.Contains(result))
                    DestroyImmediate(result);
                Debug.LogError("[C&C SAGE BIG] " + error);
                EditorUtility.DisplayDialog("Invalid BIG archive",
                    error.Message, "OK");
            }
        }

        private void Rescan()
        {
            if (_archive == null) return;
            try
            {
                // Scan into a temporary instance so failed validation does not
                // corrupt the existing persistent index.
                var temp = CreateInstance<Cnc3BigArchiveIndex>();
                try
                {
                    Cnc3BigArchiveReader.Scan(_archive.archivePath, temp);
                    Undo.RecordObject(_archive, "Rescan SAGE BIG archive");
                    _archive.archiveLength = temp.archiveLength;
                    _archive.archiveFourCC = temp.archiveFourCC;
                    _archive.archiveLastWriteUtcTicks = temp.archiveLastWriteUtcTicks;
                    _archive.directoryBytes = temp.directoryBytes;
                    _archive.entries = temp.entries;
                    EditorUtility.SetDirty(_archive);
                    AssetDatabase.SaveAssets();
                }
                finally { DestroyImmediate(temp); }
                _selected = -1;
            }
            catch (Exception error)
            {
                Debug.LogError("[C&C SAGE BIG] " + error);
                EditorUtility.DisplayDialog("BIG scan failed",
                    error.Message, "OK");
            }
        }

        private void ExportSelected(Cnc3BigEntry entry)
        {
            string filename = Path.GetFileName(entry.name.Replace('\\', '/'));
            string extension = Path.GetExtension(filename).TrimStart('.');
            string destination = EditorUtility.SaveFilePanel(
                "Extract selected BIG member",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.GetFileNameWithoutExtension(filename), extension);
            if (string.IsNullOrEmpty(destination)) return;
            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Output exists",
                    "Select a different filename; existing files will not be overwritten.",
                    "OK");
                return;
            }
            try
            {
                Cnc3BigArchiveReader.ExportMember(_archive, entry, destination);
                if (destination.Replace('\\', '/').StartsWith(
                    Application.dataPath.Replace('\\', '/') + "/",
                    StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
                Debug.Log("[C&C SAGE BIG] Extracted only " + entry.name +
                    " to " + destination);
            }
            catch (Exception error)
            {
                Debug.LogError("[C&C SAGE BIG] " + error);
                EditorUtility.DisplayDialog("Extract BIG member failed",
                    error.Message, "OK");
            }
        }
    }
}
