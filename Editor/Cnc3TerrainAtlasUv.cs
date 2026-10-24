using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Map each C&C3 terrain base/secondary/tertiary tile to one packed DDS
    /// atlas; encode blend direction and flags for SAGE shader use.
    /// The asset is always read only; source tiles remain unmodified.
    /// </summary>
    internal static class Cnc3TerrainAtlasUv
    {
        internal struct TileUvs
        {
            internal Vector2 BL;
            internal Vector2 TL;
            internal Vector2 BR;
            internal Vector2 TR;
        }

        internal sealed class Mapping
        {
            internal Texture2D BaseAtlas;
            internal Cnc3AtlasTileEntry[] AtlasRectForType;
            internal TileUvs[] TileUvLookup;
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
            {
                if (image != null && image.texture != null &&
                    image.format == "BC3/DXT5" &&
                    image.ownerAsset == manifest.compiledAtlasAssetName)
                {
                    atlas = image.texture;
                    break;
                }
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
                    reason = "Compiled atlas rectangle exceeds DDS image.";
                    return false;
                }
                if (rectByHash.ContainsKey(atlasTile.textureId))
                {
                    reason = "Duplicate TerrainTextureAtlas TextureID.";
                    return false;
                }
                rectByHash.Add(atlasTile.textureId, atlasTile);
            }
            var mapped = new Cnc3AtlasTileEntry[map.TerrainTextures.Count];
            int tileCount = 0;
            for (int i = 0; i < mapped.Length; i++)
            {
                Cnc3MapReader.TerrainTexture source = map.TerrainTextures[i];
                if (!rectByHash.TryGetValue(source.TextureId, out mapped[i]))
                {
                    reason = "Missing SAGE texture hash " + source.Name +
                        " (0x" + source.TextureId.ToString("X8") + ") in atlas.";
                    return false;
                }
                Cnc3AtlasTileEntry rect = mapped[i];
                if (rect.x0 < 0 || rect.y0 < 0 ||
                    rect.x1 > atlas.width || rect.y1 > atlas.height ||
                    (rect.x1 - rect.x0) % source.TileWidth != 0 ||
                    (rect.y1 - rect.y0) % source.TileWidth != 0)
                {
                    reason = "Atlas cell size incompatible with " + source.Name + ".";
                    return false;
                }
                tileCount = Math.Max(tileCount, source.TileStart + source.TileCount);
            }
            if (tileCount < 1 || tileCount > 65536)
            {
                reason = "Too many source texture subtiles.";
                return false;
            }

            // Every tile index is resolved once, not repeatedly during mesh generation.
            // Offsets use Morton/Z-order exactly as SAGE BlendTileTextureIndex.
            var uvByTile = new TileUvs[tileCount];
            for (int i = 0; i < mapped.Length; i++)
            {
                Cnc3MapReader.TerrainTexture source = map.TerrainTextures[i];
                Cnc3AtlasTileEntry rect = mapped[i];
                float pixelWidth = (rect.x1 - rect.x0) / (float)source.TileWidth;
                float pixelHeight = (rect.y1 - rect.y0) / (float)source.TileWidth;
                for (int local = 0; local < source.TileCount; local++)
                {
                    int x = 0, y = 0;
                    for (int bit = 0; (1 << bit) < source.TileWidth; bit++)
                    {
                        x |= ((local >> (2 * bit)) & 1) << bit;
                        y |= ((local >> (2 * bit + 1)) & 1) << bit;
                    }
                    const float inset = 0.25f;
                    float u0 = (rect.x0 + x * pixelWidth + inset) / atlas.width;
                    float u1 = (rect.x0 + (x + 1) * pixelWidth - inset) / atlas.width;
                    // Source DDS row 0 is the top image row; Unity V is upward.
                    float v0 = 1f - (rect.y1 - y * pixelHeight - inset) / atlas.height;
                    float v1 = 1f - (rect.y1 - (y + 1) * pixelHeight + inset) / atlas.height;
                    uvByTile[source.TileStart + local] = new TileUvs
                    {
                        BL = new Vector2(u0, v0),
                        TL = new Vector2(u0, v1),
                        BR = new Vector2(u1, v0),
                        TR = new Vector2(u1, v1)
                    };
                }
            }
            result = new Mapping
            {
                BaseAtlas = atlas,
                AtlasRectForType = mapped,
                TileUvLookup = uvByTile
            };
            return true;
        }

        private static float HeightAt(Cnc3MapReader.Data map, int x, int y,
            bool flip, float verticalScale)
        {
            x = Mathf.Clamp(x, 0, map.Width - 1);
            y = Mathf.Clamp(y, 0, map.Height - 1);
            int sourceY = flip ? map.Height - 1 - y : y;
            return map.Elevations[sourceY * map.Width + x] * verticalScale;
        }

        private static Vector3 NormalAt(Cnc3MapReader.Data map, int gx,
            int gy, bool flip, float hScale, float vScale)
        {
            float l = HeightAt(map, gx - 1, gy, flip, vScale);
            float r = HeightAt(map, gx + 1, gy, flip, vScale);
            float d = HeightAt(map, gx, gy - 1, flip, vScale);
            float u = HeightAt(map, gx, gy + 1, flip, vScale);
            return new Vector3(-(r - l) / (2f * hScale),
                1f, -(u - d) / (2f * hScale)).normalized;
        }

        internal static Mesh BuildTile(Cnc3MapReader.Data map, Mapping atlas,
            int lowerX, int lowerY, int tileX, int tileY,
            float horizontalScale, float verticalScale, bool flip,
            bool renderBlends, bool renderCliffs)
        {
            bool validCliffData = renderCliffs && map.CliffTextureIndices != null &&
                map.CliffMappings.Count > 0;
            bool validBlendData = renderBlends && map.Blends != null &&
                map.ThreeWayBlends != null && map.BlendDescriptions.Count > 0;
            int count = checked(tileX * tileY);
            var vertices = new Vector3[count * 4];
            var normals = new Vector3[count * 4];
            var baseUv = new Vector2[count * 4];
            var secondaryUv = validBlendData ? new Vector2[count * 4] : null;
            var tertiaryUv = validBlendData ? new Vector2[count * 4] : null;
            var directions = validBlendData ? new Vector4[count * 4] : null;
            var fraction = validBlendData ? new Vector2[count * 4] : null;
            var cliffUV = validCliffData ? new Vector2[count * 4] : null;
            var cliffRects = validCliffData ? new Vector4[count * 4] : null;
            var cliffSizes = validCliffData ? new Vector2[count * 4] : null;
            var triangles = new int[count * 6];
            int vertexIndex = 0, triIndex = 0;
            for (int y = 0; y < tileY; y++)
            for (int x = 0; x < tileX; x++)
            {
                int gx = lowerX + x, gy = lowerY + y;
                int sourceY = flip ? map.Height - 1 - gy : gy;
                int sourceIndex = checked(sourceY * map.Width + gx);
                int baseId = map.TileValues[sourceIndex];
                TileUvs baseCell = atlas.TileUvLookup[baseId];
                TileUvs secondary = baseCell, tertiary = baseCell;
                Vector4 blendFlags = Vector4.zero;
                Cnc3MapReader.CliffMapping cliff = null;
                Vector4 cliffRect = Vector4.zero;
                float cliffCellWidth = 1;
                if (validCliffData)
                {
                    uint index = map.CliffTextureIndices[sourceIndex];
                    if (index > 0 && index <= map.CliffMappings.Count)
                    {
                        cliff = map.CliffMappings[(int)index - 1];
                        for (int kind = 0; kind < map.TerrainTextures.Count; kind++)
                        {
                            var candidate = map.TerrainTextures[kind];
                            if (cliff.TextureTile < candidate.TileStart ||
                                cliff.TextureTile >= candidate.TileStart +
                                    candidate.TileCount)
                                continue;
                            Cnc3AtlasTileEntry rect = atlas.AtlasRectForType[kind];
                            cliffRect = new Vector4(
                                rect.x0 / (float)atlas.BaseAtlas.width,
                                1f - rect.y1 / (float)atlas.BaseAtlas.height,
                                rect.x1 / (float)atlas.BaseAtlas.width,
                                1f - rect.y0 / (float)atlas.BaseAtlas.height);
                            cliffCellWidth = candidate.TileWidth;
                            break;
                        }
                    }
                }
                if (validBlendData)
                {
                    uint one = map.Blends[sourceIndex];
                    uint two = map.ThreeWayBlends[sourceIndex];
                    if (one > 0)
                    {
                        Cnc3MapReader.TerrainBlend desc =
                            map.BlendDescriptions[checked((int)one - 1)];
                        secondary = atlas.TileUvLookup[desc.SecondaryTile];
                        blendFlags.x = desc.Direction;
                        blendFlags.y = (desc.Flags & 1) +
                            (desc.TwoSided ? 2 : 0);
                    }
                    if (two > 0)
                    {
                        Cnc3MapReader.TerrainBlend desc =
                            map.BlendDescriptions[checked((int)two - 1)];
                        tertiary = atlas.TileUvLookup[desc.SecondaryTile];
                        blendFlags.z = desc.Direction;
                        blendFlags.w = (desc.Flags & 1) +
                            (desc.TwoSided ? 2 : 0);
                    }
                }

                int i = vertexIndex;
                vertices[i] = new Vector3(x * horizontalScale,
                    HeightAt(map, gx, gy, flip, verticalScale), y * horizontalScale);
                normals[i] = NormalAt(map, gx, gy, flip, horizontalScale, verticalScale);
                baseUv[i] = baseCell.BL;
                if (validCliffData)
                {
                    cliffUV[i] = cliff != null ? cliff.BottomLeft : Vector2.zero;
                    cliffRects[i] = cliffRect;
                    cliffSizes[i] = new Vector2(cliffCellWidth, cliff != null ? 1f : 0f);
                }
                if (validBlendData)
                {
                    secondaryUv[i] = secondary.BL;
                    tertiaryUv[i] = tertiary.BL;
                    directions[i] = blendFlags;
                    fraction[i] = Vector2.zero;
                }
                i++;
                vertices[i] = new Vector3(x * horizontalScale,
                    HeightAt(map, gx, gy + 1, flip, verticalScale), (y + 1) * horizontalScale);
                normals[i] = NormalAt(map, gx, gy + 1, flip, horizontalScale, verticalScale);
                baseUv[i] = baseCell.TL;
                if (validCliffData)
                {
                    cliffUV[i] = cliff != null ? cliff.TopLeft : Vector2.zero;
                    cliffRects[i] = cliffRect;
                    cliffSizes[i] = new Vector2(cliffCellWidth, cliff != null ? 1f : 0f);
                }
                if (validBlendData)
                {
                    secondaryUv[i] = secondary.TL;
                    tertiaryUv[i] = tertiary.TL;
                    directions[i] = blendFlags;
                    fraction[i] = new Vector2(0f, 1f);
                }
                i++;
                vertices[i] = new Vector3((x + 1) * horizontalScale,
                    HeightAt(map, gx + 1, gy, flip, verticalScale), y * horizontalScale);
                normals[i] = NormalAt(map, gx + 1, gy, flip, horizontalScale, verticalScale);
                baseUv[i] = baseCell.BR;
                if (validCliffData)
                {
                    cliffUV[i] = cliff != null ? cliff.BottomRight : Vector2.zero;
                    cliffRects[i] = cliffRect;
                    cliffSizes[i] = new Vector2(cliffCellWidth, cliff != null ? 1f : 0f);
                }
                if (validBlendData)
                {
                    secondaryUv[i] = secondary.BR;
                    tertiaryUv[i] = tertiary.BR;
                    directions[i] = blendFlags;
                    fraction[i] = new Vector2(1f, 0f);
                }
                i++;
                vertices[i] = new Vector3((x + 1) * horizontalScale,
                    HeightAt(map, gx + 1, gy + 1, flip, verticalScale),
                    (y + 1) * horizontalScale);
                normals[i] = NormalAt(map, gx + 1, gy + 1, flip, horizontalScale, verticalScale);
                baseUv[i] = baseCell.TR;
                if (validCliffData)
                {
                    cliffUV[i] = cliff != null ? cliff.TopRight : Vector2.zero;
                    cliffRects[i] = cliffRect;
                    cliffSizes[i] = new Vector2(cliffCellWidth, cliff != null ? 1f : 0f);
                }
                if (validBlendData)
                {
                    secondaryUv[i] = secondary.TR;
                    tertiaryUv[i] = tertiary.TR;
                    directions[i] = blendFlags;
                    fraction[i] = Vector2.one;
                }
                i++;

                triangles[triIndex++] = vertexIndex;
                triangles[triIndex++] = vertexIndex + 1;
                triangles[triIndex++] = vertexIndex + 2;
                triangles[triIndex++] = vertexIndex + 2;
                triangles[triIndex++] = vertexIndex + 1;
                triangles[triIndex++] = vertexIndex + 3;
                vertexIndex += 4;
            }
            var mesh = new Mesh
            {
                name = "SAGE_AtlasTerrain_" + lowerX + "_" + lowerY,
                indexFormat = IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = baseUv,
                triangles = triangles
            };
            if (validCliffData)
            {
                mesh.SetUVs(5, new List<Vector2>(cliffUV));
                mesh.SetUVs(6, new List<Vector4>(cliffRects));
                mesh.SetUVs(7, new List<Vector2>(cliffSizes));
            }
            if (validBlendData)
            {
                mesh.uv2 = secondaryUv;
                mesh.uv3 = tertiaryUv;
                mesh.SetUVs(3, new List<Vector4>(directions));
                mesh.SetUVs(4, new List<Vector2>(fraction));
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
