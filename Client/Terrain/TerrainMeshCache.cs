#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim;
using UltraSim.ECS.Components;

namespace Client.Terrain
{
    /// <summary>
    /// LRU cache for terrain meshes.
    /// Manages mesh pooling, eviction, and memory budgets.
    /// </summary>
    public sealed class TerrainMeshCache
    {
        private readonly Dictionary<ChunkLocation, CachedMesh> _meshes = new();
        private readonly LinkedList<ChunkLocation> _lruList = new();
        private readonly Dictionary<ChunkLocation, LinkedListNode<ChunkLocation>> _lruNodes = new();

        private int _maxCachedMeshes = 500;
        private long _maxMemoryBytes = 100 * 1024 * 1024; // 100 MB default
        private long _currentMemoryBytes = 0;

        /// <summary>
        /// Gets or creates a mesh for the specified chunk.
        /// Returns null if chunk data is not available.
        /// </summary>
        public ArrayMesh? GetOrCreateMesh(ChunkLocation location, Func<TerrainMesh?> meshGenerator)
        {
            // Check cache first
            if (_meshes.TryGetValue(location, out var cached))
            {
                // Update LRU
                TouchLRU(location);
                return cached.Mesh;
            }

            // Generate new mesh
            var terrainMesh = meshGenerator();
            if (terrainMesh == null)
            {
                return null;
            }

            var arrayMesh = terrainMesh.ToArrayMesh();
            int memorySize = terrainMesh.EstimateMemoryBytes();

            // Evict if necessary
            while (_currentMemoryBytes + memorySize > _maxMemoryBytes && _lruList.Count > 0)
            {
                EvictLRU();
            }

            while (_meshes.Count >= _maxCachedMeshes && _lruList.Count > 0)
            {
                EvictLRU();
            }

            // Add to cache
            _meshes[location] = new CachedMesh
            {
                Mesh = arrayMesh,
                MemoryBytes = memorySize,
                VersionWhenCached = 0 // TODO: Track chunk version for invalidation
            };

            _currentMemoryBytes += memorySize;

            // Add to LRU
            var node = _lruList.AddFirst(location);
            _lruNodes[location] = node;

            return arrayMesh;
        }

        /// <summary>
        /// Invalidates (removes) a cached mesh.
        /// Call this when terrain is modified.
        /// </summary>
        public void Invalidate(ChunkLocation location)
        {
            if (_meshes.TryGetValue(location, out var cached))
            {
                _currentMemoryBytes -= cached.MemoryBytes;
                cached.Mesh?.Dispose();
                _meshes.Remove(location);

                if (_lruNodes.TryGetValue(location, out var node))
                {
                    _lruList.Remove(node);
                    _lruNodes.Remove(location);
                }
            }
        }

        /// <summary>
        /// Clears the entire cache.
        /// </summary>
        public void Clear()
        {
            foreach (var cached in _meshes.Values)
            {
                cached.Mesh?.Dispose();
            }

            _meshes.Clear();
            _lruList.Clear();
            _lruNodes.Clear();
            _currentMemoryBytes = 0;
        }

        /// <summary>
        /// Sets the maximum number of cached meshes.
        /// </summary>
        public void SetMaxCachedMeshes(int max)
        {
            _maxCachedMeshes = Math.Max(1, max);

            // Evict excess
            while (_meshes.Count > _maxCachedMeshes && _lruList.Count > 0)
            {
                EvictLRU();
            }
        }

        /// <summary>
        /// Sets the maximum memory budget in megabytes.
        /// </summary>
        public void SetMaxMemoryMB(int megabytes)
        {
            _maxMemoryBytes = (long)megabytes * 1024 * 1024;

            // Evict excess
            while (_currentMemoryBytes > _maxMemoryBytes && _lruList.Count > 0)
            {
                EvictLRU();
            }
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        public CacheStats GetStats()
        {
            return new CacheStats
            {
                CachedMeshCount = _meshes.Count,
                MemoryUsedBytes = _currentMemoryBytes,
                MemoryUsedMB = _currentMemoryBytes / (1024.0 * 1024.0),
                MaxCachedMeshes = _maxCachedMeshes,
                MaxMemoryMB = _maxMemoryBytes / (1024.0 * 1024.0)
            };
        }

        private void TouchLRU(ChunkLocation location)
        {
            if (_lruNodes.TryGetValue(location, out var node))
            {
                _lruList.Remove(node);
                node = _lruList.AddFirst(location);
                _lruNodes[location] = node;
            }
        }

        private void EvictLRU()
        {
            if (_lruList.Last == null)
                return;

            var location = _lruList.Last.Value;
            Invalidate(location);
        }

        private struct CachedMesh
        {
            public ArrayMesh? Mesh;
            public int MemoryBytes;
            public ulong VersionWhenCached;
        }

        public struct CacheStats
        {
            public int CachedMeshCount;
            public long MemoryUsedBytes;
            public double MemoryUsedMB;
            public int MaxCachedMeshes;
            public double MaxMemoryMB;
        }
    }
}
