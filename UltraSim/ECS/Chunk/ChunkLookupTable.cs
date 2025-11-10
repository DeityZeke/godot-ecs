#nullable enable

using System;
using System.Runtime.CompilerServices;
using UltraSim.ECS.Components;

namespace UltraSim.ECS.Chunk
{
    /// <summary>
    /// Flat open-addressing hash table specialized for mapping ChunkLocation -> Entity.
    /// Avoids Dictionary allocations in the assignment hot path.
    /// </summary>
    internal sealed class ChunkLookupTable
    {
        private const float LoadFactor = 0.72f;
        private const byte StateEmpty = 0;
        private const byte StateOccupied = 1;
        private const byte StateDeleted = 2;

        private ulong[] _keys;
        private Entity[] _values;
        private byte[] _states;
        private int _count;
        private int _tombstones;

        public int Count => _count;

        public ChunkLookupTable(int capacity = 256)
        {
            var size = NextPowerOfTwo(Math.Max(4, capacity));
            _keys = new ulong[size];
            _values = new Entity[size];
            _states = new byte[size];
        }

        public bool ContainsKey(ChunkLocation location) => FindSlot(location.Packed) >= 0;

        public bool TryGetValue(ChunkLocation location, out Entity entity)
        {
            int slot = FindSlot(location.Packed);
            if (slot >= 0)
            {
                entity = _values[slot];
                return true;
            }

            entity = Entity.Invalid;
            return false;
        }

        public bool TryAdd(ChunkLocation location, Entity entity, bool overwrite = false)
            => TryAdd(packed: location.Packed, entity, overwrite);

        private bool TryAdd(ulong packed, Entity entity, bool overwrite)
        {
            EnsureCapacity();

            int capacity = _keys.Length;
            int hash = Hash(packed);
            int slot = hash & (capacity - 1);
            int firstDeleted = -1;

            while (true)
            {
                var state = _states[slot];
                if (state == StateEmpty)
                {
                    if (firstDeleted >= 0)
                        slot = firstDeleted;

                    _keys[slot] = packed;
                    _values[slot] = entity;
                    _states[slot] = StateOccupied;
                    _count++;

                    if (firstDeleted >= 0)
                        _tombstones--;

                    return true;
                }

                if (state == StateDeleted)
                {
                    if (firstDeleted < 0)
                        firstDeleted = slot;
                }
                else if (_keys[slot] == packed)
                {
                    if (overwrite)
                    {
                        _values[slot] = entity;
                        return true;
                    }

                    return false;
                }

                slot = (slot + 1) & (capacity - 1);
            }
        }

        public bool Remove(ChunkLocation location, out Entity removed)
        {
            int slot = FindSlot(location.Packed);
            if (slot < 0)
            {
                removed = Entity.Invalid;
                return false;
            }

            removed = _values[slot];
            _states[slot] = StateDeleted;
            _values[slot] = Entity.Invalid;
            _count--;
            _tombstones++;
            return true;
        }

        public void Clear()
        {
            Array.Fill(_states, StateEmpty);
            Array.Fill(_values, Entity.Invalid);
            _count = 0;
            _tombstones = 0;
        }

        public void ForEach(Action<ChunkLocation, Entity> action)
        {
            var states = _states;
            var keys = _keys;
            var values = _values;

            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == StateOccupied)
                {
                    action(ChunkLocation.FromPacked(keys[i]), values[i]);
                }
            }
        }

        private int FindSlot(ulong packed)
        {
            int capacity = _keys.Length;
            int hash = Hash(packed);
            int slot = hash & (capacity - 1);

            while (true)
            {
                var state = _states[slot];
                if (state == StateEmpty)
                    return -1;

                if (state == StateOccupied && _keys[slot] == packed)
                    return slot;

                slot = (slot + 1) & (capacity - 1);
            }
        }

        private void EnsureCapacity()
        {
            if ((_count + _tombstones) < _keys.Length * LoadFactor)
                return;

            Resize(_keys.Length * 2);
        }

        private void Resize(int newSize)
        {
            var targetSize = NextPowerOfTwo(newSize);
            var oldKeys = _keys;
            var oldValues = _values;
            var oldStates = _states;

            _keys = new ulong[targetSize];
            _values = new Entity[targetSize];
            _states = new byte[targetSize];
            _count = 0;
            _tombstones = 0;

            for (int i = 0; i < oldStates.Length; i++)
            {
                if (oldStates[i] == StateOccupied)
                {
                    TryAdd(oldKeys[i], oldValues[i], overwrite: true);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(ulong packed)
        {
            packed ^= packed >> 33;
            packed *= 0xff51afd7ed558ccdUL;
            packed ^= packed >> 33;
            packed *= 0xc4ceb9fe1a85ec53UL;
            packed ^= packed >> 33;
            return (int)(packed & 0x7fffffff);
        }

        private static int NextPowerOfTwo(int value)
        {
            int v = value - 1;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return Math.Max(4, v + 1);
        }
    }
}
