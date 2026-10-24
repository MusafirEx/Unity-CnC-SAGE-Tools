using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Road surfaces reconstructed from SAGE source MapObject Start/End pairs.
    /// Nodes sharing exactly the same road type and position form a network.
    /// Applies data-driven RoadWidth * RoadWidthInTexture, miter joins for
    /// two-way corners, and provisional crossing pads for 3/4-way junctions.
    /// Roads.ini / original road atlas texture are external user-supplied data.
    /// </summary>
    internal static class Cnc3RoadSurfaceBuilder
    {
        internal sealed class Result
        {
            internal readonly List<Surface> Surfaces = new List<Surface>();
            internal int Segments;
            internal int TwoWayJunctions;
            internal int Crossings;
            internal int FourWayCrossings;
            internal int UnresolvedTypes;
            internal int DuplicateSegments;
            internal int IgnoredSegments;
        }

        internal sealed class Surface
        {
            internal string TypeName;
            internal Mesh Mesh;
            internal Texture2D RoadTexture;
            internal string SourceTextureName;
            internal float RoadWidth;
            internal float WidthInTexture;
            internal bool HasDefinition;
            internal int Segments, Crossings;
        }

        private sealed class Node
        {
            internal Vector2 Pos;
            internal readonly List<Edge> Edges = new List<Edge>();
        }

        private sealed class Edge
        {
            internal Node A, B;
            internal uint FlagsA, FlagsB;
            internal float Width, TextureWidth;
            internal SurfaceAccumulator Group;
        }

        private sealed class SurfaceAccumulator
        {
            internal string Name;
            internal float Width, TextureWidth;
            internal string TextureName;
            internal Texture2D Texture;
            internal bool HasDefinition;
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector2> UV = new List<Vector2>();
            internal readonly List<Vector2> AtlasKind = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();
            internal int Segments, Crossings;
        }

        internal static Result Build(Cnc3MapReader.Data map,
            Dictionary<string, Cnc3RoadLibrary.Template> definitions,
            float hScale, float vScale, bool flip, float globalWidth,
            float lift, int maxSegments)
        {
            var result = new Result();
            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            var groups = new Dictionary<string, SurfaceAccumulator>(StringComparer.OrdinalIgnoreCase);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var edges = new List<Edge>();
            if (map.Elevations == null || map.Width < 2 || map.Height < 2)
                return result;
            globalWidth = Mathf.Clamp(globalWidth, 0.1f, 4f);
            maxSegments = Mathf.Clamp(maxSegments, 0, 20000);

            for (int i = 0; i + 1 < map.Objects.Count && edges.Count < maxSegments; i++)
            {
                var first = map.Objects[i];
                var second = map.Objects[i + 1];
                if ((first.RoadFlags & 2u) == 0 || (second.RoadFlags & 4u) == 0 ||
                    string.IsNullOrEmpty(first.TypeName) ||
                    !string.Equals(first.TypeName, second.TypeName, StringComparison.Ordinal))
                    continue;
                i++;
                var start = new Vector2(first.X, first.Y);
                var end = new Vector2(second.X, second.Y);
                float length = Vector2.Distance(start, end);
                if (!IsFinite(start) || !IsFinite(end) || length < 0.01f ||
                    length > 20000f)
                {
                    result.IgnoredSegments++;
                    continue;
                }
                string name = first.TypeName;
                string ka = NodeKey(name, start), kb = NodeKey(name, end);
                string edgeId = string.CompareOrdinal(ka, kb) < 0 ?
                    ka + "|" + kb : kb + "|" + ka;
                if (!unique.Add(edgeId))
                {
                    result.DuplicateSegments++;
                    continue;
                }
                Cnc3RoadLibrary.Template definition = null;
                bool matched = definitions != null &&
                    definitions.TryGetValue(name, out definition);
                if (!matched) definition = null;
                float roadWidth = (matched ? definition.roadWidth :
                    Cnc3MapRoadBuilder.GuessSourceWidth(name));
                float textureWidth = matched ? definition.roadWidthInTexture : 1f;
                if (!groups.TryGetValue(name, out SurfaceAccumulator group))
                {
                    group = new SurfaceAccumulator
                    {
                        Name = name,
                        Width = roadWidth * globalWidth,
                        TextureWidth = textureWidth,
                        TextureName = definition != null ? definition.sourceTextureName : "",
                        Texture = definition != null ? definition.texture : null,
                        HasDefinition = matched
                    };
                    groups.Add(name, group);
                    if (!matched) result.UnresolvedTypes++;
                }
                var a = GetNode(nodes, ka, start);
                var b = GetNode(nodes, kb, end);
                var edge = new Edge
                {
                    A = a, B = b, FlagsA = first.RoadFlags,
                    FlagsB = second.RoadFlags, Group = group,
                    Width = group.Width * group.TextureWidth,
                    TextureWidth = group.TextureWidth
                };
                a.Edges.Add(edge);
                b.Edges.Add(edge);
                edges.Add(edge);
            }

            foreach (var item in nodes.Values)
            {
                if (item.Edges.Count == 2) result.TwoWayJunctions++;
                if (item.Edges.Count >= 3)
                {
                    result.Crossings++;
                    if (item.Edges.Count == 4) result.FourWayCrossings++;
                }
            }

            foreach (Edge edge in edges)
            {
                SurfaceAccumulator acc = edge.Group;
                var direction = (edge.B.Pos - edge.A.Pos).normalized;
                Vector2 baseSide = new Vector2(-direction.y, direction.x);
                float halfWidth = Mathf.Max(0.01f, edge.Width * 0.5f);
                float length = Vector2.Distance(edge.A.Pos, edge.B.Pos);
                // At junctions with two edges, calculate a shared, bounded
                // miter direction. At higher-degree junctions crossing pads
                // take precedence over the approximate individual shoulder.
                Vector2 offsetA = GetEndpointOffset(edge.A, edge, baseSide,
                    halfWidth, length);
                Vector2 offsetB = GetEndpointOffset(edge.B, edge, baseSide,
                    halfWidth, length);
                int subdivisions = Mathf.Clamp(Mathf.CeilToInt(length / 20f), 1, 160);
                int vertexStart = acc.Vertices.Count;
                float uEnd = length / Mathf.Max(1f, 4f * acc.Width);

                for (int j = 0; j <= subdivisions; j++)
                {
                    float t = j / (float)subdivisions;
                    Vector2 center = Vector2.LerpUnclamped(edge.A.Pos, edge.B.Pos, t);
                    Vector2 side = Vector2.LerpUnclamped(offsetA, offsetB, t);
                    AddVertex(acc, map, center + side, t * uEnd, 0f,
                        0f, hScale, vScale, flip, lift);
                    AddVertex(acc, map, center - side, t * uEnd, 1f,
                        0f, hScale, vScale, flip, lift);
                }
                for (int j = 0; j < subdivisions; j++)
                {
                    int index = vertexStart + j * 2;
                    AddQuad(acc.Triangles, index, index + 1,
                        index + 2, index + 3, flip);
                }
                acc.Segments++;
                result.Segments++;
            }

            // The pads cover overlapping rectangular ends at source junctions.
            // Atlas sample is chosen for 3-way or 4-way source road layout;
            // shape and angle-specific T/Y variants remain pending Phase 5B.
            foreach (Node node in nodes.Values)
            {
                if (node.Edges.Count < 3 || node.Edges.Count > 4) continue;
                var edge = node.Edges[0];
                var acc = edge.Group;
                Vector2 axis = Outward(node, edge);
                if (axis.sqrMagnitude < 0.001f) continue;
                axis.Normalize();
                Vector2 side = new Vector2(-axis.y, axis.x);
                float radius = edge.Width * 0.68f;
                int initial = acc.Vertices.Count;
                Vector2 p0 = node.Pos - axis * radius + side * radius;
                Vector2 p1 = node.Pos - axis * radius - side * radius;
                Vector2 p2 = node.Pos + axis * radius + side * radius;
                Vector2 p3 = node.Pos + axis * radius - side * radius;
                float kind = node.Edges.Count == 4 ? 2f : 1f;
                AddVertex(acc, map, p0, 0, 0, kind, hScale, vScale, flip, lift + 0.025f);
                AddVertex(acc, map, p1, 0, 1, kind, hScale, vScale, flip, lift + 0.025f);
                AddVertex(acc, map, p2, 1, 0, kind, hScale, vScale, flip, lift + 0.025f);
                AddVertex(acc, map, p3, 1, 1, kind, hScale, vScale, flip, lift + 0.025f);
                AddQuad(acc.Triangles, initial, initial + 1, initial + 2,
                    initial + 3, flip);
                acc.Crossings++;
            }

            foreach (var kv in groups)
            {
                var acc = kv.Value;
                if (acc.Vertices.Count < 3) continue;
                var mesh = new Mesh
                {
                    name = "SAGE_RoadSurface_" + Cnc3MapRoadBuilder.Sanitize(acc.Name),
                    indexFormat = acc.Vertices.Count > 65535 ?
                        IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(acc.Vertices);
                mesh.SetUVs(0, acc.UV);
                mesh.SetUVs(1, acc.AtlasKind);
                mesh.SetTriangles(acc.Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                result.Surfaces.Add(new Surface
                {
                    TypeName = acc.Name, Mesh = mesh,
                    RoadTexture = acc.Texture,
                    SourceTextureName = acc.TextureName,
                    RoadWidth = acc.Width, WidthInTexture = acc.TextureWidth,
                    HasDefinition = acc.HasDefinition,
                    Segments = acc.Segments, Crossings = acc.Crossings
                });
            }
            return result;
        }

        private static Vector2 GetEndpointOffset(Node node, Edge edge,
            Vector2 side, float halfWidth, float length)
        {
            if (node.Edges.Count != 2)
                return side * halfWidth;
            Edge other = node.Edges[0] == edge ?
                node.Edges[1] : node.Edges[0];
            Vector2 current = Outward(node, edge);
            Vector2 otherDirection = Outward(node, other);
            Vector2 difference = current - otherDirection;
            if (difference.sqrMagnitude < 0.00001f)
                return side * halfWidth;
            difference.Normalize();
            Vector2 miter = new Vector2(-difference.y, difference.x);
            if (Vector2.Dot(miter, side) < 0f) miter = -miter;
            float dot = Mathf.Max(0.25f, Vector2.Dot(miter, side));
            float limit = Mathf.Min(halfWidth * 2.5f,
                Mathf.Max(halfWidth, length * 0.45f));
            return miter * Mathf.Min(halfWidth / dot, limit);
        }

        private static Vector2 Outward(Node node, Edge edge)
        {
            return (edge.A == node ?
                edge.B.Pos - edge.A.Pos :
                edge.A.Pos - edge.B.Pos).normalized;
        }

        private static Node GetNode(Dictionary<string, Node> nodes,
            string key, Vector2 point)
        {
            Node n;
            if (!nodes.TryGetValue(key, out n))
            {
                n = new Node { Pos = point };
                nodes.Add(key, n);
            }
            return n;
        }

        private static string NodeKey(string name, Vector2 xy)
        {
            return name + "\u001f" +
                Math.Round((double)xy.x * 1000.0) + ":" +
                Math.Round((double)xy.y * 1000.0);
        }

        private static bool IsFinite(Vector2 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                   !float.IsNaN(v.y) && !float.IsInfinity(v.y);
        }

        private static void AddQuad(List<int> triangles, int a, int b,
            int c, int d, bool flip)
        {
            if (flip)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            else
            {
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        private static void AddVertex(SurfaceAccumulator group,
            Cnc3MapReader.Data map, Vector2 sageXY,
            float u, float v, float mode, float hScale, float vScale,
            bool flip, float surfaceOffset)
        {
            float sourceGridX = sageXY.x / 10f;
            float sourceGridY = sageXY.y / 10f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceGridX), 0, map.Width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceGridY), 0, map.Height - 1);
            int x1 = Mathf.Min(x0 + 1, map.Width - 1);
            int y1 = Mathf.Min(y0 + 1, map.Height - 1);
            float fx = Mathf.Clamp01(sourceGridX - x0);
            float fy = Mathf.Clamp01(sourceGridY - y0);
            float lower = Mathf.Lerp(map.Elevations[y0 * map.Width + x0],
                map.Elevations[y0 * map.Width + x1], fx);
            float upper = Mathf.Lerp(map.Elevations[y1 * map.Width + x0],
                map.Elevations[y1 * map.Width + x1], fx);
            float height = Mathf.Lerp(lower, upper, fy) * vScale + surfaceOffset;
            float unityX = sageXY.x * hScale / 10f;
            float unityZ = (flip ? (map.Height - 1) * 10f - sageXY.y :
                sageXY.y) * hScale / 10f;
            group.Vertices.Add(new Vector3(unityX, height, unityZ));
            group.UV.Add(new Vector2(u, v));
            group.AtlasKind.Add(new Vector2(mode, 0f));
        }
    }
}
