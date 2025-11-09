#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UltraSim.ECS.Components;
using UltraSim;

namespace UltraSim.ECS.Chunk
{
    /// <summary>
    /// Simplified ChunkManager - acts as a spatial index and lookup service.
    /// Does NOT directly manipulate components - ChunkSystem handles that.
    /// Uses Dictionary<(int X, int Z), List<VerticalChunkBand>> for sparse 3D storage.
    /// </summary>
    public class ChunkManager
    {
        // === CONFIGURATION ===

        public int ChunkSizeXZ { get; private set; }
        public int ChunkSizeY { get; private set; }

        private ulong _currentFrame = 0;

        // === ADAPTIVE VERTICAL STORAGE ===

        /// <summary>
        /// Represents a vertical band of chunks at a specific XZ coordinate.
        /// Only allocates chunks that are actually occupied.
        /// </summary>
        private class VerticalChunkBand
        {
            /// <summary>
            /// Sparse storage: Y-index -> Chunk Entity.
            /// </summary>
            public Dictionary<int, Entity> Chunks = new();

            public int MinY = int.MaxValue;
            public int MaxY = int.MinValue;
        }

        /// <summary>
        /// Main storage: (X, Z) -> Vertical band of chunks.
        /// </summary>
        private readonly Dictionary<(int X, int Z), VerticalChunkBand> _chunkColumns = new();

        /// <summary>
        /// Reverse lookup: Entity -> ChunkLocation.
        /// </summary>
        private readonly Dictionary<Entity, ChunkLocation> _entityToLocation = new();

        /// <summary>
        /// Fast lookup: ChunkLocation -> Entity.
        /// </summary>
        private readonly Dictionary<ChunkLocation, Entity> _locationToEntity = new();

        // === STATISTICS ===

        public int TotalChunkColumns => _chunkColumns.Count;
        public int TotalChunks => _entityToLocation.Count;

        public ChunkManager(int chunkSizeXZ = 64, int chunkSizeY = 32)
        {
            ChunkSizeXZ = chunkSizeXZ;
            ChunkSizeY = chunkSizeY;

            Logging.Log($"[ChunkManager] Initialized - ChunkSize: {chunkSizeXZ}x{chunkSizeY}x{chunkSizeXZ}");
        }

        public void IncrementFrame()
        {
            _currentFrame++;
        }

