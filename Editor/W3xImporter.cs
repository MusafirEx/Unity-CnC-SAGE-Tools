using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// W3X XML importer: meshes, source pivots, basic skin weights and materials.
    /// Binary W3D remains unsupported. Phase 3A adds limited AnimationClip sub-assets.
    /// </summary>
    [ScriptedImporter(7, "w3x")]
    public sealed class W3xImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var document = W3xReader.Read(context.assetPath);

            foreach (var warning in document.Warnings)
                Debug.LogWarning("[C&C SAGE] " + warning);

            // Only dependencies inside the Unity project can be registered.
            foreach (var source in document.SourcePaths)
            {
                var normalized = source.Replace('\\', '/');
                var projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
                if (!normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = normalized.Substring(projectRoot.Length);
                if (relative != context.assetPath.Replace('\\', '/'))
                    context.DependsOnSourceAsset(relative);
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            // Source-only hierarchies can contain importable animation
            // tracks; models with unsupported animation channels remain
            // metadata-only placeholders. Geometry import is unchanged.
            root.AddComponent<CncSageSourceInfo>()
                .SetSourceInfo(document.Declarations.ToArray(), document.Meshes.Count);
            context.AddObjectToAsset("root", root);
            context.SetMainObject(root);

            // Phase 1B: build meshes, pivot hierarchy, skinned renderers,
            // and basic FXShader-derived materials in one import pass.
            W3xModelBuilder.Build(context, document, root);
        }
    }
}
