namespace UltraSim.ECS.Components
{
    /// <summary>
    /// 3D velocity vector (units per second).
    /// </summary>
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
