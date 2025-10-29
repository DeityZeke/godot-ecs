namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Represents the position of a camera in the scene.
    /// Used for camera entities or view frustum culling.
    /// </summary>
    public struct CameraPosition
    {
        public float X, Y, Z;

        public CameraPosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"Camera({X:F2}, {Y:F2}, {Z:F2})";
    }
}
