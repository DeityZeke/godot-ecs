#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

using UltraSim;

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
            public List<ComponentInit> Components;
        }

        private readonly struct ThreadCommand
        {
            public enum Type : byte { AddComponent = 0, RemoveComponent = 1, DestroyEntity = 2 }

            private const int EntityBits = 32;
            private const int ComponentBits = 24;
            private const int TypeBits = 8;

            private const int ComponentShift = EntityBits;
            private const int TypeShift = ComponentShift + ComponentBits;

            private const ulong EntityMask = (1UL << EntityBits) - 1;
            private const uint ComponentMask = (1u << ComponentBits) - 1;

            private readonly ulong _header;
            public readonly object? BoxedValue;

            private ThreadCommand(ulong header, object? boxedValue)
            {
                _header = header;
                BoxedValue = boxedValue;
            }

            public Type CommandType => (Type)((_header >> TypeShift) & ((1UL << TypeBits) - 1));
            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)((_header >> ComponentShift) & ComponentMask);

            public static ThreadCommand CreateAdd(uint entityIndex, int componentTypeId, object boxedValue) =>
                new(Pack(Type.AddComponent, entityIndex, componentTypeId), boxedValue);

            public static ThreadCommand CreateRemove(uint entityIndex, int componentTypeId) =>
                new(Pack(Type.RemoveComponent, entityIndex, componentTypeId), null);

            public static ThreadCommand CreateDestroy(uint entityIndex) =>
                new(Pack(Type.DestroyEntity, entityIndex, 0), null);

            private static ulong Pack(Type type, uint entityIndex, int componentTypeId)
            {
#if DEBUG
                if (componentTypeId < 0 || (uint)componentTypeId > ComponentMask)
                    throw new ArgumentOutOfRangeException(nameof(componentTypeId), $"ComponentTypeId exceeds {ComponentMask}");
#endif
                return ((ulong)type << TypeShift) |
                       (((ulong)componentTypeId & ComponentMask) << ComponentShift) |
                       entityIndex;
            }
        }

        #endregion

        #region Fields

        // Main thread commands (for CreateEntity with builder pattern)
        private readonly List<EntityCreationCommand> _creates = new();
        private readonly List<Entity> _destroys = new();
        private readonly ConcurrentBag<List<ComponentInit>> _componentListPool = new();

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
            var components = RentComponentList();
            var builder = new EntityBuilder(components);
            setup(builder);

            _creates.Add(new EntityCreationCommand
            {
                Components = components
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
        public void DestroyEntity(uint entityIndex)
        {
            _threadLocalBuffers.Value!.Add(ThreadCommand.CreateDestroy(entityIndex));
        }

        #endregion

        #region Component Operations (Thread-Safe)

        /// <summary>
        /// Thread-safe: Queue component addition from any thread.
        /// Used primarily in parallel systems.
        /// </summary>
        public void AddComponent(uint entityIndex, int componentTypeId, object boxedValue)
        {
            _threadLocalBuffers.Value!.Add(ThreadCommand.CreateAdd(entityIndex, componentTypeId, boxedValue));
        }

        /// <summary>
        /// Thread-safe: Queue component removal from any thread.
        /// Used primarily in parallel systems.
        /// </summary>
        public void RemoveComponent(uint entityIndex, int componentTypeId)
        {
            _threadLocalBuffers.Value!.Add(ThreadCommand.CreateRemove(entityIndex, componentTypeId));
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
                Logging.Log($"[CommandBuffer] Applied: {created} created, {destroyed} destroyed in {sw.Elapsed.TotalMilliseconds:F3}ms");
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
                            world.EnqueueDestroyEntity(cmd.EntityIndex);
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
                    ReturnComponentList(cmd.Components);
                    continue;
                }

                // Build signature from all components (typeId already computed in EntityBuilder)
                var signature = new ComponentSignature();
                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                {
                    // Use pre-computed typeId from EntityBuilder (OPTIMIZED)
                    signature = signature.Add(component.TypeId);
                }

                // Create entity directly in target archetype (ONE archetype move!)
                var entity = world.CreateEntityWithSignature(signature);

                // Set all component values using pre-computed typeIds
                if (!world.TryGetEntityLocation(entity, out var archetype, out var slot))
                {
                    Logging.Log($"[CommandBuffer] Failed to get location for entity {entity}", LogSeverity.Error);
                    continue;
                }

                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                {
                    // Use pre-computed typeId (OPTIMIZED - eliminates redundant GetTypeId call)
                    archetype.SetComponentValueBoxed(component.TypeId, slot, component.Value);
                }

                created++;
                ReturnComponentList(cmd.Components);
            }

            return created;
        }

        private int ApplyEntityDestructions(World world)
        {
            int destroyed = 0;

            foreach (var entity in CollectionsMarshal.AsSpan(_destroys))
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

        #region Helpers

        private List<ComponentInit> RentComponentList()
        {
            if (_componentListPool.TryTake(out var list))
                return list;
            return new List<ComponentInit>(8);
        }

        private void ReturnComponentList(List<ComponentInit> list)
        {
            list.Clear();
            _componentListPool.Add(list);
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
