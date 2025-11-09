
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
        /// </summary>
        public void EnqueueAdd(uint entityIndex, int componentTypeId, object boxedValue)
        {
            _addQueue.Enqueue(ComponentAddOp.Create(entityIndex, componentTypeId, boxedValue));
        }

        /// <summary>
        /// Enqueues a component removal for the next frame.
        /// </summary>
        public void EnqueueRemove(uint entityIndex, int componentTypeId)
        {
            _removeQueue.Enqueue(ComponentRemoveOp.Create(entityIndex, componentTypeId));
        }

        /// <summary>
        /// Processes all queued component operations.
        /// Called during World.Tick() pipeline.
        /// </summary>
        public void ProcessQueues()
        {
            // Process removals first (cleaner archetype transitions)
            while (_removeQueue.TryDequeue(out var op))
            {
                try
                {
                    _world.RemoveComponentFromEntityInternal(op.EntityIndex, op.ComponentTypeId);
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
                    _world.AddComponentToEntityInternal(op.EntityIndex, op.ComponentTypeId, op.BoxedValue);
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

        private readonly struct ComponentRemoveOp
        {
            private const int EntityBits = 32;
            private const int ComponentBits = 32; // allow large component ids
            private const int ComponentShift = EntityBits;
            private const ulong EntityMask = (1UL << EntityBits) - 1;

            private readonly ulong _header;

            private ComponentRemoveOp(ulong header) => _header = header;

            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)(_header >> ComponentShift);

            public static ComponentRemoveOp Create(uint entityIndex, int componentTypeId) =>
                new(((ulong)componentTypeId << ComponentShift) | entityIndex);
        }

        private readonly struct ComponentAddOp
        {
            private const int EntityBits = 32;
            private const int ComponentBits = 32;
            private const int ComponentShift = EntityBits;
            private const ulong EntityMask = (1UL << EntityBits) - 1;

            private readonly ulong _header;
            public readonly object BoxedValue;

            private ComponentAddOp(ulong header, object boxedValue)
            {
                _header = header;
                BoxedValue = boxedValue;
            }

            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)(_header >> ComponentShift);

            public static ComponentAddOp Create(uint entityIndex, int componentTypeId, object boxedValue) =>
                new(((ulong)componentTypeId << ComponentShift) | entityIndex, boxedValue);
        }

        #endregion
    }
}
