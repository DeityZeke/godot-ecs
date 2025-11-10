#nullable enable

using System;
using UltraSim.ECS.Components;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Example usage patterns for the terrain system.
    /// This file demonstrates how to use generators, serialization, and tile manipulation.
    /// </summary>
    public static class TerrainUsageExample
    {
        /// <summary>
        /// Example 1: Create a blank flat world for manual editing (map editor mode).
        /// </summary>
        public static void Example_FlatWorld()
        {
            var generator = new FlatTerrainGenerator(materialId: 0, height: 0);
            var chunk = generator.GenerateChunk(new ChunkLocation(0, 0, 0));

            // Chunk now contains 32×32 flat tiles at height 0
            Console.WriteLine($"Generated {chunk.Chunk} with {chunk.Width}×{chunk.Depth} tiles");
        }

        /// <summary>
        /// Example 2: Generate procedural terrain with seed (Minecraft-style).
        /// </summary>
        public static void Example_ProceduralWorld()
        {
            // Create noise generator with seed for reproducible worlds
            var generator = new NoiseTerrainGenerator(
                seed: 12345,
                scale: 50.0f,          // Larger = smoother terrain
                octaves: 4,            // More = more detail
                persistence: 0.5f,     // Amplitude falloff
                lacunarity: 2.0f,      // Frequency multiplier
                baseHeight: 0,         // Sea level (0.5m units)
                heightRange: 20,       // ±10m variation
                materialId: 0
            );

            // Generate chunks in a 5×5 grid around origin
            for (int x = -2; x <= 2; x++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    var chunk = generator.GenerateChunk(new ChunkLocation(x, z, 0));
                    Console.WriteLine($"Generated {chunk.Chunk} v{chunk.Version}");

                    // Save chunk to disk
                    byte[] serialized = TerrainChunkSerializer.Serialize(chunk);
                    // ... write to file system
                }
            }
        }

        /// <summary>
        /// Example 3: Manual tile editing (map editor operations).
        /// </summary>
        public static void Example_ManualEditing()
        {
            var chunk = new TerrainChunkData(new ChunkLocation(0, 0, 0));

            // Create a hill in the center
            int centerX = TerrainChunkData.TileSizeXZ / 2;
            int centerZ = TerrainChunkData.TileSizeXZ / 2;
            int radius = 8;

            for (int localZ = 0; localZ < TerrainChunkData.TileSizeXZ; localZ++)
            {
                for (int localX = 0; localX < TerrainChunkData.TileSizeXZ; localX++)
                {
                    float dx = localX - centerX;
                    float dz = localZ - centerZ;
                    float distance = MathF.Sqrt(dx * dx + dz * dz);

                    if (distance < radius)
                    {
                        // Create smooth hill using cosine falloff
                        float heightFactor = (MathF.Cos(distance / radius * MathF.PI) + 1.0f) * 0.5f;
                        sbyte height = (sbyte)(heightFactor * 10); // 0-10 units (0-5m)

                        var tile = chunk.GetTile(localX, localZ);
                        tile.SetUniformHeight(height);
                        tile.MaterialId = 1; // Grass material
                        chunk.SetTile(localX, localZ, tile);
                    }
                }
            }

            Console.WriteLine($"Edited {chunk.Chunk}, version = {chunk.Version}");
        }

        /// <summary>
        /// Example 4: Serialization and deserialization.
        /// </summary>
        public static void Example_Serialization()
        {
            // Generate terrain
            var generator = new NoiseTerrainGenerator(seed: 42);
            var originalChunk = generator.GenerateChunk(new ChunkLocation(5, 10, 0));

            // Serialize to bytes
            byte[] data = TerrainChunkSerializer.Serialize(originalChunk);
            Console.WriteLine($"Serialized chunk: {data.Length} bytes");

            // Deserialize from bytes
            var loadedChunk = TerrainChunkSerializer.Deserialize(data, new ChunkLocation(5, 10, 0));
            Console.WriteLine($"Deserialized {loadedChunk.Chunk}, version {loadedChunk.Version}");

            // Verify tiles match
            for (int i = 0; i < TerrainChunkData.TileSizeXZ * TerrainChunkData.TileSizeXZ; i++)
            {
                var originalTile = originalChunk.AsSpan()[i];
                var loadedTile = loadedChunk.AsSpan()[i];

                if (originalTile.HeightNW != loadedTile.HeightNW ||
                    originalTile.MaterialId != loadedTile.MaterialId)
                {
                    Console.WriteLine($"Mismatch at tile {i}!");
                    return;
                }
            }

            Console.WriteLine("Serialization verified successfully!");
        }

        /// <summary>
        /// Example 5: Tile flag usage (cliffs, water, blocked areas).
        /// </summary>
        public static void Example_TileFlags()
        {
            var chunk = new TerrainChunkData(new ChunkLocation(0, 0, 0));

            // Create water tiles
            for (int z = 0; z < 10; z++)
            {
                for (int x = 0; x < 32; x++)
                {
                    var tile = chunk.GetTile(x, z);
                    tile.SetUniformHeight(-2); // Below sea level
                    tile.MaterialId = 2; // Water material
                    tile.Flags = TerrainTileFlags.Water;
                    chunk.SetTile(x, z, tile);
                }
            }

            // Create cliff edge
            for (int x = 10; x < 20; x++)
            {
                var tile = chunk.GetTile(x, 15);
                tile.HeightNW = 10;
                tile.HeightNE = 10;
                tile.HeightSW = 0;
                tile.HeightSE = 0;
                tile.MaterialId = 3; // Rock material
                tile.Flags = TerrainTileFlags.Cliff | TerrainTileFlags.Blocked;
                chunk.SetTile(x, 15, tile);
            }

            // Query tiles
            for (int z = 0; z < TerrainChunkData.TileSizeXZ; z++)
            {
                for (int x = 0; x < TerrainChunkData.TileSizeXZ; x++)
                {
                    var tile = chunk.GetTile(x, z);
                    if ((tile.Flags & TerrainTileFlags.Water) != 0)
                    {
                        Console.WriteLine($"Tile ({x}, {z}) is water");
                    }
                    if (!tile.IsWalkable)
                    {
                        Console.WriteLine($"Tile ({x}, {z}) is blocked");
                    }
                }
            }
        }
    }
}
