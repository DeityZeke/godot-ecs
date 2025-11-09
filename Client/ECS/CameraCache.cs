#nullable enable

using Godot;

namespace Client.ECS
{
    /// <summary>
    /// Thread-safe camera cache updated on main thread for systems that need camera access.
    /// Avoids "must be accessed from main thread" errors when systems run in parallel.
    /// </summary>
    public static class CameraCache
    {
        private static volatile bool _isValid;
        private static Vector3 _position;
        private static Godot.Collections.Array<Plane>? _frustumPlanes;

        public static bool IsValid
        {
            get => _isValid;
            set => _isValid = value;
        }

        public static Vector3 Position
        {
            get => _position;
            set => _position = value;
        }

        public static Godot.Collections.Array<Plane>? FrustumPlanes
        {
            get => _frustumPlanes;
            set => _frustumPlanes = value;
        }
    }
}
