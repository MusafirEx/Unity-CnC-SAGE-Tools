using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Search Texture:/GameObject:/other virtual assets inside an indexed
    /// compiled .manifest+.bin pair. No entire BIN import/extraction.
    /// </summary>
    internal sealed class Cnc3BigCompiledBrowserWindow : EditorWindow
    {
        private Cnc3BigArchiveIndex _archive;
        private Cnc3BigCompiledAssetIndex _index;
        private readonly List<string> _bins = new List<string>();
        private readonly List<string> _manifests = new List<string>();
        private int _pair;
        private string _search = "";
        private int _page;
        private Vector2 _scroll;
        private Cnc3BigVirtualAsset _selected;
        private string _sample = "";
        private const int PageSize = 70;

        [MenuItem("Tools/C&C SAGE/BIG Compiled Asset Browser")]
        private static void ShowWindow()
        {
            var w = GetWindow<Cnc3BigCompiledBrowserWindow>("BIG Compiled Assets");
            w.minSize = new Vector2(720, 480);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("C&C3/KW Virtual Compiled Asset Index",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A BIG usually contains compiled .manifest + .bin streams, " +
                "rather than loose road DDS/W3X. This tool indexes the named " +
                "SAGE records directly without extracting thousands of files. " +
                "It does not execute or instantiate compiled game assets.",
                MessageType.Info);
            EditorGUI.BeginChangeCheck();
            _archive = (Cnc3BigArchiveIndex)EditorGUILayout.ObjectField(
                "BIG Archive Index", _archive, typeof(Cnc3BigArchiveIndex), false);
            if (EditorGUI.EndChangeCheck()) RefreshPairs();
            EditorGUI.BeginChangeCheck();
            _index = (Cnc3BigCompiledAssetIndex)EditorGUILayout.ObjectField(
                "Compiled Asset Index", _index, typeof(Cnc3BigCompiledAssetIndex), false);
            if (EditorGUI.EndChangeCheck() && _index != null)
            {
                _archive = _index.archive;
                RefreshPairs();
            }
            if (_archive == null)
            {
                EditorGUILayout.HelpBox("First open the BIG Archive Browser and " +
                    "create WBData_Index.asset. Assign that asset here.",
                    MessageType.None);
                return;
            }
            if (!_archive.IsCurrent)
            {
                EditorGUILayout.HelpBox("BIG index is stale. Rescan in BIG Archive Browser.",
                    MessageType.Warning);
                return;
            }
            if (_bins.Count == 0) RefreshPairs();
            if (_bins.Count == 0)
            {
                EditorGUILayout.HelpBox("No matching .manifest + .bin pair in this BIG.",
                    MessageType.Warning);
                return;
            }
            string[] labels = new string[_bins.Count];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = _bins[i] + " + " + _manifests[i];
            _pair = EditorGUILayout.Popup("Compiled stream pair",
                Mathf.Clamp(_pair, 0, labels.Length - 1), labels);
            if (GUILayout.Button("Build Virtual Asset Index", GUILayout.Height(28)))
                BuildIndex();

            if (_index == null) return;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Source BIN", _index.binMemberName);
            EditorGUILayout.LabelField("Source manifest", _index.manifestMemberName);
            EditorGUILayout.LabelField("Manifest version",
                _index.manifestVersion.ToString());
            EditorGUILayout.LabelField("Stream identifier",
                "0x" + _index.compiledStreamId.ToString("X8"));
            EditorGUILayout.HelpBox(_index.status ?? "", MessageType.None);

            string search = EditorGUILayout.TextField("Search asset name", _search);
            if (search != _search) { _search = search; _page = 0; }
            List<int> results = new List<int>();
            for (int i = 0; i < _index.assets.Count; i++)
            {
                Cnc3BigVirtualAsset asset = _index.assets[i];
                if (string.IsNullOrEmpty(_search) ||
                    (asset.name != null && asset.name.IndexOf(
                        _search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (asset.source != null && asset.source.IndexOf(
                        _search, StringComparison.OrdinalIgnoreCase) >= 0))
                    results.Add(i);
            }
            int maxPage = Mathf.Max(0, (results.Count - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, maxPage);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(results.Count.ToString("N0") +
                    " matching records");
                GUI.enabled = _page > 0;
                if (GUILayout.Button("Previous", GUILayout.Width(80))) _page--;
                GUI.enabled = _page < maxPage;
                if (GUILayout.Button("Next", GUILayout.Width(70))) _page++;
                GUI.enabled = true;
                GUILayout.Label("Page " + (_page + 1) + " / " + (maxPage + 1));
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll,
                GUILayout.MinHeight(160));
            int from = _page * PageSize;
            for (int i = from; i < Math.Min(results.Count, from + PageSize); i++)
            {
                var asset = _index.assets[results[i]];
                if (GUILayout.Button(asset.name + " (" +
                    asset.binSize.ToString("N0") + " bytes)",
                    EditorStyles.miniButton))
                {
                    _selected = asset;
                    _sample = "";
                }
            }
            EditorGUILayout.EndScrollView();
            if (_selected != null && _index.assets.Contains(_selected))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Selected virtual asset",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_selected.name,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Source", _selected.source,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Type / instance",
                    "0x" + _selected.typeId.ToString("X8") + " / 0x" +
                    _selected.instanceId.ToString("X8"));
                EditorGUILayout.LabelField("BIN data",
                    "0x" + _selected.binOffset.ToString("X") + "  •  " +
                    _selected.binSize.ToString("N0") + " bytes");
                if (GUILayout.Button("Read first 128 bytes only"))
                    PreviewSelected();
                if (!string.IsNullOrEmpty(_sample))
                    EditorGUILayout.TextArea(_sample, GUILayout.Height(70));
            }
        }

        private void RefreshPairs()
        {
            _bins.Clear();
            _manifests.Clear();
            _pair = 0;
            _selected = null;
            if (_archive == null || _archive.entries == null) return;
            foreach (var item in _archive.entries)
            {
                if (!item.name.EndsWith(".manifest",
                    StringComparison.OrdinalIgnoreCase)) continue;
                string binName = item.name.Substring(0,
                    item.name.Length - ".manifest".Length) + ".bin";
                if (Cnc3BigArchiveReader.TryFind(_archive, binName,
                    out var matching))
                {
                    _manifests.Add(item.name);
                    _bins.Add(matching.name);
                }
            }
        }

        private void BuildIndex()
        {
            if (_pair < 0 || _pair >= _bins.Count) return;
            var generated = CreateInstance<Cnc3BigCompiledAssetIndex>();
            try
            {
                Cnc3BigCompiledAssetReader.Build(_archive,
                    _bins[_pair], _manifests[_pair], generated);
                string proposedName = Path.GetFileNameWithoutExtension(
                    _bins[_pair].Replace('\\', '/')) + "_CompiledIndex";
                string output = EditorUtility.SaveFilePanelInProject(
                    "Save Virtual Compiled Asset Index",
                    proposedName, "asset",
                    "Stores names/offsets only, not extracted compiled BIN assets.");
                if (string.IsNullOrEmpty(output))
                {
                    DestroyImmediate(generated);
                    return;
                }
                AssetDatabase.CreateAsset(generated, output);
                AssetDatabase.SaveAssets();
                _index = generated;
                _selected = null;
                Selection.activeObject = generated;
                Debug.Log("[C&C SAGE BIG2] " + generated.status);
            }
            catch (Exception ex)
            {
                if (generated != null && !AssetDatabase.Contains(generated))
                    DestroyImmediate(generated);
                Debug.LogError("[C&C SAGE BIG2] Index failed: " + ex);
                EditorUtility.DisplayDialog("Compiled index failed", ex.Message, "OK");
            }
        }

        private void PreviewSelected()
        {
            try
            {
                if (!_index.IsCurrent || _selected.binSize < 1)
                    throw new InvalidDataException("Compiled asset index is stale or empty.");
                if (!Cnc3BigArchiveReader.TryFind(_index.archive,
                    _index.binMemberName, out var member))
                    throw new InvalidDataException("BIN archive member missing.");
                int length = Math.Min(128, _selected.binSize);
                byte[] data = Cnc3BigArchiveReader.ReadMemberRange(
                    _index.archive, member, _selected.binOffset, length);
                var sb = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    if (i > 0 && i % 16 == 0) sb.AppendLine();
                    sb.Append(data[i].ToString("X2")).Append(' ');
                }
                _sample = sb.ToString();
            }
            catch (Exception ex)
            {
                _sample = ex.Message;
            }
        }
    }
}
