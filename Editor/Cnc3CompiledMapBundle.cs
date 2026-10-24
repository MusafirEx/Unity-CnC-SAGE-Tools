using System;
using System.IO;
using System.Text;

namespace MusafirEx.CncSage.Editor
{
    // Read-only diagnostics for the optional B/I/M/R compiled asset bundle.
    // The index values are locations in the compiled .bin, not file offsets to
    // standalone W3X meshes. No relocation patches or object mutation occur.
    internal static class Cnc3CompiledMapBundle
    {
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
            internal readonly System.Collections.Generic.List<string> Warnings =
                new System.Collections.Generic.List<string>();
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
                var info = new FileInfo(manifestPath);
                if (info.Length < 64 || info.Length > 64 * 1024 * 1024)
                {
                    summary.Warnings.Add("map.manifest has an unsupported length.");
                    return summary;
                }
                byte[] bytes = File.ReadAllBytes(manifestPath);
                if (Read32(bytes, 4) != Cnc3CompiledMapReader.Signature)
                {
                    summary.Warnings.Add("map.manifest signature does not match map.bin.");
                    return summary;
                }
                // Confirmed from the user's sample: bytes 0x10 and 0x1C
                // respectively record map.bin/map.imp payload bytes excluding
                // their 4-byte signatures. Do not infer further layout here.
                summary.ManifestLinksBinary = Read32(bytes, 0x10) == binLength - 4;
                if (!summary.ManifestLinksBinary)
                    summary.Warnings.Add("map.manifest bin-size field does not match map.bin.");
                if (summary.ImpPresent &&
                    Read32(bytes, 0x1c) != new FileInfo(impPath).Length - 4)
                    summary.Warnings.Add("map.manifest imp-size field does not match map.imp.");

                for (int i = 0; i < bytes.Length - 16; i++)
                {
                    if (i != 0 && bytes[i - 1] != 0) continue;
                    if (IsLabel(bytes, i, "GameObject:"))
                        summary.ManifestGameObjects++;
                    else if (IsLabel(bytes, i, "Texture:"))
                        summary.ManifestTextures++;
                }
            }
            return summary;
        }

        private static bool IsLabel(byte[] bytes, int index, string target)
        {
            if (bytes.Length - index < target.Length) return false;
            for (int j = 0; j < target.Length; j++)
                if (bytes[index + j] != (byte)target[j]) return false;
            return true;
        }

        internal static int CountIndexedOffsets(string path, long binSize,
            System.Collections.Generic.List<string> warnings, string name)
        {
            var info = new FileInfo(path);
            if (info.Length < 8 || info.Length > 64 * 1024 * 1024 ||
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
                    warnings.Add(name + " contains an invalid or nonmonotonic primary offset.");
                    return count;
                }
                previous = offset;
                count++;
            }
            if (!terminated)
                warnings.Add(name + " primary address table has no terminator.");
            return count;
        }

        private static uint Read32(byte[] bytes, int offset)
        {
            return Cnc3CompiledMapReader.U32(bytes, offset);
        }
    }
}
