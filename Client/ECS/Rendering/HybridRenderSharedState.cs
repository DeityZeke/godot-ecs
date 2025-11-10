#nullable enable

using System;
using Godot;
using UltraSim.ECS.Components;

namespace Client.ECS.Rendering
{
    /// <summary>
    /// Shared chunk-window controller updated by HybridRenderSystem and consumed by renderer systems.
    /// Provides synchronized core (MeshInstance) and near (MultiMesh) slot sets.
    /// </summary>
    internal sealed class HybridRenderSharedState
    {
        public static HybridRenderSharedState Instance { get; } = new();

        public ChunkRenderWindow CoreWindow { get; } = new();
        public ChunkRenderWindow NearWindow { get; } = new();

        public ulong CoreVersion { get; private set; }
        public ulong NearVersion { get; private set; }
        public bool IsConfigured => _configured;

        // Thread-safe frustum state using volatile for visibility
        private volatile bool _frustumCullingEnabled;
        private volatile Godot.Collections.Array<Plane>? _frustumPlanes;

        public bool FrustumCullingEnabled => _frustumCullingEnabled;
        public Godot.Collections.Array<Plane>? FrustumPlanes => _frustumPlanes;

        private bool _configured;
        private int _coreRadiusXZ;
        private int _coreRadiusY;
        private int _nearRadiusXZ;
        private int _nearRadiusY;

        private HybridRenderSharedState() { }

        public void Configure(int coreRadiusXZ, int coreRadiusY, int nearRadiusXZ, int nearRadiusY)
        {
            coreRadiusXZ = Math.Max(0, coreRadiusXZ);
            coreRadiusY = Math.Max(0, coreRadiusY);
            nearRadiusXZ = Math.Max(coreRadiusXZ, nearRadiusXZ);
            nearRadiusY = Math.Max(coreRadiusY, nearRadiusY);

            if (!_configured ||
                _coreRadiusXZ != coreRadiusXZ ||
                _coreRadiusY != coreRadiusY ||
                _nearRadiusXZ != nearRadiusXZ ||
                _nearRadiusY != nearRadiusY)
            {
                _configured = true;
                _coreRadiusXZ = coreRadiusXZ;
                _coreRadiusY = coreRadiusY;
                _nearRadiusXZ = nearRadiusXZ;
                _nearRadiusY = nearRadiusY;
                CoreWindow.Configure(coreRadiusXZ, coreRadiusY);
                NearWindow.Configure(nearRadiusXZ, nearRadiusY);
            }
        }

        public void Reset()
        {
            CoreWindow.Reset();
            NearWindow.Reset();
            CoreVersion = 0;
            NearVersion = 0;
            _configured = false;
            _frustumCullingEnabled = false;
            _frustumPlanes = null;
        }

        public void Update(ChunkLocation cameraChunk, double delta, float recenterDelaySeconds)
        {
            if (!_configured)
                return;

            if (CoreWindow.Update(cameraChunk, delta, recenterDelaySeconds))
            {
                CoreVersion++;
            }

            if (NearWindow.Update(cameraChunk, delta, recenterDelaySeconds))
            {
                NearVersion++;
            }
        }

        public void UpdateFrustum(Godot.Collections.Array<Plane>? planes, bool enabled)
        {
            if (enabled && planes != null && planes.Count > 0)
            {
                // Update planes first, then enable (ensures planes are always valid when enabled=true)
                _frustumPlanes = planes;
                _frustumCullingEnabled = true;
            }
            else
            {
                // Disable first, then clear planes (ensures planes aren't read while being cleared)
                _frustumCullingEnabled = false;
                _frustumPlanes = null;
            }
        }
    }
}
