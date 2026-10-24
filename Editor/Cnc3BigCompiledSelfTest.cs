using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    internal static class Cnc3BigCompiledSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run BIG-2 Compiled Index Self-Test")]
        private static void Run()
        {
            string path = Path.GetTempFileName();
            var archive = ScriptableObject.CreateInstance<Cnc3BigArchiveIndex>();
            var compiled = ScriptableObject.CreateInstance<Cnc3BigCompiledAssetIndex>();
            try
            {
                const uint streamId = 0x14253647u;
                byte[] bin = new byte[12];
                WriteLe(bin, 0, streamId);
                for (int i = 4; i < 12; i++) bin[i] = (byte)(i + 20);
                byte[] manifest = CreateManifest(streamId);
                File.WriteAllBytes(path, CreatePair(bin, manifest));
                Cnc3BigArchiveReader.Scan(path, archive);
                Verify(archive.entries.Count == 2, "BIG must contain BIN+manifest");
                Cnc3BigCompiledAssetReader.Build(archive,
                    "wbdata.bin", "wbdata.manifest", compiled);
                Verify(compiled.manifestVersion == 5, "Manifest version");
                Verify(compiled.assets.Count == 1, "Manifest entry count");
                Verify(compiled.textureCount == 1, "Texture asset count");
                Verify(compiled.binaryLengthVerified, "Binary size integrity");
                var entry = Cnc3BigCompiledAssetReader.Find(compiled,
                    "Texture:RoadA");
                Verify(entry != null && entry.binOffset == 4 &&
                    entry.binSize == 8, "Asset byte range");
                byte[] payload = Cnc3BigCompiledAssetReader.ReadAssetBytes(
                    compiled, entry);
                Verify(payload.Length == 8 && payload[0] == 24 &&
                    payload[7] == 31, "Random-access decoded asset body");
                Verify(Cnc3BigCompiledAssetReader.Find(compiled,
                    "Texture:NotThere") == null, "Missing lookup");

                bool rejected = false;
                try
                {
                    Cnc3BigArchiveReader.ReadMemberRange(archive,
                        archive.entries[0], int.MaxValue, 32);
                }
                catch (InvalidDataException) { rejected = true; }
                Verify(rejected, "Out-of-bounds BIN slice rejection");
                Debug.Log("[C&C SAGE BIG2] Self-test PASS: manifest v5, " +
                    "stream ID, virtual asset offsets, bounded direct BIN access.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[C&C SAGE BIG2] Self-test FAILED: " + ex);
                throw;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(compiled);
                UnityEngine.Object.DestroyImmediate(archive);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static byte[] CreateManifest(uint id)
        {
            byte[] name = Encoding.UTF8.GetBytes("Texture:RoadA\0");
            byte[] source = Encoding.UTF8.GetBytes("test.xml\0");
            var b = new byte[48 + 44 + name.Length + source.Length];
            b[2] = 5;
            WriteLe(b, 4, id);
            WriteLe(b, 12, 1); // one AssetEntry5
            WriteLe(b, 16, 8); // BIN payload excludes the 4-byte stream ID
            WriteLe(b, 40, (uint)name.Length);
            WriteLe(b, 44, (uint)source.Length);
            WriteLe(b, 48 + 32, 8); // one 8-byte asset body
            Buffer.BlockCopy(name, 0, b, 92, name.Length);
            Buffer.BlockCopy(source, 0, b, 92 + name.Length, source.Length);
            return b;
        }

        private static byte[] CreatePair(byte[] bin, byte[] manifest)
        {
            byte[] firstName = Encoding.ASCII.GetBytes("wbdata.bin");
            byte[] secondName = Encoding.ASCII.GetBytes("wbdata.manifest");
            int dirEnd = 16 + 9 + firstName.Length + 9 + secondName.Length;
            int firstOffset = Align16(dirEnd);
            int secondOffset = Align16(firstOffset + bin.Length);
            var a = new byte[secondOffset + manifest.Length];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("BIG4"), 0, a, 0, 4);
            WriteLe(a, 4, (uint)a.Length);
            WriteBe(a, 8, 2);
            WriteBe(a, 12, (uint)dirEnd);
            int p = 16;
            WriteBe(a, p, (uint)firstOffset); WriteBe(a, p + 4, (uint)bin.Length);
            Buffer.BlockCopy(firstName, 0, a, p + 8, firstName.Length);
            p += 9 + firstName.Length;
            WriteBe(a, p, (uint)secondOffset); WriteBe(a, p + 4, (uint)manifest.Length);
            Buffer.BlockCopy(secondName, 0, a, p + 8, secondName.Length);
            Buffer.BlockCopy(bin, 0, a, firstOffset, bin.Length);
            Buffer.BlockCopy(manifest, 0, a, secondOffset, manifest.Length);
            return a;
        }

        private static int Align16(int size) { return (size + 15) & ~15; }

        private static void WriteLe(byte[] a, int p, uint v)
        {
            for (int i = 0; i < 4; i++) a[p + i] = (byte)(v >> (i * 8));
        }

        private static void WriteBe(byte[] a, int p, uint v)
        {
            for (int i = 0; i < 4; i++) a[p + i] = (byte)(v >> ((3 - i) * 8));
        }

        private static void Verify(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
        }
    }
}
