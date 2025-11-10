#nullable enable

using System;
using System.IO;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Binary serializer for TerrainChunkData. Optimised for tooling throughput, not minimal size.
    /// </summary>
    public static class TerrainChunkSerializer
    {
        private const ushort Magic = 0x5452; // 'TR'
        private const ushort Version = 1;

        public static byte[] Serialize(TerrainChunkData chunk)
        {
            using var ms = new MemoryStream(chunk.Width * chunk.Depth * 8);
            using var writer = new BinaryWriter(ms);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(chunk.Version);
            writer.Write(chunk.Width);
            writer.Write(chunk.Depth);

            foreach (var tile in chunk.AsSpan())
            {
                writer.Write(tile.HeightNW);
                writer.Write(tile.HeightNE);
                writer.Write(tile.HeightSW);
                writer.Write(tile.HeightSE);
                writer.Write(tile.MaterialId);
                writer.Write((byte)tile.Flags);
            }

            writer.Flush();
            return ms.ToArray();
        }

        public static TerrainChunkData Deserialize(ReadOnlySpan<byte> buffer, UltraSim.ECS.Components.ChunkLocation chunkLocation)
        {
            using var ms = new MemoryStream(buffer.ToArray(), writable: false);
            using var reader = new BinaryReader(ms);
            ushort magic = reader.ReadUInt16();
            if (magic != Magic)
                throw new InvalidDataException("Invalid terrain chunk header.");

            ushort version = reader.ReadUInt16();
            if (version != Version)
                throw new InvalidDataException($"Unsupported terrain chunk version {version}.");

            _ = reader.ReadUInt64(); // source version
            int width = reader.ReadInt32();
            int depth = reader.ReadInt32();
            if (width != TerrainChunkData.TileSizeXZ || depth != TerrainChunkData.TileSizeXZ)
                throw new InvalidDataException("Unexpected terrain chunk dimensions.");

            var chunk = new TerrainChunkData(chunkLocation);
            var span = chunk.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                var tile = new TerrainTile
                {
                    HeightNW = reader.ReadSByte(),
                    HeightNE = reader.ReadSByte(),
                    HeightSW = reader.ReadSByte(),
                    HeightSE = reader.ReadSByte(),
                    MaterialId = reader.ReadByte(),
                    Flags = (TerrainTileFlags)reader.ReadByte()
                };
                span[i] = tile;
            }
            return chunk;
        }
    }
}
