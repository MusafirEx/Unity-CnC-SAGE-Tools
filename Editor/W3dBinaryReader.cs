using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    // A small, self-contained W3D binary reader. Intentionally independent of the
    // source-W3X parser/exporter; nothing in this file writes to game assets.
    internal static class W3dBinaryReader
    {
        internal sealed class FileData
        {
            internal readonly List<MeshData> Meshes = new List<MeshData>();
            internal readonly List<AnimationData> Animations = new List<AnimationData>();
            internal readonly List<string> Warnings = new List<string>();
            internal readonly List<ModelNode> ModelNodes = new List<ModelNode>();
            internal HierarchyData Hierarchy;
            internal string ModelName;
        }

        internal sealed class MeshData
        {
            internal string Name = "Mesh";
            internal string Container = "";
            internal uint Flags;
            internal int DeclaredVertices;
            internal int DeclaredTriangles;
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<int> Indices = new List<int>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<string> Textures = new List<string>();
            internal readonly List<ushort> BoneIndices = new List<ushort>();
        }

        internal sealed class HierarchyData
        {
            internal string Name;
            internal readonly List<PivotData> Pivots = new List<PivotData>();
        }

        internal sealed class PivotData
        {
            internal string Name;
            internal int ParentIndex;
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        internal sealed class AnimationData
        {
            internal string Name;
            internal string HierarchyName;
            internal int FrameCount;
            internal int FrameRate;
            internal readonly List<ChannelData> Channels = new List<ChannelData>();
        }

        internal sealed class ChannelData
        {
            internal int First;
            internal int Last;
            internal int Pivot;
            internal int Type;
            internal int VectorLength;
            internal float[] Values;
        }

        internal sealed class ModelNode
        {
            internal string MeshName;
            internal int PivotIndex;
        }

        private struct Chunk
        {
            internal uint Id;
            internal long Start;
            internal long End;
            internal bool IsContainer;
        }

        private const int MaxVertices = 300000;
        private const int MaxTriangles = 600000;
        private const int MaxPivots = 4096;
        private const int MaxFrames = 20000;
        private const int MaxChannelFloats = 2000000;

        internal static FileData Read(string path)
        {
            var result = new FileData();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, Encoding.ASCII))
            {
                while (stream.Position < stream.Length)
                {
                    Chunk chunk = Next(reader, stream.Length);
                    switch (chunk.Id)
                    {
                        case 0x000: result.Meshes.Add(ReadMesh(reader, chunk, result)); break;
                        case 0x100:
                            if (result.Hierarchy == null)
                                result.Hierarchy = ReadHierarchy(reader, chunk);
                            break;
                        case 0x200: result.Animations.Add(ReadAnimation(reader, chunk, result)); break;
                        case 0x280:
                            result.Warnings.Add("Compressed W3D animation is not decoded in this version.");
                            break;
                        case 0x300: ReadModel(reader, chunk, result); break;
                        case 0x700:
                            result.Warnings.Add("HLOD assembly is not supported yet; imported meshes may be unassembled.");
                            break;
                    }
                    stream.Position = chunk.End;
                }
            }
            return result;
        }

        private static Chunk Next(BinaryReader reader, long parentEnd)
        {
            long at = reader.BaseStream.Position;
            if (parentEnd - at < 8)
                throw new InvalidDataException("Incomplete W3D chunk header at byte " + at);
            uint type = reader.ReadUInt32();
            uint rawLength = reader.ReadUInt32();
            long length = (long)(rawLength & 0x7FFFFFFFU);
            long start = reader.BaseStream.Position;
            if (length > parentEnd - start)
                throw new InvalidDataException("W3D chunk 0x" + type.ToString("X") +
                                               " exceeds its parent at byte " + at);
            return new Chunk { Id = type, Start = start, End = start + length,
                               IsContainer = (rawLength & 0x80000000U) != 0 };
        }

        private static void Require(Chunk c, long required, string description)
        {
            if (required > c.End - c.Start)
                throw new InvalidDataException("Truncated W3D " + description);
        }

        private static int Count(Chunk c, int stride, int maximum, string label)
        {
            long size = c.End - c.Start;
            if (size % stride != 0 || size / stride > maximum)
                throw new InvalidDataException("Invalid W3D " + label + " count.");
            return (int)(size / stride);
        }

        private static string Name(BinaryReader reader, int bytes)
        {
            byte[] data = reader.ReadBytes(bytes);
            if (data.Length != bytes) throw new EndOfStreamException();
            int length = Array.IndexOf(data, (byte)0);
            return Encoding.ASCII.GetString(data, 0, length < 0 ? bytes : length);
        }

        private static Vector3 Vector(BinaryReader reader)
        {
            // SAGE: X/Y ground, Z up. Unity: X/Z ground, Y up.
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, z, y);
        }

        private static Quaternion Rotation(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return new Quaternion(-x, -z, -y, w);
        }

        private static MeshData ReadMesh(BinaryReader r, Chunk chunk, FileData file)
        {
            var mesh = new MeshData();
            while (r.BaseStream.Position < chunk.End)
            {
                Chunk sub = Next(r, chunk.End);
                switch (sub.Id)
                {
                    case 0x01F: // MeshHeader3
                        Require(sub, 48, "mesh header");
                        r.ReadUInt32(); // version
                        mesh.Flags = r.ReadUInt32();
                        mesh.Name = Name(r, 16);
                        mesh.Container = Name(r, 16);
                        mesh.DeclaredTriangles = CheckedCount(r.ReadUInt32(), MaxTriangles, "triangles");
                        mesh.DeclaredVertices = CheckedCount(r.ReadUInt32(), MaxVertices, "vertices");
                        break;
                    case 0x002: // vertices
                        for (int i = Count(sub, 12, MaxVertices, "vertices"); i > 0; --i)
                            mesh.Vertices.Add(Vector(r));
                        break;
                    case 0x003: // normals
                        for (int i = Count(sub, 12, MaxVertices, "normals"); i > 0; --i)
                            mesh.Normals.Add(Vector(r));
                        break;
                    case 0x020: // triangle: 3 indices, surface, normal, distance
                        for (int i = Count(sub, 32, MaxTriangles, "triangles"); i > 0; --i)
                        {
                            mesh.Indices.Add(CheckedCount(r.ReadUInt32(), MaxVertices, "vertex index"));
                            mesh.Indices.Add(CheckedCount(r.ReadUInt32(), MaxVertices, "vertex index"));
                            mesh.Indices.Add(CheckedCount(r.ReadUInt32(), MaxVertices, "vertex index"));
                            r.BaseStream.Position += 20;
                        }
                        break;
                    case 0x00E: // one-bone influence; not arbitrary multi-weight skinning
                        for (int i = Count(sub, 8, MaxVertices, "bone influences"); i > 0; --i)
                        {
                            mesh.BoneIndices.Add(r.ReadUInt16());
                            r.BaseStream.Position += 6;
                        }
                        break;
                    case 0x030: // textures
                    case 0x038: // material pass (UV data)
                    case 0x02A: // vertex materials
                        ReadMaterialNodes(r, sub, mesh, 0);
                        break;
                }
                r.BaseStream.Position = sub.End;
            }
            if (mesh.DeclaredVertices != 0 && mesh.DeclaredVertices != mesh.Vertices.Count)
                throw new InvalidDataException("W3D mesh " + mesh.Name + " vertex count disagrees with header.");
            if (mesh.DeclaredTriangles != 0 && mesh.DeclaredTriangles * 3 != mesh.Indices.Count)
                throw new InvalidDataException("W3D mesh " + mesh.Name + " triangle count disagrees with header.");
            for (int i = 0; i < mesh.Indices.Count; i++)
                if (mesh.Indices[i] >= mesh.Vertices.Count)
                    throw new InvalidDataException("W3D mesh " + mesh.Name + " contains an invalid triangle index.");
            if (mesh.BoneIndices.Count != 0 && mesh.BoneIndices.Count != mesh.Vertices.Count)
                file.Warnings.Add(mesh.Name + ": incomplete bone influences; rigid preview only.");
            return mesh;
        }

        private static void ReadMaterialNodes(BinaryReader r, Chunk parent, MeshData mesh, int depth)
        {
            if (depth > 12) throw new InvalidDataException("Excessive W3D material nesting.");
            if (!parent.IsContainer) return;
            while (r.BaseStream.Position < parent.End)
            {
                Chunk c = Next(r, parent.End);
                if (c.Id == 0x032) mesh.Textures.Add(Name(r, (int)(c.End - c.Start)));
                else if (c.Id == 0x04A && mesh.Uvs.Count == 0)
                {
                    for (int i = Count(c, 8, MaxVertices, "UV coordinates"); i > 0; --i)
                        mesh.Uvs.Add(new Vector2(r.ReadSingle(), 1f - r.ReadSingle()));
                }
                else if (c.IsContainer) ReadMaterialNodes(r, c, mesh, depth + 1);
                r.BaseStream.Position = c.End;
            }
        }

        private static int CheckedCount(uint value, int limit, string what)
        {
            if (value > limit) throw new InvalidDataException("W3D " + what + " exceeds safe import limit.");
            return (int)value;
        }

        private static HierarchyData ReadHierarchy(BinaryReader r, Chunk parent)
        {
            var tree = new HierarchyData();
            int declaredCount = -1;
            while (r.BaseStream.Position < parent.End)
            {
                Chunk c = Next(r, parent.End);
                switch (c.Id)
                {
                    case 0x101:
                        Require(c, 36, "hierarchy header");
                        r.ReadUInt32();
                        tree.Name = Name(r, 16);
                        declaredCount = CheckedCount(r.ReadUInt32(), MaxPivots, "pivots");
                        break;
                    case 0x102:
                        for (int i = Count(c, 60, MaxPivots, "pivots"); i > 0; --i)
                        {
                            var pivot = new PivotData();
                            pivot.Name = Name(r, 16);
                            uint parentIndex = r.ReadUInt32();
                            pivot.ParentIndex = parentIndex == uint.MaxValue ? -1 :
                                CheckedCount(parentIndex, MaxPivots, "pivot parent");
                            pivot.Position = Vector(r);
                            r.BaseStream.Position += 12; // Euler angles; quaternion is authoritative
                            pivot.Rotation = Rotation(r);
                            tree.Pivots.Add(pivot);
                        }
                        break;
                }
                r.BaseStream.Position = c.End;
            }
            if (declaredCount >= 0 && declaredCount != tree.Pivots.Count)
                throw new InvalidDataException("W3D hierarchy pivot count disagrees with header.");
            for (int i = 0; i < tree.Pivots.Count; i++)
                if (tree.Pivots[i].ParentIndex >= tree.Pivots.Count ||
                    tree.Pivots[i].ParentIndex == i)
                    throw new InvalidDataException("Invalid W3D hierarchy pivot parent.");
            return tree;
        }

        private static AnimationData ReadAnimation(BinaryReader r, Chunk parent, FileData file)
        {
            var anim = new AnimationData();
            while (r.BaseStream.Position < parent.End)
            {
                Chunk c = Next(r, parent.End);
                switch (c.Id)
                {
                    case 0x201:
                        Require(c, 44, "animation header");
                        r.ReadUInt32();
                        anim.Name = Name(r, 16);
                        anim.HierarchyName = Name(r, 16);
                        anim.FrameCount = CheckedCount(r.ReadUInt32(), MaxFrames, "animation frames");
                        anim.FrameRate = CheckedCount(r.ReadUInt32(), 1000, "animation frame rate");
                        break;
                    case 0x202:
                        Require(c, 12, "animation channel header");
                        var channel = new ChannelData();
                        channel.First = r.ReadUInt16();
                        channel.Last = r.ReadUInt16();
                        channel.VectorLength = r.ReadUInt16();
                        channel.Type = r.ReadUInt16();
                        channel.Pivot = r.ReadUInt16();
                        r.ReadUInt16();
                        int frameCount = channel.Last - channel.First + 1;
                        long dataBytes = c.End - r.BaseStream.Position;
                        if (frameCount <= 0 || channel.VectorLength <= 0 ||
                            channel.VectorLength > 4 ||
                            (long)frameCount * channel.VectorLength > MaxChannelFloats ||
                            dataBytes != (long)frameCount * channel.VectorLength * 4)
                        {
                            file.Warnings.Add("Skipped unsupported W3D animation channel in " + anim.Name);
                            break;
                        }
                        channel.Values = new float[frameCount * channel.VectorLength];
                        for (int i = 0; i < channel.Values.Length; i++)
                            channel.Values[i] = r.ReadSingle();
                        anim.Channels.Add(channel);
                        break;
                    case 0x203:
                        file.Warnings.Add("W3D visibility bit animation is not supported yet.");
                        break;
                }
                r.BaseStream.Position = c.End;
            }
            if (anim.FrameCount == 0 || anim.FrameRate == 0)
                file.Warnings.Add("Animation with invalid frame count / frame rate: " + anim.Name);
            return anim;
        }

        private static void ReadModel(BinaryReader r, Chunk parent, FileData file)
        {
            while (r.BaseStream.Position < parent.End)
            {
                Chunk c = Next(r, parent.End);
                if (c.Id == 0x301)
                {
                    Require(c, 36, "HModel header");
                    r.ReadUInt32();
                    file.ModelName = Name(r, 16);
                    Name(r, 16); // hierarchy identifier
                }
                else if (c.Id == 0x302 && c.End - c.Start >= 18)
                {
                    file.ModelNodes.Add(new ModelNode
                    {
                        MeshName = Name(r, 16),
                        PivotIndex = r.ReadUInt16()
                    });
                }
                r.BaseStream.Position = c.End;
            }
        }
    }
}
