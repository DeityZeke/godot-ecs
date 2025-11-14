#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using UltraSim;
using UltraSim.ECS;
using Server.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using Godot;

namespace UltraSim.Server.ECS.Systems
{
    /// <summary>
    /// Simplified ChunkSystem - pure entity tracking.
    /// NO component manipulation, NO archetype moves.
    /// Just tracks which entities are in which chunks.
    /// </summary>
    public sealed class SimplifiedChunkSystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log chunk tracking operations");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "SimplifiedChunkSystem";
        public override int SystemId => typeof(SimplifiedChunkSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame; // Process queues every frame

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(ChunkOwner) };
        public override Type[] WriteSet { get; } = new[] { typeof(ChunkOwner) };

        private SimplifiedChunkManager? _chunkManager;
        private World? _world;

        // Processing queues (thread-safe)
        private readonly ConcurrentQueue<Entity> _entityCreatedQueue = new();
        private readonly ConcurrentQueue<Entity> _entityMovedQueue = new();

        private static readonly int PosTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();

        public override void OnInitialize(World world)
        {
            _world = world;
            _chunkManager = new SimplifiedChunkManager(chunkSizeXZ: 64, chunkSizeY: 32);

            // Subscribe to World events
            UltraSim.EventSink.EntityBatchCreated += OnEntityBatchCreated;
            UltraSim.EventSink.EntityBatchDestroyRequest += OnEntityBatchDestroyRequest;

            // Subscribe to Server events (movement systems)
            UltraSim.Server.EventSink.EnqueueChunkUpdate += OnChunkUpdateRequested;

            Logging.Log($"[{Name}] Initialized with SimplifiedChunkManager (64x32x64)");
        }

        public SimplifiedChunkManager? GetChunkManager() => _chunkManager;

        /// <summary>
        /// Event handler: Just enqueue entities for processing.
        /// NO component reads/writes here - world may not be stable yet!
        /// </summary>
        private void OnEntityBatchCreated(EntityBatchCreatedEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            var entitySpan = args.GetSpan();
            for (int i = 0; i < entitySpan.Length; i++)
            {
                _entityCreatedQueue.Enqueue(entitySpan[i]);
            }
        }

        /// <summary>
        /// Event handler: Process entity destruction IMMEDIATELY.
        /// Uses hybrid approach: Fast path (component lookup) + cleanup pass (span matching).
        /// Entities are still alive, components accessible.
        /// No need to queue - cleanup happens synchronously during queue processing.
        /// </summary>
        private void OnEntityBatchDestroyRequest(EntityBatchDestroyRequestEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            var entitySpan = args.GetSpan();
            int beforeCount = _chunkManager.GetTotalTrackedEntities();

            var stopwatch = Stopwatch.StartNew();

            // PHASE 1: Fast path - use ChunkOwner component to find location
            int fastPathRemoved = 0;
            for (int i = 0; i < entitySpan.Length; i++)
            {
                var entity = entitySpan[i];

                // Entities are still alive - components guaranteed accessible!
                if (_world.TryGetEntityLocation(entity, out var archetype, out var slot) &&
                    archetype.HasComponent(ChunkOwnerTypeId))
                {
                    var owners = archetype.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                    var owner = owners[slot];
                    _chunkManager.StopTracking(entity.Packed, owner.Location);
                    fastPathRemoved++;
                }
            }

            int afterFastPath = _chunkManager.GetTotalTrackedEntities();

            // PHASE 2: Cleanup pass - handle entities that moved but component not yet updated
            int cleanupRemoved = 0;
            if (afterFastPath > 0)
            {
                // Component was stale (entity moved, but update still in deferred queue)
                cleanupRemoved = _chunkManager.RemoveDeadEntities(entitySpan);
            }

            stopwatch.Stop();

            int afterCleanup = _chunkManager.GetTotalTrackedEntities();
            int totalRemoved = fastPathRemoved + cleanupRemoved;

            if (totalRemoved > 0 && SystemSettings.EnableDebugLogs.Value)
            {
                double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                Logging.Log($"[{Name}] BEFORE destroy: {beforeCount} entities tracked");
                Logging.Log($"[{Name}] AFTER fast path: {afterFastPath} remaining (removed {fastPathRemoved})");
                if (cleanupRemoved > 0)
                {
                    Logging.Log($"[{Name}] AFTER cleanup pass: {afterCleanup} remaining (removed {cleanupRemoved})");
                }
                Logging.Log($"[{Name}] Removed {totalRemoved} entities from chunk tracking (fast: {fastPathRemoved}, cleanup: {cleanupRemoved}) in {elapsedMs:F4}ms");
            }
        }

