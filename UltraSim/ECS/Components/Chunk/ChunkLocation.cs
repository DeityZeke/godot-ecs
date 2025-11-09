#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// 3D chunk coordinates in the world grid.
    /// XZ plane forms the base grid, Y represents vertical layers.
    /// </summary>
    public struct ChunkLocation
    {
        public int X;
        public int Z;
        public int Y;

        public ChunkLocation(int x, int z, int y)
        {
            X = x;
            Z = z;
            Y = y;
        }

        public override string ToString() => $"Chunk({X}, {Z}, Y:{Y})";

        public override bool Equals(object? obj)
        {
            if (obj is ChunkLocation other)
                return X == other.X && Z == other.Z && Y == other.Y;
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X;
                hash = hash * 31 + Z;
                hash = hash * 31 + Y;
                return hash;
            }
        }

        public static bool operator ==(ChunkLocation a, ChunkLocation b)
            => a.X == b.X && a.Z == b.Z && a.Y == b.Y;

        public static bool operator !=(ChunkLocation a, ChunkLocation b)
            => !(a == b);
    }
}
