#nullable enable

using UltraSim.ECS.Components;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Simple generator that produces a uniform flat plane at height zero.
    /// Useful for editor bootstrapping and stress tests.
    /// </summary>
    public sealed class FlatTerrainGenerator : ITerrainGenerator
    {
        public byte MaterialId { get; }
        public sbyte Height { get; }

        public FlatTerrainGenerator(byte materialId = 0, sbyte height = 0)
        {
            MaterialId = materialId;
            Height = height;
        }

        public TerrainChunkData GenerateChunk(ChunkLocation chunkLocation)
        {
            var chunk = new TerrainChunkData(chunkLocation);
            var tile = TerrainTile.Empty;
            tile.SetUniformHeight(Height);
            tile.MaterialId = MaterialId;
            chunk.Fill(tile);
            return chunk;
        }
    }
}
