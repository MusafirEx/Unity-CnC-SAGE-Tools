using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Read-only, size-bounded decoder for an optional compiled SAGE map.bin.
    /// Discovers complete embedded DDS images without claiming to decode opaque
    /// compiled GameObject/W3D asset layouts. Uses source offset and DDS
    /// headers, never treats a random magic match as enough.
    /// </summary>
    internal static class Cnc3CompiledMapReader
    {
        // The first uint32 is a per-stream checksum/identifier, NOT a
        // constant format signature. It varies between compiled map bundles.
        // The matching manifest stores the same value at byte offset 4.
        // The older importer incorrectly assumed the identifier of one
        // example map (0x9CCE1238) applied to every SAGE map.
        private const int MaxBytes = 256 * 1024 * 1024;
        private const int MaxDimension = 4096;

        internal sealed class Image
        {
            internal long Offset;
            internal int Length;
            internal int Width;
            internal int Height;
            internal int Mips;
            internal string Format;
            internal int DataOffset;
            internal Texture2D Texture;
        }

        internal sealed class Result
        {
            internal uint SignatureRead;
            internal long BinarySize;
            internal int MetadataBytes;
            internal readonly List<Image> Images = new List<Image>();
            internal readonly List<string> Warnings = new List<string>();
        }

        internal static Result Read(string path, bool createUnityTextures)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 128 || file.Length > MaxBytes)
                throw new InvalidDataException("Compiled map.bin is empty or exceeds safe limits.");
            byte[] bytes = File.ReadAllBytes(path);
            var result = new Result
            {
                BinarySize = bytes.Length,
                SignatureRead = U32(bytes, 0)
            };
            if (result.SignatureRead == 0 || result.SignatureRead == uint.MaxValue)
                throw new InvalidDataException("map.bin contains an invalid stream identifier.");

            // Verify the stream against its own manifest, not the first map
            // sample's checksum. Compiled assets without a manifest may still
            // contain extractable DDS resources, but not validated atlas IDs.
            string companionManifest = Path.Combine(
                Path.GetDirectoryName(path) ?? "", "map.manifest");
            if (File.Exists(companionManifest))
            {
                using (var manifestStream = File.OpenRead(companionManifest))
                using (var manifestReader = new BinaryReader(manifestStream))
                {
                    if (manifestStream.Length < 48)
                        throw new InvalidDataException("map.manifest header is truncated.");
                    manifestStream.Position = 4;
                    uint manifestSignature = manifestReader.ReadUInt32();
                    if (manifestSignature != result.SignatureRead)
                        throw new InvalidDataException("The selected map.bin (0x" +
                            result.SignatureRead.ToString("X8") +
                            ") does not match map.manifest (0x" +
                            manifestSignature.ToString("X8") +
                            "). Ensure all files belong to the same map.");
                }
            }
            else
            {
                result.Warnings.Add("map.manifest is missing; compiled DDS can be " +
                    "extracted, but atlas/texture IDs cannot be verified.");
            }

            for (int offset = 4; offset + 128 <= bytes.Length; offset += 4)
            {
                if (U32(bytes, offset) != 0x20534444)
                    continue; // "DDS "
                Image image = DecodeDdsHeader(bytes, offset);
                if (image == null)
                    continue;
                if (result.Images.Count == 0)
                    result.MetadataBytes = offset;
                if (createUnityTextures)
                    image.Texture = DecodeTexture(bytes, image);
                result.Images.Add(image);
                offset += image.Length - 4;
            }
            if (result.Images.Count == 0)
            {
                result.MetadataBytes = bytes.Length;
                result.Warnings.Add("No supported embedded DXT5/RGB555 DDS atlas was detected. " +
                    "The compiled data header is still available for inspection.");
            }
            return result;
        }

        internal static uint U32(byte[] bytes, int at)
        {
            return (uint)(bytes[at] | bytes[at + 1] << 8 |
                bytes[at + 2] << 16 | bytes[at + 3] << 24);
        }

        private static ushort U16(byte[] bytes, int at)
        {
            return (ushort)(bytes[at] | bytes[at + 1] << 8);
        }

        private static Image DecodeDdsHeader(byte[] bytes, int at)
        {
            if (bytes.Length - at < 128 || U32(bytes, at + 4) != 124 ||
                U32(bytes, at + 76) != 32)
                return null;
            uint height = U32(bytes, at + 12);
            uint width = U32(bytes, at + 16);
            uint requestedMips = U32(bytes, at + 28);
            if (width < 1 || height < 1 || width > MaxDimension ||
                height > MaxDimension || requestedMips > 13)
                return null;
            int mips = requestedMips == 0 ? 1 : (int)requestedMips;
            uint flags = U32(bytes, at + 80);
            uint fourCc = U32(bytes, at + 84);
            string format;
            if ((flags & 0x4) != 0 && fourCc == 0x35545844) // "DXT5"
                format = "BC3/DXT5";
            else if ((flags & 0x40) != 0 && U32(bytes, at + 88) == 16 &&
                     U32(bytes, at + 92) == 0x7C00 &&
                     U32(bytes, at + 96) == 0x03E0 &&
                     U32(bytes, at + 100) == 0x001F)
                format = "RGB555";
            else
                return null;

            long compressedDataBytes = 0;
            int w = (int)width, h = (int)height;
            for (int mip = 0; mip < mips; mip++)
            {
                compressedDataBytes += format == "BC3/DXT5"
                    ? (long)((w + 3) / 4) * ((h + 3) / 4) * 16
                    : (long)w * h * 2;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            if (compressedDataBytes <= 0 ||
                compressedDataBytes > bytes.Length - (long)at - 128)
                return null;

            return new Image
            {
                Offset = at,
                Length = checked((int)(128 + compressedDataBytes)),
                DataOffset = at + 128,
                Width = (int)width,
                Height = (int)height,
                Mips = mips,
                Format = format
            };
        }

        private static Texture2D DecodeTexture(byte[] bytes, Image image)
        {
            var colors = new Color32[checked(image.Width * image.Height)];
            if (image.Format == "RGB555")
                DecodeRgb555(bytes, image, colors);
            else
                DecodeBc3(bytes, image, colors);

            var result = new Texture2D(image.Width, image.Height,
                TextureFormat.RGBA32, false, false);
            result.name = "SAGE_map_bin_" + image.Format.Replace('/', '_') +
                "_" + image.Offset.ToString("X8");
            result.SetPixels32(colors);
            result.Apply(false, true);
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Bilinear;
            return result;
        }

        private static void DecodeRgb555(byte[] b, Image img, Color32[] output)
        {
            int src = img.DataOffset;
            for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                ushort pixel = U16(b, src);
                src += 2;
                byte red5 = (byte)((pixel >> 10) & 31);
                byte green5 = (byte)((pixel >> 5) & 31);
                byte blue5 = (byte)(pixel & 31);
                output[(img.Height - 1 - y) * img.Width + x] = new Color32(
                    (byte)((red5 << 3) | (red5 >> 2)),
                    (byte)((green5 << 3) | (green5 >> 2)),
                    (byte)((blue5 << 3) | (blue5 >> 2)), 255);
            }
        }

        private static Color32 DecodeRgb565(ushort pixel)
        {
            int r = (pixel >> 11) & 31, g = (pixel >> 5) & 63, b = pixel & 31;
            return new Color32((byte)((r << 3) | (r >> 2)),
                (byte)((g << 2) | (g >> 4)),
                (byte)((b << 3) | (b >> 2)), 255);
        }

        private static void DecodeBc3(byte[] data, Image image, Color32[] output)
        {
            int baseOffset = image.DataOffset;
            int blocksWide = (image.Width + 3) / 4;
            int blocksHigh = (image.Height + 3) / 4;
            var alpha = new int[8];
            var palette = new Color32[4];
            for (int by = 0; by < blocksHigh; by++)
            for (int bx = 0; bx < blocksWide; bx++)
            {
                int at = baseOffset + (by * blocksWide + bx) * 16;
                int a0 = data[at], a1 = data[at + 1];
                alpha[0] = a0;
                alpha[1] = a1;
                if (a0 > a1)
                {
                    for (int k = 1; k <= 6; k++)
                        alpha[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                }
                else
                {
                    for (int k = 1; k <= 4; k++)
                        alpha[k + 1] = ((5 - k) * a0 + k * a1) / 5;
                    alpha[6] = 0;
                    alpha[7] = 255;
                }

                ulong alphaMask = 0;
                for (int k = 0; k < 6; k++)
                    alphaMask |= (ulong)data[at + 2 + k] << (8 * k);
                Color32 c0 = DecodeRgb565(U16(data, at + 8));
                Color32 c1 = DecodeRgb565(U16(data, at + 10));
                palette[0] = c0;
                palette[1] = c1;
                palette[2] = new Color32(
                    (byte)((2 * c0.r + c1.r) / 3),
                    (byte)((2 * c0.g + c1.g) / 3),
                    (byte)((2 * c0.b + c1.b) / 3), 255);
                palette[3] = new Color32(
                    (byte)((c0.r + 2 * c1.r) / 3),
                    (byte)((c0.g + 2 * c1.g) / 3),
                    (byte)((c0.b + 2 * c1.b) / 3), 255);
                uint pixelMask = U32(data, at + 12);
                for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                {
                    int x = bx * 4 + col, y = by * 4 + row;
                    if (x >= image.Width || y >= image.Height) continue;
                    int key = row * 4 + col;
                    Color32 rgb = palette[(int)((pixelMask >> (2 * key)) & 3)];
                    rgb.a = (byte)alpha[(int)((alphaMask >> (3 * key)) & 7)];
                    output[(image.Height - 1 - y) * image.Width + x] = rgb;
                }
            }
        }
    }
}
