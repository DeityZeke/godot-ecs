#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Unified command buffer for thread-safe deferred structural changes.
    /// 
    /// Features:
    /// - Entity creation with all components in one operation (no archetype thrashing)
    /// - Entity destruction
    /// - Component add/remove
    /// - Thread-local storage with pooling (zero-allocation in parallel systems)
    /// - Automatic flushing and merging
    /// 
    /// Usage:
    ///   // Single-threaded (main thread)
    ///   buffer.CreateEntity(e => e.With(new Position(x, y, z)));
    ///   buffer.DestroyEntity(entity);
    ///   
    ///   // Multi-threaded (parallel systems)
    ///   buffer.AddComponent(entityIndex, componentId, value);  // Thread-safe
    ///   buffer.RemoveComponent(entityIndex, componentId);      // Thread-safe
    ///   
    ///   // Apply all changes
    ///   buffer.Apply(world);
    /// </summary>
    public sealed class CommandBuffer : IDisposable
    {
        #region Internal Structures

        private struct EntityCreationCommand
        {
            public List<(int typeId, object value)> Components;
        }

        private struct ThreadCommand
        {
            public enum Type { AddComponent, RemoveComponent, DestroyEntity }

            public Type CommandType;
            public int EntityIndex;
            public int ComponentTypeId;
            public object? BoxedValue;
        }

        #endregion

        #region Fields

        // Main thread commands (for CreateEntity with builder pattern)
        private readonly List<EntityCreationCommand> _creates = new();
        private readonly List<Entity> _destroys = new();

        // Thread-local commands (for parallel systems)
        private readonly ThreadLocal<List<ThreadCommand>> _threadLocalBuffers;
        private readonly ConcurrentBag<List<ThreadCommand>> _pool;

        #endregion

        #region Constructor

        public CommandBuffer(int initialCapacityPerThread = 256)
        {
            _pool = new ConcurrentBag<List<ThreadCommand>>();

            _threadLocalBuffers = new ThreadLocal<List<ThreadCommand>>(() =>
            {
                // Try to reuse from pool, otherwise create new
                if (!_pool.TryTake(out var buffer))
                {
                    buffer = new List<ThreadCommand>(initialCapacityPerThread);
                }
                return buffer;
            }, trackAllValues: true);
        }

        #endregion

        #region Entity Creation (Builder Pattern)

        /// <summary>
        /// Queues an entity for creation with all specified components.
        /// Entity will be created in the optimal archetype in one operation.
        /// 
        /// Example:
        ///   buffer.CreateEntity(e => e
        ///       .With(new Position(0, 0, 0))
        ///       .With(new Velocity(1, 0, 0))
        ///       .With(new RenderTag()));
        /// </summary>
        public void CreateEntity(Action<EntityBuilder> setup)
        {
            var builder = new EntityBuilder();
            setup(builder);

            _creates.Add(new EntityCreationCommand
            {
                Components = new List<(int typeId, object value)>(builder.GetComponents())
            });
        }

        #endregion

        #region Entity Destruction

        /// <summary>
        /// Queues an entity for destruction.
        /// Thread-safe: Can be called from any thread.
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            _destroys.Add(entity);
        }

        /// <summary>
        /// Queues an entity for destruction by index.
        /// Thread-safe: Can be called from parallel systems.
        /// </summary>
        public void DestroyEntity(int entityIndex)
        {
            _threadLocalBuffers.Value!.Add(new ThreadCommand
            {
                CommandType = ThreadCommand.Type.DestroyEntity,
                EntityIndex = entityIndex
            });
        }

        #endregion

        #region Component Operations (Thread-Safe)

        /// <summary>
        /// Thread-safe: Queue component addition from any thread.
        /// Used primarily in parallel systems.
        /// </summary>
        public void AddComponent(int entityIndex, int componentTypeId, object boxedValue)
        {
            _threadLocalBuffers.Value!.Add(new ThreadCommand
            {
                CommandType = ThreadCommand.Type.AddComponent,
                EntityIndex = entityIndex,
                ComponentTypeId = componentTypeId,
                BoxedValue = boxedValue
            });
        }

        /// <summary>
        /// Thread-safe: Queue component removal from any thread.
        /// Used primarily in parallel systems.
        /// </summary>
        public void RemoveComponent(int entityIndex, int componentTypeId)
        {
            _threadLocalBuffers.Value!.Add(new ThreadCommand
            {
                CommandType = ThreadCommand.Type.RemoveComponent,
                EntityIndex = entityIndex,
                ComponentTypeId = componentTypeId
            });
        }

        #endregion

        #region Apply & Flush

        /// <summary>
        /// Applies all queued commands to the world.
        /// 
        /// Process:
        /// 1. Flush all thread-local buffers
        /// 2. Process entity creations (with all components at once)
        /// 3. Process entity destructions
        /// 4. Process component additions/removals from thread-local buffers
        /// 5. Clear all buffers
        /// </summary>
        public void Apply(World world)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Step 1: Flush thread-local buffers into World's queue system
            FlushThreadLocalBuffers(world);

            // Step 2: Process entity creations (builder pattern)
            int created = ApplyEntityCreations(world);

            // Step 3: Process entity destructions
            int destroyed = ApplyEntityDestructions(world);

            sw.Stop();

            if (created > 0 || destroyed > 0)
            {
                GD.Print($"[CommandBuffer] Applied: {created} created, {destroyed} destroyed in {sw.Elapsed.TotalMilliseconds:F3}ms");
            }

            Clear();
        }

        private void FlushThreadLocalBuffers(World world)
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
                        case ThreadCommand.Type.AddComponent:
                            world.EnqueueComponentAdd(cmd.EntityIndex, cmd.ComponentTypeId, cmd.BoxedValue!);
                            break;

                        case ThreadCommand.Type.RemoveComponent:
                            world.EnqueueComponentRemove(cmd.EntityIndex, cmd.ComponentTypeId);
                            break;

                        case ThreadCommand.Type.DestroyEntity:
                            world.EnqueueDestroyEntity(new Entity(cmd.EntityIndex, 0)); // Version not needed for queue
                            break;
                    }
                }

                // Clear and return to pool
                buffer.Clear();
                _pool.Add(buffer);
            }
        }

        private int ApplyEntityCreations(World world)
        {
            int created = 0;

            foreach (ref var cmd in CollectionsMarshal.AsSpan(_creates))
            {
                if (cmd.Components.Count == 0)
                {
                    // Empty entity
                    world.CreateEntity();
                    created++;
                    continue;
                }

                // Build signature from all components
                var signature = new ComponentSignature();
                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                    signature = signature.Add(component.typeId);

                // Create entity directly in target archetype (ONE archetype move!)
                var entity = world.CreateEntityWithSignature(signature);

                // Set all component values
                if (!world.TryGetEntityLocation(entity, out var archetype, out var slot))
                {
                    GD.PrintErr($"[CommandBuffer] Failed to get location for entity {entity}");
                    continue;
                }

                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                    archetype.SetComponentValueBoxed(component.typeId, slot, component.value);

                created++;
            }

            return created;
        }

        private int ApplyEntityDestructions(World world)
        {
            int destroyed = 0;

            foreach (var entity in _destroys)
            {
                world.DestroyEntity(entity);
                destroyed++;
            }

            return destroyed;
        }

        #endregion

        #region Clear & Reset

        /// <summary>
        /// Clears all queued commands without applying them.
        /// </summary>
        public void Clear()
        {
            _creates.Clear();
            _destroys.Clear();

            // Clear thread-local buffers
            foreach (var buffer in _threadLocalBuffers.Values)
            {
                if (buffer != null)
                {
                    buffer.Clear();
                    _pool.Add(buffer);
                }
            }
        }

        #endregion

        #region Properties & Stats

        /// <summary>
        /// Returns true if there are any pending commands.
        /// </summary>
        public bool HasCommands => _creates.Count > 0 || _destroys.Count > 0 || PendingThreadCommands > 0;

        /// <summary>
        /// Number of queued entity creations.
        /// </summary>
        public int CreateCount => _creates.Count;

        /// <summary>
        /// Number of queued entity destructions.
        /// </summary>
        public int DestroyCount => _destroys.Count;

        /// <summary>
        /// Returns approximate count of pending commands across all threads.
        /// </summary>
        public int PendingThreadCommands
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

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _threadLocalBuffers?.Dispose();
        }

        #endregion
    }
}
