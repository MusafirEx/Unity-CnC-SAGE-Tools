using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Read-only v5 SAGE manifest asset index. Layout independently verified
    /// against BinaryAssetBuilder's 0x30-byte ManifestHeader and 0x2C-byte
    /// AssetEntry5 and the user's actual C&C3 map manifest.
    /// No compiled object code, scripts, relocation writes or asset execution.
    /// </summary>
    internal static class Cnc3CompiledMapBundle
    {
        internal sealed class AssetInfo
        {
            internal string Name;
            internal string Source;
            internal uint TypeId;
            internal uint InstanceId;
            internal int BinaryOffset;
            internal int BinaryLength;
            internal int ImportOffset;
            internal int ImportLength;
            internal int RelocationOffset;
            internal int RelocationLength;
            internal uint ReferenceCount;
        }

        internal sealed class AtlasTile
        {
            internal uint TextureId;
            internal int X0, Y0, X1, Y1;
        }

        internal sealed class Summary
        {
            internal bool ImpPresent;
            internal bool ReloPresent;
            internal bool ManifestPresent;
            internal int ImportAddresses;
            internal int RelocationAddresses;
            internal int ManifestGameObjects;
            internal int ManifestTextures;
            internal bool ManifestLinksBinary;
            internal int ManifestVersion;
            internal int ManifestAssetCount;
            internal int ManifestAssetsWithData;
            internal string TerrainAtlasName;
            internal bool TerrainAtlasValidated;
            internal readonly List<AtlasTile> AtlasTiles = new List<AtlasTile>();
            internal readonly List<AssetInfo> Assets = new List<AssetInfo>();
            internal readonly List<string> Warnings = new List<string>();

            internal string FindOwner(long offset)
            {
                foreach (AssetInfo item in Assets)
                    if (item.BinaryLength != 0 && offset >= item.BinaryOffset &&
                        offset < (long)item.BinaryOffset + item.BinaryLength)
                        return item.Name;
                return string.Empty;
            }
        }

        internal static Summary Inspect(string folder, long binLength)
        {
            var summary = new Summary();
            string impPath = Path.Combine(folder, "map.imp");
            string reloPath = Path.Combine(folder, "map.relo");
            string manifestPath = Path.Combine(folder, "map.manifest");
            summary.ImpPresent = File.Exists(impPath);
            summary.ReloPresent = File.Exists(reloPath);
            summary.ManifestPresent = File.Exists(manifestPath);
            if (summary.ImpPresent)
                summary.ImportAddresses = CountIndexedOffsets(impPath, binLength,
                    summary.Warnings, "map.imp");
            if (summary.ReloPresent)
                summary.RelocationAddresses = CountIndexedOffsets(reloPath, binLength,
                    summary.Warnings, "map.relo");
            if (summary.ManifestPresent)
            {
                ParseManifest(manifestPath, binLength,
                    summary.ImpPresent ? new FileInfo(impPath).Length : -1,
                    summary.ReloPresent ? new FileInfo(reloPath).Length : -1,
                    summary);
                ReadTerrainAtlas(Path.Combine(folder, "map.bin"), summary);
            }
            return summary;
        }

        private static void ParseManifest(string path, long binBytes,
            long impBytes, long reloBytes, Summary result)
        {
            var file = new FileInfo(path);
            if (file.Length < 48 || file.Length > 64L * 1024 * 1024)
            {
                result.Warnings.Add("map.manifest is not a supported length.");
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            if (Read32(data, 4) != Cnc3CompiledMapReader.Signature)
            {
                result.Warnings.Add("map.manifest signature does not match map.bin.");
                return;
            }
            int version = data[2] | (data[3] << 8);
            result.ManifestVersion = version;
            if (version != 5)
            {
                result.Warnings.Add("Only map.manifest version 5 has a verified asset-entry layout.");
                return;
            }
            uint count = Read32(data, 0x0C);
            if (count > 100000)
            {
                result.Warnings.Add("map.manifest asset count is too large.");
                return;
            }
            result.ManifestAssetCount = (int)count;
            uint declaredBinBytes = Read32(data, 0x10);
            uint referenceBytes = Read32(data, 0x20);
            uint referencedManifestNameBytes = Read32(data, 0x24);
            uint nameBytes = Read32(data, 0x28);
            uint sourceBytes = Read32(data, 0x2C);
            long entryStart = 0x30;
            long referenceStart = entryStart + 44L * count;
            long referenceManifestStart = referenceStart + referenceBytes;
            long nameStart = referenceManifestStart + referencedManifestNameBytes;
            long sourceStart = nameStart + nameBytes;
            long manifestEnd = sourceStart + sourceBytes;
            if (manifestEnd != data.Length ||
                nameStart < 0 || sourceStart < nameStart || sourceStart > data.Length)
            {
                result.Warnings.Add("Manifest name/reference buffer layout failed bounds validation.");
                return;
            }

            long binaryOffset = 4, importOffset = 4, reloOffset = 4;
            for (int i = 0; i < count; i++)
            {
                int at = checked((int)(entryStart + 44L * i));
                uint typeId = Read32(data, at);
                uint instanceId = Read32(data, at + 4);
                uint referencesOffset = Read32(data, at + 16);
                uint referencesCount = Read32(data, at + 20);
                uint nameOffset = Read32(data, at + 24);
                uint sourceOffset = Read32(data, at + 28);
                uint binarySize = Read32(data, at + 32);
                uint relocationSize = Read32(data, at + 36);
                uint importsSize = Read32(data, at + 40);

                if (nameOffset >= nameBytes || sourceOffset >= sourceBytes ||
                    referencesOffset > referenceBytes ||
                    (ulong)referencesCount * 8 > referenceBytes - referencesOffset ||
                    binaryOffset + binarySize > binBytes ||
                    (impBytes >= 0 && importOffset + importsSize > impBytes) ||
                    (reloBytes >= 0 && reloOffset + relocationSize > reloBytes))
                {
                    result.Warnings.Add("Malformed map.manifest asset record #" + i + ".");
                    result.Assets.Clear();
                    return;
                }

                string name = ReadName(data, checked((int)(nameStart + nameOffset)),
                    checked((int)(nameStart + nameBytes)));
                string source = ReadName(data, checked((int)(sourceStart + sourceOffset)),
                    checked((int)(sourceStart + sourceBytes)));
                if (name.StartsWith("GameObject:", StringComparison.Ordinal))
                    result.ManifestGameObjects++;
                if (name.StartsWith("Texture:", StringComparison.Ordinal))
                    result.ManifestTextures++;
                if (binarySize > 0)
                    result.ManifestAssetsWithData++;

                result.Assets.Add(new AssetInfo
                {
                    Name = name,
                    Source = source,
                    TypeId = typeId,
                    InstanceId = instanceId,
                    BinaryOffset = checked((int)binaryOffset),
                    BinaryLength = checked((int)binarySize),
                    RelocationOffset = checked((int)reloOffset),
                    RelocationLength = checked((int)relocationSize),
                    ImportOffset = checked((int)importOffset),
                    ImportLength = checked((int)importsSize),
                    ReferenceCount = referencesCount
                });
                binaryOffset += binarySize;
                importOffset += importsSize;
                reloOffset += relocationSize;
            }
            result.ManifestLinksBinary = declaredBinBytes == binBytes - 4 &&
                binaryOffset == binBytes &&
                (impBytes < 0 || importOffset == impBytes) &&
                (reloBytes < 0 || reloOffset == reloBytes);
            if (!result.ManifestLinksBinary)
                result.Warnings.Add("Manifest compiled section lengths don't match B/I/R data sizes.");
        }


        private static void ReadTerrainAtlas(string binPath, Summary summary)
        {
            if (!summary.ManifestLinksBinary) return;
            AssetInfo source = null;
            foreach (AssetInfo entry in summary.Assets)
                if (entry.Name.StartsWith("TerrainTextureAtlas:", StringComparison.Ordinal)
                    && entry.BinaryLength > 28)
                {
                    source = entry;
                    break;
                }
            if (source == null) return;
            summary.TerrainAtlasName = source.Name;
            using (var stream = File.OpenRead(binPath))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = source.BinaryOffset;
                byte[] header = reader.ReadBytes(28);
                if (header.Length != 28) return;
                uint count = Read32(header, 4);
                uint tilesRelative = Read32(header, 8);
                uint colorRelative = Read32(header, 12);
                uint colorLength = Read32(header, 16);
                uint normalsRelative = Read32(header, 20);
                uint normalsLength = Read32(header, 24);
                if (count < 1 || count > 4096 ||
                    tilesRelative < 28 || tilesRelative + 12L * count > colorRelative ||
                    colorRelative + (long)colorLength != normalsRelative ||
                    normalsRelative + (long)normalsLength > source.BinaryLength)
                {
                    summary.Warnings.Add("TerrainTextureAtlas subasset pointer layout is invalid.");
                    return;
                }
                if (source.BinaryOffset + (long)normalsRelative +
                    normalsLength > stream.Length)
                {
                    summary.Warnings.Add("TerrainTextureAtlas images exceed map.bin.");
                    return;
                }
                stream.Position = source.BinaryOffset + tilesRelative;
                for (int i = 0; i < count; i++)
                {
                    uint id = reader.ReadUInt32();
                    int x0 = reader.ReadUInt16();
                    int y0 = reader.ReadUInt16();
                    int x1 = reader.ReadUInt16();
                    int y1 = reader.ReadUInt16();
                    if (x0 >= x1 || y0 >= y1 || x1 > 4096 || y1 > 4096)
                    {
                        summary.Warnings.Add("TerrainTextureAtlas tile rect outside DDS bounds.");
                        summary.AtlasTiles.Clear();
                        return;
                    }
                    summary.AtlasTiles.Add(new AtlasTile
                    {
                        TextureId = id,
                        X0 = x0, Y0 = y0, X1 = x1, Y1 = y1
                    });
                }
                summary.TerrainAtlasValidated = true;
            }
        }

        private static string ReadName(byte[] data, int at, int stop)
        {
            int end = at;
            while (end < stop && data[end] != 0) end++;
            if (end >= stop || end - at > 2048)
                throw new InvalidDataException("Unterminated or oversized manifest name.");
            return Encoding.UTF8.GetString(data, at, end - at);
        }

        internal static int CountIndexedOffsets(string path, long binSize,
            List<string> warnings, string name)
        {
            var info = new FileInfo(path);
            if (info.Length < 8 || info.Length > 64L * 1024 * 1024 ||
                (info.Length & 3) != 0)
            {
                warnings.Add(name + " has an unsupported length.");
                return 0;
            }
            byte[] data = File.ReadAllBytes(path);
            if (Read32(data, 0) != Cnc3CompiledMapReader.Signature)
            {
                warnings.Add(name + " signature does not match map.bin.");
                return 0;
            }
            int count = 0;
            long previous = -1;
            bool terminated = false;
            for (int i = 4; i <= data.Length - 4; i += 4)
            {
                uint offset = Read32(data, i);
                if (offset == uint.MaxValue)
                {
                    terminated = true;
                    break;
                }
                if ((offset & 3) != 0 || offset >= binSize || offset <= previous)
                {
                    warnings.Add(name + " contains an invalid primary offset.");
                    return count;
                }
                previous = offset;
                count++;
            }
            if (!terminated)
                warnings.Add(name + " primary table is not terminated.");
            return count;
        }

        private static uint Read32(byte[] data, int offset)
        {
            return Cnc3CompiledMapReader.U32(data, offset);
        }
    }
}
