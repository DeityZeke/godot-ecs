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

        private readonly ChunkLookupTable _locationIndex = new();
        private readonly Dictionary<Entity, ChunkLocation> _entityToLocation = new();
        private readonly Dictionary<(int X, int Z), ColumnStats> _columnStats = new();
        private readonly Dictionary<Entity, ChunkRuntimeInfo> _runtimeInfo = new();

        private struct ColumnStats
        {
            public int Count;
            public int MinY;
            public int MaxY;
        }

        private struct ChunkRuntimeInfo
        {
            public ulong LastAccessFrame;
        }

        // === STATISTICS ===

        public int TotalChunkColumns => _columnStats.Count;
        public int TotalChunks => _entityToLocation.Count;
        public ulong CurrentFrame => _currentFrame;

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
        /// Called by ChunkSystem after creating or reusing the chunk entity.
        /// </summary>
        public void RegisterChunk(Entity chunkEntity, ChunkLocation location)
        {
            if (_locationIndex.ContainsKey(location))
            {
                Logging.Log($"[ChunkManager] WARNING: Chunk already exists at {location}", LogSeverity.Warning);
                return;
            }

            _entityToLocation[chunkEntity] = location;
            _locationIndex.TryAdd(location, chunkEntity, overwrite: true);
            _runtimeInfo[chunkEntity] = new ChunkRuntimeInfo { LastAccessFrame = _currentFrame };

            var columnKey = (location.X, location.Z);
            if (_columnStats.TryGetValue(columnKey, out var stats))
            {
                stats.Count++;
                stats.MinY = Math.Min(stats.MinY, location.Y);
                stats.MaxY = Math.Max(stats.MaxY, location.Y);
                _columnStats[columnKey] = stats;
            }
            else
            {
                _columnStats[columnKey] = new ColumnStats
                {
                    Count = 1,
                    MinY = location.Y,
                    MaxY = location.Y
                };
            }

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

            // Remove from lookup tables
            _entityToLocation.Remove(chunkEntity);
            _locationIndex.Remove(location, out _);
            _runtimeInfo.Remove(chunkEntity);

            var columnKey = (location.X, location.Z);
            if (_columnStats.TryGetValue(columnKey, out var stats))
            {
                stats.Count--;
                if (stats.Count <= 0)
                {
                    _columnStats.Remove(columnKey);
                }
                else
                {
                    if (location.Y == stats.MinY || location.Y == stats.MaxY)
                    {
                        RecomputeColumnExtents(columnKey, ref stats);
                    }
                    _columnStats[columnKey] = stats;
                }
            }

        }

        // === CHUNK QUERIES ===

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ChunkExists(ChunkLocation location) => _locationIndex.ContainsKey(location);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetChunk(ChunkLocation location)
        {
            return _locationIndex.TryGetValue(location, out var entity) ? entity : Entity.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetChunkLocation(Entity chunkEntity, out ChunkLocation location)
        {
            return _entityToLocation.TryGetValue(chunkEntity, out location);
        }

        public void TouchChunk(Entity chunkEntity)
        {
            if (_runtimeInfo.TryGetValue(chunkEntity, out var info))
            {
                info.LastAccessFrame = _currentFrame;
                _runtimeInfo[chunkEntity] = info;
            }
        }

        public List<(Entity Entity, ChunkLocation Location, ulong LastAccess)> CollectStaleChunks(ulong olderThanFrame, int maxResults)
        {
            var result = new List<(Entity Entity, ChunkLocation Location, ulong LastAccess)>();
            if (maxResults <= 0)
                return result;

            foreach (var kvp in _runtimeInfo)
            {
                if (kvp.Value.LastAccessFrame <= olderThanFrame &&
                    _entityToLocation.TryGetValue(kvp.Key, out var location))
                {
                    result.Add((kvp.Key, location, kvp.Value.LastAccessFrame));
                }
            }

            result.Sort((a, b) => a.LastAccess.CompareTo(b.LastAccess));
            if (result.Count > maxResults)
                result.RemoveRange(maxResults, result.Count - maxResults);
            return result;
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

            _locationIndex.ForEach((location, chunkEntity) =>
            {
                if (location.X < minChunkX || location.X > maxChunkX)
                    return;
                if (location.Z < minChunkZ || location.Z > maxChunkZ)
                    return;
                if (location.Y < minChunkY || location.Y > maxChunkY)
                    return;

                var bounds = ChunkToWorldBounds(location);
                float distSq = bounds.GetSquaredDistanceToPoint(worldX, worldY, worldZ);

                if (distSq <= radiusSq)
                    result.Add(chunkEntity);
            });

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

            _locationIndex.ForEach((location, chunkEntity) =>
            {
                if (location.X < minChunkX || location.X > maxChunkX)
                    return;
                if (location.Z < minChunkZ || location.Z > maxChunkZ)
                    return;
                if (location.Y < minChunkY || location.Y > maxChunkY)
                    return;

                var chunkBounds = ChunkToWorldBounds(location);
                if (chunkBounds.Intersects(queryBounds))
                    result.Add(chunkEntity);
            });

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

        /// <summary>
        /// Enumerate all active chunks with their locations.
        /// </summary>
        public IEnumerable<KeyValuePair<ChunkLocation, Entity>> EnumerateChunks()
        {
            var items = new List<KeyValuePair<ChunkLocation, Entity>>(_locationIndex.Count);
            _locationIndex.ForEach((loc, entity) => items.Add(new KeyValuePair<ChunkLocation, Entity>(loc, entity)));
            foreach (var kvp in items)
                yield return kvp;
        }

        private void RecomputeColumnExtents((int X, int Z) key, ref ColumnStats stats)
        {
            int count = 0;
            int minY = int.MaxValue;
            int maxY = int.MinValue;

            _locationIndex.ForEach((location, _) =>
            {
                if (location.X == key.X && location.Z == key.Z)
                {
                    count++;
                    minY = Math.Min(minY, location.Y);
                    maxY = Math.Max(maxY, location.Y);
                }
            });

            if (count == 0)
            {
                stats.Count = 0;
                stats.MinY = 0;
                stats.MaxY = 0;
            }
            else
            {
                stats.Count = count;
                stats.MinY = minY;
                stats.MaxY = maxY;
            }
        }

        // === DEBUGGING / STATISTICS ===

        public string GetStatistics()
        {
            int totalChunks = _entityToLocation.Count;
            int totalColumns = _columnStats.Count;
            int maxVerticalChunks = 0;

            foreach (var stats in _columnStats.Values)
            {
                int verticalCount = stats.MaxY - stats.MinY + 1;
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
