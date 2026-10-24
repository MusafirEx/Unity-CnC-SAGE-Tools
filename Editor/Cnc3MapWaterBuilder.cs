using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Converts SAGE StandingWaterAreas polygon coordinates into upward-facing
    /// Unity X/Z meshes. Ear-clips concave polygons instead of using unsafe
    /// triangle fans. Uses a true source water height with adjustable map scale.
    /// </summary>
    internal static class Cnc3MapWaterBuilder
    {
        private const double Epsilon = 0.0001;

        internal static bool TryBuild(Cnc3MapReader.WaterArea water,
            int mapHeight, float horizontalScale, float verticalScale,
            bool flipNorthSouth, out Mesh mesh, out string error)
        {
            mesh = null;
            error = null;
            if (water == null || water.Points == null || water.Points.Length < 3)
            {
                error = "Standing-water polygon has fewer than three points.";
                return false;
            }
            float xyScale = horizontalScale / 10f;
            // World Builder stores water Z as float game-world units.
            // Match the scale already applied to imported object coordinates.
            float verticalRatio = verticalScale / 0.0390625f;
            float waterY = water.WaterHeight * verticalRatio;
            var all = new List<Vector2>(water.Points.Length);
            foreach (Vector2 sourcePoint in water.Points)
            {
                float y = flipNorthSouth ? (mapHeight - 1) * 10f - sourcePoint.y : sourcePoint.y;
                var converted = new Vector2(sourcePoint.x * xyScale, y * xyScale);
                if (all.Count == 0 ||
                    (converted - all[all.Count - 1]).sqrMagnitude > Epsilon)
                    all.Add(converted);
            }
            if (all.Count > 3 &&
                (all[0] - all[all.Count - 1]).sqrMagnitude <= Epsilon)
                all.RemoveAt(all.Count - 1);
            if (all.Count < 3 || all.Count > 4096)
            {
                error = "Standing-water polygon is degenerate or oversized.";
                return false;
            }
            double area = SignedArea(all);
            if (Math.Abs(area) < Epsilon)
            {
                error = "Standing-water polygon has no enclosed area.";
                return false;
            }
            // Work with CCW source polygon for robust convexity tests.
            // Unity X/Y/Z with source X/Y -> Unity X/Z reverses winding:
            // CCW source triangle needs reversed order to face upward.
            var order = new List<int>(all.Count);
            if (area > 0)
            {
                for (int i = 0; i < all.Count; i++) order.Add(i);
            }
            else
            {
                for (int i = all.Count - 1; i >= 0; i--) order.Add(i);
            }
            var triangles = new List<int>((all.Count - 2) * 3);
            int guard = all.Count * all.Count;
            while (order.Count > 3 && --guard > 0)
            {
                bool found = false;
                for (int k = 0; k < order.Count; k++)
                {
                    int a = order[(k + order.Count - 1) % order.Count];
                    int b = order[k];
                    int c = order[(k + 1) % order.Count];
                    if (Cross(all[a], all[b], all[c]) <= Epsilon)
                        continue;
                    bool contains = false;
                    for (int j = 0; j < order.Count; j++)
                    {
                        int check = order[j];
                        if (check == a || check == b || check == c) continue;
                        if (PointInTriangle(all[check], all[a], all[b], all[c]))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains) continue;
                    // Reverse CCW winding to face +Y in Unity's X/Z plane.
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    order.RemoveAt(k);
                    found = true;
                    break;
                }
                if (!found) break;
            }
            if (order.Count != 3 ||
                Cross(all[order[0]], all[order[1]], all[order[2]]) <= Epsilon)
            {
                error = "Standing-water polygon could not be triangulated safely.";
                return false;
            }
            triangles.Add(order[0]); triangles.Add(order[2]); triangles.Add(order[1]);

            var vertices = new Vector3[all.Count];
            var uv = new Vector2[all.Count];
            var normals = new Vector3[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                Vector2 pt = all[i];
                vertices[i] = new Vector3(pt.x, waterY, pt.y);
                uv[i] = new Vector2(pt.x / 128f, pt.y / 128f);
                normals[i] = Vector3.up;
            }
            mesh = new Mesh
            {
                name = "SAGE_Water_" + water.UniqueId,
                indexFormat = IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles.ToArray()
            };
            mesh.RecalculateBounds();
            return true;
        }

        private static double SignedArea(List<Vector2> vertices)
        {
            double result = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 a = vertices[i];
                Vector2 b = vertices[(i + 1) % vertices.Count];
                result += (double)a.x * b.y - (double)b.x * a.y;
            }
            return result * 0.5;
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((double)b.x - a.x) * ((double)c.y - a.y) -
                   ((double)b.y - a.y) * ((double)c.x - a.x);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(a, b, p) >= -Epsilon &&
                   Cross(b, c, p) >= -Epsilon &&
                   Cross(c, a, p) >= -Epsilon;
        }
    }
}
