using System;
using System.IO;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Experimental strict-texture decoder for a compiled SAGE Texture: asset.
    /// Only loads a real, structurally verified DDS stream if one exists
    /// inside the individual asset's BIN byte range. Other SAGE texture
    /// storage layouts remain unsupported; never guess raw pixel formats.
    /// </summary>
    internal static class Cnc3BigCompiledTextureBridge
    {
        internal static bool TryLoad(Cnc3BigCompiledAssetIndex index,
            string roadType, string declaredTexture, bool allowTypeFallback,
            out Texture2D texture, out string result)
        {
            texture = null;
            result = "No compiled asset index assigned.";
            if (index == null) return false;
            if (!index.IsCurrent)
            {
                result = "Compiled BIG asset index is stale.";
                return false;
            }
            string target = BaseName(declaredTexture);
            Cnc3BigVirtualAsset candidate = FindTexture(index, target);
            string reason = "Compiled manifest Texture name";
            if (candidate == null && allowTypeFallback)
            {
                target = BaseName(roadType);
                candidate = FindTexture(index, target);
                reason = "Compiled road TypeName candidate";
            }
            if (candidate == null)
            {
                result = "No matching Texture: record in manifest. Need " +
                    "actual RoadTemplate.Texture name.";
                return false;
            }
            if (candidate.binSize < 128 || candidate.binSize > 24 * 1024 * 1024)
            {
                result = "Compiled texture " + candidate.name +
                    " has no readable in-bounds payload.";
                return false;
            }
            try
            {
                byte[] bytes = Cnc3BigCompiledAssetReader.ReadAssetBytes(
                    index, candidate);
                // Only accept a structurally validated classic DDS header
                // inside THIS Texture asset's own BIN data section.
                for (int i = 0; i + 128 <= bytes.Length; i += 4)
                {
                    if (bytes[i] != 'D' || bytes[i + 1] != 'D' ||
                        bytes[i + 2] != 'S' || bytes[i + 3] != ' ') continue;
                    byte[] image = new byte[bytes.Length - i];
                    Buffer.BlockCopy(bytes, i, image, 0, image.Length);
                    var decoded = Cnc3BigTextureBridge.ReadDDS(image);
                    if (decoded != null)
                    {
                        decoded.name = "SAGE_BIG2_" + target;
                        decoded.wrapMode = TextureWrapMode.Clamp;
                        decoded.filterMode = FilterMode.Bilinear;
                        texture = decoded;
                        result = reason + ": " + candidate.name +
                            " at compiled BIN 0x" +
                            (candidate.binOffset + i).ToString("X");
                        return true;
                    }
                }
                result = "Texture: record exists (" + candidate.name +
                    ") but its BIN payload does not contain a supported " +
                    "embedded DXT1/DXT5 DDS stream. Native SAGE " +
                    "texture data decoding is still required.";
                return false;
            }
            catch (Exception ex)
            {
                result = "Compiled BIN texture read failed for " +
                    candidate.name + ": " + ex.Message;
                return false;
            }
        }

        private static Cnc3BigVirtualAsset FindTexture(
            Cnc3BigCompiledAssetIndex index, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || index.assets == null)
                return null;
            Cnc3BigVirtualAsset match = null;
            foreach (var asset in index.assets)
            {
                if (asset.name == null ||
                    !asset.name.StartsWith("Texture:",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                string baseName = BaseName(asset.name.Substring(8));
                if (!string.Equals(baseName, target,
                    StringComparison.OrdinalIgnoreCase)) continue;
                // Multiple records with the same alias are ambiguous.
                if (match != null) return null;
                match = asset;
            }
            return match;
        }

        private static string BaseName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string normalized = value.Trim().Trim('"').Replace('\\', '/');
            return Path.GetFileNameWithoutExtension(normalized);
        }
    }
}
