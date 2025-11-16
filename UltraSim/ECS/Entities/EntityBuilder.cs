#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace UltraSim.ECS
{
    internal readonly struct ComponentInit
    {
        private readonly IComponentValueHolder _holder;

        public ComponentInit(IComponentValueHolder holder)
        {
            _holder = holder ?? throw new ArgumentNullException(nameof(holder));
        }

        public int TypeId => _holder.TypeId;
        public IComponentValueHolder Holder => _holder;
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
            var holder = ComponentValueHolder<T>.Rent();
            holder.Initialize(typeId, component);
            _components.Add(new ComponentInit(holder));
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
            var holder = ComponentValueHolder<object>.Rent();
            holder.Initialize(canonicalId, value);
            _components.Add(new ComponentInit(holder));
            return this;
        }

        internal List<ComponentInit> GetComponents() => _components;
    }

    internal interface IComponentValueHolder
    {
        int TypeId { get; }
        void Apply(Archetype archetype, int slot);
        void Release();
    }

    internal sealed class ComponentValueHolder<T> : IComponentValueHolder
    {
        private static readonly ConcurrentBag<ComponentValueHolder<T>> _pool = new();

        private int _typeId;
        private T _value = default!;

        public int TypeId => _typeId;

        public static ComponentValueHolder<T> Rent()
        {
            if (!_pool.TryTake(out var holder))
                holder = new ComponentValueHolder<T>();
            return holder;
        }

        public void Initialize(int typeId, T value)
        {
            _typeId = typeId;
            _value = value;
        }

        public void Apply(Archetype archetype, int slot)
        {
            archetype.SetComponentValue(_typeId, slot, _value);
        }

        public void Release()
        {
            _typeId = 0;
            _value = default!;
            _pool.Add(this);
        }
    }
}
