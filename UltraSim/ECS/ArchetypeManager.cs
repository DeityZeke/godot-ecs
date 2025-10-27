#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages archetype storage, queries, and entity-archetype transitions.
    /// Owns the archetype list and provides efficient archetype lookup.
    /// </summary>
    public sealed class ArchetypeManager
    {
        private readonly List<Archetype> _archetypes = new();
        private readonly Dictionary<ComponentSignature, Archetype> _signatureCache = new();

        public int ArchetypeCount => _archetypes.Count;

        public ArchetypeManager()
        {
            // Create empty archetype (archetype 0)
            var emptyArch = new Archetype();
            _archetypes.Add(emptyArch);
            _signatureCache[emptyArch.Signature] = emptyArch;
        }

        #region Archetype Lookup

        /// <summary>
        /// Gets or creates an archetype with the given signature.
        /// Uses caching for O(1) lookup on repeated queries.
        /// </summary>
        public Archetype GetOrCreate(ComponentSignature signature)
        {
            // Check cache first
            if (_signatureCache.TryGetValue(signature, out var cached))
                return cached;

            // Linear search through archetypes (rare case - archetype doesn't exist yet)
            foreach (ref var arch in CollectionsMarshal.AsSpan(_archetypes))
            {
                if (arch.Signature.Equals(signature))
                {
                    _signatureCache[signature] = arch;
                    return arch;
                }
            }

            // Create new archetype
            var newArch = new Archetype(signature);

            // Ensure component lists for all types in signature
            foreach (var typeId in signature.GetIds())
            {
                var type = ComponentManager.GetComponentType(typeId);
                var method = typeof(Archetype).GetMethod(nameof(Archetype.EnsureComponentList))
                                              ?.MakeGenericMethod(type);
                method?.Invoke(newArch, new object[] { typeId });
            }

            _archetypes.Add(newArch);
            _signatureCache[signature] = newArch;
            
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
        public int GetArchetypeIndex(Archetype archetype) => _archetypes.IndexOf(archetype);

        /// <summary>
        /// Returns all archetypes (read-only).
        /// </summary>
        public IReadOnlyList<Archetype> GetAll() => _archetypes;

        #endregion

        #region Queries

        /// <summary>
        /// Queries for archetypes matching the given component types.
        /// Returns an enumerable for efficient iteration.
        /// </summary>
        public IEnumerable<Archetype> Query(params Type[] componentTypes)
        {
            foreach (var arch in _archetypes)
            {
                if (arch.Matches(componentTypes))
                    yield return arch;
            }
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
                    GD.PrintErr($"[ArchetypeManager] Validation error: {error}");
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
    }
}