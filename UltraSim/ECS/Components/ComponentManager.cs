
#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using UltraSim;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages component type registry and deferred component operations.
    /// Owns component add/remove queues and type-to-ID mappings.
    /// </summary>
    public sealed class ComponentManager
    {
        #region Component Type Registry (Shared)

        private static readonly Dictionary<Type, int> _typeToId = new();
        private static readonly List<Type> _idToType = new();
        private static readonly object _typeLock = new();
        private static ComponentManager? _sharedInstance;

        public static ComponentManager Instance =>
            _sharedInstance ?? throw new InvalidOperationException("ComponentManager has not been initialized.");

        /// <summary>
        /// Registers a component type and returns its ID (thread-safe).
        /// </summary>
        public static int RegisterType<T>()
        {
            var t = typeof(T);
            lock (_typeLock)
            {
                if (_typeToId.TryGetValue(t, out var id))
                    return id;

                id = _idToType.Count;
                _idToType.Add(t);
                _typeToId[t] = id;
                return id;
            }
        }

        /// <summary>
        /// Gets or registers an ID for the given component type.
        /// </summary>
        public static int GetTypeId(Type t)
        {
            lock (_typeLock)
            {
                if (_typeToId.TryGetValue(t, out var id))
                    return id;

                id = _idToType.Count;
                _idToType.Add(t);
                _typeToId[t] = id;
                return id;
            }
        }

        /// <summary>
        /// Gets or registers an ID for component type T.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetTypeId<T>() => RegisterType<T>();

        /// <summary>
        /// Returns the component type for a given numeric ID.
        /// </summary>
        public static Type GetComponentType(int id)
        {
            lock (_typeLock)
            {
                if (id < 0 || id >= _idToType.Count)
                    throw new ArgumentOutOfRangeException(nameof(id), $"Invalid component type ID: {id}");
                return _idToType[id];
            }
        }

        /// <summary>
        /// Returns all registered component types.
        /// </summary>
        public static IReadOnlyList<Type> GetAllTypes()
        {
            lock (_typeLock) return _idToType.AsReadOnly();
        }

        /// <summary>
        /// Number of registered component types.
        /// </summary>
        public static int TypeCount
        {
            get { lock (_typeLock) return _idToType.Count; }
        }

        /// <summary>
        /// Gets the highest component type ID currently registered.
        /// Returns -1 if no components are registered.
        /// Used for dynamic ComponentSignature sizing.
        /// </summary>
        public static int GetHighestTypeId()
        {
            lock (_typeLock) return _idToType.Count - 1;
        }

        /// <summary>
        /// Clears the registry (testing only).
        /// </summary>
        public static void ClearRegistry()
        {
            lock (_typeLock)
            {
                _typeToId.Clear();
                _idToType.Clear();
            }
        }

        #endregion

        #region Instance Fields

        private readonly World _world;

        // Deferred component operation queues
        private readonly ConcurrentQueue<ComponentRemoveOp> _removeQueue = new();
        private readonly ConcurrentQueue<ComponentAddOp> _addQueue = new();
        private readonly ConcurrentDictionary<int, IComponentAddQueue> _typedAddQueues = new();
        private readonly ConcurrentQueue<int> _typedAddDirtyIds = new();
        private readonly ConcurrentDictionary<int, byte> _typedAddDirtyFlags = new();

        #endregion

        #region Constructor

        public ComponentManager(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sharedInstance = this;
        }

        #endregion

        #region Deferred Operations

        /// <summary>
        /// Enqueues a component addition for the next frame.
        /// Stores full entity (index + version) for proper validation.
        /// </summary>
        public void EnqueueAdd(Entity entity, int componentTypeId, object boxedValue)
        {
            _addQueue.Enqueue(ComponentAddOp.Create(entity, componentTypeId, boxedValue));
        }

        /// <summary>
        /// Enqueues a strongly-typed component addition to avoid boxing costs.
        /// Falls back to boxed path if the component type was registered differently.
        /// </summary>
        public void EnqueueAdd<T>(Entity entity, int componentTypeId, T value)
        {
            var queue = _typedAddQueues.GetOrAdd(componentTypeId, static id => new ComponentAddQueue<T>(id));
            if (queue is ComponentAddQueue<T> typedQueue)
            {
                typedQueue.Enqueue(entity, value);
                if (_typedAddDirtyFlags.TryAdd(componentTypeId, 0))
                    _typedAddDirtyIds.Enqueue(componentTypeId);
            }
            else
            {
                // Type mismatch (should not happen) - fall back to boxed path
                _addQueue.Enqueue(ComponentAddOp.Create(entity, componentTypeId, value!));
            }
        }

        /// <summary>
        /// Enqueues a component removal for the next frame.
        /// Stores full entity (index + version) for proper validation.
        /// </summary>
        public void EnqueueRemove(Entity entity, int componentTypeId)
        {
            _removeQueue.Enqueue(ComponentRemoveOp.Create(entity, componentTypeId));
        }

        /// <summary>
        /// Processes all queued component operations.
        /// Called during World.Tick() pipeline.
        /// Version validation happens in World.AddComponentToEntityInternal/RemoveComponentFromEntityInternal.
        /// </summary>
        public void ProcessQueues()
        {
            // Process removals first (cleaner archetype transitions)
            while (_removeQueue.TryDequeue(out var op))
            {
                try
                {
                    ApplyComponentRemoval(op);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[ComponentManager] Error removing component: {ex}", LogSeverity.Error);
                }
            }

            // Then process additions
            while (_addQueue.TryDequeue(out var op))
            {
                try
                {
                    ApplyComponentAddition(op);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[ComponentManager] Error adding component: {ex}", LogSeverity.Error);
                }
            }

            while (_typedAddDirtyIds.TryDequeue(out var typeId))
            {
                _typedAddDirtyFlags.TryRemove(typeId, out _);
                if (!_typedAddQueues.TryGetValue(typeId, out var queue))
                    continue;

                try
                {
                    queue.Drain(this);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[ComponentManager] Error adding component (typed queue {typeId}): {ex}", LogSeverity.Error);
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns true if there are any pending component operations.
        /// </summary>
        public bool HasPendingOperations => _addQueue.Count > 0 || _removeQueue.Count > 0;

        /// <summary>
        /// Number of queued component additions.
        /// </summary>
        public int AddCount => _addQueue.Count;

        /// <summary>
        /// Number of queued component removals.
        /// </summary>
        public int RemoveCount => _removeQueue.Count;

        #endregion

        #region Packed Ops

        /// <summary>
        /// Component remove operation that stores full Entity (index + version).
        /// Memory layout: 8 bytes for entity.Packed + 4 bytes for componentTypeId = 12 bytes total
        /// </summary>
        private readonly struct ComponentRemoveOp
        {
            private readonly ulong _entityPacked;  // Full entity (index + version)
            public readonly int ComponentTypeId;

            private ComponentRemoveOp(ulong entityPacked, int componentTypeId)
            {
                _entityPacked = entityPacked;
                ComponentTypeId = componentTypeId;
            }

            public Entity Entity => new(_entityPacked);

            public static ComponentRemoveOp Create(Entity entity, int componentTypeId) =>
                new(entity.Packed, componentTypeId);
        }

        /// <summary>
        /// Component add operation that stores full Entity (index + version).
        /// Memory layout: 8 bytes for entity.Packed + 4 bytes for componentTypeId + 8 bytes for object reference = 20 bytes
        /// Slightly larger than before (was 16 bytes) but ensures correct version validation.
        /// </summary>
        private readonly struct ComponentAddOp
        {
            private readonly ulong _entityPacked;  // Full entity (index + version)
            public readonly int ComponentTypeId;
            public readonly object BoxedValue;

            private ComponentAddOp(ulong entityPacked, int componentTypeId, object boxedValue)
            {
                _entityPacked = entityPacked;
                ComponentTypeId = componentTypeId;
                BoxedValue = boxedValue;
            }

            public Entity Entity => new(_entityPacked);

            public static ComponentAddOp Create(Entity entity, int componentTypeId, object boxedValue) =>
                new(entity.Packed, componentTypeId, boxedValue);
        }

        private void ApplyComponentRemoval(ComponentRemoveOp op)
        {
            var entity = op.Entity;
            if (!_world.IsEntityValid(entity))
                return;

            if (!_world.TryGetEntityLocation(entity, out var sourceArch, out var slot))
                return;

            if (!sourceArch.TryGetEntityAtSlot(slot, out var occupant) || occupant.Packed != entity.Packed)
            {
                slot = sourceArch.FindEntitySlot(entity);
                if (slot < 0 || slot >= sourceArch.Count)
                    return;
            }

            if (!sourceArch.HasComponent(op.ComponentTypeId))
                return;

            var newSig = sourceArch.Signature.Remove(op.ComponentTypeId);
            var targetArch = _world.GetOrCreateArchetypeInternal(newSig);

            int newSlot = sourceArch.MoveEntityTo(slot, targetArch);
            _world.UpdateEntityLookup(entity.Index, targetArch, newSlot);
        }

        private readonly struct ComponentAddOp<T>
        {
            private readonly ulong _entityPacked;
            public readonly int ComponentTypeId;
            public readonly T Value;

            private ComponentAddOp(ulong entityPacked, int componentTypeId, T value)
            {
                _entityPacked = entityPacked;
                ComponentTypeId = componentTypeId;
                Value = value;
            }

            public Entity Entity => new(_entityPacked);

            public static ComponentAddOp<T> Create(Entity entity, int componentTypeId, T value) =>
                new(entity.Packed, componentTypeId, value);
        }

        private interface IComponentAddQueue
        {
            void Drain(ComponentManager manager);
        }

        private sealed class ComponentAddQueue<T> : IComponentAddQueue
        {
            private readonly int _componentTypeId;
            private readonly ConcurrentQueue<ComponentAddOp<T>> _queue = new();

            public ComponentAddQueue(int componentTypeId)
            {
                _componentTypeId = componentTypeId;
            }

            public void Enqueue(Entity entity, T value)
            {
                _queue.Enqueue(ComponentAddOp<T>.Create(entity, _componentTypeId, value));
            }

            public void Drain(ComponentManager manager)
            {
                while (_queue.TryDequeue(out var op))
                {
                    manager.ApplyComponentAddition(op);
                }
            }
        }

        private void ApplyComponentAddition(ComponentAddOp op)
        {
            var entity = op.Entity;
            if (!_world.IsEntityValid(entity))
                return;

            if (!_world.TryGetEntityLocation(entity, out var sourceArch, out var slot))
                return;

            if (!sourceArch.TryGetEntityAtSlot(slot, out var occupant) || occupant.Packed != entity.Packed)
            {
                slot = sourceArch.FindEntitySlot(entity);
                if (slot < 0 || slot >= sourceArch.Count)
                    return;
            }

            var newSig = sourceArch.Signature.Add(op.ComponentTypeId);
            var targetArch = _world.GetOrCreateArchetypeInternal(newSig);

            if (ReferenceEquals(targetArch, sourceArch))
            {
                sourceArch.SetComponentValueBoxed(op.ComponentTypeId, slot, op.BoxedValue);
                return;
            }

            int newSlot = sourceArch.MoveEntityTo(slot, targetArch, op.BoxedValue);
            _world.UpdateEntityLookup(entity.Index, targetArch, newSlot);
        }

        private void ApplyComponentAddition<T>(ComponentAddOp<T> op)
        {
            var entity = op.Entity;
            if (!_world.IsEntityValid(entity))
                return;

            if (!_world.TryGetEntityLocation(entity, out var sourceArch, out var slot))
                return;

            if (!sourceArch.TryGetEntityAtSlot(slot, out var occupant) || occupant.Packed != entity.Packed)
            {
                slot = sourceArch.FindEntitySlot(entity);
                if (slot < 0 || slot >= sourceArch.Count)
                    return;
            }

            var newSig = sourceArch.Signature.Add(op.ComponentTypeId);
            var targetArch = _world.GetOrCreateArchetypeInternal(newSig);

            if (ReferenceEquals(targetArch, sourceArch))
            {
                sourceArch.SetComponentValue(op.ComponentTypeId, slot, op.Value);
                return;
            }

            int newSlot = sourceArch.MoveEntityTo(slot, targetArch);
            targetArch.SetComponentValue(op.ComponentTypeId, newSlot, op.Value);
            _world.UpdateEntityLookup(entity.Index, targetArch, newSlot);
        }

        #endregion
    }
}
