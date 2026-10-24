using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Bounded, read-only CkMp / EAR map data reader. Only HeightMapData is
    /// decoded together with ObjectsList; other versioned sections are skipped.
    /// This intentionally does not execute any map scripts or properties.
    /// </summary>
    internal static class Cnc3MapReader
    {
        internal sealed class WaterArea
        {
            internal uint UniqueId;
            internal string Name;
            internal string LayerName;
            internal string BumpMap;
            internal string SkyTexture;
            internal string FxShader;
            internal string DepthColors;
            internal float UvScrollSpeed;
            internal bool Additive;
            internal uint WaterHeight;
            internal UnityEngine.Vector2[] Points;
        }

        internal sealed class TerrainBlend
        {
            internal int SecondaryTile;
            internal byte Direction;
            internal byte Flags;
            internal bool TwoSided;
        }

        internal sealed class TerrainTexture
        {
            internal string Name;
            internal uint TextureId;
            internal int TileStart;
            internal int TileCount;
            internal int TileWidth;
        }

        internal sealed class ObjectData
        {
            internal string TypeName;
            internal float X;
            internal float Y;
            internal float Z;
            internal float Angle;
            internal uint RoadFlags;
            internal ushort Version;
        }

        internal sealed class Data
        {
            internal int Width;
            internal int Height;
            internal int Border;
            internal ushort HeightmapVersion;
            internal ushort[] Elevations;
            internal ushort[] TileValues;
            internal uint[] Blends;
            internal uint[] ThreeWayBlends;
            internal readonly List<TerrainBlend> BlendDescriptions = new List<TerrainBlend>();
            internal readonly List<TerrainTexture> TerrainTextures = new List<TerrainTexture>();
            internal bool Compressed;
            internal bool ReflectionEnabled;
            internal float ReflectionPlaneZ;
            internal readonly List<WaterArea> StandingWaters = new List<WaterArea>();
            internal readonly List<ObjectData> Objects = new List<ObjectData>();
            internal readonly List<string> SectionNames = new List<string>();
            internal readonly List<string> Warnings = new List<string>();
        }

        private const int MaxCompressedBytes = 96 * 1024 * 1024;
        private const int MaxDecodedBytes = 192 * 1024 * 1024;
        private const int MaxSamples = 4 * 1024 * 1024;
        private const int MaxObjects = 100000;

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
                    else if (name == "BlendTileData")
                    {
                        // An unsupported map version must not break the already
                        // functioning terrain height/ObjectsList import.
                        try
                        {
                            ReadTerrainTextureData(reader, stop, version, result);
                        }
                        catch (InvalidDataException ex)
                        {
                            result.TileValues = null;
                            result.Blends = null;
                            result.ThreeWayBlends = null;
                            result.BlendDescriptions.Clear();
                            result.TerrainTextures.Clear();
                            result.Warnings.Add("BlendTileData could not be decoded: " +
                                ex.Message + " (terrain-only fallback).");
                        }
                    }
                    else if (name == "ObjectsList")
                    {
                        ReadObjects(reader, stop, names, result);
                    }
                    else if (name == "GlobalWaterSettings")
                    {
                        ReadGlobalWater(reader, stop, result);
                    }
                    else if (name == "StandingWaterAreas")
                    {
                        ReadStandingWaters(reader, stop, version, result);
                    }
                    stream.Position = stop;
                }
            }
            if (result.Elevations == null)
                throw new InvalidDataException("C&C map has no HeightMapData section.");
            if (result.SectionNames.Count > 1)
                result.Warnings.Add("Map terrain and source objects decoded; water preview polygons are " +
                    "supported, but original SAGE reflections, roads and scripts are not reconstructed.");
            return result;
        }



        private static void ReadGlobalWater(BinaryReader reader, long stop, Data result)
        {
            if (stop - reader.BaseStream.Position < 8)
                throw new InvalidDataException("Truncated GlobalWaterSettings.");
            uint flag = reader.ReadUInt32();
            float height = reader.ReadSingle();
            if (flag > 1 || float.IsNaN(height) || float.IsInfinity(height))
                throw new InvalidDataException("Invalid global water reflection settings.");
            result.ReflectionEnabled = flag != 0;
            result.ReflectionPlaneZ = height;
        }

        private static void ReadStandingWaters(BinaryReader reader, long stop,
            ushort version, Data result)
        {
            if (version != 2)
            {
                result.Warnings.Add("Unsupported StandingWaterAreas version " + version +
                    "; water geometry skipped.");
                return;
            }
            if (stop - reader.BaseStream.Position < 4)
                throw new InvalidDataException("Truncated StandingWaterAreas header.");
            uint count = reader.ReadUInt32();
            if (count > 256)
                throw new InvalidDataException("StandingWaterAreas count exceeds safety limit.");
            for (int i = 0; i < count; i++)
            {
                if (stop - reader.BaseStream.Position < 4)
                    throw new InvalidDataException("Truncated water area ID.");
                var water = new WaterArea();
                water.UniqueId = reader.ReadUInt32();
                water.Name = ReadWaterString(reader, stop);
                water.LayerName = ReadWaterString(reader, stop);
                if (stop - reader.BaseStream.Position < 5)
                    throw new InvalidDataException("Truncated water scroll and additive data.");
                water.UvScrollSpeed = reader.ReadSingle();
                byte additive = reader.ReadByte();
                if (additive > 1 ||
                    float.IsNaN(water.UvScrollSpeed) ||
                    float.IsInfinity(water.UvScrollSpeed))
                    throw new InvalidDataException("Invalid standing-water render settings.");
                water.Additive = additive != 0;
                water.BumpMap = ReadWaterString(reader, stop);
                water.SkyTexture = ReadWaterString(reader, stop);
                if (stop - reader.BaseStream.Position < 4)
                    throw new InvalidDataException("Truncated standing-water point count.");
                uint pointCount = reader.ReadUInt32();
                if (pointCount < 3 || pointCount > 4096 ||
                    (long)pointCount * 8 + 4 > stop - reader.BaseStream.Position)
                    throw new InvalidDataException("Standing-water polygon count outside section.");
                water.Points = new UnityEngine.Vector2[pointCount];
                for (int k = 0; k < pointCount; k++)
                {
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    if (float.IsNaN(x) || float.IsNaN(y) ||
                        float.IsInfinity(x) || float.IsInfinity(y))
                        throw new InvalidDataException("Non-finite standing-water coordinates.");
                    water.Points[k] = new UnityEngine.Vector2(x, y);
                }
                water.WaterHeight = reader.ReadUInt32();
                water.FxShader = ReadWaterString(reader, stop);
                water.DepthColors = ReadWaterString(reader, stop);
                result.StandingWaters.Add(water);
            }
            if (reader.BaseStream.Position != stop)
                result.Warnings.Add("StandingWaterAreas contains " +
                    (stop - reader.BaseStream.Position) +
                    " unparsed bytes after known v2 records.");
        }

        private static string ReadWaterString(BinaryReader reader, long stop)
        {
            if (stop - reader.BaseStream.Position < 2)
                throw new InvalidDataException("Truncated water UTF-8 length.");
            ushort length = reader.ReadUInt16();
            if (length > 2048 || length > stop - reader.BaseStream.Position)
                throw new InvalidDataException("Water string exceeds source section bounds.");
            byte[] value = reader.ReadBytes(length);
            if (value.Length != length)
                throw new EndOfStreamException("Truncated standing-water string.");
            return Encoding.ASCII.GetString(value);
        }

        private static void ReadTerrainTextureData(BinaryReader reader, long stop,
            ushort version, Data output)
        {
            if (output.Elevations == null || version < 6)
            {
                output.Warnings.Add("BlendTileData encountered before terrain or has an unsupported version.");
                return;
            }
            int width = output.Width, height = output.Height;
            int count = checked(width * height);
            Stream stream = reader.BaseStream;
            if (stream.Position + 4L + count * 2L > stop)
                throw new InvalidDataException("BlendTileData has an invalid terrain grid.");
            if (reader.ReadUInt32() != count)
                throw new InvalidDataException("BlendTileData cell count differs from HeightMapData.");
            var tiles = new ushort[count];
            for (int i = 0; i < count; i++)
                tiles[i] = reader.ReadUInt16();

            // C&C3 v24 uses 16-bit blend, three-way and cliff indices.
            // Earlier version ranges 14..23 use 32-bit indices.
            int bits = version >= 14 && version < 24 ? 32 : 16;
            var blendIndices = new uint[count];
            var threeWayIndices = new uint[count];
            for (int i = 0; i < count; i++)
                blendIndices[i] = bits == 16 ? reader.ReadUInt16() : reader.ReadUInt32();
            for (int i = 0; i < count; i++)
                threeWayIndices[i] = bits == 16 ? reader.ReadUInt16() : reader.ReadUInt32();
            SkipTerrainBytes(stream, stop, (long)count * (bits / 8)); // CliffTextures
            int rowBits = ((width + 7) / 8) * height;
            if (version > 6)
            {
                int bitWidth = version == 7 ? ((width + 1) / 8) * 8 : width;
                SkipTerrainBytes(stream, stop, ((bitWidth + 7) / 8) * (long)height);
            }
            if (version >= 10) SkipTerrainBytes(stream, stop, rowBits);
            if (version >= 11) SkipTerrainBytes(stream, stop, rowBits);
            if (version >= 14 && version < 25) SkipTerrainBytes(stream, stop, rowBits);
            if (version >= 15) SkipTerrainBytes(stream, stop, rowBits);
            if (version >= 16 && version < 25) SkipTerrainBytes(stream, stop, count);
            if (version >= 17) SkipTerrainBytes(stream, stop, rowBits);
            if (version >= 24)
                SkipTerrainBytes(stream, stop, rowBits * 3L);
            if (version >= 25) SkipTerrainBytes(stream, stop, count);

            if (stop - stream.Position < 16)
                throw new InvalidDataException("BlendTileData texture index header is truncated.");
            uint textureCellCount = reader.ReadUInt32();
            uint encodedBlendCount = reader.ReadUInt32();
            uint encodedCliffCount = reader.ReadUInt32();
            uint textureCount = reader.ReadUInt32();
            if (textureCount < 1 || textureCount > 2048)
            {
                output.Warnings.Add("BlendTileData has no usable texture name table.");
                return;
            }
            int tilesIndexed = 0;
            for (int i = 0; i < textureCount; i++)
            {
                if (stop - stream.Position < 18)
                    throw new InvalidDataException("Truncated BlendTileTexture record.");
                uint cellStart = reader.ReadUInt32();
                uint cellCount = reader.ReadUInt32();
                uint cellSize = reader.ReadUInt32();
                uint magic = reader.ReadUInt32();
                ushort nameLength = reader.ReadUInt16();
                if (nameLength == 0 || nameLength > 512 ||
                    nameLength > stop - stream.Position ||
                    cellSize < 1 || cellSize > 128 ||
                    cellSize * cellSize != cellCount || magic != 0)
                    throw new InvalidDataException("Invalid BlendTileTexture metadata.");
                string name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
                int side = checked((int)(cellSize * 2));
                int span = checked(side * side);
                output.TerrainTextures.Add(new TerrainTexture
                {
                    Name = name,
                    TextureId = SageTerrainHash(name),
                    TileStart = tilesIndexed,
                    TileCount = span,
                    TileWidth = side
                });
                tilesIndexed = checked(tilesIndexed + span);
            }
            if (textureCellCount < textureCount || tilesIndexed > ushort.MaxValue + 1)
                throw new InvalidDataException("BlendTileData texture index table is corrupt.");
            foreach (ushort tile in tiles)
                if (tile >= tilesIndexed)
                    throw new InvalidDataException("A terrain cell references an unknown tile.");
            output.TileValues = tiles;

            // Source stores the number of blends plus a reserved 0 index.
            // Each record is fixed 18 bytes: u32 secondary texture-tile ID,
            // 4 direction bytes, u8 flags, u8 two-sided, and two u32 sentinels.
            int blendCount = encodedBlendCount > 0
                ? checked((int)encodedBlendCount - 1) : 0;
            if (blendCount > 65535 || (long)blendCount * 18 + 8 >
                stop - stream.Position)
                throw new InvalidDataException("Blend description table exceeds source map bounds.");
            uint magic1 = reader.ReadUInt32();
            uint magic2 = reader.ReadUInt32();
            if (magic2 != 0)
                throw new InvalidDataException("Unexpected BlendTileData texture-table marker.");
            for (int i = 0; i < blendCount; i++)
            {
                uint secondaryTile = reader.ReadUInt32();
                int direction = 0;
                for (int j = 0; j < 4; j++)
                {
                    byte flag = reader.ReadByte();
                    if (flag > 1)
                        throw new InvalidDataException("Unexpected SAGE blend direction encoding.");
                    if (flag != 0) direction |= 1 << j;
                }
                byte flags = reader.ReadByte();
                bool twoSided = reader.ReadByte() != 0;
                uint marker1 = reader.ReadUInt32();
                uint marker2 = reader.ReadUInt32();
                if (secondaryTile >= tilesIndexed || 
                    (direction != 1 && direction != 2 && direction != 4 && direction != 8) ||
                    marker2 != 0x7ADA0000)
                    throw new InvalidDataException("Unsupported BlendDescription record.");
                output.BlendDescriptions.Add(new TerrainBlend
                {
                    SecondaryTile = checked((int)secondaryTile),
                    Direction = checked((byte)direction),
                    Flags = flags,
                    TwoSided = twoSided
                });
            }
            foreach (uint blendIndex in blendIndices)
                if (blendIndex > blendCount)
                    throw new InvalidDataException("Blend grid references invalid description ID.");
            foreach (uint blendIndex in threeWayIndices)
                if (blendIndex > blendCount)
                    throw new InvalidDataException("ThreeWayBlends references invalid description ID.");
            output.Blends = blendIndices;
            output.ThreeWayBlends = threeWayIndices;
            if (encodedCliffCount > 1)
                output.Warnings.Add("CliffTextureMappings are not decoded by the terrain shader.");
        }

        private static void SkipTerrainBytes(Stream stream, long limit, long length)
        {
            if (length < 0 || length > limit - stream.Position)
                throw new InvalidDataException("Truncated BlendTileData packed grid.");
            stream.Position += length;
        }

        // SAGE FastHash, case-insensitive string-ID form. Verified against
        // all 43 source texture names and their compiled atlas TextureIDs.
        internal static uint SageTerrainHash(string name)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name.ToLowerInvariant());
            uint hash = (uint)bytes.Length;
            int at = 0;
            int remaining = bytes.Length;
            unchecked
            {
                while (remaining >= 4)
                {
                    hash += (uint)(bytes[at] | bytes[at + 1] << 8);
                    uint high = (uint)(bytes[at + 2] | bytes[at + 3] << 8);
                    hash ^= (high ^ (hash << 5)) << 11;
                    hash += hash >> 11;
                    at += 4;
                    remaining -= 4;
                }
                if (remaining == 1)
                {
                    hash += bytes[at];
                    hash ^= hash << 10;
                    hash += hash >> 1;
                }
                else if (remaining == 2)
                {
                    hash += (uint)(bytes[at] | bytes[at + 1] << 8);
                    hash ^= hash << 11;
                    hash += hash >> 17;
                }
                else if (remaining == 3)
                {
                    hash += (uint)(bytes[at] | bytes[at + 1] << 8);
                    hash ^= hash << 16;
                    hash ^= (uint)bytes[at + 2] << 18;
                    hash += hash >> 11;
                }
                hash ^= hash << 3;
                hash += hash >> 5;
                hash ^= hash << 2;
                hash += hash >> 15;
                hash ^= hash << 10;
            }
            return hash;
        }

        private static void ReadObjects(BinaryReader reader, long sectionEnd,
            Dictionary<uint, string> names, Data output)
        {
            Stream stream = reader.BaseStream;
            while (stream.Position < sectionEnd)
            {
                if (sectionEnd - stream.Position < 10)
                    throw new InvalidDataException("Truncated ObjectsList record header.");
                uint index = reader.ReadUInt32();
                ushort version = reader.ReadUInt16();
                uint size = reader.ReadUInt32();
                long end = stream.Position + (long)size;
                if (end > sectionEnd || end < stream.Position)
                    throw new InvalidDataException("Object record escapes its ObjectsList section.");
                string recordName;
                if (!names.TryGetValue(index, out recordName) || recordName != "Object")
                {
                    output.Warnings.Add("Skipping unrecognized ObjectsList record index " + index);
                    stream.Position = end;
                    continue;
                }
                if (end - stream.Position < 22)
                    throw new InvalidDataException("Truncated Object coordinates or type name.");
                var obj = new ObjectData
                {
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle(),
                    Z = reader.ReadSingle(),
                    Angle = reader.ReadSingle(),
                    RoadFlags = reader.ReadUInt32(),
                    Version = version
                };
                ushort bytes = reader.ReadUInt16();
                if (bytes > 2048 || bytes > end - stream.Position)
                    throw new InvalidDataException("Invalid Object type name length.");
                byte[] characters = reader.ReadBytes(bytes);
                if (characters.Length != bytes)
                    throw new InvalidDataException("Truncated Object type name.");
                obj.TypeName = Encoding.ASCII.GetString(characters);
                if (float.IsNaN(obj.X) || float.IsInfinity(obj.X) ||
                    float.IsNaN(obj.Y) || float.IsInfinity(obj.Y) ||
                    float.IsNaN(obj.Z) || float.IsInfinity(obj.Z) ||
                    float.IsNaN(obj.Angle) || float.IsInfinity(obj.Angle))
                    throw new InvalidDataException("Invalid Object coordinate or angle.");
                if (output.Objects.Count >= MaxObjects)
                    throw new InvalidDataException("Too many C&C3 objects (>100000).");
                output.Objects.Add(obj);

                // Preserve the raw source .map; per-object property collections
                // are not decoded in Mode 1. Each record is byte-counted.
                stream.Position = end;
            }
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
