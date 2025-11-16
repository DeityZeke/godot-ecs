#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using Client.ECS;
using Client.ECS.Components;
using Client.ECS.Rendering;

namespace Client.ECS.Systems
{
    /// <summary>
    /// RenderVisibilitySystem - SINGLE RESPONSIBILITY: Frustum culling.
    ///
    /// Design doc quote: "Performed per frame (lightweight): frustum cull and toggle visibility."
    ///
    /// This system:
    /// 1. Runs EVERY FRAME (EveryFrame tick rate)
    /// 2. Queries chunks from all zone tag archetypes (Near/Mid/Far)
    /// 3. Tests chunk bounds against camera frustum
    /// 4. Toggles RenderChunk.Visible flag
    ///
    /// ECS PATTERN: Queries all zone tags to cull all visible chunks.
    /// - No read conflicts with zone systems (they read their specific tags, we read all tags)
    /// - Each zone tag query returns separate archetype
    ///
    /// DOES NOT:
    /// - Build visuals (that's zone systems' job)
    /// - Assign zones (that's RenderChunkManager's job)
    /// - Upload to GPU (that's zone systems' job)
    ///
    /// Performance: O(active chunks) per frame - lightweight, ~1k checks max
    /// Parallelization: Per-chunk culling tests run in parallel
    ///
    /// DEPENDENCIES: Requires RenderChunkManager to have tagged chunks with zones.
    /// Runs AFTER zone systems to ensure visuals are built before culling.
    /// </summary>
    [RequireSystem("Client.ECS.Systems.RenderChunkManager")]
    [RequireSystem("Client.ECS.Systems.DynamicEntityRenderSystem")]
    [RequireSystem("Client.ECS.Systems.StaticEntityRenderSystem")]
    [RequireSystem("Client.ECS.Systems.BillboardEntityRenderSystem")]
    public sealed class RenderVisibilitySystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SystemSettings
        {
            public BoolSetting EnableFrustumCulling { get; private set; }
            public FloatSetting FrustumPadding { get; private set; }
            public BoolSetting UseXZOnlyFrustum { get; private set; }
            public IntSetting ParallelThreshold { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                EnableFrustumCulling = RegisterBool("Enable Frustum Culling", true,
                    tooltip: "Cull chunks outside camera frustum");

                FrustumPadding = RegisterFloat("Frustum Padding", 32.0f,
                    min: 0.0f, max: 128.0f, step: 8.0f,
                    tooltip: "Expand frustum bounds by this many meters (prevents pop-in at edges)");

                UseXZOnlyFrustum = RegisterBool("Use XZ-Only Frustum", true,
                    tooltip: "Ignore top/bottom frustum planes (prevents hiding ground chunks when looking down)");

                ParallelThreshold = RegisterInt("Parallel Threshold", 128,
                    min: 32, max: 2048, step: 32,
                    tooltip: "Minimum chunks before parallel culling kicks in");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log culling statistics");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Render Visibility System";
        public override int SystemId => typeof(RenderVisibilitySystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: Zone tags for archetype filtering, RenderChunk for bounds
        public override Type[] ReadSet { get; } = new[] { typeof(RenderChunk), typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };
        // Write: Update Visible flag in RenderChunk
        public override Type[] WriteSet { get; } = new[] { typeof(RenderChunk) };

        private int _frameCounter = 0;
        private int _visibleCount = 0;
        private int _culledCount = 0;

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Frustum culling: {SystemSettings.EnableFrustumCulling.Value}, Padding: {SystemSettings.FrustumPadding.Value}m");
        }

        public override void Update(World world, double delta)
        {
            _frameCounter++;

            // Get camera frustum planes
            if (!CameraCache.IsValid)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] CameraCache not valid");
                }
                return;
            }

            var frustumPlanes = CameraCache.FrustumPlanes;
            if (!SystemSettings.EnableFrustumCulling.Value || frustumPlanes == null || frustumPlanes.Count == 0)
            {
                // Culling disabled - mark all chunks visible
                MarkAllVisible(world);
                return;
            }

