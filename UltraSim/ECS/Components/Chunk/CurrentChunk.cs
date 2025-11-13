#nullable enable

using UltraSim.ECS.Chunk;

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Optional optimization: Tracks entity's current chunk for fast boundary checks.
    /// Allows entities to self-detect when they cross chunk boundaries after movement.
    ///
    /// USAGE:
    /// 1. After movement, entity checks: !CurrentChunk.Bounds.Contains(newPosition)
    /// 2. If outside bounds, queue chunk transfer
    /// 3. ChunkSystem processes queue and reassigns entities
    ///
    /// BENEFITS:
    /// - Fast boundary checks without world queries
    /// - Reduces chunk reassignment queue spam (only queue when actually crossed)
    /// - Avoids processing entities that stay in their chunks
    ///
    /// OPTIONAL: Entities can still be assigned to chunks without this component.
    /// ChunkSystem will detect chunk changes via position queries.
    /// </summary>
    public struct CurrentChunk
    {
        /// <summary>Current chunk entity this entity belongs to.</summary>
        public Entity ChunkEntity;

        /// <summary>Current chunk location in world grid.</summary>
        public ChunkLocation Location;

        /// <summary>
        /// Cached chunk bounds for fast containment checks.
        /// Entity can check if new position is within bounds without querying world.
        /// Example: if (!currentChunk.Bounds.Contains(pos.X, pos.Y, pos.Z)) { QueueTransfer(); }
        /// </summary>
        public ChunkBounds Bounds;

        public CurrentChunk(Entity chunkEntity, ChunkLocation location, ChunkBounds bounds)
        {
            ChunkEntity = chunkEntity;
            Location = location;
            Bounds = bounds;
        }
    }
}
