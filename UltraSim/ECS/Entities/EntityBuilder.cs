#nullable enable

using System;
using System.Collections.Generic;

namespace UltraSim.ECS
{
    internal readonly struct ComponentInit
    {
        public readonly int TypeId;
        public readonly object Value;

        public ComponentInit(int typeId, object value)
        {
            TypeId = typeId;
            Value = value;
        }
    }

    /// <summary>
    /// Fluent API for building entities with multiple components.
    /// Collects all components before creating the entity, avoiding archetype thrashing.
    /// </summary>
    public sealed class EntityBuilder
    {
        private readonly List<ComponentInit> _components;

        internal EntityBuilder(List<ComponentInit> backingList)
        {
            _components = backingList ?? throw new ArgumentNullException(nameof(backingList));
        }

        /// <summary>
        /// Adds a component to the entity being built.
        /// </summary>
        public EntityBuilder Add<T>(T component) where T : struct
        {
            int typeId = ComponentManager.GetTypeId<T>();
            _components.Add(new ComponentInit(typeId, component));
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
            _components.Add(new ComponentInit(canonicalId, value));
            return this;
        }

        internal List<ComponentInit> GetComponents() => _components;
    }
}
