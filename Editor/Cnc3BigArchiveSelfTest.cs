using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    internal static class Cnc3BigArchiveSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run BIG Archive Self-Test")]
        private static void Run()
        {
            string file = Path.GetTempFileName();
            var archive = ScriptableObject.CreateInstance<Cnc3BigArchiveIndex>();
            try
            {
                byte[] plain = Encoding.ASCII.GetBytes("BIG-OK");
                File.WriteAllBytes(file, CreateBig("BIG4", "test.bin", plain));
                Cnc3BigArchiveReader.Scan(file, archive);
                Verify(archive.entries.Count == 1, "BIG4 directory count");
                Verify(archive.entries[0].name == "test.bin", "BIG4 member name");
                Verify(Encoding.ASCII.GetString(Cnc3BigArchiveReader.ReadMember(
                    archive, archive.entries[0])) == "BIG-OK", "BIG4 direct read");

                byte[] pack = { 0x10, 0xfb, 0x00, 0x00, 0x03,
                    0xff, 0x41, 0x42, 0x43 };
                File.WriteAllBytes(file, CreateBig("BIGF", "packed.bin", pack));
                Cnc3BigArchiveReader.Scan(file, archive);
                Verify(Encoding.ASCII.GetString(Cnc3BigArchiveReader.ReadMember(
                    archive, archive.entries[0])) == "ABC", "RefPack literal end");

                byte[] bad = CreateBig("BIG4", "../traversal.dds", plain);
                File.WriteAllBytes(file, bad);
                bool rejected = false;
                try { Cnc3BigArchiveReader.Scan(file, archive); }
                catch (InvalidDataException) { rejected = true; }
                Verify(rejected, "Reject directory traversal in BIG member");

                Debug.Log("[C&C SAGE BIG] Self-test passed: BIG4, BIGF, " +
                    "bounded RefPack and unsafe member rejection.");
            }
            catch (Exception error)
            {
                Debug.LogError("[C&C SAGE BIG] Self-test FAILED: " + error);
                throw;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(archive);
                if (File.Exists(file)) File.Delete(file);
            }
        }

        private static byte[] CreateBig(string signature, string name,
            byte[] member)
        {
            byte[] fileName = Encoding.ASCII.GetBytes(name);
            int directoryEnd = 16 + 8 + fileName.Length + 1;
            int memberStart = (directoryEnd + 15) & ~15;
            var bytes = new byte[memberStart + member.Length];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(signature), 0,
                bytes, 0, 4);
            uint length = checked((uint)bytes.Length);
            bytes[4] = (byte)length;
            bytes[5] = (byte)(length >> 8);
            bytes[6] = (byte)(length >> 16);
            bytes[7] = (byte)(length >> 24);
            WriteBe(bytes, 8, 1);
            WriteBe(bytes, 12, (uint)directoryEnd);
            WriteBe(bytes, 16, (uint)memberStart);
            WriteBe(bytes, 20, (uint)member.Length);
            Buffer.BlockCopy(fileName, 0, bytes, 24, fileName.Length);
            Buffer.BlockCopy(member, 0, bytes, memberStart, member.Length);
            return bytes;
        }

        private static void WriteBe(byte[] b, int i, uint v)
        {
            b[i] = (byte)(v >> 24);
            b[i + 1] = (byte)(v >> 16);
            b[i + 2] = (byte)(v >> 8);
            b[i + 3] = (byte)v;
        }

        private static void Verify(bool good, string message)
        {
            if (!good) throw new InvalidDataException(message);
        }
    }
}
