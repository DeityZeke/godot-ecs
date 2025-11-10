#nullable enable

using UltraSim.ECS.Components;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Abstraction for producing terrain chunk payloads (flat, noise-based, map-editor, etc.).
    /// </summary>
    public interface ITerrainGenerator
    {
        TerrainChunkData GenerateChunk(ChunkLocation chunkLocation);
    }
}
