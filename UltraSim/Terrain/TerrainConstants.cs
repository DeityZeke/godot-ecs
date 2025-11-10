#nullable enable

namespace UltraSim.Terrain
{
    /// <summary>
    /// Core constants for the terrain system.
    /// Aligns with TerrainChunkData and TerrainTile definitions.
    /// </summary>
    public static class TerrainConstants
    {
        /// <summary>
        /// Size of each tile in world units (0.5m grid).
        /// </summary>
        public const float TileSize = 0.5f;

        /// <summary>
        /// Number of tiles along each axis in a terrain chunk (32×32 grid).
        /// </summary>
        public const int ChunkTilesPerSide = 32;

        /// <summary>
        /// Total number of tiles in a chunk (32×32 = 1024).
        /// </summary>
        public const int ChunkTileCount = ChunkTilesPerSide * ChunkTilesPerSide;

        /// <summary>
        /// World-space size of a terrain chunk (32 tiles × 0.5m = 16m).
        /// </summary>
        public const float ChunkWorldSize = ChunkTilesPerSide * TileSize;

        /// <summary>
        /// Height unit scale (height values are sbyte, scaled by this factor).
        /// Height range: -128 to 127 → -64m to +63.5m with 0.5m precision.
        /// </summary>
        public const float HeightScale = 0.5f;

        /// <summary>
        /// Maximum material/atlas region ID (255 materials supported).
        /// </summary>
        public const byte MaxMaterialId = byte.MaxValue;

        /// <summary>
        /// Terrain chunk serialization version.
        /// Increment when changing TerrainTile or TerrainChunkData structure.
        /// </summary>
        public const ushort SerializationVersion = 1;
    }
}
