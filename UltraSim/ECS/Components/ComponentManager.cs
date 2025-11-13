
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

        private static readonly ConcurrentDictionary<Type, int> _typeToId = new();
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
            if (_typeToId.TryGetValue(t, out var id))
                return id;

            lock (_typeLock)
            {
                if (_typeToId.TryGetValue(t, out id))
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
            if (_typeToId.TryGetValue(t, out var id))
                return id;

            lock (_typeLock)
            {
                if (_typeToId.TryGetValue(t, out id))
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
                    _world.RemoveComponentFromEntityInternal(op.Entity, op.ComponentTypeId);
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
                    _world.AddComponentToEntityInternal(op.Entity, op.ComponentTypeId, op.BoxedValue);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[ComponentManager] Error adding component: {ex}", LogSeverity.Error);
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

        #endregion
    }
}
