using System.Runtime.InteropServices;

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// 3D velocity vector (units per second).
    /// Sequential layout ensures predictable memory layout for SIMD operations.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Velocity
    {
        public float X, Y, Z;

        public Velocity(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
