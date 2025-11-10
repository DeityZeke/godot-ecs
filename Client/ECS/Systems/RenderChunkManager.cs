#nullable enable

using System;
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
    /// and assigns Near/Mid/Far zones. Combines previous Zone + Window logic."
    ///
    /// This system:
    /// 1. Determines player chunk location
    /// 2. Builds window of active chunks around player
    /// 3. Tags each chunk with ChunkZone (Near/Mid/Far) based on distance
    /// 4. Updates only when player crosses chunk boundary or every 2s
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

        // Read-only: We read chunk locations and camera position
        public override Type[] ReadSet { get; } = new[] { typeof(ChunkLocation), typeof(ChunkBounds) };
        // Write: We update RenderChunk components
        public override Type[] WriteSet { get; } = new[] { typeof(RenderChunk) };

        private ChunkManager? _chunkManager;
        private ChunkLocation _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private double _timeSinceLastUpdate = 0;
        private int _frameCounter = 0;

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Near: {SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}x{SystemSettings.NearBubbleSize.Value}, Mid: {SystemSettings.MidRenderDistance.Value} chunks");
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

            // Rebuild window and assign zones
            UpdateChunkWindowAndZones(world, playerChunk);

            _timeSinceLastUpdate = 0;
        }

        /// <summary>
        /// Core logic: Build window of active chunks and tag each with Near/Mid/Far zone.
        /// This is the ONLY place where ChunkZone is assigned.
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

            int chunksTagged = 0;
            int nearCount = 0;
            int midCount = 0;
            int farCount = 0;

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
                        {
                            // Chunk doesn't exist yet - skip (ChunkSystem will create it)
                            continue;
                        }

                        // Calculate XZ distance from player chunk (Y doesn't affect zone)
                        float distXZ = MathF.Sqrt(dx * dx + dz * dz);

                        // Assign zone based on distance
                        ChunkZone zone;
                        if (distXZ <= nearRadius)
                        {
                            zone = ChunkZone.Near;
                            nearCount++;
                        }
                        else if (distXZ <= midRadius)
                        {
                            zone = ChunkZone.Mid;
                            midCount++;
                        }
                        else if (distXZ <= farRadius)
                        {
                            zone = ChunkZone.Far;
                            farCount++;
                        }
                        else
                        {
                            zone = ChunkZone.Culled;
                        }

                        // Get chunk bounds
                        var bounds = _chunkManager.ChunkToWorldBounds(chunkLoc);

                        // Create or update RenderChunk component
                        if (world.TryGetEntityLocation(chunkEntity, out var archetype, out var slot))
                        {
                            var renderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();

                            var renderChunk = new RenderChunk(chunkLoc, bounds, zone, visible: true);

                            if (archetype.HasComponent(renderChunkTypeId))
                            {
                                // Update existing
                                archetype.SetComponentValue(renderChunkTypeId, slot, renderChunk);
                            }
                            else
                            {
                                // Add new (shouldn't happen often)
                                world.EnqueueComponentAdd(chunkEntity, renderChunk);
                            }

                            chunksTagged++;
                        }
                    }
                }
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Tagged {chunksTagged} chunks: Near={nearCount}, Mid={midCount}, Far={farCount}");
            }
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
