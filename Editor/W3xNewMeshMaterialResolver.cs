using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Selects the requested existing FXShader template and maps resolvable
    /// project texture files to its existing SAGE texture constants.
    /// Never assumes a Unity shader name is a game-compatible FXShader.
    /// </summary>
    internal static class W3xNewMeshMaterialResolver
    {
        private static readonly string[] SupportedExtensions =
            { ".tga", ".dds", ".png", ".jpg", ".jpeg", ".bmp" };

        internal static void Apply(
            XElement newMesh, XElement[] originalMeshes, GameObject part,
            string destinationDirectory, W3xExportCore.Report report)
        {
            string id = (string)newMesh.Attribute("id") ?? part.name;
            var identity = part.GetComponent<CncSageMaterialIdentity>();
            XElement source = null;
            if (identity != null &&
                !string.IsNullOrWhiteSpace(identity.ShaderTemplateMeshId))
            {
                source = originalMeshes.FirstOrDefault(m =>
                    string.Equals((string)m.Attribute("id"),
                        identity.ShaderTemplateMeshId.Trim(),
                        StringComparison.OrdinalIgnoreCase));
                if (source == null)
                    throw new InvalidDataException(id + ": shader template mesh '" +
                        identity.ShaderTemplateMeshId +
                        "' does not exist in the original W3X. " +
                        "Select an existing W3DMesh identifier.");
            }

            var available = (source == null ? originalMeshes : new[] { source })
                .SelectMany(m => m.Elements()
                    .Where(x => x.Name.LocalName == "FXShader")).ToArray();
            if (available.Length == 0)
            {
                if (identity != null)
                    throw new InvalidDataException(id +
                        ": no source FXShader template found. Material " +
                        "identity needs an existing source shader.");
                report.Notes.Add("NEW " + id +
                    ": no source FXShader found; no material will be emitted.");
                return;
            }
            if (source != null && available.Length != 1)
                throw new InvalidDataException(id + ": source W3DMesh " +
                    identity.ShaderTemplateMeshId + " has " +
                    available.Length + " FXShader passes; single-pass " +
                    "material identity only.");

            var shader = new XElement(available[0]);
            newMesh.Add(shader);
            string shaderName = (string)shader.Attribute("ShaderName") ??
                "(unnamed)";
            report.Notes.Add("NEW " + id + ": FXShader template " +
                shaderName + " from " +
                (source == null ? "first source shader (legacy fallback)" :
                    (string)source.Attribute("id")) + ".");

            var renderer = part.GetComponent<MeshRenderer>();
            var mat = renderer == null ? null : renderer.sharedMaterial;
            bool allowMaterial = identity == null ||
                identity.UseRendererTextures;

            ApplySlot(shader, identity == null ? null :
                    identity.DiffuseTexture,
                identity == null ? "" : identity.DiffuseTextureName,
                allowMaterial ? ReadMaterialTexture(mat,
                    "_BaseMap", "_MainTex") : null,
                new[] { "DiffuseTexture", "Texture_0" },
                "Diffuse", id, destinationDirectory, report);
            ApplySlot(shader, identity == null ? null :
                    identity.NormalTexture,
                identity == null ? "" : identity.NormalTextureName,
                allowMaterial ? ReadMaterialTexture(mat,
                    "_BumpMap", "_NormalMap") : null,
                new[] { "NormalMap" },
                "Normal", id, destinationDirectory, report);
            ApplySlot(shader, identity == null ? null :
                    identity.SpecularTexture,
                identity == null ? "" : identity.SpecularTextureName,
                allowMaterial ? ReadMaterialTexture(mat,
                    "_SpecGlossMap") : null,
                new[] { "SpecMap" },
                "Specular", id, destinationDirectory, report);
        }

        private static Texture2D ReadMaterialTexture(
            Material mat, params string[] names)
        {
            if (mat == null)
                return null;
            foreach (var name in names)
            {
                if (mat.HasProperty(name))
                {
                    var texture = mat.GetTexture(name) as Texture2D;
                    if (texture != null)
                        return texture;
                }
            }
            return null;
        }

        private static void ApplySlot(
            XElement shader, Texture2D explicitTexture,
            string requestedName, Texture2D materialTexture,
            string[] candidateConstants, string label, string meshId,
            string outputDirectory, W3xExportCore.Report report)
        {
            Texture2D nameResolved = null;
            if (explicitTexture == null && !string.IsNullOrWhiteSpace(requestedName))
            {
                nameResolved = FindNamedTexture(requestedName,
                    outputDirectory);
                if (nameResolved == null)
                    report.Notes.Add("NEW " + meshId + " " + label +
                        ": no matching texture for '" + requestedName +
                        "' beside exported W3X. Trying material fallback.");
            }
            Texture2D texture = explicitTexture != null ? explicitTexture :
                (nameResolved != null ? nameResolved : materialTexture);
            if (texture == null)
                return;
            bool mandatory = explicitTexture != null || nameResolved != null;

            string file = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(file) ||
                !file.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !SupportedExtensions.Any(ext => file.EndsWith(
                    ext, StringComparison.OrdinalIgnoreCase)) ||
                !File.Exists(file))
            {
                HandleUnresolvable(meshId, label, mandatory,
                    "the texture must be a TGA/DDS/PNG/JPG/BMP file in Assets",
                    report);
                return;
            }

            // The current W3X importer resolves diffuse/normal/spec by file
            // name in the W3X folder. Refuse a path which will not reimport.
            if (!string.IsNullOrEmpty(outputDirectory) &&
                !string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(file)),
                    Path.GetFullPath(outputDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                HandleUnresolvable(meshId, label, mandatory,
                    "texture " + file +
                    " is not beside the output W3X. " +
                    "Move/copy the texture into that folder first",
                    report);
                return;
            }

            var constants = shader.Elements().FirstOrDefault(x =>
                x.Name.LocalName == "Constants");
            var entry = constants == null ? null :
                constants.Elements().FirstOrDefault(x =>
                    x.Name.LocalName == "Texture" &&
                    candidateConstants.Any(name => string.Equals(
                        (string)x.Attribute("Name"), name,
                        StringComparison.OrdinalIgnoreCase)));
            var value = entry == null ? null :
                entry.Elements().FirstOrDefault(x =>
                    x.Name.LocalName == "Value");
            if (value == null)
            {
                HandleUnresolvable(meshId, label, mandatory,
                    "FXShader template has no compatible " + label +
                    " texture constant. Choose another template",
                    report);
                return;
            }

            string newName = Path.GetFileNameWithoutExtension(file);
            if (!string.Equals(value.Value.Trim(), newName,
                StringComparison.OrdinalIgnoreCase))
            {
                value.Value = newName;
                report.ChangedShaderTextures++;
            }
            report.Notes.Add("NEW " + meshId + " " + label +
                " texture: " + newName + " (" +
                (explicitTexture != null ? "Material Identity asset" :
                    nameResolved != null ? "Material Identity texture name" :
                    "Unity material") + ").");
        }

        private static Texture2D FindNamedTexture(
            string name, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
                return null;

            name = name.Trim();
            // Names are local texture IDs, never paths or URLs.
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                name == "." || name == "..")
                throw new InvalidDataException(
                    "Texture Identity name must be a local image base name.");
            string stem = Path.GetFileNameWithoutExtension(name);
            string assets = Path.GetFullPath("Assets").Replace('\\', '/');

            foreach (var path in Directory.GetFiles(directory).OrderBy(x => x))
            {
                if (!SupportedExtensions.Any(ext =>
                    path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(path),
                    stem, StringComparison.OrdinalIgnoreCase))
                    continue;
                string absolute = Path.GetFullPath(path).Replace('\\', '/');
                if (!absolute.StartsWith(assets + "/",
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                string assetPath = "Assets/" +
                    absolute.Substring(assets.Length + 1);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null)
                    return tex;
            }
            return null;
        }

        private static void HandleUnresolvable(
            string meshId, string slot, bool mandatory,
            string reason, W3xExportCore.Report report)
        {
            if (mandatory)
                throw new InvalidDataException(
                    meshId + " " + slot + " override: " + reason);
            report.Notes.Add("NEW " + meshId + " " + slot +
                " material texture not exported: " + reason +
                ". Kept source FXShader constant.");
        }
    }
}
