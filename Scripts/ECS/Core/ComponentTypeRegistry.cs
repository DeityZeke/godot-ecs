#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UltraSim.ECS
{
    /// <summary>
    /// Thread-safe global registry mapping component types to numeric IDs.
    /// Lazily populates as components are encountered.
    /// </summary>
    public static class ComponentTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, int> _typeToId = new();
        private static readonly List<Type> _idToType = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Registers a component type and returns its ID (thread-safe).
        /// </summary>
        public static int Register<T>()
        {
            var t = typeof(T);
            if (_typeToId.TryGetValue(t, out var id))
                return id;

            lock (_lock)
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
        public static int GetId(Type t)
        {
            if (_typeToId.TryGetValue(t, out var id))
                return id;

            lock (_lock)
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
        public static int GetId<T>() => Register<T>();

        /// <summary>
        /// Returns the component type for a given numeric ID.
        /// </summary>
        public static Type GetTypeById(int id)
        {
            lock (_lock)
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
            lock (_lock) return _idToType.AsReadOnly();
        }

        /// <summary>
        /// Number of registered component types.
        /// </summary>
        public static int Count
        {
            get { lock (_lock) return _idToType.Count; }
        }

        /// <summary>
        /// Clears the registry (testing only).
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _typeToId.Clear();
                _idToType.Clear();
            }
        }
    }
}
