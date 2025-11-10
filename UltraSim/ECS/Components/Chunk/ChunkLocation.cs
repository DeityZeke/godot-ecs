#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Bit-packed chunk coordinates in the world grid (64-bit total).
    /// Layout: [20 bits X | 20 bits Z | 16 bits Y | 8 bits reserved]
    /// Range: X/Z ∈ [-524,288, 524,287], Y ∈ [-32,768, 32,767].
    /// </summary>
    public readonly struct ChunkLocation : IEquatable<ChunkLocation>
    {
        private readonly ulong _packed;

        private const int XBits = 20;
        private const int ZBits = 20;
        private const int YBits = 16;

        private const int ZShift = XBits;
        private const int YShift = XBits + ZBits;

        private const ulong XMask = (1UL << XBits) - 1UL;
        private const ulong ZMask = (1UL << ZBits) - 1UL;
        private const ulong YMask = (1UL << YBits) - 1UL;

        private const int XBias = 1 << (XBits - 1);
        private const int ZBias = 1 << (ZBits - 1);
        private const int YBias = 1 << (YBits - 1);

        public ChunkLocation(int x, int z, int y)
        {
            _packed = Pack(x, z, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ChunkLocation(ulong packed, bool _)
        {
            _packed = packed;
        }

        public int X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)((_packed & XMask) - (ulong)XBias);
        }

        public int Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(((_packed >> ZShift) & ZMask) - (ulong)ZBias);
        }

        public int Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(((_packed >> YShift) & YMask) - (ulong)YBias);
        }

        public ulong Packed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ChunkLocation other) => _packed == other._packed;

        public override bool Equals(object? obj) => obj is ChunkLocation other && Equals(other);

        public override int GetHashCode() => _packed.GetHashCode();

        public override string ToString() => $"Chunk({X}, {Z}, Y:{Y})";

        public static bool operator ==(ChunkLocation left, ChunkLocation right) => left._packed == right._packed;
        public static bool operator !=(ChunkLocation left, ChunkLocation right) => left._packed != right._packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ChunkLocation FromPacked(ulong packed) => new ChunkLocation(packed, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Pack(int x, int z, int y)
        {
            ulong ux = (ulong)((x + XBias) & (int)XMask);
            ulong uz = (ulong)((z + ZBias) & (int)ZMask);
            ulong uy = (ulong)((y + YBias) & (int)YMask);
            return ux | (uz << ZShift) | (uy << YShift);
        }
    }
}
