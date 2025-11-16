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
    /// 5. Pools chunks that exit the window (single-pass algorithm)
    ///
    /// SINGLE-PASS ALGORITHM:
    /// - Query all existing RenderChunk entities
    /// - Calculate new window bounds
    /// - For existing chunks:
    ///   - Inside window → Update zone tags
    ///   - Outside window → Remove zone tags + pool entity
    /// - Create missing chunks (window - existing) from pool or new
    ///
    /// ECS PATTERN: Uses tag components for zone assignment.
    /// - Near chunks: Have NearZoneTag → Query<NearZoneTag>() finds them
    /// - Mid chunks: Have MidZoneTag → Query<MidZoneTag>() finds them
    /// - Far chunks: Have FarZoneTag → Query<FarZoneTag>() finds them
    /// - Pooled chunks: Have RenderChunkPoolTag → Query<RenderChunkPoolTag>() finds them for reuse
    ///
    /// Benefits:
    /// - Zone systems query DIFFERENT archetypes (no read conflicts → parallelizable!)
    /// - Automatic filtering by ECS (no manual enum checks)
    /// - Complete render coverage (no gaps in frustum)
    /// - Predictable memory usage (fixed window size)
    /// - Entity pooling prevents microstutters (no allocations on window slide)
    /// </summary>
    public sealed class RenderChunkManager : BaseSystem
    {
        #region Settings

        public sealed class Settings : SystemSettings
        {
            public IntSetting NearBubbleSize { get; private set; }
            public IntSetting MidRenderDistance { get; private set; }
            public IntSetting FarRenderDistance { get; private set; }
            public BoolSetting EnableFarZone { get; private set; }
            public FloatSetting RecenterDelaySeconds { get; private set; }
            public IntSetting PoolCapacity { get; private set; }
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

                PoolCapacity = RegisterInt("Pool Capacity", 512,
                    min: 64, max: 4096, step: 64,
                    tooltip: "Maximum pooled render chunk entities (prevents allocation overhead)");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log zone assignments and window updates");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Render Chunk Manager";
        public override int SystemId => typeof(RenderChunkManager).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: RenderChunk for querying existing chunks, RenderChunkPoolTag for pool queries
        public override Type[] ReadSet { get; } = new[] { typeof(RenderChunk), typeof(RenderChunkPoolTag) };
        // Write: Zone tags + RenderChunkPoolTag + create/destroy entities
        // NOTE: RenderChunk entities do NOT have ChunkLocation component to avoid conflicts with ChunkSystem's spatial chunks
        public override Type[] WriteSet { get; } = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag), typeof(RenderChunkPoolTag), typeof(RenderChunk) };

        private ChunkManager? _chunkManager;
        private ChunkLocation _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private double _timeSinceLastUpdate = 0;
        private int _frameCounter = 0;
        private bool _legacyCommandBufferWarningLogged = false;

        // Track previous zone assignments to know which tags to remove
        private enum ZoneType : byte { None, Near, Mid, Far }
        private readonly Dictionary<ChunkLocation, ZoneType> _previousZones = new();

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Near: {SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}, Mid: {SystemSettings.MidRenderDistance.Value} chunks");
            Logging.Log($"[{Name}] Proactive render chunk creation enabled with tag-based pooling (max capacity: {SystemSettings.PoolCapacity.Value})");
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

            if (!_legacyCommandBufferWarningLogged)
            {
                Logging.Log($"[{Name}] Legacy CommandBuffer path disabled pending deferred queue rewrite.");
                _legacyCommandBufferWarningLogged = true;
            }

            // TODO: Reimplement proactive render window updates using the deferred queue pipeline.
            return;

