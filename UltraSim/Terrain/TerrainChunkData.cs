#nullable enable

using System;
using UltraSim.ECS.Components;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Editable terrain chunk containing a fixed grid of TerrainTile elements.
    /// </summary>
    public sealed class TerrainChunkData
    {
        public const int TileSizeXZ = 32;
        public const float TileWorldSize = 0.5f;

        private readonly TerrainTile[] _tiles;

        public ChunkLocation Chunk { get; }
        public int Width => TileSizeXZ;
        public int Depth => TileSizeXZ;
        public ulong Version { get; private set; }

        public TerrainChunkData(ChunkLocation chunk)
        {
            Chunk = chunk;
            _tiles = new TerrainTile[TileSizeXZ * TileSizeXZ];
            Version = 1;
        }

        public TerrainTile GetTile(int x, int z)
        {
            ValidateCoords(x, z);
            return _tiles[z * TileSizeXZ + x];
        }

        public void SetTile(int x, int z, TerrainTile tile)
        {
            ValidateCoords(x, z);
            _tiles[z * TileSizeXZ + x] = tile;
            Version++;
        }

        public Span<TerrainTile> AsSpan() => _tiles.AsSpan();

        public void Fill(TerrainTile tile)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i] = tile;
            }
            Version++;
        }

        private static void ValidateCoords(int x, int z)
        {
            if ((uint)x >= TileSizeXZ)
                throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)z >= TileSizeXZ)
                throw new ArgumentOutOfRangeException(nameof(z));
        }
    }
}
