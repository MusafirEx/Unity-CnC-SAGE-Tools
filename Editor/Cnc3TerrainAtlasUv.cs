using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Direct per-cell atlas UV mapping for SAGE's 32x32 texture tile grid.
    /// Duplicates vertices along terrain cell seams so adjacent cells may
    /// sample different subrectangles of the same atlas without UV conflicts.
    /// Only base texture mapping is implemented. Blend descriptions are a
    /// separate shader-stage feature and are NOT silently approximated.
    /// </summary>
    internal static class Cnc3TerrainAtlasUv
    {
        internal sealed class Mapping
        {
            internal Texture2D BaseAtlas;
            internal Cnc3AtlasTileEntry[] AtlasRectForType;
        }

        internal static bool TryMap(Cnc3MapReader.Data map,
            Cnc3MapManifest manifest, out Mapping result, out string reason)
        {
            result = null;
            reason = null;
            if (map.TileValues == null || map.TerrainTextures.Count == 0)
            {
                reason = "Source map has no decoded BlendTileData base texture grid.";
                return false;
            }
            if (!manifest.compiledAtlasLayoutVerified ||
                manifest.compiledAtlasTiles.Count == 0)
            {
                reason = "Compiled TerrainTextureAtlas tile rectangles are not validated.";
                return false;
            }
            Texture2D atlas = null;
            foreach (var image in manifest.compiledTextures)
                if (image != null && image.texture != null &&
                    image.format == "BC3/DXT5" &&
                    image.ownerAsset == manifest.compiledAtlasAssetName)
                {
                    atlas = image.texture;
                    break;
                }
            if (atlas == null)
            {
                reason = "Decoded BC3 base DDS atlas was not found in map.bin.";
                return false;
            }

            var rectByHash = new Dictionary<uint, Cnc3AtlasTileEntry>();
            foreach (var atlasTile in manifest.compiledAtlasTiles)
            {
                if (atlasTile.x0 < 0 || atlasTile.x1 > atlas.width ||
                    atlasTile.y0 < 0 || atlasTile.y1 > atlas.height ||
                    atlasTile.x0 >= atlasTile.x1 ||
                    atlasTile.y0 >= atlasTile.y1)
                {
                    reason = "Compiled atlas tile rectangle exceeds texture bounds.";
                    return false;
                }
                rectByHash[atlasTile.textureId] = atlasTile;
            }
            var mapped = new Cnc3AtlasTileEntry[map.TerrainTextures.Count];
            for (int i = 0; i < mapped.Length; i++)
            {
                var source = map.TerrainTextures[i];
                Cnc3AtlasTileEntry atlasTile;
                if (!rectByHash.TryGetValue(source.TextureId, out atlasTile))
                {
                    reason = "Cannot resolve SAGE terrain texture '" + source.Name +
                        "' (0x" + source.TextureId.ToString("X8") + ") in compiled atlas.";
                    return false;
                }
                if ((atlasTile.x1 - atlasTile.x0) % source.TileWidth != 0 ||
                    (atlasTile.y1 - atlasTile.y0) % source.TileWidth != 0)
                {
                    reason = "Atlas tile size incompatible with " + source.Name + ".";
                    return false;
                }
                mapped[i] = atlasTile;
            }
            result = new Mapping
            {
                BaseAtlas = atlas,
                AtlasRectForType = mapped
            };
            return true;
        }

        private static float HeightAt(Cnc3MapReader.Data map, int gx,
            int gy, bool flip, float verticalScale)
        {
            gx = Mathf.Clamp(gx, 0, map.Width - 1);
            gy = Mathf.Clamp(gy, 0, map.Height - 1);
            int y = flip ? map.Height - 1 - gy : gy;
            return map.Elevations[y * map.Width + gx] * verticalScale;
        }

        private static Vector3 NormalAt(Cnc3MapReader.Data map, int gx,
            int gy, bool flip, float horizontalScale, float verticalScale)
        {
            float hL = HeightAt(map, gx - 1, gy, flip, verticalScale);
            float hR = HeightAt(map, gx + 1, gy, flip, verticalScale);
            float hD = HeightAt(map, gx, gy - 1, flip, verticalScale);
            float hU = HeightAt(map, gx, gy + 1, flip, verticalScale);
            return new Vector3(-(hR - hL) / (2f * horizontalScale),
                1f, -(hU - hD) / (2f * horizontalScale)).normalized;
        }

        internal static Mesh BuildTile(Cnc3MapReader.Data map, Mapping atlas,
            int lowerX, int lowerY, int tileX, int tileY,
            float horizontalScale, float verticalScale, bool flip)
        {
            int count = checked(tileX * tileY);
            var vertices = new Vector3[count * 4];
            var normals = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var triangles = new int[count * 6];
            int n = 0, t = 0;
            for (int y = 0; y < tileY; y++)
            for (int x = 0; x < tileX; x++)
            {
                int gx = lowerX + x, gy = lowerY + y;
                int sourceY = flip ? map.Height - 1 - gy : gy;
                ushort tileIndex = map.TileValues[sourceY * map.Width + gx];
                Cnc3MapReader.TerrainTexture info = null;
                Cnc3AtlasTileEntry rect = null;
                for (int i = 0; i < map.TerrainTextures.Count; i++)
                {
                    var candidate = map.TerrainTextures[i];
                    if (tileIndex >= candidate.TileStart &&
                        tileIndex < candidate.TileStart + candidate.TileCount)
                    {
                        info = candidate;
                        rect = atlas.AtlasRectForType[i];
                        break;
                    }
                }
                if (info == null || rect == null)
                    throw new InvalidOperationException("Invalid C&C3 terrain texture tile " + tileIndex);
                int morton = tileIndex - info.TileStart;
                int cellX = 0, cellY = 0;
                for (int bit = 0; (1 << bit) < info.TileWidth; bit++)
                {
                    cellX |= ((morton >> (2 * bit)) & 1) << bit;
                    cellY |= ((morton >> (2 * bit + 1)) & 1) << bit;
                }
                float cellWidth = (rect.x1 - rect.x0) / (float)info.TileWidth;
                float cellHeight = (rect.y1 - rect.y0) / (float)info.TileWidth;
                float atlasW = atlas.BaseAtlas.width, atlasH = atlas.BaseAtlas.height;
                const float inset = 0.25f; // Avoid filtering the adjacent atlas subtile.
                float u0 = (rect.x0 + cellX * cellWidth + inset) / atlasW;
                float u1 = (rect.x0 + (cellX + 1) * cellWidth - inset) / atlasW;
                // DDS and decoded Unity atlas both use top-row source coordinates.
                // Unity UV V increases upward; source tile offset 0 is bottom-left.
                float v0 = 1f - (rect.y1 - cellY * cellHeight - inset) / atlasH;
                float v1 = 1f - (rect.y1 - (cellY + 1) * cellHeight + inset) / atlasH;
                int baseVertex = n;
                vertices[n] = new Vector3(x * horizontalScale,
                    HeightAt(map, gx, gy, flip, verticalScale), y * horizontalScale);
                normals[n] = NormalAt(map, gx, gy, flip, horizontalScale, verticalScale);
                uv[n++] = new Vector2(u0, v0);

                vertices[n] = new Vector3(x * horizontalScale,
                    HeightAt(map, gx, gy + 1, flip, verticalScale), (y + 1) * horizontalScale);
                normals[n] = NormalAt(map, gx, gy + 1, flip, horizontalScale, verticalScale);
                uv[n++] = new Vector2(u0, v1);

                vertices[n] = new Vector3((x + 1) * horizontalScale,
                    HeightAt(map, gx + 1, gy, flip, verticalScale), y * horizontalScale);
                normals[n] = NormalAt(map, gx + 1, gy, flip, horizontalScale, verticalScale);
                uv[n++] = new Vector2(u1, v0);

                vertices[n] = new Vector3((x + 1) * horizontalScale,
                    HeightAt(map, gx + 1, gy + 1, flip, verticalScale), (y + 1) * horizontalScale);
                normals[n] = NormalAt(map, gx + 1, gy + 1, flip, horizontalScale, verticalScale);
                uv[n++] = new Vector2(u1, v1);

                triangles[t++] = baseVertex;
                triangles[t++] = baseVertex + 1;
                triangles[t++] = baseVertex + 2;
                triangles[t++] = baseVertex + 2;
                triangles[t++] = baseVertex + 1;
                triangles[t++] = baseVertex + 3;
            }
            var result = new Mesh
            {
                name = "SAGE_AtlasTerrain_" + lowerX + "_" + lowerY,
                indexFormat = IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles
            };
            result.RecalculateBounds();
            return result;
        }
    }
}
