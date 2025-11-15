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
        private World _world;

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

        public Archetype(World world) : this(world, new ComponentSignature(), DefaultEntityCapacity, DefaultComponentCapacity) { }

        public Archetype(World world, ComponentSignature signature, int entityCapacity = DefaultEntityCapacity, int componentCapacity = DefaultComponentCapacity)
        {
            _world = world;

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
            // Validate component list sizes match entity list
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
            if (!TryGetIndex(componentTypeId, out int idx))
                return null;
            return _lists[idx] as ComponentList<T>;
        }

        public int AddEntity(Entity e)
        {
            int slot = _entities.Count;
            _entities.Add(e);

            // Expand each component list to accommodate the new slot
            for (int i = 0; i < _componentCount; i++)
            {
                _lists[i].AddDefault();
            }

            return slot;
        }

        public void MoveEntityTo(int slot, Archetype target, object? newComponent = null)
        {
            if (slot < 0 || slot >= _entities.Count)
                throw new InvalidOperationException($"Invalid slot {slot} for archetype with {_entities.Count} entities.");

            var entity = _entities[slot];
            int newSlot = target.AddEntity(entity);

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

            RemoveAtSwap(slot, entity);
        }

        public void RemoveAtSwap(int slot, Entity expectedEntity)
        {
            int last = _entities.Count - 1;
            int countBefore = _entities.Count;

            if (slot < 0 || slot > last)
            {
                Logging.Log($"[Archetype.RemoveAtSwap] Slot {slot} out of bounds (Count={countBefore}, last={last}). Entity: {expectedEntity}", LogSeverity.Warning);
                return;
            }

            // CRITICAL: Validate we're destroying the RIGHT entity using Packed (index + version)
            // This prevents destroying the wrong entity due to stale lookups during batch operations
            var actualEntity = _entities[slot];
            if (actualEntity.Packed != expectedEntity.Packed)
            {
                Logging.Log($"[Archetype.RemoveAtSwap] Entity mismatch at slot {slot}! Expected {expectedEntity}, found {actualEntity}. Ignoring to prevent destroying wrong entity.", LogSeverity.Warning);
                return;
            }

            // Swap-and-pop: move last entity into the removed slot
            if (slot != last)
            {
                var movedEntity = _entities[last];
                _entities[slot] = movedEntity;

                for (int i = 0; i < _componentCount; i++)
                    _lists[i].SwapLastIntoSlot(slot, last);

                // Update lookup for the entity that moved (using Packed for better tracking)
                _world?.UpdateEntityLookup(movedEntity.Index, this, slot);
            }

            // Remove the last element (original entity or already swapped)
            for (int i = 0; i < _componentCount; i++)
                _lists[i].RemoveLast();

            _entities.RemoveAt(last);

            int countAfter = _entities.Count;
            if (countAfter != countBefore - 1)
            {
                Logging.Log($"[Archetype.RemoveAtSwap] BUG! Count didn't decrement! Before={countBefore}, After={countAfter}, Expected={countBefore-1}", LogSeverity.Error);
            }
        }

        // Ensure a typed component list exists for this archetype
        internal void EnsureComponentList<T>(int componentTypeId) where T : struct
        {
            if (TryGetIndex(componentTypeId, out _))
                return;

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
            if (!TryGetIndex(componentTypeId, out int idx))
                throw new InvalidOperationException($"Component {componentTypeId} not found after EnsureComponentList.");
            var list = (ComponentList<T>)_lists[idx];
            list.AddAtSlot(slot, value);
        }

        public void SetComponentValueBoxed(int componentTypeId, int slot, object value)
        {
            if (!TryGetIndex(componentTypeId, out int idx))
                throw new InvalidOperationException($"Component {componentTypeId} not found in archetype.");
            if (slot >= _entities.Count)
                throw new InvalidOperationException($"Invalid slot {slot}, entityCount={_entities.Count}");
            _lists[idx].SetValueBoxed(slot, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpan<T>(int componentTypeId)
        {
            if (!TryGetIndex(componentTypeId, out int idx))
                throw new InvalidOperationException($"Component {componentTypeId} not in archetype.");
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

        public Entity[] GetEntityArray()
        {
            return _entities.ToArray();
        }
    }
}
