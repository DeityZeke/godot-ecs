#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Marks which chunk an entity belongs to.
    /// Used for spatial partitioning and queries.
    /// Origin-based ownership: entity belongs to chunk containing its pivot point.
    /// </summary>
    public struct ChunkOwner
    {
        /// <summary>
        /// The chunk entity that owns this entity.
        /// Entity.Invalid if not assigned to any chunk.
        /// </summary>
        public Entity ChunkEntity;

        /// <summary>
        /// Cached chunk location (for fast lookups without reading ChunkLocation component).
        /// </summary>
        public ChunkLocation Location;

        public ChunkOwner(Entity chunkEntity, ChunkLocation location)
        {
            ChunkEntity = chunkEntity;
            Location = location;
        }

        public bool IsAssigned => ChunkEntity != Entity.Invalid;

        public override string ToString()
            => IsAssigned ? $"Owner[Chunk:{ChunkEntity.Index}, {Location}]" : "Owner[Unassigned]";
    }
}
