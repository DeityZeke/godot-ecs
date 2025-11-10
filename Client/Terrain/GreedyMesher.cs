#nullable enable

using System;
using Godot;
using UltraSim.Terrain;

namespace Client.Terrain
{
    /// <summary>
    /// Greedy meshing algorithm for terrain chunks.
    /// Combines adjacent coplanar tiles into larger quads to reduce vertex count.
    /// Based on: https://0fps.net/2012/06/30/meshing-in-a-minecraft-game/
    /// </summary>
    public static class GreedyMesher
    {
        /// <summary>
        /// Generates a mesh from a terrain chunk using greedy meshing.
        /// </summary>
        public static TerrainMesh GenerateMesh(TerrainChunkData chunk)
        {
            var mesh = new TerrainMesh();

            // Generate top surface (most common - ground plane)
            GenerateTopSurface(chunk, mesh);

            // TODO: Generate side surfaces for cliff edges and walls
            // TODO: Generate bottom surface for overhangs/caves (multi-layer terrain)

            return mesh;
        }

        /// <summary>
        /// Generates the top surface of the terrain using greedy meshing.
        /// </summary>
        private static void GenerateTopSurface(TerrainChunkData chunk, TerrainMesh mesh)
        {
            int size = TerrainConstants.ChunkTilesPerSide;
            bool[,] visited = new bool[size, size];

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (visited[x, z])
                        continue;

                    var tile = chunk.GetTile(x, z);

                    // Check if tile is flat (all corners same height)
                    if (!IsFlatTile(tile))
                    {
                        // For sloped tiles, generate individual quad
                        GenerateSlopedQuad(chunk, x, z, mesh);
                        visited[x, z] = true;
                        continue;
                    }

                    // Greedy expansion for flat tiles
                    int width = 1;
                    int height = 1;

                    // Expand along X axis
                    while (x + width < size && !visited[x + width, z])
                    {
                        var nextTile = chunk.GetTile(x + width, z);
                        if (!CanMerge(tile, nextTile))
                            break;
                        width++;
                    }

                    // Expand along Z axis
                    bool canExpandZ = true;
                    while (z + height < size && canExpandZ)
                    {
                        // Check entire row
                        for (int dx = 0; dx < width; dx++)
                        {
                            if (visited[x + dx, z + height])
                            {
                                canExpandZ = false;
                                break;
                            }

                            var nextTile = chunk.GetTile(x + dx, z + height);
                            if (!CanMerge(tile, nextTile))
                            {
                                canExpandZ = false;
                                break;
                            }
                        }

                        if (canExpandZ)
                            height++;
                    }

                    // Mark visited
                    for (int dz = 0; dz < height; dz++)
                    {
                        for (int dx = 0; dx < width; dx++)
                        {
                            visited[x + dx, z + dz] = true;
                        }
                    }

                    // Generate merged quad
                    GenerateFlatQuad(chunk, x, z, width, height, tile, mesh);
                }
            }
        }

        /// <summary>
        /// Generates a quad for a flat tile region.
        /// </summary>
        private static void GenerateFlatQuad(TerrainChunkData chunk, int startX, int startZ, int width, int height, TerrainTile tile, TerrainMesh mesh)
        {
            float tileSize = TerrainConstants.TileSize;
            float worldHeight = tile.HeightNW * TerrainConstants.HeightScale;

            // World positions (origin at chunk bottom-left)
            float x0 = startX * tileSize;
            float z0 = startZ * tileSize;
            float x1 = (startX + width) * tileSize;
            float z1 = (startZ + height) * tileSize;

            // Vertices (counter-clockwise for correct winding)
            var v0 = new Vector3(x0, worldHeight, z0); // Bottom-left
            var v1 = new Vector3(x1, worldHeight, z0); // Bottom-right
            var v2 = new Vector3(x1, worldHeight, z1); // Top-right
            var v3 = new Vector3(x0, worldHeight, z1); // Top-left

            // Normal (flat surface points up)
            var normal = Vector3.Up;

            // UVs (tile texture coordinates based on material ID)
            // TODO: Map material ID to atlas regions
            float uvScale = 1.0f / 16.0f; // Assume 16×16 atlas
            int atlasX = tile.MaterialId % 16;
            int atlasY = tile.MaterialId / 16;

            var uv0 = new Vector2(atlasX * uvScale, atlasY * uvScale);
            var uv1 = new Vector2((atlasX + width) * uvScale, atlasY * uvScale);
            var uv2 = new Vector2((atlasX + width) * uvScale, (atlasY + height) * uvScale);
            var uv3 = new Vector2(atlasX * uvScale, (atlasY + height) * uvScale);

            mesh.AddQuad(v0, v1, v2, v3, normal, uv0, uv1, uv2, uv3);
        }

        /// <summary>
        /// Generates a quad for a sloped tile (corner heights differ).
        /// </summary>
        private static void GenerateSlopedQuad(TerrainChunkData chunk, int x, int z, TerrainMesh mesh)
        {
            var tile = chunk.GetTile(x, z);
            float tileSize = TerrainConstants.TileSize;
            float heightScale = TerrainConstants.HeightScale;

            // World positions with per-corner heights
            float x0 = x * tileSize;
            float z0 = z * tileSize;
            float x1 = (x + 1) * tileSize;
            float z1 = (z + 1) * tileSize;

            var v0 = new Vector3(x0, tile.HeightSW * heightScale, z0); // Bottom-left (SW)
            var v1 = new Vector3(x1, tile.HeightSE * heightScale, z0); // Bottom-right (SE)
            var v2 = new Vector3(x1, tile.HeightNE * heightScale, z1); // Top-right (NE)
            var v3 = new Vector3(x0, tile.HeightNW * heightScale, z1); // Top-left (NW)

            // Calculate normal from cross product
            var edge1 = v1 - v0;
            var edge2 = v3 - v0;
            var normal = edge1.Cross(edge2).Normalized();

            // UVs for single tile
            float uvScale = 1.0f / 16.0f;
            int atlasX = tile.MaterialId % 16;
            int atlasY = tile.MaterialId / 16;

            var uv0 = new Vector2(atlasX * uvScale, atlasY * uvScale);
            var uv1 = new Vector2((atlasX + 1) * uvScale, atlasY * uvScale);
            var uv2 = new Vector2((atlasX + 1) * uvScale, (atlasY + 1) * uvScale);
            var uv3 = new Vector2(atlasX * uvScale, (atlasY + 1) * uvScale);

            mesh.AddQuad(v0, v1, v2, v3, normal, uv0, uv1, uv2, uv3);
        }

        /// <summary>
        /// Checks if a tile is flat (all corners same height).
        /// </summary>
        private static bool IsFlatTile(TerrainTile tile)
        {
            return tile.HeightNW == tile.HeightNE &&
                   tile.HeightNW == tile.HeightSW &&
                   tile.HeightNW == tile.HeightSE;
        }

        /// <summary>
        /// Checks if two tiles can be merged in greedy meshing.
        /// </summary>
        private static bool CanMerge(TerrainTile a, TerrainTile b)
        {
            // Must be flat
            if (!IsFlatTile(a) || !IsFlatTile(b))
                return false;

            // Must have same height
            if (a.HeightNW != b.HeightNW)
                return false;

            // Must have same material
            if (a.MaterialId != b.MaterialId)
                return false;

            // Must have same flags (water, cliff, etc.)
            if (a.Flags != b.Flags)
                return false;

            return true;
        }
    }
}
