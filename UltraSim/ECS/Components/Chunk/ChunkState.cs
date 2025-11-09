#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Lifecycle and streaming state of a chunk.
    /// </summary>
    public enum ChunkLifecycleState : byte
    {
        /// <summary>
        /// Chunk is queued for loading but not yet initialized.
        /// </summary>
        PendingLoad = 0,

        /// <summary>
        /// Chunk data is being loaded from disk or generated.
        /// </summary>
        Loading = 1,

        /// <summary>
        /// Chunk is fully loaded and active in the world.
        /// </summary>
        Active = 2,

        /// <summary>
        /// Chunk is visible and being rendered.
        /// </summary>
        Rendering = 3,

        /// <summary>
        /// Chunk is flagged for unloading (out of range or memory pressure).
        /// </summary>
        PendingUnload = 4,

        /// <summary>
        /// Chunk data is being saved to disk.
        /// </summary>
        Unloading = 5,

        /// <summary>
        /// Chunk has been unloaded and entity is pending destruction.
        /// </summary>
        Inactive = 6
    }

    /// <summary>
    /// Runtime state information for a chunk entity.
    /// </summary>
    public struct ChunkState
    {
        public ChunkLifecycleState Lifecycle;

        /// <summary>
        /// Number of entities currently assigned to this chunk (excluding the chunk entity itself).
        /// </summary>
        public int EntityCount;

        /// <summary>
        /// Frame number when this chunk was last accessed (for LRU eviction).
        /// </summary>
        public ulong LastAccessFrame;

        /// <summary>
        /// Whether this chunk has been modified since last save.
        /// </summary>
        public bool IsDirty;

        /// <summary>
        /// Whether this chunk was procedurally generated (vs loaded from disk).
        /// </summary>
        public bool IsGenerated;

        public ChunkState(ChunkLifecycleState lifecycle = ChunkLifecycleState.PendingLoad)
        {
            Lifecycle = lifecycle;
            EntityCount = 0;
            LastAccessFrame = 0;
            IsDirty = false;
            IsGenerated = false;
        }

        public override string ToString()
            => $"State[{Lifecycle}, Entities:{EntityCount}, Dirty:{IsDirty}]";
    }
}
