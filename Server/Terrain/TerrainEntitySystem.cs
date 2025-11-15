#nullable enable

using System;
using System.Collections.Generic;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.ECS.Systems;
using UltraSim.Terrain;
using Server.ECS.Components.Terrain;

namespace Server.Terrain
{
    /// <summary>
    /// Server-side ECS system that creates terrain chunk ENTITIES.
    /// Each terrain chunk becomes an entity with TerrainChunkComponent + rendering components.
    /// Integrates directly with the existing Hybrid Render System.
    /// </summary>
    public sealed class TerrainEntitySystem : BaseSystem
    {
        private readonly Dictionary<ChunkLocation, Entity> _chunkEntities = new();
        private readonly Dictionary<ChunkLocation, TerrainChunkData> _chunkData = new();
        private readonly Queue<ChunkLocation> _loadQueue = new();

        private ITerrainGenerator? _generator;
        private string _saveDirectory = "Saves/Terrain";
        private int _chunksLoadedPerFrame = 4;
        private int _radiusXZ = 8;
        private int _radiusY = 2;
        private bool _autoSave = true;
        private bool _commandBufferDisabledLogged = false;

        public override int SystemId => GetHashCode();
        public override string Name => "Terrain Entity Manager";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => new[] { typeof(TerrainChunkComponent), typeof(ChunkBounds), typeof(StaticRenderTag) };
        public override TickRate Rate => TickRate.Tick100ms;

        public void SetGenerator(ITerrainGenerator generator)
        {
            _generator = generator;
            Logging.Log($"[{Name}] Generator set: {generator.GetType().Name}", source: Name);
        }

        public void SetSaveDirectory(string directory)
        {
            _saveDirectory = directory;
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
        }

        public TerrainChunkData? GetChunkData(ChunkLocation location)
        {
            return _chunkData.TryGetValue(location, out var chunk) ? chunk : null;
        }

        public void RequestChunksAroundPosition(Position worldPos)
        {
            int centerChunkX = (int)Math.Floor(worldPos.X / TerrainConstants.ChunkWorldSize);
            int centerChunkZ = (int)Math.Floor(worldPos.Z / TerrainConstants.ChunkWorldSize);
            int centerChunkY = (int)Math.Floor(worldPos.Y / TerrainConstants.ChunkWorldSize);

            for (int y = -_radiusY; y <= _radiusY; y++)
            {
                for (int z = -_radiusXZ; z <= _radiusXZ; z++)
                {
                    for (int x = -_radiusXZ; x <= _radiusXZ; x++)
                    {
                        var location = new ChunkLocation(
                            centerChunkX + x,
                            centerChunkZ + z,
                            centerChunkY + y
                        );

                        if (!_chunkEntities.ContainsKey(location) && !_loadQueue.Contains(location))
                        {
                            _loadQueue.Enqueue(location);
                        }
                    }
                }
            }
        }

        public override void Update(World world, double delta)
        {
            if (!_commandBufferDisabledLogged)
            {
                Logging.Log($"[{Name}] Terrain entity spawning paused (CommandBuffer implementation removed)", LogSeverity.Warning, Name);
                _commandBufferDisabledLogged = true;
            }

            return;

#if false
            int loaded = 0;
            while (_loadQueue.Count > 0 && loaded < _chunksLoadedPerFrame)
            {
                var location = _loadQueue.Dequeue();

                if (_chunkEntities.ContainsKey(location))
                {
                    continue; // Already loaded
                }

                // Load or generate chunk data
                var chunkData = LoadChunkFromDisk(location);
                if (chunkData == null && _generator != null)
                {
                    chunkData = _generator.GenerateChunk(location);
                }
                else if (chunkData == null)
                {
                    chunkData = new TerrainChunkData(location);
                }

                _chunkData[location] = chunkData;

                // Create entity for this terrain chunk
                var buffer = new CommandBuffer();
                buffer.CreateEntity(e => e
                    .Add(new TerrainChunkComponent
                    {
                        ChunkData = chunkData,
                        Location = location,
                        IsDirty = true, // Needs initial mesh
                        MeshVersion = 0
                    })
                    .Add(CalculateChunkBounds(location))
                    .Add(new StaticRenderTag()) // Terrain is static → MultiMesh rendering
                );

                buffer.Apply(world);

                // Track the entity (we'll need to query it later)
                // TODO: Store Entity from buffer result
                loaded++;
            }
#endif
        }

        private ChunkBounds CalculateChunkBounds(ChunkLocation location)
        {
            float chunkSize = TerrainConstants.ChunkWorldSize;
            float worldX = location.X * chunkSize;
            float worldZ = location.Z * chunkSize;
            float worldY = location.Y * chunkSize;

            return new ChunkBounds
            {
                MinX = worldX,
                MaxX = worldX + chunkSize,
                MinZ = worldZ,
                MaxZ = worldZ + chunkSize,
                MinY = worldY - 64, // Terrain can go below chunk base
                MaxY = worldY + 64  // Terrain can go above chunk base
            };
        }

        private TerrainChunkData? LoadChunkFromDisk(ChunkLocation location)
        {
            string filePath = GetChunkFilePath(location);
            if (!System.IO.File.Exists(filePath))
            {
                return null;
            }

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(filePath);
                return TerrainChunkSerializer.Deserialize(data, location);
            }
            catch (Exception ex)
            {
                Logging.Log($"[{Name}] Failed to load chunk {location}: {ex.Message}", LogSeverity.Error, Name);
                return null;
            }
        }

        private void SaveChunkToDisk(TerrainChunkData chunk)
        {
            string filePath = GetChunkFilePath(chunk.Chunk);
            string directory = System.IO.Path.GetDirectoryName(filePath)!;

            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            try
            {
                byte[] data = TerrainChunkSerializer.Serialize(chunk);
                System.IO.File.WriteAllBytes(filePath, data);
            }
            catch (Exception ex)
            {
                Logging.Log($"[{Name}] Failed to save chunk {chunk.Chunk}: {ex.Message}", LogSeverity.Error, Name);
            }
        }

        private string GetChunkFilePath(ChunkLocation location)
        {
            string yDir = $"y{location.Y}";
            return System.IO.Path.Combine(_saveDirectory, yDir, $"terrain_{location.X}_{location.Z}.chunk");
        }

        public void SaveAllChunks()
        {
            int saved = 0;
            foreach (var chunk in _chunkData.Values)
            {
                if (chunk.Version > 1) // Only save modified chunks
                {
                    SaveChunkToDisk(chunk);
                    saved++;
                }
            }
            Logging.Log($"[{Name}] Saved {saved} terrain chunks", source: Name);
        }

        public void Cleanup(World world)
        {
            if (_autoSave)
            {
                SaveAllChunks();
            }

            _chunkEntities.Clear();
            _chunkData.Clear();
            _loadQueue.Clear();
        }
    }
}