#if false
            // Calculate all chunk locations in window
            var windowLocations = new HashSet<ChunkLocation>();
            var windowZones = new Dictionary<ChunkLocation, ZoneType>();

            for (int dz = -farRadius; dz <= farRadius; dz++)
            {
                for (int dx = -farRadius; dx <= farRadius; dx++)
                {
                    for (int dy = -yRadius; dy <= yRadius; dy++)
                    {
                        // Relative render offset (camera-relative coordinates)
                        var renderOffset = new ChunkLocation(dx, dz, dy);

                        // Absolute server chunk location (world coordinates)
                        var serverChunkLoc = new ChunkLocation(
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
                            // Store server chunk location for existing chunk lookups
                            windowLocations.Add(serverChunkLoc);
                            windowZones[serverChunkLoc] = zone;
                        }
                    }
                }
            }

            // Query ACTIVE render chunks (have zone tags, not pooled)
            var existingChunks = new Dictionary<ChunkLocation, Entity>();
            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();

            var zoneTagTypes = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };
            foreach (var zoneTagType in zoneTagTypes)
            {
                using var archetypes = world.QueryArchetypes(zoneTagType);
                foreach (var archetype in archetypes)
                {
                    if (archetype.Count == 0)
                        continue;

                    var renderChunks = archetype.GetComponentSpan<RenderChunk>(renderChunkTypeId);
                    var entities = archetype.GetEntitySpan();

                    for (int i = 0; i < renderChunks.Length; i++)
                    {
                        // Key by ServerChunkLocation (absolute world position) for window comparison
                        existingChunks[renderChunks[i].ServerChunkLocation] = entities[i];
                    }
                }
            }

            // Query POOLED render chunks (for reuse)
            var pooledEntities = new List<Entity>();
            using var pooledArchetypes = world.QueryArchetypes(typeof(RenderChunkPoolTag));
            foreach (var archetype in pooledArchetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var entities = archetype.GetEntitySpan();
                for (int i = 0; i < entities.Length; i++)
                    pooledEntities.Add(entities[i]);
            }

            // Process existing chunks: Update or Pool
            int pooled = 0;
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
                    // Outside window - remove zone tags and pool (if capacity allows)
                    var previousZone = _previousZones.TryGetValue(location, out var prev) ? prev : ZoneType.None;
                    RemoveAllZoneTags(_buffer, entity, previousZone);

                    // Check current pool size + entities we're adding this frame
                    if (pooledEntities.Count + pooled < SystemSettings.PoolCapacity.Value)
                    {
                        // Add to pool via RenderChunkPoolTag
                        _buffer.AddComponent(entity, ComponentManager.GetTypeId<RenderChunkPoolTag>(), new RenderChunkPoolTag());
                        pooled++;
                    }
                    else
                    {
                        // Pool full - destroy
                        _buffer.DestroyEntity(entity);
                    }
                }
            }

            // Create missing chunks (from pool or new)
            int created = 0;
            int reused = 0;
            int poolIndex = 0;

            foreach (var serverChunkLoc in windowLocations)
            {
                if (!existingChunks.ContainsKey(serverChunkLoc))
                {
                    // Calculate relative render offset (camera-relative coordinates)
                    var renderOffset = new ChunkLocation(
                        serverChunkLoc.X - playerChunk.X,
                        serverChunkLoc.Z - playerChunk.Z,
                        serverChunkLoc.Y - playerChunk.Y);

                    var bounds = _chunkManager.ChunkToWorldBounds(serverChunkLoc);
                    var zone = windowZones[serverChunkLoc];

                    // Try to reuse pooled entity first
                    if (poolIndex < pooledEntities.Count)
                    {
                        var entity = pooledEntities[poolIndex++];

                        // Remove pool tag
                        _buffer.RemoveComponent(entity, ComponentManager.GetTypeId<RenderChunkPoolTag>());

                        // Update RenderChunk component with new location/bounds
                        // NOTE: We don't add ChunkLocation as separate component to avoid conflicts with ChunkSystem
                        _buffer.RemoveComponent(entity, renderChunkTypeId);
                        _buffer.AddComponent(entity, renderChunkTypeId, new RenderChunk(renderOffset, serverChunkLoc, bounds, visible: false));

                        // Add zone tag to activate
                        AddZoneTag(_buffer, entity, zone);

                        reused++;
                    }
                    else
                    {
                        // Pool empty - create new entity
                        // NOTE: RenderChunk contains location, so we don't add ChunkLocation as separate component
                        // This prevents conflicts with ChunkSystem's spatial chunks
                        _buffer.CreateEntity(builder =>
                        {
                            builder.Add(new RenderChunk(renderOffset, serverChunkLoc, bounds, visible: false));

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
                int poolSizeAfter = pooledEntities.Count - reused + pooled;

                Logging.Log($"[{Name}] Window: Near={nearCount}, Mid={midCount}, Far={farCount}, Created={created}, Reused={reused}, Pooled={pooled}, Updated={updated}, PoolSize={poolSizeAfter}");
            }
#endif
        }

#if false
        private void UpdateZoneTags(CommandBuffer buffer, Entity chunkEntity, ChunkLocation location, ZoneType oldZone, ZoneType newZone)
        {
            // Remove old tag
            RemoveZoneTag(buffer, chunkEntity, oldZone);

            // Add new tag
            AddZoneTag(buffer, chunkEntity, newZone);
        }

        private void RemoveAllZoneTags(CommandBuffer buffer, Entity chunkEntity, ZoneType previousZone)
        {
            // Remove previous zone tag (if any) to make chunk invisible to zone systems
            RemoveZoneTag(buffer, chunkEntity, previousZone);
        }

        private void RemoveZoneTag(CommandBuffer buffer, Entity chunkEntity, ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Near:
                    buffer.RemoveComponent(chunkEntity, ComponentManager.GetTypeId<NearZoneTag>());
                    break;
                case ZoneType.Mid:
                    buffer.RemoveComponent(chunkEntity, ComponentManager.GetTypeId<MidZoneTag>());
                    break;
                case ZoneType.Far:
                    buffer.RemoveComponent(chunkEntity, ComponentManager.GetTypeId<FarZoneTag>());
                    break;
            }
        }

        private void AddZoneTag(CommandBuffer buffer, Entity chunkEntity, ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Near:
                    buffer.AddComponent(chunkEntity, ComponentManager.GetTypeId<NearZoneTag>(), new NearZoneTag());
                    break;
                case ZoneType.Mid:
                    buffer.AddComponent(chunkEntity, ComponentManager.GetTypeId<MidZoneTag>(), new MidZoneTag());
                    break;
                case ZoneType.Far:
                    buffer.AddComponent(chunkEntity, ComponentManager.GetTypeId<FarZoneTag>(), new FarZoneTag());
                    break;
            }
        }
#endif

        public override void OnShutdown(World world)
        {
            _previousZones.Clear();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down (legacy render chunk pipeline disabled)");
            }
        }
    }
}
