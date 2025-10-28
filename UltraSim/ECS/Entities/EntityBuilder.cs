#nullable enable

using System;
using System.Collections.Generic;

namespace UltraSim.ECS
{
    /// <summary>
    /// Fluent API for building entities with multiple components.
    /// Collects all components before creating the entity, avoiding archetype thrashing.
    /// </summary>
    public sealed class EntityBuilder
    {
        private readonly List<(int typeId, object value)> _components = new();

        /// <summary>
        /// Adds a component to the entity being built.
        /// </summary>
        public EntityBuilder Add<T>(T component) where T : struct
        {
            int typeId = ComponentManager.GetTypeId<T>();
            _components.Add((typeId, component));
            return this;
        }

        /// <summary>
        /// Adds a component by type ID (for dynamic scenarios).
        /// We normalize to the canonical ID derived from the runtime component value's Type
        /// so the stored IDs are always consistent with ComponentManager.
        /// </summary>
        public EntityBuilder Add(int typeId, object value)
        {
            int canonicalId = ComponentManager.GetTypeId(value.GetType());
            _components.Add((canonicalId, value));
            return this;
        }

        /// <summary>
        /// Returns all components added to this builder.
        /// </summary>
        internal IReadOnlyList<(int typeId, object value)> GetComponents() => _components;

        /// <summary>
        /// Clears all components from this builder for reuse.
        /// </summary>
        public void Clear() => _components.Clear();
    }
}