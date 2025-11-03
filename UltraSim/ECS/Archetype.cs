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
        // parallel arrays for compact storage and cache locality
        private int[] _typeIds;
        private IComponentList[] _lists;
        private int _componentCount;

        // fast O(1) lookup from typeId -> index in arrays
        private readonly Dictionary<int, int> _indexMap;

        private readonly List<Entity> _entities;

        private const int DefaultComponentCapacity = 8;
        private const int DefaultEntityCapacity = 1024;

        public ComponentSignature Signature { get; }
        public int Count => _entities.Count;

        public Archetype() : this(new ComponentSignature(), DefaultEntityCapacity, DefaultComponentCapacity) { }

        public Archetype(ComponentSignature signature, int entityCapacity = DefaultEntityCapacity, int componentCapacity = DefaultComponentCapacity)
        {
            Signature = signature;
            _entities = new List<Entity>(entityCapacity);

            _typeIds = new int[componentCapacity];
            _lists = new IComponentList[componentCapacity];
            _componentCount = 0;
            _indexMap = new Dictionary<int, int>(componentCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureComponentCapacity(int needed)
        {
            if (needed <= _typeIds.Length) return;
            int newSize = Math.Max(_typeIds.Length * 2, needed);
            Array.Resize(ref _typeIds, newSize);
            Array.Resize(ref _lists, newSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindIndex(int typeId)
        {
            return _indexMap.TryGetValue(typeId, out var idx) ? idx : -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetIndex(int typeId, out int idx) // No Branching, Faster than FindIndex // TODO - Implement Where Applicable
        {
            return _indexMap.TryGetValue(typeId, out idx);
        }

        public IEnumerable<string> DebugValidate()
        {
            for (int i = 0; i < _componentCount; i++)
            {
                var list = _lists[i];
                if (list.Count != _entities.Count)
                    yield return $"Archetype mismatch: component {_typeIds[i]} has {list.Count} entries, entities={_entities.Count}";
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentList<T>? GetComponentListTyped<T>(int componentTypeId)
        {
            int idx = FindIndex(componentTypeId);
            if (idx < 0) return null;
            return _lists[idx] as ComponentList<T>;
        }

        public void AddEntity(Entity e)
        {
            int newIndex = _entities.Count;
            _entities.Add(e);

            // expand each component list to accommodate the new slot
            for (int i = 0; i < _componentCount; i++)
            {
                _lists[i].AddDefault();
            }
        }

        public void MoveEntityTo(int slot, Archetype target, object? newComponent = null)
        {
            if (slot < 0 || slot >= _entities.Count)
                throw new InvalidOperationException($"Invalid slot {slot} for archetype with {_entities.Count} entities.");

            var entity = _entities[slot];
            target.AddEntity(entity);
            int newSlot = target.Count - 1;

            // copy shared components using linear iteration (no enumerator)
            for (int i = 0; i < _componentCount; i++)
            {
                int typeId = _typeIds[i];
                if (!target.Signature.Contains(typeId)) continue;

                var list = _lists[i];
                var value = list.GetValueBoxed(slot);
                target.SetComponentValueBoxed(typeId, newSlot, value!);
            }

            // set new component if provided
            if (newComponent != null)
            {
                int newTypeId = ComponentManager.GetTypeId(newComponent.GetType());
                target.SetComponentValueBoxed(newTypeId, newSlot, newComponent);
            }

            RemoveAtSwap(slot);
        }

        public void RemoveAtSwap(int slot)
        {
            int last = _entities.Count - 1;
            if (slot != last)
            {
                var movedEntity = _entities[last];
                _entities[slot] = movedEntity;

                // fast linear swap for each component list
                for (int i = 0; i < _componentCount; i++)
                    _lists[i].SwapLastIntoSlot(slot, last);

                World.Current?.UpdateEntityLookup(movedEntity.Index, this, slot);
            }

            // remove last element in each component list
            for (int i = 0; i < _componentCount; i++)
                _lists[i].RemoveLast();

            _entities.RemoveAt(last);
        }

        // Ensure a typed component list exists for this archetype
        internal void EnsureComponentList<T>(int componentTypeId) where T : struct
        {
            int idx = FindIndex(componentTypeId);
            if (idx >= 0) return;

            // create a new ComponentList<T> with current entity count pre-allocated
            var list = new ComponentList<T>(_entities.Count);
            for (int i = 0; i < _entities.Count; i++) list.AddDefault();

            // add to arrays
            EnsureComponentCapacity(_componentCount + 1);
            _typeIds[_componentCount] = componentTypeId;
            _lists[_componentCount] = list;
            _indexMap[componentTypeId] = _componentCount;
            _componentCount++;
            Signature.Add(componentTypeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(int id) => Signature.Contains(id);

        public void SetComponentValue<T>(int componentTypeId, int slot, T value) where T : struct
        {
            EnsureComponentList<T>(componentTypeId);
            int idx = FindIndex(componentTypeId);
            var list = (ComponentList<T>)_lists[idx];
            list.AddAtSlot(slot, value);
        }

        public void SetComponentValueBoxed(int componentTypeId, int slot, object value)
        {
            int idx = FindIndex(componentTypeId);
            if (idx < 0) throw new InvalidOperationException($"Component {componentTypeId} not found in archetype.");
            if (slot >= _entities.Count) throw new InvalidOperationException($"Invalid slot {slot}, entityCount={_entities.Count}");
            _lists[idx].SetValueBoxed(slot, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpan<T>(int componentTypeId)
        {
            int idx = FindIndex(componentTypeId);
            if (idx < 0) throw new InvalidOperationException($"Component {componentTypeId} not in archetype.");
            if (_lists[idx] is ComponentList<T> typed)
                return typed.AsSpan();

            throw new InvalidOperationException($"Component list for id {componentTypeId} is not of expected type {typeof(T).Name}.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetComponentSpan<T>(out Span<T> span)
        {
            // fast linear search for typed list
            for (int i = 0; i < _componentCount; i++)
            {
                if (_lists[i] is ComponentList<T> typed)
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
            for (int i = 0; i < componentTypes.Length; i++)
            {
                int id = ComponentManager.GetTypeId(componentTypes[i]);
                if (FindIndex(id) < 0) return false;
            }
            return true;
        }

        public Entity[] GetEntityArray() => _entities.ToArray();
    }
}



    /*
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        internal void EnsureComponentList<T>(int componentTypeId) where T : struct
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

        public void SetComponentValue<T>(int componentTypeId, int slot, T value) where T : struct
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
}*/