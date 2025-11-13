#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Tag component indicating a server spatial chunk has been modified.
    /// Set when entities move in/out of the chunk.
    /// Cleared when client rebuilds visual cache for this chunk.
    ///
    /// Using a tag component (instead of bool in ChunkState) enables:
    /// - Query-based dirty chunk enumeration: world.QueryArchetypes(typeof(DirtyChunkTag))
    /// - Archetype-based filtering (ECS pattern)
    /// - No need to iterate all chunks to find dirty ones
    /// </summary>
    public struct DirtyChunkTag { }
}