        /// <summary>
        /// Event handler: Entities that crossed chunk boundaries (fired by movement systems).
        /// Just enqueue them - actual chunk reassignment happens in Update().
        /// </summary>
        private void OnChunkUpdateRequested(Server.ChunkUpdateEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            var entitySpan = args.GetSpan();

            if (SystemSettings.EnableDebugLogs.Value && entitySpan.Length > 0)
            {
                Logging.Log($"[{Name}] Received chunk update event with {entitySpan.Length} entities");
            }

            for (int i = 0; i < entitySpan.Length; i++)
            {
                _entityMovedQueue.Enqueue(entitySpan[i]);
            }
        }

        public override void Update(World world, double delta)
        {
            if (_chunkManager == null)
                return;

            Logging.Log($"Queues: Create: {_entityCreatedQueue.Count} | Move: {_entityMovedQueue.Count}");

            // Process queues in order
            ProcessCreatedEntities();
            ProcessMovedEntities();
            // NOTE: Destroyed entities are handled IMMEDIATELY via EntityBatchDestroyRequest event - no queue!

            // Periodic statistics
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log(_chunkManager.GetStatistics());
            }
        }

        /// <summary>
        /// Process newly created entities - add them to chunk tracking.
        /// </summary>
        private void ProcessCreatedEntities()
        {
            int processed = 0;

            while (_entityCreatedQueue.TryDequeue(out var entity))
            {
                // SAFETY: Entity might have been destroyed before we process it
                if (!_world!.IsEntityValid(entity))
                    continue;

                // Entity should already have ChunkOwner (added during creation)
                if (!_world!.TryGetEntityLocation(entity, out var archetype, out var slot))
                    continue;

                if (!archetype.HasComponent(ChunkOwnerTypeId))
                    continue;

                var owners = archetype.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                var owner = owners[slot];

                // Track entity in chunk
                _chunkManager!.TrackEntity(entity.Packed, owner.Location);
                processed++;
            }

            if (processed > 0 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Tracked {processed} new entities in chunks");
            }
        }

        /// <summary>
        /// Process entities that moved between chunks.
        /// </summary>
        private void ProcessMovedEntities()
        {
            int processed = 0;

            while (_entityMovedQueue.TryDequeue(out var entity))
            {
                if (!_world!.IsEntityValid(entity))
                    continue;

                if (!_world.TryGetEntityLocation(entity, out var archetype, out var slot))
                    continue;

                if (!archetype.HasComponent(ChunkOwnerTypeId) || !archetype.HasComponent(PosTypeId))
                    continue;

                var owners = archetype.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                var oldOwner = owners[slot];

                var positions = archetype.GetComponentSpan<Position>(PosTypeId);
                var position = positions[slot];

                // Calculate new chunk location
                var newChunkLoc = _chunkManager!.WorldToChunk(position.X, position.Y, position.Z);

                // Still in same chunk?
                if (oldOwner.Location.Equals(newChunkLoc))
                    continue;

                // Move tracking
                _chunkManager.MoveEntity(entity.Packed, oldOwner.Location, newChunkLoc);

                // Update ChunkOwner component VALUE using deferred queue (thread-safe!)
                _world!.EnqueueComponentAdd(entity, ChunkOwnerTypeId, new ChunkOwner(Entity.Invalid, newChunkLoc));

                processed++;
            }

            if (processed > 0 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Moved {processed} entities between chunks");
            }
        }

    }
}
