#nullable enable

using UltraSim.ECS.Components;
using UltraSim.Terrain;

namespace Server.ECS.Components.Terrain
{
    /// <summary>
    /// Component marking an entity as a terrain chunk.
    /// Contains reference to the terrain tile data.
    /// </summary>
    public struct TerrainChunkComponent
    {
        /// <summary>
        /// Reference to the terrain chunk data (shared, not copied).
        /// Managed by TerrainManagerSystem.
        /// </summary>
        public TerrainChunkData? ChunkData;

        /// <summary>
        /// Chunk location in the world grid.
        /// </summary>
        public ChunkLocation Location;

        /// <summary>
        /// Dirty flag - true if terrain was modified and mesh needs rebuild.
        /// </summary>
        public bool IsDirty;

        /// <summary>
        /// Version of the terrain data when mesh was last built.
        /// Used to detect if mesh needs regeneration.
        /// </summary>
        public ulong MeshVersion;
    }
}
