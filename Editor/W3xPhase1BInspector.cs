using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>Read-only structural validation for selected imported W3X assets.</summary>
    public static class W3xPhase1BInspector
    {
        [MenuItem("Tools/C&C SAGE/Inspect Selected Model Rig & Materials")]
        private static void Inspect()
        {
            string sourcePath;
            GameObject asset;
            if (!W3xExportMenu.TryGetSelectedModel(
                out sourcePath, out asset))
            {
                EditorUtility.DisplayDialog("C&C SAGE Inspector",
                    "Select an imported W3X or linked editable prefab in Project.",
                    "OK");
                return;
            }

            var report = new StringBuilder();
            var meshRenderers = asset.GetComponentsInChildren<MeshRenderer>(true);
            var skins = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var filters = asset.GetComponentsInChildren<MeshFilter>(true);
            var source = asset.GetComponent<CncSageSourceInfo>();
            string selectedPath = AssetDatabase.GetAssetPath(asset);
            report.AppendLine("[C&C SAGE Phase 1B] Project model: " +
                selectedPath);
            report.AppendLine("Original W3X source: " + sourcePath);
            var sourceLink = asset.GetComponent<CncSageEditableSource>();
            if (sourceLink != null)
                report.AppendLine("Editable asset source GUID: " +
                    sourceLink.SourceGuid);
            report.AppendLine("MeshFilters: " + filters.Length +
                " | SkinnedMeshRenderers: " + skins.Length +
                " | MeshRenderers: " + meshRenderers.Length);
            if (source != null)
                report.AppendLine("SAGE meshes: " + source.ImportedMeshCount +
                    " | Declarations: " + string.Join("; ", source.Declarations));
            foreach (var renderer in skins)
                report.AppendLine("  SKIN " + renderer.name +
                    " | vertices=" + (renderer.sharedMesh == null ? 0 :
                        renderer.sharedMesh.vertexCount) +
                    " | bones=" + renderer.bones.Length +
                    " | bindposes=" + (renderer.sharedMesh == null ? 0 :
                        renderer.sharedMesh.bindposes.Length) +
                    " | materials=" + renderer.sharedMaterials.Length +
                    " | firstMaterial=" + (renderer.sharedMaterial == null ? "NONE" :
                        renderer.sharedMaterial.name));
            foreach (var renderer in meshRenderers)
                report.AppendLine("  STATIC " + renderer.name +
                    " | materials=" + renderer.sharedMaterials.Length +
                    " | firstMaterial=" + (renderer.sharedMaterial == null ? "NONE" :
                        renderer.sharedMaterial.name));
            var holder = asset.transform.Find("SAGE_Pivots");
            report.AppendLine("Pivot root: " + (holder == null ? "not found" :
                holder.name + " (" + holder.GetComponentsInChildren<Transform>().Length +
                " transforms incl. holder)"));
            Debug.Log(report.ToString());
        }
    }
}
