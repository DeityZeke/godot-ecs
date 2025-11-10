#nullable enable

using System;
using System.Collections.Generic;
using UltraSim;
using UltraSim.ECS.Components;
using UltraSim.ECS.Systems;
using UltraSim.Terrain;
using UltraSim.ECS;

namespace Server.Terrain
{
    /// <summary>
    /// Server-side ECS system managing terrain chunk lifecycle.
    /// Handles chunk loading, unloading, streaming, and persistence.
    /// </summary>
    public sealed class TerrainManagerSystem : BaseSystem
    {
        private readonly Dictionary<ChunkLocation, TerrainChunkData> _loadedChunks = new();
        private readonly Queue<ChunkLocation> _loadQueue = new();
        private readonly Queue<ChunkLocation> _unloadQueue = new();

        private ITerrainGenerator? _generator;
        private string _saveDirectory = "Saves/Terrain";
        private int _maxLoadedChunks = 1000;
        private int _chunksLoadedPerFrame = 4;
        private int _radiusXZ = 8; // 256m radius around tracked positions
        private int _radiusY = 2;  // 64m vertical
        private bool _autoSave = true;

        public override int SystemId => GetHashCode();
        public override string Name => "Terrain Manager";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        public override TickRate Rate => TickRate.Tick100ms; // 10 Hz streaming updates

        /// <summary>
        /// Sets the terrain generator for creating new chunks.
        /// </summary>
        public void SetGenerator(ITerrainGenerator generator)
        {
            _generator = generator;
            Logging.Log($"[{Name}] Generator set: {generator.GetType().Name}", source: Name);
        }

        /// <summary>
        /// Configures the save directory for terrain chunks.
        /// </summary>
        public void SetSaveDirectory(string directory)
        {
            _saveDirectory = directory;
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            Logging.Log($"[{Name}] Save directory: {directory}", source: Name);
        }

        /// <summary>
        /// Gets a loaded terrain chunk, or null if not loaded.
        /// </summary>
        public TerrainChunkData? GetChunk(ChunkLocation location)
        {
            return _loadedChunks.TryGetValue(location, out var chunk) ? chunk : null;
        }

        /// <summary>
        /// Gets or loads a terrain chunk (synchronous - may cause hitches).
        /// Prefer using RequestChunk() for async streaming.
        /// </summary>
        public TerrainChunkData GetOrLoadChunk(ChunkLocation location)
        {
            if (_loadedChunks.TryGetValue(location, out var chunk))
            {
                return chunk;
            }

            // Try load from disk
            chunk = LoadChunkFromDisk(location);
            if (chunk != null)
            {
                _loadedChunks[location] = chunk;
                return chunk;
            }

            // Generate new chunk
            if (_generator == null)
            {
                Logging.Log($"[{Name}] No generator set, creating empty chunk at {location}", LogSeverity.Warning, Name);
                chunk = new TerrainChunkData(location);
            }
            else
            {
                chunk = _generator.GenerateChunk(location);
            }

            _loadedChunks[location] = chunk;
            return chunk;
        }

        /// <summary>
        /// Requests a chunk to be loaded asynchronously (queued).
        /// </summary>
        public void RequestChunk(ChunkLocation location)
        {
            if (!_loadedChunks.ContainsKey(location) && !_loadQueue.Contains(location))
            {
                _loadQueue.Enqueue(location);
            }
        }

        /// <summary>
        /// Marks a chunk for unloading (will be saved if modified).
        /// </summary>
        public void UnloadChunk(ChunkLocation location)
        {
            if (_loadedChunks.ContainsKey(location) && !_unloadQueue.Contains(location))
            {
                _unloadQueue.Enqueue(location);
            }
        }

        /// <summary>
        /// Saves a chunk to disk.
        /// </summary>
        public void SaveChunk(ChunkLocation location)
        {
            if (!_loadedChunks.TryGetValue(location, out var chunk))
            {
                return;
            }

            SaveChunkToDisk(chunk);
        }

