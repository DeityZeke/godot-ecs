
#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Reflection;

using UltraSim;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages archetype storage, queries, and entity-archetype transitions.
    /// Owns the archetype list and provides efficient archetype lookup.
    /// </summary>
    public sealed class ArchetypeManager
    {
        private readonly World _world;
        private readonly List<Archetype> _archetypes = new();
        private readonly Dictionary<ComponentSignatureKey, Archetype> _signatureCache = new();
        private readonly Dictionary<Archetype, int> _archetypeIndexCache = new();

        public int ArchetypeCount => _archetypes.Count;

        public ArchetypeManager(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            // Create empty archetype (archetype 0)
            var emptyArch = new Archetype(_world);
            RegisterArchetype(emptyArch);
        }

        private readonly Stack<List<Archetype>> _queryListPool = new();

        public readonly struct QueryResult : IDisposable, IEnumerable<Archetype>
        {
            private readonly ArchetypeManager _owner;
            private readonly List<Archetype> _list;

            internal QueryResult(ArchetypeManager owner, List<Archetype> list)
            {
                _owner = owner;
                _list = list;
            }

            public ReadOnlySpan<Archetype> AsSpan() => CollectionsMarshal.AsSpan(_list);

            public void Dispose()
            {
                _owner.ReturnQueryList(_list);
            }

            public List<Archetype>.Enumerator GetEnumerator() => _list.GetEnumerator();

            IEnumerator<Archetype> IEnumerable<Archetype>.GetEnumerator() => _list.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();

            public int Count => _list.Count;

            public Archetype this[int index] => _list[index];
        }

        #region Archetype Lookup

        // CACHED DELEGATES: Type → Action<Archetype, int>
        private static readonly ConcurrentDictionary<Type, Action<Archetype, int>> _ensureComponentListDelegates = new();

        /// <summary>
        /// Gets or creates an archetype with the given signature.
        /// Uses caching for O(1) lookup on repeated queries.
        /// Reflection is cached — zero cost after first use per type.
        /// </summary>
        public Archetype GetOrCreate(ComponentSignature signature)
        {
            var key = new ComponentSignatureKey(signature);

            // 1. Fast cache hit
            if (_signatureCache.TryGetValue(key, out var cached))
                return cached;

            // 2. Linear search (only when new archetype)
            foreach (ref var arch in CollectionsMarshal.AsSpan(_archetypes))
            {
                if (arch.Signature.Equals(signature))
                {
                    _signatureCache[key] = arch;
                    return arch;
                }
            }

            // 3. Create new archetype
            var newArch = new Archetype(_world, signature);

            // ENSURE COMPONENT LISTS — CACHED DELEGATES
            foreach (var typeId in signature.GetIds())
            {
                if (typeId < 0 || typeId >= ComponentManager.TypeCount)
                {
                    Logging.Log($"[ArchetypeManager] Unknown component type id {typeId} in signature {signature}. Registered types: {ComponentManager.TypeCount}", LogSeverity.Error);
                    throw new ArgumentOutOfRangeException(nameof(typeId), $"Invalid component type ID: {typeId} (signature={signature})");
                }

                var componentType = ComponentManager.GetComponentType(typeId);

                // GET CACHED DELEGATE
                if (!_ensureComponentListDelegates.TryGetValue(componentType, out var ensureAction))
                {
                    // First time: create and cache
                    var genericMethod = typeof(Archetype)
                        .GetMethod(nameof(Archetype.EnsureComponentList), BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.MakeGenericMethod(componentType);

                    if (genericMethod == null)
                        throw new MissingMethodException($"EnsureComponentList<{componentType.Name}> not found.");

                    ensureAction = (Action<Archetype, int>)Delegate.CreateDelegate(
                        typeof(Action<Archetype, int>), genericMethod);

                    _ensureComponentListDelegates[componentType] = ensureAction;
                }

                // INVOKE CACHED DELEGATE
                ensureAction(newArch, typeId);
            }

            RegisterArchetype(newArch);
            _signatureCache[key] = newArch;

            return newArch;
        }


        /// <summary>
        /// Gets the empty archetype (archetype 0).
        /// </summary>
        public Archetype GetEmptyArchetype() => _archetypes[0];

        /// <summary>
        /// Gets an archetype by index.
        /// Used by EntityManager for entity-archetype lookups.
        /// </summary>
        public Archetype GetArchetype(int index) => _archetypes[index];

        /// <summary>
        /// Gets the index of an archetype.
        /// Used by EntityManager to store entity locations.
        /// </summary>
        public int GetArchetypeIndex(Archetype archetype)
        {
            if (_archetypeIndexCache.TryGetValue(archetype, out var index))
                return index;

            int fallback = _archetypes.IndexOf(archetype);
            if (fallback >= 0)
                _archetypeIndexCache[archetype] = fallback;
            return fallback;
        }

        /// <summary>
        /// Returns all archetypes (read-only).
        /// </summary>
        public IReadOnlyList<Archetype> GetAll() => _archetypes;

        #endregion

        private List<Archetype> RentQueryList()
        {
            lock (_queryListPool)
            {
                if (_queryListPool.Count > 0)
                {
                    var list = _queryListPool.Pop();
                    list.Clear();
                    return list;
                }
            }

            return new List<Archetype>(16);
        }

        private void ReturnQueryList(List<Archetype> list)
        {
            list.Clear();
            lock (_queryListPool)
            {
                _queryListPool.Push(list);
            }
        }

        private void RegisterArchetype(Archetype archetype)
        {
            int index = _archetypes.Count;
            _archetypes.Add(archetype);
            _archetypeIndexCache[archetype] = index;
        }

        #region Queries

        /// <summary>
        /// Queries for archetypes matching the given component types.
        /// Returns a pooled result that must be disposed.
        /// </summary>
        public QueryResult Query(params Type[] componentTypes)
        {
            var list = RentQueryList();
            foreach (ref var arch in CollectionsMarshal.AsSpan(_archetypes))
            {
                if (arch.Matches(componentTypes))
                    list.Add(arch);
            }

            return new QueryResult(this, list);
        }

        /// <summary>
        /// Gets archetypes containing specific component types (generic version).
        /// Cached by systems for zero-allocation queries.
        /// </summary>
        public List<Archetype> GetArchetypesWithComponents(params Type[] componentTypes)
        {
            var result = new List<Archetype>();
            foreach (var arch in _archetypes)
            {
                if (arch.Matches(componentTypes))
                    result.Add(arch);
            }
            return result;
        }

        #endregion

        #region Entity Transitions

        /// <summary>
        /// Moves an entity from one archetype to another.
        /// Called when components are added/removed.
        /// </summary>
        public void MoveEntity(Archetype source, int sourceSlot, Archetype target, object? newComponent = null)
        {
            if (newComponent != null)
            {
                // Adding a component
                source.MoveEntityTo(sourceSlot, target, newComponent);
            }
            else
            {
                // Removing a component (or just moving)
                source.MoveEntityTo(sourceSlot, target);
            }
        }

        #endregion

        #region Compaction

        /// <summary>
        /// No-op method for backward compatibility.
        /// Archetype now uses immediate swap-and-pop removal (no deferred compaction needed).
        /// </summary>
        public void CompactAll()
        {
            // Immediate swap-and-pop keeps archetypes compact automatically
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates all archetypes for debugging.
        /// Only runs in DEBUG builds.
        /// </summary>
        public void ValidateAll()
        {
#if DEBUG
            foreach (var arch in _archetypes)
            {
                foreach (var error in arch.DebugValidate())
                    Logging.Log($"[ArchetypeManager] Validation error: {error}", LogSeverity.Error);
            }
#endif
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Returns statistics about archetype usage.
        /// Useful for debugging and optimization.
        /// </summary>
        public (int totalArchetypes, int totalEntities, int avgEntitiesPerArchetype) GetStatistics()
        {
            int totalEntities = 0;
            foreach (var arch in _archetypes)
            {
                totalEntities += arch.Count;
            }

            int avg = _archetypes.Count > 0 ? totalEntities / _archetypes.Count : 0;
            return (_archetypes.Count, totalEntities, avg);
        }

        #endregion

        private readonly struct ComponentSignatureKey : IEquatable<ComponentSignatureKey>
        {
            private readonly ulong _hash1;
            private readonly ulong _hash2;
            private readonly int _count;

            public ComponentSignatureKey(ComponentSignature signature)
            {
                _count = signature.Count;
                var bits = signature.GetRawBits();
                ulong h1 = 0xcbf29ce484222325;
                ulong h2 = 0x100000001b3;
                foreach (var word in bits)
                {
                    h1 ^= word;
                    h1 *= 0x100000001b3;
                    h2 += word * 0x9e3779b185ebca87;
                }
                _hash1 = h1;
                _hash2 = h2;
            }

            public bool Equals(ComponentSignatureKey other) =>
                _hash1 == other._hash1 && _hash2 == other._hash2 && _count == other._count;

            public override bool Equals(object? obj) =>
                obj is ComponentSignatureKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_hash1, _hash2, _count);
        }
    }
}
