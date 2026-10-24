using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// An index only. This asset never contains the BIG file's bytes.
    /// It can reference an archive outside Assets, avoiding thousands of
    /// imported W3X, DDS and XML objects.
    /// </summary>
    public sealed class Cnc3BigArchiveIndex : ScriptableObject
    {
        public string archivePath;
        public string archiveFourCC;
        public long archiveLength;
        public long archiveLastWriteUtcTicks;
        public int directoryBytes;
        public List<Cnc3BigEntry> entries = new List<Cnc3BigEntry>();

        public bool IsCurrent
        {
            get
            {
                if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                    return false;
                var file = new FileInfo(archivePath);
                return file.Length == archiveLength &&
                    file.LastWriteTimeUtc.Ticks == archiveLastWriteUtcTicks;
            }
        }
    }

    [Serializable]
    public sealed class Cnc3BigEntry
    {
        public string name;
        public long offset;
        public long storedLength;
    }

    /// <summary>
    /// EA SAGE BIG4/BIGF uncompressed index + bounded member reader.
    /// The header uses little-endian archive size and big-endian directory
    /// count/first-data-offset. Members may additionally use RefPack;
    /// decode is available on demand with strict size/offset limits.
    /// </summary>
    internal static class Cnc3BigArchiveReader
    {
        private const int MaxEntries = 300000;
        private const int MaxMemberName = 512;
        private const int MaxReadBytes = 96 * 1024 * 1024;
        private const int MaxUnpackedBytes = 128 * 1024 * 1024;
        private static readonly Encoding NameEncoding = Encoding.ASCII;

        internal static void Scan(string path, Cnc3BigArchiveIndex output)
        {
            if (output == null) throw new ArgumentNullException("output");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("BIG archive not found.", path);

            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, NameEncoding))
            {
                if (stream.Length < 16 || stream.Length > uint.MaxValue)
                    throw new InvalidDataException("Invalid BIG archive length.");
                string signature = NameEncoding.GetString(reader.ReadBytes(4));
                if (signature != "BIG4" && signature != "BIGF")
                    throw new InvalidDataException("Archive is not BIG4/BIGF: " + signature);

                uint declaredSize = reader.ReadUInt32(); // little endian
                uint count = ReadBigEndian32(reader);
                uint directoryEnd = ReadBigEndian32(reader);
                if (declaredSize != stream.Length)
                    throw new InvalidDataException("BIG header archive length mismatch.");
                if (count > MaxEntries || directoryEnd < 16 ||
                    directoryEnd > stream.Length ||
                    directoryEnd > 96L * 1024 * 1024)
                    throw new InvalidDataException("BIG directory exceeds safe limits.");

                var discovered = new List<Cnc3BigEntry>((int)count);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (uint i = 0; i < count; i++)
                {
                    if (stream.Position + 9 > directoryEnd)
                        throw new InvalidDataException("BIG directory entry truncated.");
                    long start = ReadBigEndian32(reader);
                    long size = ReadBigEndian32(reader);
                    string name = ReadName(reader, directoryEnd);
                    if (!SafeMemberName(name))
                        throw new InvalidDataException("Unsafe BIG entry path: " + name);
                    if (start < directoryEnd || start > stream.Length ||
                        size > stream.Length - start)
                        throw new InvalidDataException("BIG member exceeds bounds: " + name);
                    if (!names.Add(name))
                        throw new InvalidDataException("Duplicate BIG member name: " + name);
                    discovered.Add(new Cnc3BigEntry
                    {
                        name = name.Replace('\\', '/'),
                        offset = start,
                        storedLength = size
                    });
                }
                // Some later SAGE archives have an 8-byte tag/flags at the
                // end of their index; it is not a member record.
                if (stream.Position > directoryEnd)
                    throw new InvalidDataException("BIG directory overran its header.");
                var info = new FileInfo(path);
                output.archivePath = info.FullName;
                output.archiveFourCC = signature;
                output.archiveLength = stream.Length;
                output.archiveLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
                output.directoryBytes = (int)directoryEnd;
                output.entries = discovered;
            }
        }

        internal static bool TryFind(Cnc3BigArchiveIndex archive, string memberName,
            out Cnc3BigEntry entry)
        {
            entry = null;
            if (archive == null || archive.entries == null ||
                string.IsNullOrEmpty(memberName)) return false;
            string normalized = memberName.Replace('\\', '/');
            for (int i = 0; i < archive.entries.Count; i++)
            {
                var next = archive.entries[i];
                if (string.Equals(next.name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    entry = next;
                    return true;
                }
            }
            return false;
        }

        internal static byte[] ReadMember(Cnc3BigArchiveIndex archive,
            Cnc3BigEntry entry)
        {
            if (archive == null || entry == null ||
                archive.entries == null || !archive.entries.Contains(entry))
                throw new ArgumentException("Unknown BIG archive member.");
            if (!archive.IsCurrent)
                throw new InvalidDataException("BIG file has moved or changed. Rescan archive.");
            if (entry.storedLength < 0 || entry.storedLength > MaxReadBytes)
                throw new InvalidDataException("This member exceeds the on-demand memory limit.");

            using (var file = File.Open(archive.archivePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                if (entry.offset < archive.directoryBytes ||
                    entry.offset > file.Length ||
                    entry.storedLength > file.Length - entry.offset)
                    throw new InvalidDataException("BIG member offset is invalid.");
                file.Position = entry.offset;
                var stored = new byte[checked((int)entry.storedLength)];
                ReadFully(file, stored, stored.Length);
                if (Cnc3RefPack.IsRefPack(stored))
                    return Cnc3RefPack.Unpack(stored, MaxUnpackedBytes);
                return stored;
            }
        }


        /// <summary>
        /// Bounded random-access read of an archive member. Uncompressed BIG
        /// members are read by seeking directly into the original BIG file;
        /// the entire WBData.bin is never loaded into memory. Compressed members
        /// use the existing bounded RefPack decoder and are rejected if too big.
        /// </summary>
        internal static byte[] ReadMemberRange(Cnc3BigArchiveIndex archive,
            Cnc3BigEntry entry, long relativeOffset, int length)
        {
            if (archive == null || entry == null || archive.entries == null ||
                !archive.entries.Contains(entry) || !archive.IsCurrent)
                throw new InvalidDataException("BIG archive/index is missing or stale.");
            if (relativeOffset < 0 || length < 0 || length > 24 * 1024 * 1024)
                throw new InvalidDataException("BIG requested slice exceeds the 24 MiB limit.");
            if (entry.offset < archive.directoryBytes || entry.storedLength < 0 ||
                entry.offset > archive.archiveLength ||
                entry.storedLength > archive.archiveLength - entry.offset)
                throw new InvalidDataException("BIG member's position is invalid.");
            using (var file = File.Open(archive.archivePath,
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Position = entry.offset;
                byte[] probe = new byte[(int)Math.Min(16L, entry.storedLength)];
                ReadFully(file, probe, probe.Length);
                if (Cnc3RefPack.IsRefPack(probe))
                {
                    if (entry.storedLength > MaxReadBytes)
                        throw new InvalidDataException("Compressed BIG stream too large for bounded access.");
                    byte[] inflated = ReadMember(archive, entry);
                    if (relativeOffset > inflated.Length ||
                        length > inflated.Length - relativeOffset)
                        throw new InvalidDataException("BIG decoded slice outside member bounds.");
                    var slice = new byte[length];
                    Buffer.BlockCopy(inflated, checked((int)relativeOffset),
                        slice, 0, length);
                    return slice;
                }
                if (relativeOffset > entry.storedLength ||
                    length > entry.storedLength - relativeOffset)
                    throw new InvalidDataException("BIG slice outside member bounds.");
                file.Position = entry.offset + relativeOffset;
                byte[] result = new byte[length];
                ReadFully(file, result, length);
                return result;
            }
        }

        internal static void ExportMember(Cnc3BigArchiveIndex archive,
            Cnc3BigEntry entry, string destination)
        {
            if (string.IsNullOrEmpty(destination))
                throw new ArgumentException("Output file must be specified.");
            if (archive == null || entry == null || archive.entries == null ||
                !archive.entries.Contains(entry) || !archive.IsCurrent)
                throw new InvalidDataException("BIG index is stale or member is invalid.");
            string folder = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("Output folder unavailable.");
            if (entry.offset < archive.directoryBytes || entry.storedLength < 0 ||
                entry.offset > archive.archiveLength ||
                entry.storedLength > archive.archiveLength - entry.offset)
                throw new InvalidDataException("BIG member range is invalid.");

            // Stream ordinary big members in bounded chunks. RefPack members
            // need decompression and still observe the independent memory cap.
            using (var file = File.Open(archive.archivePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite))
            {
                file.Position = entry.offset;
                byte[] probe = new byte[(int)Math.Min(16L, entry.storedLength)];
                ReadFully(file, probe, probe.Length);
                bool compressed = Cnc3RefPack.IsRefPack(probe);
                if (compressed && entry.storedLength > MaxReadBytes)
                    throw new InvalidDataException(
                        "Compressed BIG member exceeds 96 MiB decode limit.");
                byte[] unpacked = compressed ? ReadMember(archive, entry) : null;
                file.Position = entry.offset;
                try
                {
                    using (var dest = new FileStream(destination, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        if (unpacked != null)
                            dest.Write(unpacked, 0, unpacked.Length);
                        else
                        {
                            var buffer = new byte[64 * 1024];
                            long remaining = entry.storedLength;
                            while (remaining > 0)
                            {
                                int part = (int)Math.Min(buffer.Length, remaining);
                                int count = file.Read(buffer, 0, part);
                                if (count <= 0) throw new EndOfStreamException();
                                dest.Write(buffer, 0, count);
                                remaining -= count;
                            }
                        }
                    }
                }
                catch
                {
                    // Do not leave a half-extracted resource behind.
                    if (File.Exists(destination)) File.Delete(destination);
                    throw;
                }
            }
        }

        private static void ReadFully(Stream stream, byte[] data, int count)
        {
            int done = 0;
            while (done < count)
            {
                int read = stream.Read(data, done, count - done);
                if (read <= 0) throw new EndOfStreamException();
                done += read;
            }
        }

        private static uint ReadBigEndian32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) | bytes[3];
        }

        private static string ReadName(BinaryReader reader, long directoryEnd)
        {
            var bytes = new List<byte>(64);
            while (reader.BaseStream.Position < directoryEnd &&
                bytes.Count < MaxMemberName)
            {
                byte b = reader.ReadByte();
                if (b == 0) return NameEncoding.GetString(bytes.ToArray());
                bytes.Add(b);
            }
            throw new InvalidDataException("BIG member name exceeds directory or length limit.");
        }

        private static bool SafeMemberName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) ||
                raw.StartsWith("/", StringComparison.Ordinal) ||
                raw.StartsWith("\\", StringComparison.Ordinal) ||
                raw.Contains(":") || raw.IndexOf('\0') >= 0)
                return false;
            string[] parts = raw.Replace('\\', '/').Split('/');
            foreach (string part in parts)
                if (part == "." || part == ".." || part.Length == 0)
                    return false;
            return true;
        }
    }

    /// <summary>
    /// Bounded RefPack member decoder. This is an independent implementation
    /// of the public EA command format; output size and back references are
    /// checked before every copy. Rejects truncated, malformed inputs.
    /// </summary>
    internal static class Cnc3RefPack
    {
        internal static bool IsRefPack(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7) return false;
            int tag = (bytes[0] << 8) | bytes[1];
            return tag == 0x10fb || tag == 0x11fb ||
                tag == 0x90fb || tag == 0x91fb;
        }

        internal static byte[] Unpack(byte[] packed, int maximumOutput)
        {
            if (!IsRefPack(packed))
                throw new InvalidDataException("Not a supported RefPack header.");
            int marker = (packed[0] << 8) | packed[1];
            int bytesInLength = (marker & 0x8000) != 0 ? 4 : 3;
            int pos = 2;
            if ((marker & 0x100) != 0)
                pos += bytesInLength;
            if (packed.Length - pos < bytesInLength)
                throw new InvalidDataException("RefPack has no output-length header.");
            long required = 0;
            for (int i = 0; i < bytesInLength; i++)
                required = (required << 8) | packed[pos++];
            if (required < 0 || required > maximumOutput)
                throw new InvalidDataException("RefPack output exceeds memory limit.");
            var output = new byte[(int)required];
            int written = 0;
            bool finished = false;
            while (pos < packed.Length)
            {
                int token = packed[pos++];
                int literal, copyLength = 0, distance = 0;
                if (token < 0x80)
                {
                    if (packed.Length - pos < 1) throw new EndOfStreamException();
                    int next = packed[pos++];
                    literal = token & 3;
                    copyLength = ((token >> 2) & 7) + 3;
                    distance = (((token >> 5) & 3) << 8) + next + 1;
                }
                else if (token < 0xc0)
                {
                    if (packed.Length - pos < 2) throw new EndOfStreamException();
                    int a = packed[pos++], b = packed[pos++];
                    literal = a >> 6;
                    copyLength = (token & 0x3f) + 4;
                    distance = ((a & 0x3f) << 8) + b + 1;
                }
                else if (token < 0xe0)
                {
                    if (packed.Length - pos < 3) throw new EndOfStreamException();
                    int a = packed[pos++], b = packed[pos++], c = packed[pos++];
                    literal = token & 3;
                    copyLength = (((token >> 2) & 3) << 8) + c + 5;
                    distance = (((token >> 4) & 1) << 16) + (a << 8) + b + 1;
                }
                else if (token < 0xfc)
                {
                    literal = ((token & 31) << 2) + 4;
                }
                else
                {
                    literal = token & 3;
                    finished = true;
                }
                if (literal > packed.Length - pos || literal > output.Length - written)
                    throw new InvalidDataException("RefPack literal runs beyond stream.");
                Buffer.BlockCopy(packed, pos, output, written, literal);
                pos += literal;
                written += literal;
                if (copyLength != 0)
                {
                    if (distance < 1 || distance > written ||
                        copyLength > output.Length - written)
                        throw new InvalidDataException("Invalid RefPack back reference.");
                    // Overlapping copy is significant for LZ dictionary expansion.
                    for (int i = 0; i < copyLength; i++)
                        output[written + i] = output[written + i - distance];
                    written += copyLength;
                }
                if (finished) break;
            }
            if (!finished || written != output.Length)
                throw new InvalidDataException("RefPack terminator or size mismatch.");
            return output;
        }
    }
}