            // Perform frustum culling
            PerformFrustumCulling(world, frustumPlanes);

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                Logging.Log($"[{Name}] Visible: {_visibleCount}, Culled: {_culledCount}");
            }
        }

        private void MarkAllVisible(World world)
        {
            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();

            // Query all zone tag archetypes (Near, Mid, Far)
            var zoneTagTypes = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };
            var zoneTagTypesSpan = zoneTagTypes.AsSpan();

            for (int t = 0; t < zoneTagTypesSpan.Length; t++)
            {
                using var archetypes = world.QueryArchetypes(zoneTagTypesSpan[t]);

                foreach (var archetype in archetypes)
                {
                    if (archetype.Count == 0)
                        continue;

                    var renderChunks = archetype.GetComponentSpan<RenderChunk>(renderChunkTypeId);

                    for (int i = 0; i < renderChunks.Length; i++)
                    {
                        ref var renderChunk = ref renderChunks[i];
                        renderChunk.Visible = true;
                    }
                }
            }
        }

        private void PerformFrustumCulling(World world, Godot.Collections.Array<Plane> frustumPlanes)
        {
            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();

            // Query all zone tag archetypes (Near, Mid, Far)
            var zoneTagTypes = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };
            var zoneTagTypesSpan = zoneTagTypes.AsSpan();
            var allArchetypes = new List<Archetype>();

            for (int t = 0; t < zoneTagTypesSpan.Length; t++)
            {
                using var archetypes = world.QueryArchetypes(zoneTagTypesSpan[t]);
                foreach (var arch in archetypes)
                    allArchetypes.Add(arch);
            }

            _visibleCount = 0;
            _culledCount = 0;

            // Count total chunks for parallelization decision
            int totalChunks = 0;
            foreach (var archetype in allArchetypes)
            {
                totalChunks += archetype.Count;
            }

            // Use parallel culling if we have many chunks
            bool useParallel = totalChunks >= SystemSettings.ParallelThreshold.Value;

            if (useParallel)
            {
                PerformParallelCulling(allArchetypes, renderChunkTypeId, frustumPlanes);
            }
            else
            {
                PerformSequentialCulling(allArchetypes, renderChunkTypeId, frustumPlanes);
            }
        }

        private void PerformSequentialCulling(
            List<Archetype> archetypes,
            int renderChunkTypeId,
            Godot.Collections.Array<Plane> frustumPlanes)
        {
            float padding = SystemSettings.FrustumPadding.Value;

            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var renderChunks = archetype.GetComponentSpan<RenderChunk>(renderChunkTypeId);

                for (int i = 0; i < renderChunks.Length; i++)
                {
                    ref var renderChunk = ref renderChunks[i];

                    // Perform frustum test (all chunks with zone tags are active)
                    bool visible = FrustumUtility.IsVisible(renderChunk.Bounds, frustumPlanes, padding);
                    renderChunk.Visible = visible;

                    if (visible)
                        _visibleCount++;
                    else
                        _culledCount++;
                }
            }
        }

        private void PerformParallelCulling(
            List<Archetype> archetypes,
            int renderChunkTypeId,
            Godot.Collections.Array<Plane> frustumPlanes)
        {
            float padding = SystemSettings.FrustumPadding.Value;
            int visibleCount = 0;
            int culledCount = 0;

            Parallel.ForEach(archetypes, archetype =>
            {
                if (archetype.Count == 0)
                    return;

                var renderChunks = archetype.GetComponentSpan<RenderChunk>(renderChunkTypeId);
                int localVisible = 0;
                int localCulled = 0;

                for (int i = 0; i < renderChunks.Length; i++)
                {
                    ref var renderChunk = ref renderChunks[i];

                    // Perform frustum test (all chunks with zone tags are active)
                    bool visible = FrustumUtility.IsVisible(renderChunk.Bounds, frustumPlanes, padding);
                    renderChunk.Visible = visible;

                    if (visible)
                        localVisible++;
                    else
                        localCulled++;
                }

                System.Threading.Interlocked.Add(ref visibleCount, localVisible);
                System.Threading.Interlocked.Add(ref culledCount, localCulled);
            });

            _visibleCount = visibleCount;
            _culledCount = culledCount;
        }

        public override void OnShutdown(World world)
        {
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
        }
    }
}
