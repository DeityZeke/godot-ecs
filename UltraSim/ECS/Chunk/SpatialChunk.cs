#nullable enable

using System.Collections.Generic;
using UltraSim.ECS.Components;

namespace UltraSim.ECS.Chunk
{
    /// <summary>
    /// Represents a spatial chunk that tracks entities within its bounds.
    /// Does NOT modify entities - just tracks their packed IDs.
    /// </summary>
    public sealed class SpatialChunk
    {
        public ChunkLocation Location { get; }

        private readonly HashSet<ulong> _trackedEntities = new();
        private readonly object _lock = new object();

        public int EntityCount
        {
            get
            {
                lock (_lock)
                {
                    return _trackedEntities.Count;
                }
            }
        }

        public bool IsActive => EntityCount > 0;

        public SpatialChunk(ChunkLocation location)
        {
            Location = location;
        }

        /// <summary>
        /// Start tracking an entity in this chunk.
        /// Thread-safe.
        /// </summary>
        public void TrackEntity(ulong entityPacked)
        {
            lock (_lock)
            {
                _trackedEntities.Add(entityPacked);
            }
        }

        /// <summary>
        /// Stop tracking an entity in this chunk.
        /// Thread-safe. Safe to call even if entity not tracked.
        /// </summary>
        public void StopTracking(ulong entityPacked)
        {
            lock (_lock)
            {
                _trackedEntities.Remove(entityPacked);
            }
        }

        /// <summary>
        /// Check if entity is currently tracked in this chunk.
        /// Thread-safe.
        /// </summary>
        public bool IsTracking(ulong entityPacked)
        {
            lock (_lock)
            {
                return _trackedEntities.Contains(entityPacked);
            }
        }

        /// <summary>
        /// Get all tracked entities (copy for thread safety).
        /// </summary>
        public List<ulong> GetTrackedEntities()
        {
            lock (_lock)
            {
                return new List<ulong>(_trackedEntities);
            }
        }

        /// <summary>
        /// Clear all tracked entities.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _trackedEntities.Clear();
            }
        }
    }
}
