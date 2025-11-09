#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// World-space axis-aligned bounding box for a chunk.
    /// Used for spatial queries and frustum culling.
    /// </summary>
    public struct ChunkBounds
    {
        public float MinX;
        public float MinY;
        public float MinZ;
        public float MaxX;
        public float MaxY;
        public float MaxZ;

        public ChunkBounds(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        /// <summary>
        /// Get the center point of the chunk bounds.
        /// </summary>
        public void GetCenter(out float x, out float y, out float z)
        {
            x = (MinX + MaxX) * 0.5f;
            y = (MinY + MaxY) * 0.5f;
            z = (MinZ + MaxZ) * 0.5f;
        }

        /// <summary>
        /// Get the size of the chunk bounds.
        /// </summary>
        public void GetSize(out float width, out float height, out float depth)
        {
            width = MaxX - MinX;
            height = MaxY - MinY;
            depth = MaxZ - MinZ;
        }

        /// <summary>
        /// Check if a world-space point is inside this chunk.
        /// </summary>
        public bool Contains(float x, float y, float z)
        {
            return x >= MinX && x < MaxX
                && y >= MinY && y < MaxY
                && z >= MinZ && z < MaxZ;
        }

        /// <summary>
        /// Check if this chunk bounds intersects with another.
        /// </summary>
        public bool Intersects(ChunkBounds other)
        {
            return MinX < other.MaxX && MaxX > other.MinX
                && MinY < other.MaxY && MaxY > other.MinY
                && MinZ < other.MaxZ && MaxZ > other.MinZ;
        }

        /// <summary>
        /// Get squared distance from point to nearest point on bounds.
        /// Returns 0 if point is inside.
        /// </summary>
        public float GetSquaredDistanceToPoint(float x, float y, float z)
        {
            float dx = 0f;
            float dy = 0f;
            float dz = 0f;

            if (x < MinX) dx = MinX - x;
            else if (x > MaxX) dx = x - MaxX;

            if (y < MinY) dy = MinY - y;
            else if (y > MaxY) dy = y - MaxY;

            if (z < MinZ) dz = MinZ - z;
            else if (z > MaxZ) dz = z - MaxZ;

            return dx * dx + dy * dy + dz * dz;
        }

        public override string ToString()
            => $"Bounds[({MinX:F1}, {MinY:F1}, {MinZ:F1}) to ({MaxX:F1}, {MaxY:F1}, {MaxZ:F1})]";
    }
}
