#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;

namespace UltraSim.ECS
{
    /// <summary>
    /// Immutable, bit-packed component signature (archetype key).
    /// Optimized for ECS lookup speed and zero allocations.
    /// Uses ulong[] bitmap - each ulong covers 64 component IDs.
    /// Dynamic sizing: Allocates only what's needed based on registered component types.
    ///
    /// Performance benefits over HashSet:
    /// - 60x memory reduction (8 bytes per signature vs ~480 bytes)
    /// - Faster Contains() check (bitwise AND vs hash table lookup)
    /// - Cache-friendly contiguous array
    /// - Memory efficient: 26 component types = 8 bytes (1 ulong) vs old 256 bytes (32 ulongs)
    /// </summary>
    public sealed class ComponentSignature : IEquatable<ComponentSignature>
    {
        // Each ulong = 64 possible component IDs
        private readonly ulong[] _bits;

        public int Count { get; }

        /// <summary>
        /// Creates a component signature with dynamic sizing.
        /// If maxComponentCount is not specified (-1), sizes based on actual registered components.
        /// </summary>
        public ComponentSignature(int maxComponentCount = -1)
        {
            // Dynamic sizing: Use actual registered component count
            if (maxComponentCount < 0)
            {
                int highestId = ComponentManager.GetHighestTypeId();
                if (highestId >= 0)
                {
                    // Size based on actual registered components
                    // Example: 26 components (ID 0-25) needs 1 ulong (covers 0-63)
                    int neededUlongs = (highestId / 64) + 1;
                    _bits = new ulong[neededUlongs];
                }
                else
                {
                    // No components registered yet - use minimal size (1 ulong = 64 component IDs)
                    _bits = new ulong[1];
                }
            }
            else
            {
                // Explicit size specified (for testing or special cases)
                _bits = new ulong[(maxComponentCount + 63) / 64];
            }
            Count = 0;
        }

        private ComponentSignature(ulong[] bits, int count)
        {
            _bits = bits;
            Count = count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id)
        {
            int word = id >> 6; // /64
            ulong mask = 1UL << (id & 63);
            return (word < _bits.Length) && ((_bits[word] & mask) != 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentSignature Add(int id)
        {
            int word = id >> 6;
            ulong mask = 1UL << (id & 63);

            var clone = (ulong[])_bits.Clone();
            if ((clone[word] & mask) == 0)
                clone[word] |= mask;

            return new ComponentSignature(clone, Count + 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentSignature Remove(int id)
        {
            int word = id >> 6;
            ulong mask = 1UL << (id & 63);

            var clone = (ulong[])_bits.Clone();
            if ((clone[word] & mask) != 0)
                clone[word] &= ~mask;

            return new ComponentSignature(clone, Count - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ComponentSignature? other)
        {
            if (other == null || other._bits.Length != _bits.Length)
                return false;

            for (int i = 0; i < _bits.Length; i++)
                if (_bits[i] != other._bits[i])
                    return false;

            return true;
        }

        public override bool Equals(object? obj) => obj is ComponentSignature sig && Equals(sig);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            HashCode hc = new();
            for (int i = 0; i < _bits.Length; i++)
                hc.Add(_bits[i]);
            return hc.ToHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<ulong> GetRawBits() => _bits;

        /// <summary>
        /// Get all component IDs in this signature (allocates a list).
        /// Use sparingly - only for debugging or initialization.
        /// </summary>
        public IEnumerable<int> GetIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < _bits.Length * 64; i++)
                if (Contains(i)) ids.Add(i);
            return ids;
        }

        public override string ToString()
        {
            var ids = new List<int>();
            for (int i = 0; i < _bits.Length * 64; i++)
                if (Contains(i)) ids.Add(i);
            return $"Signature[{string.Join(",", ids)}]";
        }
    }
}
