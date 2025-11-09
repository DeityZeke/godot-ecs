
#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// Compact entity handle using 64-bit bit-packing (Index:Version).
    /// Upper 32 bits store Version, lower 32 bits store Index.
    /// Performance improvements over separate fields:
    /// - Single comparison for equality (instead of 2)
    /// - Faster hashing (single ulong vs HashCode.Combine)
    /// - Better cache efficiency for Entity arrays
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        private readonly ulong _packed;

        public static readonly Entity Invalid = new(0, 0);

        public Entity(uint index, uint version)
        {
            // Version occupies high 32 bits, Index occupies low 32 bits
            _packed = ((ulong)version << 32) | index;
        }

        /// <summary>
        /// Create entity from packed ulong (for deserialization)
        /// </summary>
        public Entity(ulong packed)
        {
            _packed = packed;
        }

        public uint Index => (uint)(_packed & 0xFFFFFFFF);
        public uint Version => (uint)(_packed >> 32);

        public bool IsValid => _packed != 0; // (0,0) = invalid

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) => _packed == other._packed;
        public override bool Equals(object? obj) => obj is Entity e && Equals(e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => _packed.GetHashCode();

        public override string ToString() => $"Entity({Index},v{Version})";

        public static bool operator ==(Entity left, Entity right) => left._packed == right._packed;
        public static bool operator !=(Entity left, Entity right) => left._packed != right._packed;

        /// <summary>
        /// Expose raw packed value for serialization/hashing
        /// </summary>
        public ulong Packed => _packed;
    }
}