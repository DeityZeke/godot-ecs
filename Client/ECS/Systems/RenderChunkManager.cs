#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// RenderChunkManager - SINGLE RESPONSIBILITY: Proactive window management + zone tagging.
    ///
    /// NEW ARCHITECTURE: This system creates render chunk entities proactively.
    ///
    /// This system:
    /// 1. Determines player chunk location
    /// 2. Calculates required render window (camera ± render distance)
    /// 3. Creates render chunk entities for all locations in window
    /// 4. Tags each chunk with zone components (NearZoneTag/MidZoneTag/FarZoneTag)
    /// 5. Destroys chunks that exit the window (single-pass algorithm)
    ///
    /// SINGLE-PASS ALGORITHM:
    /// - Query all existing RenderChunk entities
    /// - Calculate new window bounds
    /// - For existing chunks:
    ///   - Inside window → Update zone tags
    ///   - Outside window → Destroy entity
    /// - Create missing chunks (window - existing)
    ///
    /// ECS PATTERN: Uses tag components for zone assignment.
    /// - Near chunks: Have NearZoneTag → Query<NearZoneTag>() finds them
    /// - Mid chunks: Have MidZoneTag → Query<MidZoneTag>() finds them
    /// - Far chunks: Have FarZoneTag → Query<FarZoneTag>() finds them
    ///
    /// Benefits:
    /// - Zone systems query DIFFERENT archetypes (no read conflicts → parallelizable!)
    /// - Automatic filtering by ECS (no manual enum checks)
    /// - Complete render coverage (no gaps in frustum)
    /// - Predictable memory usage (fixed window size)
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

        // Read: RenderChunk for querying existing chunks
        public override Type[] ReadSet { get; } = new[] { typeof(RenderChunk) };
        // Write: Zone tags + create/destroy entities
        public override Type[] WriteSet { get; } = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag), typeof(RenderChunk) };

        private ChunkManager? _chunkManager;
        private ChunkLocation _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private double _timeSinceLastUpdate = 0;
        private int _frameCounter = 0;

        // Track previous zone assignments to know which tags to remove
        private enum ZoneType : byte { None, Near, Mid, Far }
        private readonly Dictionary<ChunkLocation, ZoneType> _previousZones = new();

        private readonly CommandBuffer _buffer = new();

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Near: {SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}, Mid: {SystemSettings.MidRenderDistance.Value} chunks");
            Logging.Log($"[{Name}] Proactive render chunk creation enabled");
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

            // Skip updates if player is stationary (and < recenter delay)
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

            // Single-pass algorithm: Query → Slide → Create/Destroy
            UpdateRenderWindow(world, playerChunk);

            _timeSinceLastUpdate = 0;
        }

        /// <summary>
        /// Single-pass algorithm: Query existing chunks, slide window, create missing, destroy outside.
        /// </summary>
        private void UpdateRenderWindow(World world, ChunkLocation playerChunk)
        {
            if (_chunkManager == null)
                return;

            // Calculate window bounds
            int nearRadius = Math.Max(0, SystemSettings.NearBubbleSize.Value / 2);
            int midRadius = Math.Max(nearRadius, SystemSettings.MidRenderDistance.Value);
            int farRadius = SystemSettings.EnableFarZone.Value
                ? Math.Max(midRadius, SystemSettings.FarRenderDistance.Value)
                : midRadius;

            int yRadius = nearRadius; // Y range matches near radius

            // Calculate all chunk locations in window
            var windowLocations = new HashSet<ChunkLocation>();
            var windowZones = new Dictionary<ChunkLocation, ZoneType>();

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

                        // Calculate XZ distance from player chunk (Y doesn't affect zone)
                        float distXZ = MathF.Sqrt(dx * dx + dz * dz);

                        // Determine zone based on distance
                        ZoneType zone;
                        if (distXZ <= nearRadius)
                            zone = ZoneType.Near;
                        else if (distXZ <= midRadius)
                            zone = ZoneType.Mid;
                        else if (distXZ <= farRadius)
                            zone = ZoneType.Far;
                        else
                            zone = ZoneType.None; // Should not happen, but handle gracefully

                        if (zone != ZoneType.None)
                        {
                            windowLocations.Add(chunkLoc);
                            windowZones[chunkLoc] = zone;
                        }
                    }
                }
            }

            // Query all existing RenderChunk entities
            var existingChunks = new Dictionary<ChunkLocation, Entity>();
            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();

            var archetypes = world.QueryArchetypes(typeof(RenderChunk));
            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var renderChunks = archetype.GetComponentSpan<RenderChunk>(renderChunkTypeId);
                var entities = archetype.GetEntityArray();

                for (int i = 0; i < renderChunks.Length; i++)
                {
                    existingChunks[renderChunks[i].Location] = entities[i];
                }
            }

            // Process existing chunks: Update or Destroy
            int destroyed = 0;
            int updated = 0;

            foreach (var kvp in existingChunks)
            {
                var location = kvp.Key;
                var entity = kvp.Value;

                if (windowLocations.Contains(location))
                {
                    // Still in window - update zone tags if needed
                    var desiredZone = windowZones[location];
                    var previousZone = _previousZones.TryGetValue(location, out var prev) ? prev : ZoneType.None;

                    if (previousZone != desiredZone)
                    {
                        UpdateZoneTags(_buffer, entity, location, previousZone, desiredZone);
                        updated++;
                    }
                }
                else
                {
                    // Outside window - destroy
                    _buffer.DestroyEntity(entity);
                    destroyed++;
                }
            }

            // Create missing chunks
            int created = 0;

            foreach (var location in windowLocations)
            {
                if (!existingChunks.ContainsKey(location))
                {
                    // Missing chunk - create it
                    var bounds = _chunkManager.ChunkToWorldBounds(location);
                    var zone = windowZones[location];

                    _buffer.CreateEntity(builder =>
                    {
                        builder.Add(location);
                        builder.Add(new RenderChunk(location, bounds, visible: false));

                        // Add zone tag immediately
                        switch (zone)
                        {
                            case ZoneType.Near:
                                builder.Add(new NearZoneTag());
                                break;
                            case ZoneType.Mid:
                                builder.Add(new MidZoneTag());
                                break;
                            case ZoneType.Far:
                                builder.Add(new FarZoneTag());
                                break;
                        }
                    });

                    created++;
                }
            }

            // Apply all changes
            _buffer.Apply(world);

            // Update zone tracking
            _previousZones.Clear();
            foreach (var kvp in windowZones)
                _previousZones[kvp.Key] = kvp.Value;

            if (SystemSettings.EnableDebugLogs.Value)
            {
                int nearCount = windowZones.Values.Count(z => z == ZoneType.Near);
                int midCount = windowZones.Values.Count(z => z == ZoneType.Mid);
                int farCount = windowZones.Values.Count(z => z == ZoneType.Far);

                Logging.Log($"[{Name}] Window: Near={nearCount}, Mid={midCount}, Far={farCount}, Created={created}, Destroyed={destroyed}, Updated={updated}");
            }
        }

        private void UpdateZoneTags(CommandBuffer buffer, Entity chunkEntity, ChunkLocation location, ZoneType oldZone, ZoneType newZone)
        {
            // Remove old tag
            switch (oldZone)
            {
                case ZoneType.Near:
                    buffer.RemoveComponent(chunkEntity.Index, ComponentManager.GetTypeId<NearZoneTag>());
                    break;
                case ZoneType.Mid:
                    buffer.RemoveComponent(chunkEntity.Index, ComponentManager.GetTypeId<MidZoneTag>());
                    break;
                case ZoneType.Far:
                    buffer.RemoveComponent(chunkEntity.Index, ComponentManager.GetTypeId<FarZoneTag>());
                    break;
            }

            // Add new tag
            switch (newZone)
            {
                case ZoneType.Near:
                    buffer.AddComponent(chunkEntity.Index, ComponentManager.GetTypeId<NearZoneTag>(), new NearZoneTag());
                    break;
                case ZoneType.Mid:
                    buffer.AddComponent(chunkEntity.Index, ComponentManager.GetTypeId<MidZoneTag>(), new MidZoneTag());
                    break;
                case ZoneType.Far:
                    buffer.AddComponent(chunkEntity.Index, ComponentManager.GetTypeId<FarZoneTag>(), new FarZoneTag());
                    break;
            }
        }

        public override void OnShutdown(World world)
        {
            _previousZones.Clear();
            _buffer.Dispose();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
        }
    }
}
