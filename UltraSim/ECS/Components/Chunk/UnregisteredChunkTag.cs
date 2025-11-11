#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Tag component marking chunk entities that haven't been registered with ChunkManager yet.
    /// Removed by ChunkSystem.RegisterNewChunks() after registration.
    /// This enables efficient querying of only NEW chunks instead of scanning all chunks.
    /// </summary>
    public struct UnregisteredChunkTag
    {
        // Empty tag component - presence indicates unregistered state
    }
}
