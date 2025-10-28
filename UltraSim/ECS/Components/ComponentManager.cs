
#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UltraSim.Logging;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages component type registry and deferred component operations.
    /// Owns component add/remove queues and type-to-ID mappings.
    /// </summary>
    public sealed class ComponentManager
    {
        #region Component Type Registry (Static - backward compatibility)

        // Keep as static for now since it's used throughout the codebase
        // Can be refactored to instance methods later if needed
        private static readonly ConcurrentDictionary<Type, int> _typeToId = new();
        private static readonly List<Type> _idToType = new();
        private static readonly object _typeLock = new();

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
        private readonly ConcurrentQueue<(int entityIndex, int componentTypeId)> _removeQueue = new();
        private readonly ConcurrentQueue<(int entityIndex, int componentTypeId, object boxedValue)> _addQueue = new();

        #endregion

        #region Constructor

        public ComponentManager(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        #endregion

        #region Deferred Operations

        /// <summary>
        /// Enqueues a component addition for the next frame.
        /// </summary>
        public void EnqueueAdd(int entityIndex, int componentTypeId, object boxedValue)
        {
            _addQueue.Enqueue((entityIndex, componentTypeId, boxedValue));
        }

        /// <summary>
        /// Enqueues a component removal for the next frame.
        /// </summary>
        public void EnqueueRemove(int entityIndex, int componentTypeId)
        {
            _removeQueue.Enqueue((entityIndex, componentTypeId));
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
                    _world.RemoveComponentFromEntityInternal(op.entityIndex, op.componentTypeId);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ComponentManager] Error removing component: {ex}", LogSeverity.Error);
                }
            }

            // Then process additions
            while (_addQueue.TryDequeue(out var op))
            {
                try
                {
                    _world.AddComponentToEntityInternal(op.entityIndex, op.componentTypeId, op.boxedValue);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ComponentManager] Error adding component: {ex}", LogSeverity.Error);
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
    }
}