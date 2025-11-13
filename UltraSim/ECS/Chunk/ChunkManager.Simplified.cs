#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UltraSim;

namespace UltraSim.ECS.Chunk
{
    /// <summary>
    /// Simplified ChunkManager - pure spatial index.
    /// Chunks are pre-created and never destroyed.
    /// Each chunk tracks entity.Packed values (no component manipulation).
    /// </summary>
    public sealed class SimplifiedChunkManager
    {
        // === CONFIGURATION ===
        public int ChunkSizeXZ { get; }
        public int ChunkSizeY { get; }

        // === STORAGE ===
        // Sparse storage: Only creates chunks as entities enter them
        private readonly ConcurrentDictionary<ChunkLocation, SpatialChunk> _chunks = new();

        // === STATISTICS ===
        public int TotalChunks => _chunks.Count;
        public int ActiveChunks
        {
            get
            {
                int count = 0;
                foreach (var chunk in _chunks.Values)
                {
                    if (chunk.IsActive) count++;
                }
                return count;
            }
        }

        public SimplifiedChunkManager(int chunkSizeXZ = 64, chunkSizeY = 32)
        {
            ChunkSizeXZ = chunkSizeXZ;
            ChunkSizeY = chunkSizeY;

            Logging.Log($"[SimplifiedChunkManager] Initialized - ChunkSize: {chunkSizeXZ}x{chunkSizeY}x{chunkSizeXZ}");
        }

        // === COORDINATE CONVERSION ===

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

        // === CHUNK ACCESS ===

        /// <summary>
        /// Get or create a chunk at the specified location.
        /// Thread-safe.
        /// </summary>
        public SpatialChunk GetOrCreateChunk(ChunkLocation location)
        {
            return _chunks.GetOrAdd(location, loc => new SpatialChunk(loc));
        }

        /// <summary>
        /// Get chunk if it exists, null otherwise.
        /// </summary>
        public SpatialChunk? GetChunk(ChunkLocation location)
        {
            return _chunks.TryGetValue(location, out var chunk) ? chunk : null;
        }

        /// <summary>
        /// Check if chunk exists at location.
        /// </summary>
        public bool ChunkExists(ChunkLocation location)
        {
            return _chunks.ContainsKey(location);
        }

        // === ENTITY TRACKING ===

        /// <summary>
        /// Track an entity at the specified location.
        /// Creates chunk if needed.
        /// </summary>
        public void TrackEntity(ulong entityPacked, ChunkLocation location)
        {
            var chunk = GetOrCreateChunk(location);
            chunk.TrackEntity(entityPacked);
        }

        /// <summary>
        /// Stop tracking an entity at the specified location.
        /// Safe to call even if chunk doesn't exist or entity not tracked.
        /// </summary>
        public void StopTracking(ulong entityPacked, ChunkLocation location)
        {
            var chunk = GetChunk(location);
            chunk?.StopTracking(entityPacked);
        }

        /// <summary>
        /// Move entity from one chunk to another.
        /// </summary>
        public void MoveEntity(ulong entityPacked, ChunkLocation fromLocation, ChunkLocation toLocation)
        {
            if (fromLocation.Equals(toLocation))
                return;

            StopTracking(entityPacked, fromLocation);
            TrackEntity(entityPacked, toLocation);
        }

        // === SPATIAL QUERIES ===

        /// <summary>
        /// Get all chunks within a radius of a world-space point.
        /// </summary>
        public List<SpatialChunk> GetChunksInRadius(float worldX, float worldY, float worldZ, float radius)
        {
            var result = new List<SpatialChunk>();
            float radiusSq = radius * radius;

            // Calculate chunk bounds for search
            int minChunkX = (int)Math.Floor((worldX - radius) / ChunkSizeXZ);
            int maxChunkX = (int)Math.Floor((worldX + radius) / ChunkSizeXZ);
            int minChunkZ = (int)Math.Floor((worldZ - radius) / ChunkSizeXZ);
            int maxChunkZ = (int)Math.Floor((worldZ + radius) / ChunkSizeXZ);
            int minChunkY = (int)Math.Floor((worldY - radius) / ChunkSizeY);
            int maxChunkY = (int)Math.Floor((worldY + radius) / ChunkSizeY);

            foreach (var kvp in _chunks)
            {
                var location = kvp.Key;
                var chunk = kvp.Value;

                if (location.X < minChunkX || location.X > maxChunkX) continue;
                if (location.Z < minChunkZ || location.Z > maxChunkZ) continue;
                if (location.Y < minChunkY || location.Y > maxChunkY) continue;

                var bounds = ChunkToWorldBounds(location);
                float distSq = bounds.GetSquaredDistanceToPoint(worldX, worldY, worldZ);

                if (distSq <= radiusSq)
                    result.Add(chunk);
            }

            return result;
        }

        /// <summary>
        /// Get all chunks that intersect with an axis-aligned bounding box.
        /// </summary>
        public List<SpatialChunk> GetChunksInBounds(ChunkBounds queryBounds)
        {
            var result = new List<SpatialChunk>();

            // Convert world bounds to chunk coordinates
            int minChunkX = (int)Math.Floor(queryBounds.MinX / ChunkSizeXZ);
            int maxChunkX = (int)Math.Floor(queryBounds.MaxX / ChunkSizeXZ);
            int minChunkZ = (int)Math.Floor(queryBounds.MinZ / ChunkSizeXZ);
            int maxChunkZ = (int)Math.Floor(queryBounds.MaxZ / ChunkSizeXZ);
            int minChunkY = (int)Math.Floor(queryBounds.MinY / ChunkSizeY);
            int maxChunkY = (int)Math.Floor(queryBounds.MaxY / ChunkSizeY);

            foreach (var kvp in _chunks)
            {
                var location = kvp.Key;
                var chunk = kvp.Value;

                if (location.X < minChunkX || location.X > maxChunkX) continue;
                if (location.Z < minChunkZ || location.Z > maxChunkZ) continue;
                if (location.Y < minChunkY || location.Y > maxChunkY) continue;

                result.Add(chunk);
            }

            return result;
        }

        // === STATISTICS ===

        public string GetStatistics()
        {
            int totalChunks = _chunks.Count;
            int activeChunks = 0;
            int totalEntities = 0;

            foreach (var chunk in _chunks.Values)
            {
                int count = chunk.EntityCount;
                if (count > 0)
                {
                    activeChunks++;
                    totalEntities += count;
                }
            }

            return $"[SimplifiedChunkManager] Chunks: {totalChunks} total, {activeChunks} active | Entities: {totalEntities} tracked";
        }
    }
}
