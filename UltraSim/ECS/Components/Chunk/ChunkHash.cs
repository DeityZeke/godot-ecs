#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Hash-based verification for chunk data integrity.
    /// Used for UO-style static caching and validation.
    /// </summary>
    public struct ChunkHash
    {
        /// <summary>
        /// 64-bit hash of chunk terrain/tile data.
        /// </summary>
        public ulong TerrainHash;

        /// <summary>
        /// 64-bit hash of static objects in chunk (trees, rocks, buildings, etc.).
        /// </summary>
        public ulong StaticsHash;

        /// <summary>
        /// Combined hash for quick comparison.
        /// </summary>
        public ulong CombinedHash;

        public ChunkHash(ulong terrainHash, ulong staticsHash)
        {
            TerrainHash = terrainHash;
            StaticsHash = staticsHash;
            CombinedHash = HashCombine(terrainHash, staticsHash);
        }

        /// <summary>
        /// Check if this chunk's data matches another hash.
        /// </summary>
        public bool Matches(ChunkHash other)
            => CombinedHash == other.CombinedHash;

        /// <summary>
        /// Check if only terrain data has changed.
        /// </summary>
        public bool TerrainMatches(ChunkHash other)
            => TerrainHash == other.TerrainHash;

        /// <summary>
        /// Check if only statics data has changed.
        /// </summary>
        public bool StaticsMatch(ChunkHash other)
            => StaticsHash == other.StaticsHash;

        /// <summary>
        /// FNV-1a style hash combination.
        /// </summary>
        private static ulong HashCombine(ulong h1, ulong h2)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL; // FNV offset basis
                hash = (hash ^ h1) * 1099511628211UL; // FNV prime
                hash = (hash ^ h2) * 1099511628211UL;
                return hash;
            }
        }

        public override string ToString()
            => $"Hash[Terrain:{TerrainHash:X16}, Statics:{StaticsHash:X16}]";
    }
}
