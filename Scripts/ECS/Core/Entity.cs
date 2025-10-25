
#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// Lightweight entity handle with index and version for safe references.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Index;
        public readonly int Version;

        public static readonly Entity Invalid = new(-1, 0);

        public Entity(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public bool IsValid => Index >= 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object? obj) => obj is Entity e && Equals(e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode.Combine(Index, Version);
        public override string ToString() => $"Entity({Index},v{Version})";

        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
    }
}