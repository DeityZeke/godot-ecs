#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using Client.ECS;
using Client.ECS.Components;

namespace Client.ECS.Systems
{
    /// <summary>
    /// Hybrid rendering system with 3 zones based on camera distance:
    /// - Core (3x3x3 or 5x5x5): Individual MeshInstance3D per chunk (full interactivity)
    /// - Near (configurable): MultiMesh batching per chunk (visual only)
    /// - Far (optional): Billboard/impostor rendering (ultra low-res)
    ///
    /// Updates RenderZone component based on camera position and chunk location.
    ///
    /// DEPENDENCY: Requires ChunkSystem to be registered first for chunk management.
    /// </summary>
    [RequireSystem("UltraSim.Server.ECS.Systems.ChunkSystem")]
    public sealed class HybridRenderSystem : BaseSystem
    {
        #region Settings
        public sealed class Settings : SettingsManager
        {
            public IntSetting CoreBubbleSize { get; private set; }
            public IntSetting NearZoneDistance { get; private set; }
            public BoolSetting EnableFarZone { get; private set; }
            public IntSetting FarZoneDistance { get; private set; }
            public BoolSetting EnableFrustumCulling { get; private set; }
            public IntSetting UpdateFrequency { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }
            public Settings()
            {
                CoreBubbleSize = RegisterInt("Core Bubble Size", 3,
                    min: 1, max: 11, step: 2,
                    tooltip: "Size of MeshInstance3D bubble (3 = 3x3x3 = 27 chunks, 5 = 5x5x5 = 125 chunks)");
                NearZoneDistance = RegisterInt("Near Zone Distance", 8,
                    min: 0, max: 64, step: 1,
                    tooltip: "MultiMesh render distance in chunks beyond core bubble (0 = disabled)");
                EnableFarZone = RegisterBool("Enable Far Zone", false,
                    tooltip: "Enable billboard/impostor rendering for distant chunks");
                FarZoneDistance = RegisterInt("Far Zone Distance", 16,
                    min: 0, max: 128, step: 1,
                    tooltip: "Billboard render distance in chunks (only if Far Zone enabled)");
                EnableFrustumCulling = RegisterBool("Enable Frustum Culling", true,
                    tooltip: "Cull chunks outside camera frustum");
                UpdateFrequency = RegisterInt("Update Frequency", 10,
                    min: 1, max: 60, step: 1,
                    tooltip: "Update zone assignments every N frames");
                EnableDebugLogs = RegisterBool("Enable Debug Logs", true,
                    tooltip: "Log zone transitions and culling operations");
            }
        }
        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;
        #endregion
        public override string Name => "Hybrid Render System";
        public override int SystemId => typeof(HybridRenderSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;
        public override Type[] ReadSet { get; } = new[] { typeof(ChunkLocation), typeof(ChunkBounds) };
        public override Type[] WriteSet { get; } = new[] { typeof(RenderZone), typeof(RenderChunk) };
        private ChunkManager? _chunkManager;
        private CommandBuffer _buffer = new();
        private int _frameCounter = 0;
        private ChunkLocation _lastCameraChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private readonly Dictionary<ChunkLocation, RenderZoneType> _chunkZoneCache = new();
        private static readonly int ChunkLocationTypeId = ComponentManager.GetTypeId<ChunkLocation>();
        private static readonly int ChunkBoundsTypeId = ComponentManager.GetTypeId<ChunkBounds>();
        private static readonly int RenderZoneTypeId = ComponentManager.GetTypeId<RenderZone>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();
        public override void OnInitialize(World world)
        {
            // ChunkManager will be accessed via static service locator pattern
            // This is set during WorldECS initialization
            _chunkManager = null; // Will be set by SetChunkManager() from WorldECS
            Logging.Log($"[{Name}] Initialized - Core bubble: {SystemSettings.CoreBubbleSize.Value}x{SystemSettings.CoreBubbleSize.Value}x{SystemSettings.CoreBubbleSize.Value}");
            Logging.Log($"[{Name}] Near zone: {SystemSettings.NearZoneDistance.Value} chunks, Far zone: {(SystemSettings.EnableFarZone.Value ? SystemSettings.FarZoneDistance.Value + " chunks" : "disabled")}");
        }
        /// <summary>
        /// Set the ChunkManager reference (called by WorldECS after ChunkSystem initialization).
        /// </summary>
        public void SetChunkManager(ChunkManager chunkManager)
        {
            _chunkManager = chunkManager;
            Logging.Log($"[{Name}] ChunkManager reference set");
        }
        public override void Update(World world, double delta)
        {
            if (_frameCounter == 0 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] First Update call - initializing");
            }
            if (_chunkManager == null)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] ChunkManager is null, waiting for connection");
                }
                _frameCounter++;
                return;
            }
            _frameCounter++;
            // Use camera cache (updated on main thread by WorldECS)
            if (!CameraCache.IsValid)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] CameraCache not valid");
                }
                return;
            }
            Vector3 cameraPos = CameraCache.Position;
            ChunkLocation cameraChunk = _chunkManager.WorldToChunk(cameraPos.X, cameraPos.Y, cameraPos.Z);
            if (_frameCounter == 1 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Camera at chunk {cameraChunk}, position {cameraPos}");
            }
            // Only update zone assignments at specified frequency
            int frequency = SystemSettings.UpdateFrequency.Value;
            bool shouldUpdate = _frameCounter % frequency == 0;
            // Always update if camera moved to a different chunk
            bool cameraMovedChunk = cameraChunk != _lastCameraChunk;
            if (cameraMovedChunk)
            {
                if (SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Camera moved to chunk {cameraChunk}");
                }
                _lastCameraChunk = cameraChunk;
                shouldUpdate = true;
            }
            if (shouldUpdate)
            {
                UpdateRenderZones(world, cameraChunk);
                _buffer.Apply(world);
            }
        }
        /// <summary>
        /// Update render zone assignments for all chunks based on camera position.
        /// </summary>
        private void UpdateRenderZones(World world, ChunkLocation cameraChunk)
        {
            if (_chunkManager == null)
                return;
            int coreBubbleRadius = SystemSettings.CoreBubbleSize.Value / 2; // 3 -> 1, 5 -> 2
            int nearDistance = SystemSettings.NearZoneDistance.Value;
            int farDistance = SystemSettings.FarZoneDistance.Value;
            bool enableFarZone = SystemSettings.EnableFarZone.Value;
            bool enableFrustumCulling = SystemSettings.EnableFrustumCulling.Value;
            // Get frustum planes from cache if culling is enabled
            Godot.Collections.Array<Plane>? frustumPlanes = enableFrustumCulling ? CameraCache.FrustumPlanes : null;
            // First, build a map of chunk location -> render zone
            _chunkZoneCache.Clear();
            foreach (var kvp in _chunkManager.EnumerateChunks())
            {
                var location = kvp.Key;

                int dx = Math.Abs(location.X - cameraChunk.X);
                int dy = Math.Abs(location.Y - cameraChunk.Y);
                int dz = Math.Abs(location.Z - cameraChunk.Z);
                int maxDist = Math.Max(Math.Max(dx, dz), dy);

                RenderZoneType zone;
                if (maxDist <= coreBubbleRadius)
                {
                    zone = RenderZoneType.Core;
                }
                else if (maxDist <= coreBubbleRadius + nearDistance)
                {
                    zone = RenderZoneType.Near;
                }
                else if (enableFarZone && maxDist <= coreBubbleRadius + farDistance)
                {
                    zone = RenderZoneType.Far;
                }
                else
                {
                    zone = RenderZoneType.None;
                }

                // Only cull far-zone chunks; keep core/near populated so turning the camera shows nearby entities immediately.
                if (zone == RenderZoneType.Far && enableFrustumCulling && frustumPlanes != null)
                {
                    var chunkBounds = _chunkManager.ChunkToWorldBounds(location);
                    if (!IsInFrustum(chunkBounds, frustumPlanes))
                    {
                        zone = RenderZoneType.None;
                    }
                }

                _chunkZoneCache[location] = zone;
            }
            // Now assign render zones to entities based on their chunk
            var entityArchetypes = world.QueryArchetypes(typeof(Position), typeof(RenderTag), typeof(ChunkOwner));
            int totalEntities = 0;
            int coreCount = 0, nearCount = 0, farCount = 0, culledCount = 0;
            foreach (var arch in entityArchetypes)
            {
                if (arch.Count == 0) continue;
                var chunkOwners = arch.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                bool hasRenderZone = arch.HasComponent(RenderZoneTypeId);
                Span<RenderZone> existingZones = hasRenderZone ? arch.GetComponentSpan<RenderZone>(RenderZoneTypeId) : Span<RenderZone>.Empty;
                var entities = arch.GetEntityArray();
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var chunkOwner = chunkOwners[i];
                    if (!chunkOwner.IsAssigned)
                        continue;
                    totalEntities++;
                    // Look up the zone for this entity's chunk
                    RenderZoneType zone = RenderZoneType.None;
                    if (_chunkZoneCache.TryGetValue(chunkOwner.Location, out var mappedZone))
                    {
                        zone = mappedZone;
                    }

                    bool needsUpdate = true;
                    if (hasRenderZone)
                    {
                        var currentZone = existingZones[i];
                        needsUpdate = currentZone.Zone != zone || currentZone.LastUpdateFrame != (ulong)_frameCounter;
                    }

                    if (needsUpdate)
                    {
                        _buffer.AddComponent(entity.Index, RenderZoneTypeId, new RenderZone(zone, (ulong)_frameCounter));
                    }

                    switch (zone)
                    {
                        case RenderZoneType.Core:
                            coreCount++;
                            break;
                        case RenderZoneType.Near:
                            nearCount++;
                            break;
                        case RenderZoneType.Far:
                            farCount++;
                            break;
                        default:
                            culledCount++;
                            break;
                    }
                }
            }
            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                Logging.Log($"[{Name}] Zone distribution: Core={coreCount}, Near={nearCount}, Far={farCount}, Culled={culledCount} (Total: {totalEntities} entities, {_chunkZoneCache.Count} chunks)");
            }
        }
        /// <summary>
        /// Check if an AABB is inside or intersecting the camera frustum.
        /// </summary>
        private bool IsInFrustum(ChunkBounds bounds, Godot.Collections.Array<Plane> frustumPlanes)
        {
            var min = new Vector3(bounds.MinX, bounds.MinY, bounds.MinZ);
            var max = new Vector3(bounds.MaxX, bounds.MaxY, bounds.MaxZ);
            var center = (min + max) * 0.5f;
            var extents = (max - min) * 0.5f;

            foreach (var plane in frustumPlanes)
            {
                var normal = plane.Normal;
                float r =
                    extents.X * Math.Abs(normal.X) +
                    extents.Y * Math.Abs(normal.Y) +
                    extents.Z * Math.Abs(normal.Z);

                float dist = normal.Dot(center) + plane.D;
                if (dist + r < 0)
                    return false;
            }

            return true;
        }
        public override void OnShutdown(World world)
        {
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
            _buffer.Dispose();
        }
    }
}
