using System;
using System.IO;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Load a standalone DDS/PNG image directly from one indexed BIG member
    /// only when actually requested by a road map. The resulting Texture2D
    /// is stored as a subasset of that imported map (no separate DDS asset).
    /// This does NOT pretend that compiled WBData.bin texture resources are
    /// loose archive members; compiled resource decoding is a distinct phase.
    /// </summary>
    internal static class Cnc3BigTextureBridge
    {
        internal static bool TryLoad(Cnc3BigArchiveIndex index, string roadType,
            string declaredTexture, bool allowTypeFallback,
            out Texture2D texture, out string matchDescription)
        {
            texture = null;
            matchDescription = "No BIG archive selected.";
            if (index == null) return false;
            if (!index.IsCurrent)
            {
                matchDescription = "BIG archive has moved or changed; rescan its index.";
                return false;
            }
            string exact = ImageKey(declaredTexture);
            Cnc3BigEntry member;
            if (!string.IsNullOrEmpty(exact) &&
                FindByBaseName(index, exact, out member))
            {
                return Load(index, member, "BIG declared RoadTemplate.Texture",
                    out texture, out matchDescription);
            }
            if (allowTypeFallback &&
                FindByBaseName(index, ImageKey(roadType), out member))
            {
                return Load(index, member, "BIG exact road-type name (candidate)",
                    out texture, out matchDescription);
            }
            matchDescription = "No standalone DDS/PNG member for " + roadType +
                " in BIG. Compiled .bin resources require separate SAGE " +
                "texture-asset decoding.";
            return false;
        }

        private static bool FindByBaseName(Cnc3BigArchiveIndex index,
            string name, out Cnc3BigEntry matching)
        {
            matching = null;
            if (string.IsNullOrEmpty(name) || index.entries == null) return false;
            foreach (Cnc3BigEntry entry in index.entries)
            {
                string path = entry.name.Replace('\\', '/');
                string ext = Path.GetExtension(path);
                if (!ext.Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ImageKey(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Duplicate base names across archive subfolders are ambiguous:
                // avoid silently loading the wrong road atlas.
                if (matching != null) return false;
                matching = entry;
            }
            return matching != null;
        }

        private static string ImageKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string fixedName = name.Replace('\\', '/').Trim().Trim('"');
            return Path.GetFileNameWithoutExtension(fixedName);
        }

        private static bool Load(Cnc3BigArchiveIndex index, Cnc3BigEntry member,
            string source, out Texture2D texture, out string message)
        {
            texture = null;
            message = "";
            try
            {
                byte[] data = Cnc3BigArchiveReader.ReadMember(index, member);
                string ext = Path.GetExtension(member.name);
                if (ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    texture = ReadDDS(data);
                else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, data, true))
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                    }
                }
                if (texture == null)
                {
                    message = "Unsupported or invalid DDS/PNG inside BIG: " + member.name;
                    return false;
                }
                texture.name = "SAGE_BIG_" +
                    Path.GetFileNameWithoutExtension(member.name.Replace('\\', '/'));
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                message = source + " -> " + member.name;
                return true;
            }
            catch (Exception ex)
            {
                message = "BIG member " + member.name + ": " + ex.Message;
                return false;
            }
        }

        private static Texture2D ReadDDS(byte[] file)
        {
            // Classic DDS header + BC1 or BC3. DX10, BC7, 3D and cube-map
            // variants are explicitly unsupported for now.
            if (file.Length < 128 || U32(file, 0) != 0x20534444 ||
                U32(file, 4) != 124 || U32(file, 76) != 32)
                return null;
            uint w = U32(file, 16), h = U32(file, 12);
            uint pixelFlags = U32(file, 80), fourcc = U32(file, 84);
            if (w == 0 || h == 0 || w > 4096 || h > 4096 ||
                (pixelFlags & 4u) == 0)
                return null;
            bool bc1 = fourcc == 0x31545844; // DXT1
            bool bc3 = fourcc == 0x35545844; // DXT5
            if (!bc1 && !bc3) return null;
            long bytesNeeded = (((long)w + 3) / 4) * (((long)h + 3) / 4) *
                (bc1 ? 8 : 16);
            if (bytesNeeded > file.Length - 128 || bytesNeeded > int.MaxValue)
                return null;
            var compressed = new byte[(int)bytesNeeded];
            Buffer.BlockCopy(file, 128, compressed, 0, compressed.Length);
            var tex = new Texture2D((int)w, (int)h,
                bc1 ? TextureFormat.DXT1 : TextureFormat.DXT5,
                false, false);
            try
            {
                tex.LoadRawTextureData(compressed);
                tex.Apply(false, true);
                return tex;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }
        }

        private static uint U32(byte[] b, int offset)
        {
            return (uint)(b[offset] | b[offset + 1] << 8 |
                b[offset + 2] << 16 | b[offset + 3] << 24);
        }
    }
}
