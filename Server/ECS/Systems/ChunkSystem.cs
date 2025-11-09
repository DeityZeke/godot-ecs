#nullable enable

using System;
using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;

namespace UltraSim.Server.ECS.Systems
{
    /// <summary>
    /// Manages chunk lifecycle and entity-to-chunk assignment.
    /// Creates chunk entities, assigns regular entities to chunks based on Position.
    /// Works WITH ChunkManager (lookup service) to maintain spatial index.
    /// </summary>
    public sealed class ChunkSystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public BoolSetting EnableAutoAssignment { get; private set; }
            public IntSetting AssignmentFrequency { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                EnableAutoAssignment = RegisterBool("Auto-Assign Entities", true,
                    tooltip: "Automatically assign entities to chunks based on Position");

                AssignmentFrequency = RegisterInt("Assignment Frequency", 60,
                    min: 1, max: 600, step: 1,
                    tooltip: "Update entity assignments every N frames (60 = ~1 per second at 60fps)");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log chunk creation/destruction and assignment operations");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "ChunkSystem";
        public override int SystemId => typeof(ChunkSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(ChunkOwner), typeof(ChunkLocation) };
        public override Type[] WriteSet { get; } = new[] { typeof(ChunkOwner), typeof(ChunkState) };

        private ChunkManager? _chunkManager;
        private CommandBuffer _buffer = new();
        private int _frameCounter = 0;

        // Track chunks that are pending creation (to avoid duplicate creation requests)
        private readonly HashSet<ChunkLocation> _pendingChunkCreations = new();

        private static readonly int PosTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int ChunkLocationTypeId = ComponentManager.GetTypeId<ChunkLocation>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();

        public override void OnInitialize(World world)
        {
            _cachedQuery = world.QueryArchetypes(typeof(Position));
            _chunkManager = new ChunkManager(chunkSizeXZ: 64, chunkSizeY: 32);

            Logging.Log($"[ChunkSystem] Initialized with ChunkManager (64x32x64)");
        }

        public ChunkManager? GetChunkManager() => _chunkManager;

        public override void Update(World world, double delta)
        {
            if (_chunkManager == null)
                return;

            _frameCounter++;
            _chunkManager.IncrementFrame();

            // === REGISTER NEW CHUNKS ===
            // Scan for chunk entities that aren't registered yet
            RegisterNewChunks(world);

            // === AUTO-ASSIGNMENT ===
            if (SystemSettings.EnableAutoAssignment.Value)
            {
                int frequency = SystemSettings.AssignmentFrequency.Value;

                if (_frameCounter % frequency == 0)
                {
                    int reassignedCount = AssignEntitiesToChunks(world);

                    if (SystemSettings.EnableDebugLogs.Value && reassignedCount > 0)
                    {
                        Logging.Log($"[ChunkSystem] Reassigned {reassignedCount} entities to chunks");
                    }
                }
            }

            // Apply all component changes
            _buffer.Apply(world);

            // === STATISTICS ===
            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 300 == 0)
            {
                Logging.Log(_chunkManager.GetStatistics());
            }
        }

        /// <summary>
        /// Scan for chunk entities (have ChunkLocation) and register them with ChunkManager.
        /// </summary>
        private void RegisterNewChunks(World world)
        {
            if (_chunkManager == null)
                return;

            var archetypes = world.QueryArchetypes(typeof(ChunkLocation));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                var locations = arch.GetComponentSpan<ChunkLocation>(ChunkLocationTypeId);
                var entities = arch.GetEntityArray();

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var location = locations[i];

                    // Check if already registered
                    if (!_chunkManager.TryGetChunkLocation(entity, out _))
                    {
                        _chunkManager.RegisterChunk(entity, location);

                        // Remove from pending set now that chunk is created
                        _pendingChunkCreations.Remove(location);

                        if (SystemSettings.EnableDebugLogs.Value)
                        {
                            Logging.Log($"[ChunkSystem] Registered new chunk at {location}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Assign entities with Position components to their appropriate chunks.
        /// Creates chunks on-demand if they don't exist.
        /// </summary>
        private int AssignEntitiesToChunks(World world)
        {
            if (_chunkManager == null)
                return 0;

            int assignedCount = 0;
            var archetypes = world.QueryArchetypes(typeof(Position));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                // Skip chunk entities themselves (they have ChunkLocation)
                if (arch.HasComponent(ChunkLocationTypeId))
                    continue;

                var positions = arch.GetComponentSpan<Position>(PosTypeId);
                var entities = arch.GetEntityArray();

                // Check if this archetype has ChunkOwner component
                bool hasChunkOwner = arch.HasComponent(ChunkOwnerTypeId);
                Span<ChunkOwner> chunkOwners = hasChunkOwner ? arch.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId) : Span<ChunkOwner>.Empty;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var pos = positions[i];

                    // Calculate chunk location for this position
                    var chunkLoc = _chunkManager.WorldToChunk(pos.X, pos.Y, pos.Z);

                    // If entity already has ChunkOwner and is in the correct chunk, skip
                    if (hasChunkOwner && chunkOwners[i].IsAssigned && chunkOwners[i].Location.Equals(chunkLoc))
                        continue;

                    // Get or create chunk
                    var chunkEntity = GetOrCreateChunk(world, chunkLoc);
                    if (chunkEntity == Entity.Invalid)
                        continue;

                    var owner = new ChunkOwner(chunkEntity, chunkLoc);

                    if (hasChunkOwner)
                    {
                        ref var existingOwner = ref chunkOwners[i];

                        if (!existingOwner.IsAssigned ||
                            existingOwner.ChunkEntity != chunkEntity ||
                            !existingOwner.Location.Equals(chunkLoc))
                        {
                            existingOwner = owner;
                            assignedCount++;
                        }
                    }
                    else
                    {
                        _buffer.AddComponent(entity.Index, ChunkOwnerTypeId, owner);
                        assignedCount++;
                    }
                }
            }

            return assignedCount;
        }

        /// <summary>
        /// Get or create a chunk entity at the specified location.
        /// </summary>
        private Entity GetOrCreateChunk(World world, ChunkLocation location)
        {
            if (_chunkManager == null)
                return Entity.Invalid;

            // Check if chunk already exists
            var existingChunk = _chunkManager.GetChunk(location);
            if (existingChunk != Entity.Invalid)
                return existingChunk;

            // Check if chunk creation is already pending (FIX FOR INFINITE LOOP BUG)
            if (_pendingChunkCreations.Contains(location))
                return Entity.Invalid; // Already queued, skip

            // Mark as pending to prevent duplicate creation requests
            _pendingChunkCreations.Add(location);

            // Create new chunk entity
            var bounds = _chunkManager.ChunkToWorldBounds(location);
            var state = new ChunkState(ChunkLifecycleState.Active)
            {
                IsGenerated = true
            };

            _buffer.CreateEntity(builder =>
            {
                builder.Add(location);
                builder.Add(bounds);
                builder.Add(state);
                builder.Add(new ChunkHash(0, 0)); // Will be computed when terrain/statics are added
            });

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[ChunkSystem] Queued chunk creation at {location}");
            }

            return Entity.Invalid; // Will be available next frame
        }

        public override void OnShutdown(World world)
        {
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[ChunkSystem] Shutting down - Final stats:\n{_chunkManager?.GetStatistics()}");
            }

            _buffer.Dispose();
        }
    }
}
