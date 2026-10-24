using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Bounded, read-only CkMp / EAR map data reader. Only HeightMapData is
    /// decoded; other versioned sections are indexed and skipped.
    /// This intentionally does not execute map scripts or instantiate map objects.
    /// </summary>
    internal static class Cnc3MapReader
    {
        internal sealed class Data
        {
            internal int Width;
            internal int Height;
            internal int Border;
            internal ushort HeightmapVersion;
            internal ushort[] Elevations;
            internal bool Compressed;
            internal readonly List<string> SectionNames = new List<string>();
            internal readonly List<string> Warnings = new List<string>();
        }

        private const int MaxCompressedBytes = 96 * 1024 * 1024;
        private const int MaxDecodedBytes = 192 * 1024 * 1024;
        private const int MaxSamples = 4 * 1024 * 1024;

        internal static Data Read(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 8 || info.Length > MaxCompressedBytes)
                throw new InvalidDataException("C&C map source is empty or exceeds the 96 MB safety limit.");

            byte[] source = File.ReadAllBytes(path);
            bool compressed = false;
            if (Header(source, 0, "EAR\0"))
            {
                compressed = true;
                int expectedLength = BitConverter.ToInt32(source, 4);
                if (expectedLength < 4 || expectedLength > MaxDecodedBytes)
                    throw new InvalidDataException("Invalid EAR decompressed map length.");
                source = UnpackRefPack(source, 8, expectedLength);
            }

            if (!Header(source, 0, "CkMp"))
                throw new InvalidDataException("Not a supported C&C 3 World Builder .map (expected CkMp / EAR header).");

            var result = new Data { Compressed = compressed };
            using (var stream = new MemoryStream(source, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                stream.Position = 4;
                uint count = reader.ReadUInt32();
                if (count == 0 || count > 2048)
                    throw new InvalidDataException("Invalid map section-name table.");

                var names = new Dictionary<uint, string>();
                for (uint i = 0; i < count; i++)
                {
                    string name = ReadMapString(reader);
                    uint index = reader.ReadUInt32();
                    if (index < 1 || index > count || names.ContainsKey(index))
                        throw new InvalidDataException("Invalid map section index.");
                    names.Add(index, name);
                }

                while (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 10)
                        throw new InvalidDataException("Truncated C&C map section.");

                    uint type = reader.ReadUInt32();
                    ushort version = reader.ReadUInt16();
                    uint length = reader.ReadUInt32();
                    long stop = stream.Position + (long)length;
                    if (stop > stream.Length || stop < stream.Position)
                        throw new InvalidDataException("C&C map section exceeds input bounds.");
                    string name;
                    if (!names.TryGetValue(type, out name))
                        throw new InvalidDataException("Unknown C&C map section index " + type + ".");
                    result.SectionNames.Add(name + " (v" + version + ")");
                    if (name == "HeightMapData")
                    {
                        if (result.Elevations != null)
                            throw new InvalidDataException("Map contains duplicate HeightMapData sections.");
                        ReadHeights(reader, stop, version, result);
                    }
                    stream.Position = stop;
                }
            }
            if (result.Elevations == null)
                throw new InvalidDataException("C&C map has no HeightMapData section.");
            if (result.SectionNames.Count > 1)
                result.Warnings.Add("Terrain preview only: objects, roads, blend tiles, " +
                    "lighting, water and scripts are retained in the source map, but not yet visualized.");
            return result;
        }

        private static string ReadMapString(BinaryReader reader)
        {
            // Matches BinaryWriter.Write(string) and 7-bit UTF-8 byte length.
            int bytes = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                byte current = reader.ReadByte();
                if (i == 4 && current > 0x07)
                    throw new InvalidDataException("Invalid map section-name length.");
                bytes |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (bytes > 1024 || bytes > reader.BaseStream.Length - reader.BaseStream.Position)
                        throw new InvalidDataException("Map section name exceeds safe bounds.");
                    byte[] data = reader.ReadBytes(bytes);
                    if (data.Length != bytes) throw new EndOfStreamException();
                    return Encoding.UTF8.GetString(data);
                }
                shift += 7;
            }
            throw new InvalidDataException("Invalid map section-name encoding.");
        }

        private static void ReadHeights(BinaryReader reader, long stop,
            ushort version, Data output)
        {
            if (stop - reader.BaseStream.Position < 20)
                throw new InvalidDataException("Truncated C&C heightmap header.");
            uint width = reader.ReadUInt32();
            uint height = reader.ReadUInt32();
            uint border = reader.ReadUInt32();
            uint borderCount = reader.ReadUInt32();
            if (width < 2 || height < 2 || width > 2048 || height > 2048 ||
                (ulong)width * height > MaxSamples)
                throw new InvalidDataException("Map heightmap exceeds supported dimensions.");
            if (border > width / 2 || border > height / 2 || borderCount > 512)
                throw new InvalidDataException("Invalid C&C heightmap border data.");
            int borderBytes = version >= 6 ? 16 : 8;
            if ((long)borderCount * borderBytes + 4 > stop - reader.BaseStream.Position)
                throw new InvalidDataException("Truncated heightmap boundary records.");
            reader.BaseStream.Position += (long)borderCount * borderBytes;
            uint area = reader.ReadUInt32();
            if (area != width * height)
                throw new InvalidDataException("C&C heightmap sample count does not match its dimensions.");
            int count = checked((int)area);
            int byteWidth = version >= 5 ? 2 : 1;
            if ((long)count * byteWidth > stop - reader.BaseStream.Position)
                throw new InvalidDataException("C&C height samples exceed section bounds.");
            var elevation = new ushort[count];
            for (int i = 0; i < count; i++)
                elevation[i] = byteWidth == 2 ? reader.ReadUInt16() : reader.ReadByte();
            output.Width = (int)width;
            output.Height = (int)height;
            output.Border = (int)border;
            output.HeightmapVersion = version;
            output.Elevations = elevation;
            if (version < 5)
                output.Warnings.Add("Older 8-bit heightmap detected (Generals-era file). " +
                    "Use 0.625 as the vertical scale instead of the C&C 3 default.");
        }

        private static bool Header(byte[] bytes, int at, string text)
        {
            return bytes.Length >= at + text.Length &&
                bytes[at] == text[0] && bytes[at + 1] == text[1] &&
                bytes[at + 2] == text[2] && bytes[at + 3] == text[3];
        }

        private static byte[] UnpackRefPack(byte[] input, int at, int expectedSize)
        {
            // Original implementation of documented EA RefPack command semantics.
            // The RefPack payload follows EAR + uint32 expected decompressed size.
            if (at + 5 > input.Length)
                throw new InvalidDataException("Truncated RefPack payload.");
            int flags = input[at++];
            if ((flags & 0x3E) != 0x10 || input[at++] != 0xFB)
                throw new InvalidDataException("Invalid RefPack payload signature.");
            bool wide = (flags & 0x80) != 0;
            bool packedSizePresent = (flags & 0x01) != 0;
            int byteCount = wide ? 4 : 3;
            if (packedSizePresent) ReadBigEndian(input, ref at, byteCount);
            int unpackedSize = ReadBigEndian(input, ref at, byteCount);
            if (unpackedSize != expectedSize)
                throw new InvalidDataException("EAR and RefPack map sizes disagree.");

            var output = new byte[expectedSize];
            int cursor = 0;
            bool finished = false;
            while (at < input.Length && !finished)
            {
                int control = input[at++];
                int rawLength;
                int matchLength = 0;
                int matchOffset = 0;
                if (control < 0x80)
                {
                    Require(input, at, 1);
                    int b = input[at++];
                    rawLength = control & 3;
                    matchLength = ((control & 0x1C) >> 2) + 3;
                    matchOffset = ((control & 0x60) << 3) + b + 1;
                }
                else if (control < 0xC0)
                {
                    Require(input, at, 2);
                    int b = input[at++];
                    int c = input[at++];
                    rawLength = b >> 6;
                    matchLength = (control & 0x3F) + 4;
                    matchOffset = ((b & 0x3F) << 8) + c + 1;
                }
                else if (control < 0xE0)
                {
                    Require(input, at, 3);
                    int b = input[at++];
                    int c = input[at++];
                    int d = input[at++];
                    rawLength = control & 3;
                    matchLength = ((control & 0x0C) << 6) + d + 5;
                    matchOffset = ((control & 0x10) << 12) + (b << 8) + c + 1;
                }
                else if (control < 0xFC)
                {
                    rawLength = ((control & 0x1F) + 1) * 4;
                }
                else
                {
                    rawLength = control & 3;
                    finished = true;
                }

                Require(input, at, rawLength);
                if (cursor + rawLength > output.Length)
                    throw new InvalidDataException("RefPack literal bytes exceed map output.");
                Buffer.BlockCopy(input, at, output, cursor, rawLength);
                at += rawLength;
                cursor += rawLength;
                if (matchLength != 0)
                {
                    if (matchOffset < 1 || matchOffset > cursor ||
                        matchLength > output.Length - cursor)
                        throw new InvalidDataException("Invalid RefPack back-reference.");
                    for (int i = 0; i < matchLength; i++)
                    {
                        output[cursor] = output[cursor - matchOffset];
                        cursor++;
                    }
                }
            }
            if (!finished || cursor != output.Length)
                throw new InvalidDataException("RefPack map decompressed incompletely.");
            return output;
        }

        private static int ReadBigEndian(byte[] bytes, ref int offset, int count)
        {
            Require(bytes, offset, count);
            int result = 0;
            for (int i = 0; i < count; i++)
                result = checked((result << 8) | bytes[offset++]);
            return result;
        }

        private static void Require(byte[] bytes, int at, int needed)
        {
            if (needed < 0 || at < 0 || at > bytes.Length - needed)
                throw new InvalidDataException("Truncated RefPack compressed stream.");
        }
    }
}
