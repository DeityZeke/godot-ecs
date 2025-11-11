#nullable enable

using System;
using System.Collections.Generic;
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
    /// RenderChunkManager - SINGLE RESPONSIBILITY: Window sliding + zone tagging.
    ///
    /// Design doc quote: "RenderChunkManager: Maintains active window, spawns/despawns pooled chunks,
    /// and assigns Near/Mid/Far zones."
    ///
    /// This system:
    /// 1. Determines player chunk location
    /// 2. Builds window of active chunks around player
    /// 3. Tags each chunk with zone tag components (NearZoneTag/MidZoneTag/FarZoneTag)
    /// 4. Adds/removes tags to move chunks between archetypes
    /// 5. Updates only when player crosses chunk boundary or every 2s
    ///
    /// ECS PATTERN: Uses tag components instead of enums for zone assignment.
    /// - Near chunks: Have NearZoneTag component → Query<NearZoneTag>() finds them
    /// - Mid chunks: Have MidZoneTag component → Query<MidZoneTag>() finds them
    /// - Far chunks: Have FarZoneTag component → Query<FarZoneTag>() finds them
    ///
    /// Benefits:
    /// - Zone systems query DIFFERENT archetypes (no read conflicts → parallelizable!)
    /// - Automatic filtering by ECS (no manual enum checks)
    /// - Cache-friendly (archetype groups chunks by zone)
    ///
    /// DOES NOT:
    /// - Build visuals (that's zone systems' job)
    /// - Do frustum culling (that's RenderVisibilitySystem's job)
    /// - Upload to GPU (that's zone systems' job)
    /// </summary>
    public sealed class RenderChunkManager : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public IntSetting NearBubbleSize { get; private set; }
            public IntSetting MidRenderDistance { get; private set; }
            public IntSetting FarRenderDistance { get; private set; }
            public BoolSetting EnableFarZone { get; private set; }
            public FloatSetting RecenterDelaySeconds { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                NearBubbleSize = RegisterInt("Near Bubble Size", 3,
                    min: 1, max: 11, step: 2,
                    tooltip: "Size of Near zone bubble (3 = 3x3x3 = 27 chunks with MeshInstances)");

                MidRenderDistance = RegisterInt("Mid Render Distance", 16,
                    min: 4, max: 64, step: 1,
                    tooltip: "Mid zone radius for MultiMesh rendering (chunks)");

                FarRenderDistance = RegisterInt("Far Render Distance", 32,
                    min: 0, max: 128, step: 1,
                    tooltip: "Far zone radius for billboard rendering (chunks, 0 = disabled)");

                EnableFarZone = RegisterBool("Enable Far Zone", false,
                    tooltip: "Enable Far zone (billboard/impostor rendering)");

                RecenterDelaySeconds = RegisterFloat("Recenter Delay", 0.5f,
                    min: 0.0f, max: 2.0f, step: 0.1f,
                    tooltip: "Delay before recentering window around new camera chunk (prevents thrashing)");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log zone assignments and window updates");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Render Chunk Manager";
        public override int SystemId => typeof(RenderChunkManager).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: Chunk locations for zone calculation
        public override Type[] ReadSet { get; } = new[] { typeof(ChunkLocation), typeof(ChunkBounds), typeof(RenderChunk) };
        // Write: Zone tags (add/remove components)
        public override Type[] WriteSet { get; } = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };

        private ChunkManager? _chunkManager;
        private ChunkLocation _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private double _timeSinceLastUpdate = 0;
        private int _frameCounter = 0;

        // Track previous zone assignments to know which tags to remove
        private enum ZoneType : byte { None, Near, Mid, Far }
        private readonly Dictionary<ChunkLocation, ZoneType> _previousZones = new();

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Near: {SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}, Mid: {SystemSettings.MidRenderDistance.Value} chunks");
            Logging.Log($"[{Name}] Using tag component pattern for archetype-based zone filtering");
        }

        public void SetChunkManager(ChunkManager chunkManager)
        {
            _chunkManager = chunkManager;
            Logging.Log($"[{Name}] ChunkManager reference set");
        }

        public override void Update(World world, double delta)
        {
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

            var cameraPos = CameraCache.Position;
            var playerChunk = _chunkManager.WorldToChunk(cameraPos.X, cameraPos.Y, cameraPos.Z);

            // Design doc: "Skip updates if player is stationary (and < 2s since last update)"
            bool playerMovedChunk = playerChunk != _lastPlayerChunk;
            _timeSinceLastUpdate += delta;

            if (!playerMovedChunk && _timeSinceLastUpdate < SystemSettings.RecenterDelaySeconds.Value)
            {
                // Skip - player hasn't moved to new chunk and recenter delay not reached
                return;
            }

            if (playerMovedChunk)
            {
                _lastPlayerChunk = playerChunk;
                _timeSinceLastUpdate = 0;

                if (SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Player moved to chunk {playerChunk}");
                }
            }

            // Rebuild window and assign zones (via tag components)
            UpdateChunkWindowAndZones(world, playerChunk);

            _timeSinceLastUpdate = 0;
        }

        /// <summary>
        /// Core logic: Build window of active chunks and tag each with appropriate zone tag component.
        /// This is the ONLY place where zone tags are added/removed.
        /// Chunks move between archetypes as tags are added/removed.
        /// </summary>
        private void UpdateChunkWindowAndZones(World world, ChunkLocation playerChunk)
        {
            if (_chunkManager == null)
                return;

            int nearRadius = Math.Max(0, SystemSettings.NearBubbleSize.Value / 2);
            int midRadius = Math.Max(nearRadius, SystemSettings.MidRenderDistance.Value);
            int farRadius = SystemSettings.EnableFarZone.Value
                ? Math.Max(midRadius, SystemSettings.FarRenderDistance.Value)
                : midRadius;

            // Determine Y range (for now, match near radius - can be configured separately)
            int yRadius = nearRadius;

            int nearCount = 0;
            int midCount = 0;
            int farCount = 0;
            int updated = 0;

            var currentZones = new Dictionary<ChunkLocation, ZoneType>();

            // Iterate over all chunks in the Far radius (largest window)
            for (int dz = -farRadius; dz <= farRadius; dz++)
            {
                for (int dx = -farRadius; dx <= farRadius; dx++)
                {
                    for (int dy = -yRadius; dy <= yRadius; dy++)
                    {
                        var chunkLoc = new ChunkLocation(
                            playerChunk.X + dx,
                            playerChunk.Z + dz,
                            playerChunk.Y + dy);

                        // Get or ensure chunk exists
                        var chunkEntity = _chunkManager.GetChunk(chunkLoc);
                        if (chunkEntity == Entity.Invalid)
                            continue;

                        // Calculate XZ distance from player chunk (Y doesn't affect zone)
                        float distXZ = MathF.Sqrt(dx * dx + dz * dz);

                        // Determine desired zone based on distance
                        ZoneType desiredZone;
                        if (distXZ <= nearRadius)
                        {
                            desiredZone = ZoneType.Near;
                            nearCount++;
                        }
                        else if (distXZ <= midRadius)
                        {
                            desiredZone = ZoneType.Mid;
                            midCount++;
                        }
                        else if (distXZ <= farRadius)
                        {
                            desiredZone = ZoneType.Far;
                            farCount++;
                        }
                        else
                        {
                            desiredZone = ZoneType.None; // Culled
                        }

                        currentZones[chunkLoc] = desiredZone;

                        // Check if zone changed
                        ZoneType previousZone = _previousZones.TryGetValue(chunkLoc, out var prev) ? prev : ZoneType.None;

                        if (previousZone != desiredZone)
                        {
                            // Zone changed - update tags
                            UpdateZoneTags(world, chunkEntity, chunkLoc, previousZone, desiredZone);
                            updated++;
                        }

                        // Ensure RenderChunk component exists with correct bounds
                        EnsureRenderChunkComponent(world, chunkEntity, chunkLoc);
                    }
                }
            }

            // Remove tags from chunks that left the window
            foreach (var kvp in _previousZones)
            {
                if (!currentZones.ContainsKey(kvp.Key))
                {
                    var chunkEntity = _chunkManager.GetChunk(kvp.Key);
                    if (chunkEntity != Entity.Invalid)
                    {
                        RemoveAllZoneTags(world, chunkEntity, kvp.Value);
                        updated++;
                    }
                }
            }

            // Update tracking
            _previousZones.Clear();
            foreach (var kvp in currentZones)
                _previousZones[kvp.Key] = kvp.Value;

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Tagged chunks: Near={nearCount}, Mid={midCount}, Far={farCount}, Updated={updated}");
            }
        }

        private void UpdateZoneTags(World world, Entity chunkEntity, ChunkLocation location, ZoneType oldZone, ZoneType newZone)
        {
            // Remove old tag
            RemoveAllZoneTags(world, chunkEntity, oldZone);

            // Add new tag
            switch (newZone)
            {
                case ZoneType.Near:
                    world.EnqueueComponentAdd(chunkEntity.Index, ComponentManager.GetTypeId<NearZoneTag>(), new NearZoneTag());
                    break;
                case ZoneType.Mid:
                    world.EnqueueComponentAdd(chunkEntity.Index, ComponentManager.GetTypeId<MidZoneTag>(), new MidZoneTag());
                    break;
                case ZoneType.Far:
                    world.EnqueueComponentAdd(chunkEntity.Index, ComponentManager.GetTypeId<FarZoneTag>(), new FarZoneTag());
                    break;
                case ZoneType.None:
                    // No tag = culled
                    break;
            }
        }

        private void RemoveAllZoneTags(World world, Entity chunkEntity, ZoneType oldZone)
        {
            switch (oldZone)
            {
                case ZoneType.Near:
                    world.EnqueueComponentRemove(chunkEntity.Index, ComponentManager.GetTypeId<NearZoneTag>());
                    break;
                case ZoneType.Mid:
                    world.EnqueueComponentRemove(chunkEntity.Index, ComponentManager.GetTypeId<MidZoneTag>());
                    break;
                case ZoneType.Far:
                    world.EnqueueComponentRemove(chunkEntity.Index, ComponentManager.GetTypeId<FarZoneTag>());
                    break;
            }
        }

        private void EnsureRenderChunkComponent(World world, Entity chunkEntity, ChunkLocation chunkLoc)
        {
            if (!world.TryGetEntityLocation(chunkEntity, out var archetype, out var slot))
                return;

            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();
            var bounds = _chunkManager!.ChunkToWorldBounds(chunkLoc);

            if (!archetype.HasComponent(renderChunkTypeId))
            {
                // Start invisible - RenderVisibilitySystem will set to true if in frustum
                var renderChunk = new RenderChunk(chunkLoc, bounds, visible: false);
                world.EnqueueComponentAdd(chunkEntity.Index, renderChunkTypeId, renderChunk);
            }
        }

        public override void OnShutdown(World world)
        {
            _previousZones.Clear();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
        }
    }
}
