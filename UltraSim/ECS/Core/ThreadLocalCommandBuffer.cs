#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace UltraSim.ECS
{
    /// <summary>
    /// Thread-local command buffer with pooling for zero-allocation structural changes.
    /// 
    /// Design:
    /// - Each thread gets its own buffer (thread-local storage)
    /// - Buffers are pooled and reused across frames
    /// - Flush merges all thread-local buffers into World queues (thread-safe)
    /// - Works WITH your existing World.Enqueue* APIs
    /// 
    /// Usage:
    ///   // Inside parallel loop
    ///   _commandBuffer.EnqueueComponentAdd(entityIndex, componentId, value);
    ///   
    ///   // After parallel loop completes
    ///   _commandBuffer.FlushAll(world);
    /// </summary>
    public sealed class ThreadLocalCommandBuffer
    {
        private struct Command
        {
            public enum Type { AddComponent, RemoveComponent, DestroyEntity }

            public Type CommandType;
            public int EntityIndex;
            public int ComponentTypeId;
            public object? BoxedValue;
        }

        // Thread-local storage with pooling
        private readonly ThreadLocal<List<Command>> _threadLocalBuffers;
        private readonly ConcurrentBag<List<Command>> _pool;

        public ThreadLocalCommandBuffer(int initialCapacityPerThread = 256)
        {
            _pool = new ConcurrentBag<List<Command>>();

            _threadLocalBuffers = new ThreadLocal<List<Command>>(() =>
            {
                // Try to reuse from pool, otherwise create new
                if (!_pool.TryTake(out var buffer))
                {
                    buffer = new List<Command>(initialCapacityPerThread);
                }
                return buffer;
            }, trackAllValues: true);
        }

        /// <summary>
        /// Thread-safe: Queue component addition from any thread.
        /// </summary>
        public void EnqueueComponentAdd(int entityIndex, int componentTypeId, object boxedValue)
        {
            _threadLocalBuffers.Value!.Add(new Command
            {
                CommandType = Command.Type.AddComponent,
                EntityIndex = entityIndex,
                ComponentTypeId = componentTypeId,
                BoxedValue = boxedValue
            });
        }

        /// <summary>
        /// Thread-safe: Queue component removal from any thread.
        /// </summary>
        public void EnqueueComponentRemove(int entityIndex, int componentTypeId)
        {
            _threadLocalBuffers.Value!.Add(new Command
            {
                CommandType = Command.Type.RemoveComponent,
                EntityIndex = entityIndex,
                ComponentTypeId = componentTypeId
            });
        }

        /// <summary>
        /// Thread-safe: Queue entity destruction from any thread.
        /// </summary>
        public void EnqueueEntityDestroy(int entityIndex)
        {
            _threadLocalBuffers.Value!.Add(new Command
            {
                CommandType = Command.Type.DestroyEntity,
                EntityIndex = entityIndex
            });
        }

        /// <summary>
        /// Flush all thread-local buffers into World's existing queue system.
        /// Call this AFTER parallel work completes, BEFORE World.ApplyDeferredChanges().
        /// </summary>
        public void FlushAll(World world)
        {
            foreach (var buffer in _threadLocalBuffers.Values)
            {
                if (buffer == null || buffer.Count == 0)
                    continue;

                // Merge commands into World's existing queue system
                foreach (var cmd in buffer)
                {
                    switch (cmd.CommandType)
                    {
                        case Command.Type.AddComponent:
                            world.EnqueueComponentAdd(cmd.EntityIndex, cmd.ComponentTypeId, cmd.BoxedValue!);
                            break;

                        case Command.Type.RemoveComponent:
                            world.EnqueueComponentRemove(cmd.EntityIndex, cmd.ComponentTypeId);
                            break;

                        case Command.Type.DestroyEntity:
                            world.EnqueueDestroyEntity(new Entity(cmd.EntityIndex, 0)); // Version not needed for queue
                            break;
                    }
                }

                // Clear and return to pool
                buffer.Clear();
                _pool.Add(buffer);
            }
        }

        /// <summary>
        /// Clear all buffers without flushing (for cleanup/reset).
        /// </summary>
        public void ClearAll()
        {
            foreach (var buffer in _threadLocalBuffers.Values)
            {
                if (buffer != null)
                {
                    buffer.Clear();
                    _pool.Add(buffer);
                }
            }
        }

        /// <summary>
        /// Returns approximate count of pending commands across all threads.
        /// </summary>
        public int PendingCount
        {
            get
            {
                int total = 0;
                foreach (var buffer in _threadLocalBuffers.Values)
                {
                    if (buffer != null)
                        total += buffer.Count;
                }
                return total;
            }
        }

        public void Dispose()
        {
            _threadLocalBuffers?.Dispose();
        }
    }
}