        /// <summary>
        /// Saves all loaded chunks to disk.
        /// </summary>
        public void SaveAllChunks()
        {
            int savedCount = 0;
            foreach (var chunk in _loadedChunks.Values)
            {
                SaveChunkToDisk(chunk);
                savedCount++;
            }
            Logging.Log($"[{Name}] Saved {savedCount} terrain chunks", source: Name);
        }

        /// <summary>
        /// Requests chunks around a world position to be loaded.
        /// </summary>
        public void RequestChunksAroundPosition(Position worldPos)
        {
            // Convert to terrain chunk coordinates (2×2 terrain chunks per entity chunk)
            int terrainChunkX = (int)Math.Floor(worldPos.X / TerrainConstants.ChunkWorldSize);
            int terrainChunkZ = (int)Math.Floor(worldPos.Z / TerrainConstants.ChunkWorldSize);
            int terrainChunkY = (int)Math.Floor(worldPos.Y / TerrainConstants.ChunkWorldSize);

            for (int y = -_radiusY; y <= _radiusY; y++)
            {
                for (int z = -_radiusXZ; z <= _radiusXZ; z++)
                {
                    for (int x = -_radiusXZ; x <= _radiusXZ; x++)
                    {
                        var location = new ChunkLocation(
                            terrainChunkX + x,
                            terrainChunkZ + z,
                            terrainChunkY + y
                        );
                        RequestChunk(location);
                    }
                }
            }
        }

        public override void Update(World world, double delta)
        {
            // Process load queue
            int loaded = 0;
            while (_loadQueue.Count > 0 && loaded < _chunksLoadedPerFrame)
            {
                var location = _loadQueue.Dequeue();

                if (_loadedChunks.ContainsKey(location))
                {
                    continue; // Already loaded
                }

                // Try load from disk first
                var chunk = LoadChunkFromDisk(location);

                // Generate if not found
                if (chunk == null && _generator != null)
                {
                    chunk = _generator.GenerateChunk(location);
                }
                else if (chunk == null)
                {
                    chunk = new TerrainChunkData(location); // Empty chunk
                }

                _loadedChunks[location] = chunk;
                loaded++;
            }

            // Process unload queue
            while (_unloadQueue.Count > 0)
            {
                var location = _unloadQueue.Dequeue();

                if (!_loadedChunks.TryGetValue(location, out var chunk))
                {
                    continue;
                }

                // Save if modified and auto-save enabled
                if (_autoSave && chunk.Version > 1)
                {
                    SaveChunkToDisk(chunk);
                }

                _loadedChunks.Remove(location);
            }

            // Enforce max loaded chunks limit (LRU eviction)
            if (_loadedChunks.Count > _maxLoadedChunks)
            {
                // TODO: Implement LRU eviction based on last access time
                // For now, just log warning
                if (_loadedChunks.Count % 100 == 0)
                {
                    Logging.Log($"[{Name}] Warning: {_loadedChunks.Count} chunks loaded (limit: {_maxLoadedChunks})", LogSeverity.Warning, Name);
                }
            }
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
            // Format: Saves/Terrain/y0/terrain_x_z.chunk
            string yDir = $"y{location.Y}";
            return System.IO.Path.Combine(_saveDirectory, yDir, $"terrain_{location.X}_{location.Z}.chunk");
        }

        public void Cleanup(World world)
        {
            // Save all chunks on shutdown
            if (_autoSave)
            {
                SaveAllChunks();
            }

            _loadedChunks.Clear();
            _loadQueue.Clear();
            _unloadQueue.Clear();
        }

        /// <summary>
        /// Gets debug info about loaded chunks.
        /// </summary>
        public string GetDebugInfo()
        {
            return $"Loaded: {_loadedChunks.Count}, Queue: {_loadQueue.Count}, Unload: {_unloadQueue.Count}";
        }
    }
}
