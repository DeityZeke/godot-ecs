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

        // Deferred compaction: track dead slots for reuse
        private readonly List<int> _deadSlots = new();
        private int _liveCount;  // Number of live entities (excludes dead slots)

        // Sentinel value for dead entities
        private static readonly Entity DeadEntity = new Entity(uint.MaxValue);

        private const int DefaultComponentCapacity = 8;
        private const int DefaultEntityCapacity = 1024;

        public ComponentSignature Signature { get; }

        // Count returns LIVE entities only (excluding dead slots)
        public int Count => _liveCount;

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

            // Validate live count matches actual live entities
            int actualLiveCount = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Packed != DeadEntity.Packed)
                    actualLiveCount++;
            }

            if (actualLiveCount != _liveCount)
                yield return $"Archetype live count mismatch: _liveCount={_liveCount}, actual={actualLiveCount}";

            // Validate dead slots are actually dead
            foreach (var deadSlot in _deadSlots)
            {
                if (deadSlot < 0 || deadSlot >= _entities.Count)
                    yield return $"Dead slot {deadSlot} out of bounds (count={_entities.Count})";
                else if (_entities[deadSlot].Packed != DeadEntity.Packed)
                    yield return $"Dead slot {deadSlot} contains live entity {_entities[deadSlot]}";
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
            int slot;

            // Try to reuse a dead slot first
            if (_deadSlots.Count > 0)
            {
                slot = _deadSlots[_deadSlots.Count - 1];
                _deadSlots.RemoveAt(_deadSlots.Count - 1);
                _entities[slot] = e;

                // Component data already exists at this slot, no need to expand lists
            }
            else
            {
                // No dead slots available, append new slot
                slot = _entities.Count;
                _entities.Add(e);

                // Expand each component list to accommodate the new slot
                for (int i = 0; i < _componentCount; i++)
                {
                    _lists[i].AddDefault();
                }
            }

            _liveCount++;
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

            RemoveAtSwap(slot);
        }

        public void RemoveAtSwap(int slot)
        {
            // Bounds check - slot may be invalid if entity already removed
            if (slot < 0 || slot >= _entities.Count)
                return;

            // Check if already dead
            if (_entities[slot].Packed == DeadEntity.Packed)
                return;

            // Mark slot as dead instead of swapping
            _entities[slot] = DeadEntity;
            _deadSlots.Add(slot);
            _liveCount--;

            // Component data remains in place - will be overwritten when slot is reused
            // No need to update entity lookups - this entity is being destroyed anyway
        }

        /// <summary>
        /// Compact the archetype by removing all dead slots.
        /// This rebuilds the entity list to contain only live entities.
        /// Should be called periodically or when fragmentation is high.
        /// </summary>
        public void Compact()
        {
            if (_deadSlots.Count == 0)
                return;

            if (_liveCount == 0)
            {
                // All entities are dead - clear everything
                _entities.Clear();
                _deadSlots.Clear();

                for (int i = 0; i < _componentCount; i++)
                {
                    int count = _lists[i].Count;
                    for (int j = 0; j < count; j++)
                    {
                        _lists[i].RemoveLast();
                    }
                }
                return;
            }

            // Build new compacted lists
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < _entities.Count; readIndex++)
            {
                var entity = _entities[readIndex];

                // Skip dead entities
                if (entity.Packed == DeadEntity.Packed)
                    continue;

                // If we're not writing to the same position, copy data
                if (writeIndex != readIndex)
                {
                    // Move entity
                    _entities[writeIndex] = entity;

                    // Move component data
                    for (int i = 0; i < _componentCount; i++)
                    {
                        _lists[i].SwapLastIntoSlot(writeIndex, readIndex);
                    }

                    // Update entity lookup to new position
                    _world?.UpdateEntityLookup(entity.Index, this, writeIndex);
                }

                writeIndex++;
            }

            // Trim excess entries
            int toRemove = _entities.Count - writeIndex;
            if (toRemove > 0)
            {
                _entities.RemoveRange(writeIndex, toRemove);

                for (int i = 0; i < _componentCount; i++)
                {
                    for (int j = 0; j < toRemove; j++)
                    {
                        _lists[i].RemoveLast();
                    }
                }
            }

            // Clear dead slots list - all slots are now compact
            _deadSlots.Clear();
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
            // Return only live entities (skip dead slots)
            var result = new Entity[_liveCount];
            int writeIndex = 0;

            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Packed != DeadEntity.Packed)
                {
                    result[writeIndex++] = _entities[i];
                }
            }

            return result;
        }
    }
}