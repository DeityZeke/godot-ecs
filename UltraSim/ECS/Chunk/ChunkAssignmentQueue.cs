#nullable enable

using System.Collections.Concurrent;
using Godot;
using UltraSim.ECS.Components;

namespace UltraSim.ECS.Chunk
{
    public readonly struct ChunkAssignmentRequest
    {
        public ChunkAssignmentRequest(Entity entity, ChunkLocation location)
        {
            Entity = entity;
            Location = location;
        }

        public Entity Entity { get; }
        public ChunkLocation Location { get; }
    }

    public static class ChunkAssignmentQueue
    {
        private static readonly ConcurrentQueue<ChunkAssignmentRequest> _queue = new();

        public static void Enqueue(Entity entity, ChunkLocation location)
        {
            _queue.Enqueue(new ChunkAssignmentRequest(entity, location));
        }

        public static void Enqueue(Entity entity, Vector3 worldPosition, ChunkManager chunkManager)
        {
            var chunk = chunkManager.WorldToChunk(worldPosition.X, worldPosition.Y, worldPosition.Z);
            Enqueue(entity, chunk);
        }

        internal static bool TryDequeue(out ChunkAssignmentRequest request) => _queue.TryDequeue(out request);
        public static int Count => _queue.Count;

        public static void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