        // === WORLD <-> CHUNK COORDINATE CONVERSION ===

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkLocation WorldToChunk(float worldX, float worldY, float worldZ)
        {
            int chunkX = (int)Math.Floor(worldX / ChunkSizeXZ);
            int chunkZ = (int)Math.Floor(worldZ / ChunkSizeXZ);
            int chunkY = (int)Math.Floor(worldY / ChunkSizeY);

            return new ChunkLocation(chunkX, chunkZ, chunkY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkBounds ChunkToWorldBounds(ChunkLocation location)
        {
            float minX = location.X * ChunkSizeXZ;
            float minY = location.Y * ChunkSizeY;
            float minZ = location.Z * ChunkSizeXZ;

            float maxX = minX + ChunkSizeXZ;
            float maxY = minY + ChunkSizeY;
            float maxZ = minZ + ChunkSizeXZ;

            return new ChunkBounds(minX, minY, minZ, maxX, maxY, maxZ);
        }

        // === CHUNK REGISTRATION ===

        /// <summary>
        /// Register a chunk entity with the manager.
        /// Called by ChunkSystem after creating the chunk entity.
        /// </summary>
        public void RegisterChunk(Entity chunkEntity, ChunkLocation location)
        {
            if (_locationToEntity.ContainsKey(location))
            {
                Logging.Log($"[ChunkManager] WARNING: Chunk already exists at {location}", LogSeverity.Warning);
                return;
            }

            // Register in sparse storage
            var key = (location.X, location.Z);
            if (!_chunkColumns.TryGetValue(key, out var band))
            {
                band = new VerticalChunkBand();
                _chunkColumns[key] = band;
            }

            band.Chunks[location.Y] = chunkEntity;
            band.MinY = Math.Min(band.MinY, location.Y);
            band.MaxY = Math.Max(band.MaxY, location.Y);

            // Register in lookup tables
            _entityToLocation[chunkEntity] = location;
            _locationToEntity[location] = chunkEntity;

            Logging.Log($"[ChunkManager] Registered chunk {location} -> Entity {chunkEntity.Index}", LogSeverity.Debug);
        }

        /// <summary>
        /// Unregister a chunk entity from the manager.
        /// Called by ChunkSystem before destroying the chunk entity.
        /// </summary>
        public void UnregisterChunk(Entity chunkEntity)
        {
            if (!_entityToLocation.TryGetValue(chunkEntity, out ChunkLocation location))
            {
                Logging.Log($"[ChunkManager] WARNING: Tried to unregister non-existent chunk entity {chunkEntity.Index}", LogSeverity.Warning);
                return;
            }

            // Remove from sparse storage
            var key = (location.X, location.Z);
            if (_chunkColumns.TryGetValue(key, out var band))
            {
                band.Chunks.Remove(location.Y);

                if (band.Chunks.Count == 0)
                {
                    _chunkColumns.Remove(key);
                }
                else if (location.Y == band.MinY || location.Y == band.MaxY)
                {
                    // Recalculate bounds
                    band.MinY = int.MaxValue;
                    band.MaxY = int.MinValue;
                    foreach (int y in band.Chunks.Keys)
                    {
                        band.MinY = Math.Min(band.MinY, y);
                        band.MaxY = Math.Max(band.MaxY, y);
                    }
                }
            }

            // Remove from lookup tables
            _entityToLocation.Remove(chunkEntity);
            _locationToEntity.Remove(location);

            Logging.Log($"[ChunkManager] Unregistered chunk {location}", LogSeverity.Debug);
        }

        // === CHUNK QUERIES ===

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ChunkExists(ChunkLocation location)
        {
            return _locationToEntity.ContainsKey(location);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetChunk(ChunkLocation location)
        {
            return _locationToEntity.TryGetValue(location, out Entity entity) ? entity : Entity.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetChunkLocation(Entity chunkEntity, out ChunkLocation location)
        {
            return _entityToLocation.TryGetValue(chunkEntity, out location);
        }

        /// <summary>
        /// Get all chunks within a radius of a world-space point.
        /// </summary>
        public List<Entity> GetChunksInRadius(float worldX, float worldY, float worldZ, float radius)
        {
            var result = new List<Entity>();
            float radiusSq = radius * radius;

            // Calculate chunk bounds for search
            int minChunkX = (int)Math.Floor((worldX - radius) / ChunkSizeXZ);
            int maxChunkX = (int)Math.Floor((worldX + radius) / ChunkSizeXZ);
            int minChunkZ = (int)Math.Floor((worldZ - radius) / ChunkSizeXZ);
            int maxChunkZ = (int)Math.Floor((worldZ + radius) / ChunkSizeXZ);
            int minChunkY = (int)Math.Floor((worldY - radius) / ChunkSizeY);
            int maxChunkY = (int)Math.Floor((worldY + radius) / ChunkSizeY);

            // Iterate through XZ columns
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                for (int z = minChunkZ; z <= maxChunkZ; z++)
                {
                    var key = (x, z);
                    if (!_chunkColumns.TryGetValue(key, out var band))
                        continue;

                    // Search vertical band within Y range
                    int searchMinY = Math.Max(minChunkY, band.MinY);
                    int searchMaxY = Math.Min(maxChunkY, band.MaxY);

                    for (int y = searchMinY; y <= searchMaxY; y++)
                    {
                        if (!band.Chunks.TryGetValue(y, out Entity chunkEntity))
                            continue;

                        // Distance check using chunk bounds
                        var bounds = ChunkToWorldBounds(new ChunkLocation(x, z, y));
                        float distSq = bounds.GetSquaredDistanceToPoint(worldX, worldY, worldZ);

                        if (distSq <= radiusSq)
                            result.Add(chunkEntity);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get all chunks that intersect with an axis-aligned bounding box.
        /// </summary>
        public List<Entity> GetChunksInBounds(ChunkBounds queryBounds)
        {
            var result = new List<Entity>();

            // Calculate chunk range
            int minChunkX = (int)Math.Floor(queryBounds.MinX / ChunkSizeXZ);
            int maxChunkX = (int)Math.Floor(queryBounds.MaxX / ChunkSizeXZ);
            int minChunkZ = (int)Math.Floor(queryBounds.MinZ / ChunkSizeXZ);
            int maxChunkZ = (int)Math.Floor(queryBounds.MaxZ / ChunkSizeXZ);
            int minChunkY = (int)Math.Floor(queryBounds.MinY / ChunkSizeY);
            int maxChunkY = (int)Math.Floor(queryBounds.MaxY / ChunkSizeY);

            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                for (int z = minChunkZ; z <= maxChunkZ; z++)
                {
                    var key = (x, z);
                    if (!_chunkColumns.TryGetValue(key, out var band))
                        continue;

                    int searchMinY = Math.Max(minChunkY, band.MinY);
                    int searchMaxY = Math.Min(maxChunkY, band.MaxY);

                    for (int y = searchMinY; y <= searchMaxY; y++)
                    {
                        if (!band.Chunks.TryGetValue(y, out Entity chunkEntity))
                            continue;

                        var chunkBounds = ChunkToWorldBounds(new ChunkLocation(x, z, y));
                        if (chunkBounds.Intersects(queryBounds))
                            result.Add(chunkEntity);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get all registered chunk entities.
        /// </summary>
        public List<Entity> GetAllChunks()
        {
            var result = new List<Entity>(_entityToLocation.Count);
            foreach (var entity in _entityToLocation.Keys)
                result.Add(entity);
            return result;
        }

        // === DEBUGGING / STATISTICS ===

        public string GetStatistics()
        {
            int totalChunks = _entityToLocation.Count;
            int totalColumns = _chunkColumns.Count;
            int maxVerticalChunks = 0;

            foreach (var band in _chunkColumns.Values)
            {
                int verticalCount = band.MaxY - band.MinY + 1;
                maxVerticalChunks = Math.Max(maxVerticalChunks, verticalCount);
            }

            float avgChunksPerColumn = totalColumns > 0 ? (float)totalChunks / totalColumns : 0;

            return $"ChunkManager Statistics:\n" +
                   $"  Total Chunks: {totalChunks}\n" +
                   $"  XZ Columns: {totalColumns}\n" +
                   $"  Avg Chunks/Column: {avgChunksPerColumn:F2}\n" +
                   $"  Max Vertical Chunks: {maxVerticalChunks}\n" +
                   $"  Frame: {_currentFrame}";
        }
    }
}
