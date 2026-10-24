using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Index of individual compiled SAGE assets inside a pair of BIG members.
    /// No W3X/DDS files are extracted: just manifest names, types, offsets.
    /// </summary>
    public sealed class Cnc3BigCompiledAssetIndex : ScriptableObject
    {
        public Cnc3BigArchiveIndex archive;
        public string binMemberName;
        public string manifestMemberName;
        public ushort manifestVersion;
        public uint compiledStreamId;
        public uint binaryPayloadBytes;
        public int gameObjectCount;
        public int textureCount;
        public bool binaryLengthVerified;
        public string status;
        public List<Cnc3BigVirtualAsset> assets = new List<Cnc3BigVirtualAsset>();

        public bool IsCurrent
        {
            get
            {
                return archive != null && archive.IsCurrent &&
                    Cnc3BigArchiveReader.TryFind(archive, binMemberName,
                        out var ignoredBin) &&
                    Cnc3BigArchiveReader.TryFind(archive, manifestMemberName,
                        out var ignoredManifest);
            }
        }
    }

    [Serializable]
    public sealed class Cnc3BigVirtualAsset
    {
        public string name;
        public string source;
        public uint typeId;
        public uint instanceId;
        public uint referenceCount;
        public long binOffset;
        public int binSize;
        public long importOffset;
        public int importSize;
        public long relocationOffset;
        public int relocationSize;
    }

    /// <summary>
    /// Verified SAGE BinaryAssetBuilder v5 manifest: fixed 0x30 header,
    /// 0x2C per-asset entry, name/source buffers following reference buffers.
    /// Input is a pair of BIG members. No script execution or relocation.
    /// </summary>
    internal static class Cnc3BigCompiledAssetReader
    {
        private const int MaxManifestSize = 64 * 1024 * 1024;
        private const int MaxAssets = 100000;

        internal static void Build(Cnc3BigArchiveIndex archive, string binName,
            string manifestName, Cnc3BigCompiledAssetIndex output)
        {
            if (archive == null || !archive.IsCurrent || output == null)
                throw new InvalidDataException("A valid BIG index and output are required.");
            if (!Cnc3BigArchiveReader.TryFind(archive, binName, out var bin) ||
                !Cnc3BigArchiveReader.TryFind(archive, manifestName, out var manifest))
                throw new InvalidDataException("Compiled BIN/manifest member not found.");
            if (manifest.storedLength < 48 || manifest.storedLength > MaxManifestSize)
                throw new InvalidDataException("Compiled manifest exceeds the 64 MiB limit.");
            byte[] data = Cnc3BigArchiveReader.ReadMember(archive, manifest);
            if (data.Length < 48 || data.Length > MaxManifestSize)
                throw new InvalidDataException("Compiled manifest size invalid.");
            ushort version = (ushort)(data[2] | (data[3] << 8));
            if (version != 5)
                throw new NotSupportedException("Only compiled SAGE manifest v5 is validated; found v" + version);
            uint signature = U32(data, 4);
            if (signature == 0 || signature == uint.MaxValue)
                throw new InvalidDataException("Invalid compiled stream identifier.");

            // Header of the compiled BIN is a matching 4-byte stream ID.
            byte[] binHead = Cnc3BigArchiveReader.ReadMemberRange(archive, bin, 0, 4);
            if (U32(binHead, 0) != signature)
                throw new InvalidDataException("BIN and manifest compiled stream IDs do not match.");

            uint count = U32(data, 0x0c);
            uint declaredBinBytes = U32(data, 0x10);
            uint refBytes = U32(data, 0x20);
            uint externalNameBytes = U32(data, 0x24);
            uint nameBytes = U32(data, 0x28);
            uint sourceBytes = U32(data, 0x2c);
            if (count > MaxAssets)
                throw new InvalidDataException("Too many compiled assets.");
            long entryStart = 48, entryBytes = (long)count * 44;
            long namesStart = entryStart + entryBytes + refBytes + externalNameBytes;
            long sourcesStart = namesStart + nameBytes;
            long total = sourcesStart + sourceBytes;
            if (total != data.Length || namesStart < 0 ||
                sourcesStart < namesStart || sourcesStart > data.Length)
                throw new InvalidDataException("Compiled manifest buffer layout invalid.");

            var records = new List<Cnc3BigVirtualAsset>((int)count);
            long binaryCursor = 4, importCursor = 4, relocationCursor = 4;
            int textureCount = 0, gameObjectCount = 0;
            for (int i = 0; i < count; i++)
            {
                int at = checked((int)(entryStart + (long)i * 44));
                uint typeId = U32(data, at);
                uint instanceId = U32(data, at + 4);
                uint refsOffset = U32(data, at + 16);
                uint refsCount = U32(data, at + 20);
                uint nameOffset = U32(data, at + 24);
                uint sourceOffset = U32(data, at + 28);
                uint binSize = U32(data, at + 32);
                uint reloSize = U32(data, at + 36);
                uint impSize = U32(data, at + 40);
                if (nameOffset >= nameBytes || sourceOffset >= sourceBytes ||
                    refsOffset > refBytes ||
                    (ulong)refsCount * 8 > refBytes - refsOffset ||
                    binSize > int.MaxValue || reloSize > int.MaxValue ||
                    impSize > int.MaxValue ||
                    binaryCursor + binSize > (long)declaredBinBytes + 4)
                    throw new InvalidDataException("Invalid compiled asset record #" + i);
                string name = ReadName(data,
                    checked((int)(namesStart + nameOffset)),
                    checked((int)(namesStart + nameBytes)));
                string source = ReadName(data,
                    checked((int)(sourcesStart + sourceOffset)),
                    checked((int)(sourcesStart + sourceBytes)));
                if (name.StartsWith("Texture:", StringComparison.OrdinalIgnoreCase))
                    textureCount++;
                if (name.StartsWith("GameObject:", StringComparison.OrdinalIgnoreCase))
                    gameObjectCount++;
                records.Add(new Cnc3BigVirtualAsset
                {
                    name = name,
                    source = source,
                    typeId = typeId,
                    instanceId = instanceId,
                    referenceCount = refsCount,
                    binOffset = binaryCursor,
                    binSize = (int)binSize,
                    importOffset = importCursor,
                    importSize = (int)impSize,
                    relocationOffset = relocationCursor,
                    relocationSize = (int)reloSize
                });
                binaryCursor += binSize;
                importCursor += impSize;
                relocationCursor += reloSize;
            }
            if (binaryCursor != 4L + declaredBinBytes)
                throw new InvalidDataException("Manifest asset BIN offsets do not sum to declared stream size.");
            // For raw (non-RefPack) members, check actual file length. For
            // compressed BIN, only small bounded random slices can be decoded.
            bool verifiedLength = bin.storedLength == 4L + declaredBinBytes;
            if (!verifiedLength)
            {
                byte[] probe = Cnc3BigArchiveReader.ReadMemberRange(archive, bin, 0, 4);
                if (U32(probe, 0) != signature)
                    throw new InvalidDataException("Unexpected compressed BIN contents.");
            }
            output.archive = archive;
            output.manifestMemberName = manifest.name;
            output.binMemberName = bin.name;
            output.manifestVersion = version;
            output.compiledStreamId = signature;
            output.binaryPayloadBytes = declaredBinBytes;
            output.gameObjectCount = gameObjectCount;
            output.textureCount = textureCount;
            output.binaryLengthVerified = verifiedLength;
            output.assets = records;
            output.status = records.Count + " virtual SAGE assets indexed. " +
                textureCount + " Texture entries; " + gameObjectCount +
                " GameObject entries. " +
                (verifiedLength
                    ? "Binary member size verified."
                    : "BIN appears packed/has nonstandard stored length; bounded member reads only.");
        }

        internal static byte[] ReadAssetBytes(Cnc3BigCompiledAssetIndex index,
            Cnc3BigVirtualAsset asset, int maxBytes = 24 * 1024 * 1024)
        {
            if (index == null || !index.IsCurrent ||
                asset == null || index.assets == null || !index.assets.Contains(asset))
                throw new InvalidDataException("BIG compiled asset/index is unavailable.");
            if (asset.binSize < 1 || asset.binSize > maxBytes)
                throw new InvalidDataException("Compiled asset size outside read limit.");
            if (asset.binOffset < 4 || asset.binOffset + asset.binSize >
                4L + index.binaryPayloadBytes)
                throw new InvalidDataException("Compiled asset offset outside BIN payload.");
            Cnc3BigEntry member;
            if (!Cnc3BigArchiveReader.TryFind(index.archive, index.binMemberName,
                out member))
                throw new InvalidDataException("Compiled BIN member missing.");
            return Cnc3BigArchiveReader.ReadMemberRange(index.archive,
                member, asset.binOffset, asset.binSize);
        }

        internal static Cnc3BigVirtualAsset Find(Cnc3BigCompiledAssetIndex index,
            string name)
        {
            if (index == null || index.assets == null || string.IsNullOrEmpty(name))
                return null;
            foreach (var a in index.assets)
                if (string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }

        private static uint U32(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 |
                data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static string ReadName(byte[] data, int at, int limit)
        {
            if (at < 0 || at >= limit || limit > data.Length)
                throw new InvalidDataException("Invalid manifest name offset.");
            int end = at;
            while (end < limit && data[end] != 0 && end - at <= 2048) end++;
            if (end >= limit || end - at > 2048)
                throw new InvalidDataException("Unterminated or oversized compiled asset name.");
            return Encoding.UTF8.GetString(data, at, end - at);
        }
    }
}
