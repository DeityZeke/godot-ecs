#nullable enable

using System;
using UltraSim.ECS.Components;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Procedural terrain generator using multi-octave Perlin noise.
    /// Supports seeded generation for reproducible worlds (Minecraft-style).
    /// </summary>
    public sealed class NoiseTerrainGenerator : ITerrainGenerator
    {
        private readonly int _seed;
        private readonly float _scale;
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly sbyte _baseHeight;
        private readonly sbyte _heightRange;
        private readonly byte _materialId;

        /// <summary>
        /// Creates a noise-based terrain generator.
        /// </summary>
        /// <param name="seed">Random seed for reproducible generation.</param>
        /// <param name="scale">Base noise scale (larger = smoother terrain). Default: 50.0</param>
        /// <param name="octaves">Number of noise layers (more = more detail). Default: 4</param>
        /// <param name="persistence">Amplitude falloff per octave (0-1). Default: 0.5</param>
        /// <param name="lacunarity">Frequency multiplier per octave. Default: 2.0</param>
        /// <param name="baseHeight">Base terrain height in 0.5m units. Default: 0</param>
        /// <param name="heightRange">Height variation range in 0.5m units. Default: 20 (±10m)</param>
        /// <param name="materialId">Default material/atlas region. Default: 0</param>
        public NoiseTerrainGenerator(
            int seed = 0,
            float scale = 50.0f,
            int octaves = 4,
            float persistence = 0.5f,
            float lacunarity = 2.0f,
            sbyte baseHeight = 0,
            sbyte heightRange = 20,
            byte materialId = 0)
        {
            _seed = seed;
            _scale = Math.Max(0.0001f, scale);
            _octaves = Math.Clamp(octaves, 1, 8);
            _persistence = Math.Clamp(persistence, 0.0f, 1.0f);
            _lacunarity = Math.Max(1.0f, lacunarity);
            _baseHeight = baseHeight;
            _heightRange = heightRange;
            _materialId = materialId;
        }

        public TerrainChunkData GenerateChunk(ChunkLocation chunkLocation)
        {
            var chunk = new TerrainChunkData(chunkLocation);
            var span = chunk.AsSpan();

            // Chunk world offset in tiles
            int chunkTileX = chunkLocation.X * TerrainChunkData.TileSizeXZ;
            int chunkTileZ = chunkLocation.Z * TerrainChunkData.TileSizeXZ;

            int idx = 0;
            for (int localZ = 0; localZ < TerrainChunkData.TileSizeXZ; localZ++)
            {
                for (int localX = 0; localX < TerrainChunkData.TileSizeXZ; localX++)
                {
                    int worldTileX = chunkTileX + localX;
                    int worldTileZ = chunkTileZ + localZ;

                    // Sample noise at tile corners (0.5m grid)
                    float worldX = worldTileX * TerrainChunkData.TileWorldSize;
                    float worldZ = worldTileZ * TerrainChunkData.TileWorldSize;
                    float tileSize = TerrainChunkData.TileWorldSize;

                    sbyte heightNW = SampleHeight(worldX, worldZ);
                    sbyte heightNE = SampleHeight(worldX + tileSize, worldZ);
                    sbyte heightSW = SampleHeight(worldX, worldZ + tileSize);
                    sbyte heightSE = SampleHeight(worldX + tileSize, worldZ + tileSize);

                    var tile = new TerrainTile
                    {
                        HeightNW = heightNW,
                        HeightNE = heightNE,
                        HeightSW = heightSW,
                        HeightSE = heightSE,
                        MaterialId = _materialId,
                        Flags = DetermineFlags(heightNW, heightNE, heightSW, heightSE)
                    };

                    span[idx++] = tile;
                }
            }

            return chunk;
        }

        private sbyte SampleHeight(float worldX, float worldZ)
        {
            float noiseValue = OctaveNoise(worldX, worldZ, _octaves, _persistence, _lacunarity);

            // Map noise [-1, 1] to height range
            float normalizedNoise = (noiseValue + 1.0f) * 0.5f; // [0, 1]
            float height = _baseHeight + (normalizedNoise - 0.5f) * _heightRange;

            return (sbyte)Math.Clamp((int)Math.Round(height), sbyte.MinValue, sbyte.MaxValue);
        }

        private float OctaveNoise(float x, float z, int octaves, float persistence, float lacunarity)
        {
            float total = 0.0f;
            float amplitude = 1.0f;
            float frequency = 1.0f;
            float maxValue = 0.0f;

            for (int i = 0; i < octaves; i++)
            {
                float sampleX = x / _scale * frequency;
                float sampleZ = z / _scale * frequency;

                float noiseValue = PerlinNoise(sampleX, sampleZ);
                total += noiseValue * amplitude;

                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxValue; // Normalize to [-1, 1]
        }

        private float PerlinNoise(float x, float z)
        {
            // Simple 2D Perlin noise implementation
            // Grid cell coordinates
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            // Local coordinates within cell [0, 1]
            float localX = x - x0;
            float localZ = z - z0;

            // Gradient vectors at grid corners
            float g00 = DotGridGradient(x0, z0, x, z);
            float g10 = DotGridGradient(x1, z0, x, z);
            float g01 = DotGridGradient(x0, z1, x, z);
            float g11 = DotGridGradient(x1, z1, x, z);

            // Interpolate
            float sx = Fade(localX);
            float sz = Fade(localZ);

            float ix0 = Lerp(g00, g10, sx);
            float ix1 = Lerp(g01, g11, sx);
            float value = Lerp(ix0, ix1, sz);

            return value;
        }

        private float DotGridGradient(int ix, int iz, float x, float z)
        {
            // Compute pseudo-random gradient vector
            uint hash = Hash2D(ix, iz);
            float angle = (hash & 0xFF) * (MathF.PI * 2.0f / 256.0f);
            float gradX = MathF.Cos(angle);
            float gradZ = MathF.Sin(angle);

            // Distance vector
            float dx = x - ix;
            float dz = z - iz;

            // Dot product
            return dx * gradX + dz * gradZ;
        }

        private uint Hash2D(int x, int z)
        {
            // Simple hash function combining seed and coordinates
            uint h = (uint)_seed;
            h = (h ^ (uint)x) * 0x45d9f3b;
            h = (h ^ (uint)z) * 0x45d9f3b;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            return h ^ (h >> 16);
        }

        private static float Fade(float t)
        {
            // Smoothstep function: 6t^5 - 15t^4 + 10t^3
            return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + t * (b - a);
        }

        private static TerrainTileFlags DetermineFlags(sbyte heightNW, sbyte heightNE, sbyte heightSW, sbyte heightSE)
        {
            // Calculate height difference to detect cliffs
            int minHeight = Math.Min(Math.Min(heightNW, heightNE), Math.Min(heightSW, heightSE));
            int maxHeight = Math.Max(Math.Max(heightNW, heightNE), Math.Max(heightSW, heightSE));
            int heightDiff = maxHeight - minHeight;

            // Mark as cliff if height difference exceeds threshold (4 units = 2m)
            if (heightDiff >= 4)
            {
                return TerrainTileFlags.Cliff | TerrainTileFlags.Blocked;
            }

            // Mark as water if all corners are below sea level (height < 0)
            if (heightNW < 0 && heightNE < 0 && heightSW < 0 && heightSE < 0)
            {
                return TerrainTileFlags.Water;
            }

            return TerrainTileFlags.None;
        }
    }
}
