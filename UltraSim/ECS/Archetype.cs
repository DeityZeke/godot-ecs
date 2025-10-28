#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// Stores entities with identical component combinations in SoA (Structure of Arrays) format.
    /// Provides high-performance span-based iteration.
    /// </summary>
    public sealed class Archetype
    {
        private readonly Dictionary<int, IComponentList> _componentLists = new(65535);
        private readonly List<Entity> _entities = new(65535);

        public ComponentSignature Signature { get; }
        public int Count => _entities.Count;

        public Archetype() : this(new ComponentSignature()) { }

        public Archetype(ComponentSignature signature)
        {
            Signature = signature;
        }

        /// <summary>
        /// Validates archetype integrity (debug builds only).
        /// </summary>
        public IEnumerable<string> DebugValidate()
        {
            foreach (var kv in _componentLists)
            {
                if (kv.Value.Count != _entities.Count)
                    yield return $"Archetype mismatch: component {kv.Key} has {kv.Value.Count} entries, entities={_entities.Count}";
            }
        }

        /// <summary>
        /// Gets the typed component list for zero-allocation parallel processing.
        /// </summary>
        public ComponentList<T>? GetComponentListTyped<T>(int componentTypeId)
        {
            if (!_componentLists.TryGetValue(componentTypeId, out var obj))
                return null;

            return obj as ComponentList<T>;
        }

        /// <summary>
        /// Adds an entity to this archetype and allocates component slots.
        /// </summary>
        public void AddEntity(Entity e)
        {
            _entities.Add(e);

            // Ensure all component lists have space for the new entity
            foreach (var kv in _componentLists)
            {
                var list = kv.Value;
                int missing = _entities.Count - list.Count;
                for (int i = 0; i < missing; i++)
                    list.AddDefault();
            }
        }

        /// <summary>
        /// Moves an entity to another archetype, copying shared components.
        /// </summary>
        public void MoveEntityTo(int slot, Archetype target, object? newComponent = null)
        {
            if (slot < 0 || slot >= _entities.Count)
                throw new InvalidOperationException($"[MoveEntityTo] Invalid slot {slot} for archetype with {_entities.Count} entities.");

            var entity = _entities[slot];

            // Add entity to target archetype first
            target.AddEntity(entity);

            // Copy existing component values that both archetypes share
            foreach (var kv in _componentLists)
            {
                int typeId = kv.Key;
                var storage = kv.Value;

                if (typeId < 0 || slot >= storage.Count) continue;

                if (target.Signature.Contains(typeId))
                {
                    var value = storage.GetValueBoxed(slot);
                    int newSlot = target.Count - 1;
                    target.SetComponentValueBoxed(typeId, newSlot, value!);
                }
            }

            // Add the new component if provided
            if (newComponent != null)
            {
                //int typeId = ComponentTypeRegistry.GetId(newComponent.GetType());
                int typeId = ComponentManager.GetTypeId(newComponent.GetType());
                int newSlot = target.Count - 1;
                target.SetComponentValueBoxed(typeId, newSlot, newComponent);
            }

            // Remove entity from this archetype
            RemoveAtSwap(slot);
        }

        /// <summary>
        /// Removes an entity using swap-and-pop for O(1) removal.
        /// </summary>
        public void RemoveAtSwap(int slot)
        {
            int last = _entities.Count - 1;
            if (slot != last)
            {
                var movedEntity = _entities[last];
                _entities[slot] = movedEntity;

                // Swap component data
                foreach (var kv in _componentLists)
                    kv.Value.SwapLastIntoSlot(slot, last);

                // Update world lookup for the swapped entity
                World.Current?.UpdateEntityLookup(movedEntity.Index, this, slot);
            }

            // Remove last element from all lists
            foreach (var kv in _componentLists)
                kv.Value.RemoveLast();

            _entities.RemoveAt(last);
        }

        /// <summary>
        /// Ensures a component list exists for the given type.
        /// </summary>
        public void EnsureComponentList<T>(int componentTypeId)
        {
            if (!_componentLists.TryGetValue(componentTypeId, out var existing))
            {
                var list = new ComponentList<T>(_entities.Count);
                for (int i = 0; i < _entities.Count; i++)
                    list.AddDefault();

                _componentLists[componentTypeId] = list;
            }
            else if (existing is not ComponentList<T>)
            {
                throw new InvalidOperationException($"Component list for id {componentTypeId} exists with different type.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(int id) => Signature.Contains(id);

        public void SetComponentValue<T>(int componentTypeId, int slot, T value)
        {
            EnsureComponentList<T>(componentTypeId);
            var list = (ComponentList<T>)_componentLists[componentTypeId];
            list.AddAtSlot(slot, value);
        }

        public void SetComponentValueBoxed(int componentTypeId, int slot, object value)
        {
            if (!_componentLists.TryGetValue(componentTypeId, out var list))
                throw new InvalidOperationException($"Component {componentTypeId} not found in archetype.");

            if (slot >= _entities.Count)
                throw new InvalidOperationException($"Invalid slot {slot}, entityCount={_entities.Count}");

            list.SetValueBoxed(slot, value);
        }

        /// <summary>
        /// Returns a high-performance Span for iterating components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpan<T>(int componentTypeId)
        {
            if (!_componentLists.TryGetValue(componentTypeId, out var obj))
                throw new InvalidOperationException($"Component {componentTypeId} not in archetype.");

            if (obj is ComponentList<T> typed)
                return typed.AsSpan();

            throw new InvalidOperationException($"Component list for id {componentTypeId} is not of expected type {typeof(T).Name}.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetComponentSpan<T>(out Span<T> span)
        {
            foreach (var kv in _componentLists)
            {
                if (kv.Value is ComponentList<T> typed)
                {
                    span = typed.AsSpan();
                    return true;
                }
            }
            span = default;
            return false;
        }

        public bool Matches(params Type[] componentTypes)
        {
            foreach (var t in componentTypes)
            {
                //int id = ComponentTypeRegistry.GetId(t);
                int id = ComponentManager.GetTypeId(t);
                if (!_componentLists.ContainsKey(id))
                    return false;
            }
            return true;
        }

        public Entity[] GetEntityArray() => _entities.ToArray();
    }
}