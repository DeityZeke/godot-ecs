namespace UltraSim.ECS.Components
{
    /// <summary>
    /// 3D position in world space.
    /// </summary>
    public struct Position
    {
        public float X, Y, Z;

        public Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
