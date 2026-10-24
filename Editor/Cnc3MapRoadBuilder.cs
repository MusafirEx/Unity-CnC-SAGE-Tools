using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Road CONTROL-POINT GUIDE only. Source MapObject RoadType.Start/End
    /// occurs in consecutive pairs of equal TypeName. Actual textured road
    /// geometry must be resolved from external SAGE Road assets later.
    /// This builder makes safe, heightfield-draped strips for comparison.
    /// </summary>
    internal static class Cnc3MapRoadBuilder
    {
        internal sealed class RoadGroup
        {
            internal string TypeName;
            internal Mesh Mesh;
            internal int Segments;
            internal int SourceCurves;
        }

        private sealed class Accumulator
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector2> Uv = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();
            internal int Segments;
            internal int Curves;
        }

        internal static List<RoadGroup> Build(Cnc3MapReader.Data map,
            float hScale, float vScale, bool flipNorthSouth, float widthMultiplier,
            float surfaceOffset, int maximumSegments)
        {
            var output = new List<RoadGroup>();
            var grouped = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            int segments = 0;
            // SAGE stores each road piece as adjacent start/end MapObject
            // records; never connect random nearest road positions.
            for (int i = 0; i + 1 < map.Objects.Count && segments < maximumSegments; i++)
            {
                Cnc3MapReader.ObjectData start = map.Objects[i];
                Cnc3MapReader.ObjectData end = map.Objects[i + 1];
                if ((start.RoadFlags & 2u) == 0 ||
                    (end.RoadFlags & 4u) == 0 ||
                    !string.Equals(start.TypeName, end.TypeName, StringComparison.Ordinal))
                    continue;
                i++;
                string name = start.TypeName ?? "";
                var p0 = new Vector2(start.X, start.Y);
                var p1 = new Vector2(end.X, end.Y);
                float distance = Vector2.Distance(p0, p1);
                if (distance < 0.05f || distance > 2000f) continue;
                Accumulator accumulator;
                if (!grouped.TryGetValue(name, out accumulator))
                {
                    accumulator = new Accumulator();
                    grouped.Add(name, accumulator);
                }
                float sourceWidth = GuessSourceWidth(name) * widthMultiplier;
                if (sourceWidth <= 0) continue;
                Vector2 unit = (p1 - p0) / distance;
                Vector2 side = new Vector2(-unit.y, unit.x) * (sourceWidth * 0.5f);
                // Ground is sampled along each road, including both edges.
                int steps = Mathf.Clamp(Mathf.CeilToInt(distance / 20f), 1, 128);
                int first = accumulator.Vertices.Count;
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    Vector2 at = Vector2.LerpUnclamped(p0, p1, t);
                    AddRoadVertex(accumulator, map, at + side, hScale, vScale,
                        flipNorthSouth, surfaceOffset, t, 0f);
                    AddRoadVertex(accumulator, map, at - side, hScale, vScale,
                        flipNorthSouth, surfaceOffset, t, 1f);
                }
                for (int j = 0; j < steps; j++)
                {
                    int b = first + j * 2;
                    accumulator.Triangles.Add(b);
                    accumulator.Triangles.Add(b + 2);
                    accumulator.Triangles.Add(b + 1);
                    accumulator.Triangles.Add(b + 1);
                    accumulator.Triangles.Add(b + 2);
                    accumulator.Triangles.Add(b + 3);
                }
                accumulator.Segments++;
                if (((start.RoadFlags | end.RoadFlags) & (8u | 64u)) != 0)
                    accumulator.Curves++;
                segments++;
            }
            foreach (var item in grouped)
            {
                if (item.Value.Vertices.Count < 3) continue;
                // Split before UInt16 index limit: each group is bounded by
                // 4096 segment cap but may still exceed 65535 vertices.
                var mesh = new Mesh
                {
                    name = "SAGE_RoadGuides_" + Sanitize(item.Key),
                    indexFormat = item.Value.Vertices.Count > 65535
                        ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(item.Value.Vertices);
                mesh.SetUVs(0, item.Value.Uv);
                mesh.SetTriangles(item.Value.Triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                output.Add(new RoadGroup
                {
                    TypeName = item.Key,
                    Mesh = mesh,
                    Segments = item.Value.Segments,
                    SourceCurves = item.Value.Curves
                });
            }
            return output;
        }

        private static void AddRoadVertex(Accumulator group, Cnc3MapReader.Data map,
            Vector2 sageXY, float hScale, float vScale, bool flip,
            float lift, float u, float v)
        {
            float scaledX = sageXY.x * hScale / 10f;
            float scaledZ = (flip
                ? (map.Height - 1) * 10f - sageXY.y
                : sageXY.y) * hScale / 10f;
            float cellX = sageXY.x / 10f;
            float cellY = sageXY.y / 10f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cellX), 0, map.Width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(cellY), 0, map.Height - 1);
            int x1 = Mathf.Min(x0 + 1, map.Width - 1);
            int y1 = Mathf.Min(y0 + 1, map.Height - 1);
            float tx = Mathf.Clamp01(cellX - x0);
            float ty = Mathf.Clamp01(cellY - y0);
            float low = Mathf.Lerp(map.Elevations[y0 * map.Width + x0],
                map.Elevations[y0 * map.Width + x1], tx);
            float high = Mathf.Lerp(map.Elevations[y1 * map.Width + x0],
                map.Elevations[y1 * map.Width + x1], tx);
            float height = Mathf.Lerp(low, high, ty) * vScale + lift;
            group.Vertices.Add(new Vector3(scaledX, height, scaledZ));
            group.Uv.Add(new Vector2(u, v));
        }

        internal static float GuessSourceWidth(string typeName)
        {
            if (typeName.IndexOf("Marking", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2.5f;
            if (typeName.IndexOf("Sidewalk", StringComparison.OrdinalIgnoreCase) >= 0)
                return 14f;
            return 24f;
        }

        internal static string Sanitize(string text)
        {
            return (text ?? "Unnamed").Replace('/', '_').Replace('\\', '_')
                .Replace(':', '_').Replace('\n', '_');
        }
    }
}
