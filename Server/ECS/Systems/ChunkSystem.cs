#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using UltraSim.ECS.Events;

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
            public BoolSetting UseDirtyAssignmentQueue { get; private set; }
            public BoolSetting EnableParallelAssignments { get; private set; }
            public IntSetting ParallelThreshold { get; private set; }
            public IntSetting ParallelBatchSize { get; private set; }
            public BoolSetting EnableChunkPreallocation { get; private set; }
            public IntSetting PreallocateRadiusXZ { get; private set; }
            public IntSetting PreallocateHeight { get; private set; }
            public IntSetting PreallocationBatchSize { get; private set; }
            public BoolSetting EnableChunkPooling { get; private set; }
            public IntSetting MaxChunkCount { get; private set; }
            public IntSetting ChunkIdleFrames { get; private set; }
            public IntSetting PoolCleanupBatch { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }
            public BoolSetting EnableDeferredBatchProcessing { get; private set; }
            public BoolSetting ParallelBatchProcessing { get; private set; }
            public IntSetting ParallelBatchThreshold { get; private set; }

            public Settings()
            {
                UseDirtyAssignmentQueue = RegisterBool("Use Dirty Assignment Queue", true,
                    tooltip: "Process chunk assignments from the dirty queue.");

                EnableParallelAssignments = RegisterBool("Parallel Dirty Queue", true,
                    tooltip: "Process dirty chunk assignments in parallel when the queue is large.");

                ParallelThreshold = RegisterInt("Parallel Threshold", 1_000,
                    min: 64, max: 100_000, step: 64,
                    tooltip: "Minimum dirty assignments required before parallel processing kicks in.");

                ParallelBatchSize = RegisterInt("Parallel Batch Size", 512,
                    min: 64, max: 10_000, step: 64,
                    tooltip: "Approximate number of assignments processed per worker batch.");

                EnableChunkPreallocation = RegisterBool("Enable Chunk Preallocation", false,
                    tooltip: "Pre-create chunk entities for a configurable grid to avoid runtime spikes.");

                PreallocateRadiusXZ = RegisterInt("Preallocate Radius (XZ chunks)", 4,
                    min: 1, max: 32, step: 1,
                    tooltip: "Half-width of the chunk grid to pre-create in the X/Z plane.");

                PreallocateHeight = RegisterInt("Preallocate Height (chunks)", 0,
                    min: 0, max: 16, step: 1,
                    tooltip: "Vertical chunk layers above/below origin to pre-create.");

                PreallocationBatchSize = RegisterInt("Preallocation Batch Size", 32,
                    min: 1, max: 4096, step: 1,
                    tooltip: "Maximum number of chunks to preallocate per frame.");

                EnableChunkPooling = RegisterBool("Enable Chunk Pooling", false,
                    tooltip: "Recycle inactive chunk entities instead of destroying them.");

                MaxChunkCount = RegisterInt("Max Active Chunks", 6000,
                    min: 0, max: 50000, step: 100,
                    tooltip: "Optional upper bound on simultaneously active chunks (0 = unlimited).");

                ChunkIdleFrames = RegisterInt("Chunk Idle Frames", 600,
                    min: 0, max: 10000, step: 10,
                    tooltip: "Chunks idle for this many frames become eviction candidates (0 = disabled).");

                PoolCleanupBatch = RegisterInt("Pool Cleanup Batch", 32,
                    min: 1, max: 1024, step: 1,
                    tooltip: "Maximum number of idle chunks to recycle per frame.");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log chunk creation/destruction and assignment operations");

                EnableDeferredBatchProcessing = RegisterBool("Deferred Batch Processing", true,
                    tooltip: "Defer entity CREATION batches to Update() for parallel processing. Movement uses immediate smart filtering.");

                ParallelBatchProcessing = RegisterBool("Parallel Batch Processing", true,
                    tooltip: "Process multiple event batches in parallel when deferred processing is enabled");

                ParallelBatchThreshold = RegisterInt("Parallel Batch Threshold", 2,
                    min: 1, max: 100, step: 1,
                    tooltip: "Minimum number of batches required before parallel processing kicks in");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "ChunkSystem";
        public override int SystemId => typeof(ChunkSystem).GetHashCode();
        public override TickRate Rate => TickRate.Tick100ms; // 10Hz - spatial indexing doesn't need 60fps

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(ChunkOwner), typeof(ChunkLocation) };
        public override Type[] WriteSet { get; } = new[] { typeof(ChunkOwner), typeof(ChunkState) };

        private ChunkManager? _chunkManager;
        private World? _world;
        private int _frameCounter = 0;
        private int _chunksQueuedThisFrame = 0;
        private int _chunksRegisteredThisFrame = 0;

        // Deferred batch processing queues
        private readonly ConcurrentQueue<EntityBatchProcessedEventArgs> _movementBatchQueue = new();
        private readonly ConcurrentQueue<EntityBatchCreatedEventArgs> _creationBatchQueue = new();

        // Track chunks that are pending creation (to avoid duplicate creation requests)
        // Using ConcurrentDictionary for thread-safe access (value is unused, using as ConcurrentHashSet)
        private readonly ConcurrentDictionary<ChunkLocation, byte> _pendingChunkCreations = new();
        private readonly Dictionary<ChunkLocation, Entity> _chunkEntityCache = new();
        private readonly List<ChunkAssignmentRequest> _assignmentBatch = new(capacity: 2048);
        private readonly List<ChunkLocation> _preallocationTargets = new();
        private int _preallocationCursor = 0;
        private bool _preallocationInitialized;
        private bool _preallocationLogged;
        private readonly Queue<Entity> _chunkPool = new();
        private readonly ChunkEntityTracker _chunkEntityTracker = new();
        private Entity[] _ownerChunkEntities = new Entity[1024];
        private ChunkLocation[] _ownerLocations = new ChunkLocation[1024];
        private bool[] _ownerAssigned = new bool[1024];

        // NEW: Track which entities belong to each chunk for efficient render queries

        private static readonly int PosTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int ChunkLocationTypeId = ComponentManager.GetTypeId<ChunkLocation>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();
        private static readonly int ChunkBoundsTypeId = ComponentManager.GetTypeId<ChunkBounds>();
        private static readonly int ChunkStateTypeId = ComponentManager.GetTypeId<ChunkState>();
        private static readonly int ChunkHashTypeId = ComponentManager.GetTypeId<ChunkHash>();
        private static readonly int UnregisteredChunkTagTypeId = ComponentManager.GetTypeId<UnregisteredChunkTag>();
        private static readonly int DirtyChunkTagTypeId = ComponentManager.GetTypeId<DirtyChunkTag>();

        public override void OnInitialize(World world)
        {
            _world = world;
            _cachedQuery = world.QueryArchetypes(typeof(Position));
            _chunkManager = new ChunkManager(chunkSizeXZ: 64, chunkSizeY: 32);

            // Subscribe to World's entity batch created event for initial chunk assignment
            UltraSim.EventSink.EntityBatchCreated += OnEntityBatchCreated;
            Logging.Log($"[ChunkSystem] Subscribed to World entity creation events");

            // Subscribe to World's entity batch destroyed event for chunk UN-assignment
            UltraSim.EventSink.EntityBatchDestroyed += OnEntityBatchDestroyed;
            Logging.Log($"[ChunkSystem] Subscribed to World entity destruction events");

            Logging.Log($"[ChunkSystem] Initialized with ChunkManager (64x32x64)");
        }

        public ChunkManager? GetChunkManager() => _chunkManager;

        /// <summary>
        /// Event handler for World's EntityBatchCreated event.
        /// Either enqueues batch for deferred processing or processes immediately.
        /// </summary>
        private void OnEntityBatchCreated(EntityBatchCreatedEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            // Deferred processing: O(1) - just enqueue the batch reference
            if (SystemSettings.EnableDeferredBatchProcessing.Value)
            {
                _creationBatchQueue.Enqueue(args);
                return;
            }

            // Synchronous processing (old behavior)
            ProcessCreationBatchImmediate(args);
        }

        /// <summary>
        /// Event handler for World's EntityBatchDestroyed event.
        /// Enqueues destroyed entities for chunk UN-assignment.
        /// </summary>
        private void OnEntityBatchDestroyed(EntityBatchDestroyedEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            var entitySpan = args.GetSpan();

            // Enqueue each destroyed entity for UN-assignment
            for (int i = 0; i < entitySpan.Length; i++)
            {
                var entity = entitySpan[i];

                // Check if entity had a chunk assignment
                if (TryGetOwner(entity, out var chunkEntity, out _))
                {
                    // Remove from chunk tracking
                    _chunkEntityTracker.Remove(chunkEntity, entity);

                    // Clear owner data
                    ClearOwner(entity);
                }
            }

            if (SystemSettings.EnableDebugLogs.Value && entitySpan.Length > 0)
            {
                Logging.Log($"[ChunkSystem] UN-assigned {entitySpan.Length} destroyed entities from chunks");
            }
        }

        /// <summary>
        /// Process a creation batch immediately (synchronous).
        /// </summary>
        private void ProcessCreationBatchImmediate(EntityBatchCreatedEventArgs args)
        {
            var entitySpan = args.GetSpan();

            for (int i = 0; i < entitySpan.Length; i++)
            {
                var entity = entitySpan[i];

                // SAFETY: Skip entities that were destroyed before deferred processing
                if (!_world!.IsEntityValid(entity))
                    continue;

                if (!_world!.TryGetEntityLocation(entity, out var archetype, out var slot))
                    continue;

                if (!archetype.HasComponent(PosTypeId) || archetype.HasComponent(ChunkLocationTypeId))
                    continue;

                var positions = archetype.GetComponentSpan<Position>(PosTypeId);
                if ((uint)slot >= (uint)positions.Length)
                    continue;

                var position = positions[slot];
                var chunkLoc = _chunkManager!.WorldToChunk(position.X, position.Y, position.Z);
                ChunkAssignmentQueue.Enqueue(entity, chunkLoc);
            }
        }

        /// <summary>
        /// Process all deferred creation batches from the queue.
        /// Can process in parallel if multiple batches are available.
        /// </summary>
        private void ProcessDeferredCreationBatches()
        {
            if (_chunkManager == null || _world == null)
                return;

            // Drain queue into list
            var batches = new List<EntityBatchCreatedEventArgs>();
            while (_creationBatchQueue.TryDequeue(out var batch))
            {
                batches.Add(batch);
            }

            if (batches.Count == 0)
                return;

            // Decide parallel vs sequential
            bool useParallel = SystemSettings.ParallelBatchProcessing.Value &&
                               batches.Count >= SystemSettings.ParallelBatchThreshold.Value;

            if (useParallel)
            {
                Parallel.ForEach(batches, batch => ProcessCreationBatchImmediate(batch));
            }
            else
            {
                foreach (var batch in batches)
                {
                    ProcessCreationBatchImmediate(batch);
                }
            }

            if (SystemSettings.EnableDebugLogs.Value && batches.Count > 0)
            {
                Logging.Log($"[ChunkSystem] Processed {batches.Count} deferred creation batches ({(useParallel ? "parallel" : "sequential")})");
            }
        }

        /// <summary>
        /// Process all deferred movement batches from the queue.
        /// NOTE: This method is NOT currently used - movement events process immediately with smart filtering.
        /// Kept for potential future use or testing scenarios.
        /// </summary>
        private void ProcessDeferredMovementBatches()
        {
            if (_chunkManager == null || _world == null)
                return;

            // Drain queue into list
            var batches = new List<EntityBatchProcessedEventArgs>();
            while (_movementBatchQueue.TryDequeue(out var batch))
            {
                batches.Add(batch);
            }

            if (batches.Count == 0)
                return;

            // TODO: Implement ProcessMovementBatchSmart method
            // This method was referenced but not implemented
            /*
            // Decide parallel vs sequential
            bool useParallel = SystemSettings.ParallelBatchProcessing.Value &&
                               batches.Count >= SystemSettings.ParallelBatchThreshold.Value;

            if (useParallel)
            {
                Parallel.ForEach(batches, batch => ProcessMovementBatchSmart(batch));
            }
            else
            {
                foreach (var batch in batches)
                {
                    ProcessMovementBatchSmart(batch);
                }
            }

            if (SystemSettings.EnableDebugLogs.Value && batches.Count > 0)
            {
                Logging.Log($"[ChunkSystem] Processed {batches.Count} deferred movement batches ({(useParallel ? "parallel" : "sequential")})");
            }
            */
        }

        /// <summary>
        /// Get all entities currently assigned to a specific chunk.
        /// Returns the set of entities currently assigned to a given chunk.
        /// </summary>
        public ChunkEntityTracker.ChunkEntityEnumerable GetEntitiesInChunk(Entity chunkEntity) =>
            _chunkEntityTracker.GetEntities(chunkEntity);

        /// <summary>
        /// Get entity count in a chunk without allocating a collection.
        /// </summary>
        public int GetEntityCountInChunk(Entity chunkEntity) => _chunkEntityTracker.GetEntityCount(chunkEntity);

        private void EnsureOwnerCapacity(uint entityIndex)
        {
            int idx = (int)entityIndex;
            if (idx < _ownerChunkEntities.Length)
                return;

            int newSize = Math.Max(_ownerChunkEntities.Length * 2, idx + 1);
            Array.Resize(ref _ownerChunkEntities, newSize);
            Array.Resize(ref _ownerLocations, newSize);
            Array.Resize(ref _ownerAssigned, newSize);
        }

        private bool TryGetOwner(Entity entity, out Entity chunkEntity, out ChunkLocation location)
        {
            uint index = entity.Index;
            if (index < _ownerAssigned.Length && _ownerAssigned[index])
            {
                chunkEntity = _ownerChunkEntities[index];
                location = _ownerLocations[index];
                return true;
            }

            chunkEntity = Entity.Invalid;
            location = default;
            return false;
        }

        private void SetOwner(Entity entity, Entity chunkEntity, ChunkLocation location)
        {
            EnsureOwnerCapacity(entity.Index);
            _ownerChunkEntities[entity.Index] = chunkEntity;
            _ownerLocations[entity.Index] = location;
            _ownerAssigned[entity.Index] = true;
        }

        private void ClearOwner(Entity entity)
        {
            uint index = entity.Index;
            if (index < _ownerAssigned.Length && _ownerAssigned[index])
            {
                _ownerChunkEntities[index] = Entity.Invalid;
                _ownerLocations[index] = default;
                _ownerAssigned[index] = false;
            }
        }

        public override void Update(World world, double delta)
        {
            if (_chunkManager == null)
                return;

            _frameCounter++;
            _chunksQueuedThisFrame = 0;
            _chunksRegisteredThisFrame = 0;
            _chunkManager.IncrementFrame();
            ProcessPreallocation(world);

            // === REGISTER NEW CHUNKS ===
            // Scan for chunk entities that aren't registered yet
            RegisterNewChunks(world);

            // === PROCESS DEFERRED CREATION BATCHES ===
            // Only creation events use deferred batching (works great for one-time operations)
            // Movement events process immediately with smart filtering (see OnEntityBatchProcessed)
            if (SystemSettings.EnableDeferredBatchProcessing.Value)
            {
                ProcessDeferredCreationBatches();
            }

            // === PROCESS ASSIGNMENT QUEUE ===
            bool queueProcessed = false;
            if (SystemSettings.UseDirtyAssignmentQueue.Value)
            {
                int processed = ProcessAssignmentQueue(world);
                queueProcessed = processed > 0;
                if (queueProcessed && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Processed {processed:N0} dirty chunk assignments");
                }
            }

            EvictStaleChunks(world);

            // === STATISTICS ===
            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 300 == 0)
            {
                Logging.Log(_chunkManager.GetStatistics());
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                if (_chunksRegisteredThisFrame > 0)
                {
                    Logging.Log($"[ChunkSystem] Registered {_chunksRegisteredThisFrame} new chunks this frame");
                }
                if (_chunksQueuedThisFrame > 0)
                {
                    Logging.Log($"[ChunkSystem] Queued {_chunksQueuedThisFrame} chunks for creation this frame");
                }
            }
        }

        /// <summary>
        /// Scan for NEW chunk entities (have UnregisteredChunkTag) and register them with ChunkManager.
        /// OPTIMIZATION: Only processes chunks with UnregisteredChunkTag instead of ALL chunks.
        /// This is critical for performance - with 500k entities across 8000 chunks, scanning all chunks
        /// every update was taking 5ms. Now we only scan newly created chunks (~0.1ms).
        /// </summary>
        private void RegisterNewChunks(World world)
        {
            if (_chunkManager == null)
                return;

            // OPTIMIZATION: Query only UNREGISTERED chunks (not all chunks)
            // This changes from O(all chunks) to O(new chunks) - massive speedup!
            var archetypes = world.QueryArchetypes(typeof(UnregisteredChunkTag));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                // Verify chunk has required components
                if (!arch.HasComponent(ChunkLocationTypeId))
                    continue;

                var locations = arch.GetComponentSpan<ChunkLocation>(ChunkLocationTypeId);
                var entities = arch.GetEntityArray();
                Span<ChunkState> states = arch.HasComponent(ChunkStateTypeId)
                    ? arch.GetComponentSpan<ChunkState>(ChunkStateTypeId)
                    : Span<ChunkState>.Empty;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];

                    // SAFETY: Verify entity is still valid before processing
                    // (Could have been destroyed/pooled during deferred processing)
                    if (!_world!.IsEntityValid(entity))
                        continue;

                    var location = locations[i];
                    if (!states.IsEmpty)
                    {
                        ref var state = ref states[i];
                        if (state.Lifecycle == ChunkLifecycleState.Inactive)
                            continue;
                        state.LastAccessFrame = _chunkManager.CurrentFrame;
                        state.Lifecycle = ChunkLifecycleState.Active;
                    }

                    // Register chunk with ChunkManager
                    _chunkManager.RegisterChunk(entity, location);

                    // Remove from pending set now that chunk is registered
                    _pendingChunkCreations.TryRemove(location, out _);

                    // Remove UnregisteredChunkTag - chunk is now registered!
                    // Using deferred queue for component removal
                    _world!.EnqueueComponentRemove(entity, UnregisteredChunkTagTypeId);

                    _chunksRegisteredThisFrame++;
                    _chunkManager.TouchChunk(entity);
                    // Detailed per-chunk logging removed to avoid log spam; summary emitted at end of frame.
                }
            }
        }

        private void EnsurePreallocationTargets()
        {
            if (_preallocationInitialized)
                return;

            _preallocationTargets.Clear();
            _preallocationCursor = 0;
            _preallocationLogged = false;
            _preallocationInitialized = true;

            if (!SystemSettings.EnableChunkPreallocation.Value || _chunkManager == null)
                return;

            int radius = Math.Max(0, SystemSettings.PreallocateRadiusXZ.Value);
            int height = Math.Max(0, SystemSettings.PreallocateHeight.Value);

            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    for (int y = -height; y <= height; y++)
                    {
                        _preallocationTargets.Add(new ChunkLocation(x, z, y));
                    }
                }
            }
        }

        private void ProcessPreallocation(World world)
        {
            if (_chunkManager == null || !SystemSettings.EnableChunkPreallocation.Value)
                return;

            EnsurePreallocationTargets();

            if (_preallocationTargets.Count == 0 || _preallocationCursor >= _preallocationTargets.Count)
            {
                if (!_preallocationLogged && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Chunk preallocation complete ({_preallocationTargets.Count:N0} chunks).");
                    _preallocationLogged = true;
                }
                return;
            }

            int batch = Math.Max(1, SystemSettings.PreallocationBatchSize.Value);
            int processed = 0;

            while (_preallocationCursor < _preallocationTargets.Count && processed < batch)
            {
                var target = _preallocationTargets[_preallocationCursor++];
                if (!_chunkManager.ChunkExists(target))
                {
                    var reused = SystemSettings.EnableChunkPooling.Value ? TryReuseChunkFromPool(world, target) : Entity.Invalid;
                    if (reused == Entity.Invalid)
                        GetOrCreateChunk(world, target);
                }
                processed++;
            }
        }

        private int ProcessAssignmentQueue(World world)
        {
            if (_chunkManager == null)
                return 0;

            int queued = DrainAssignmentQueue();
            if (queued == 0)
                return 0;

            if (!SystemSettings.EnableParallelAssignments.Value ||
                queued < SystemSettings.ParallelThreshold.Value)
            {
                return ProcessAssignmentsSequential(world, _assignmentBatch);
            }

            return ProcessAssignmentsParallel(world, _assignmentBatch);
        }

        private int DrainAssignmentQueue()
        {
            _assignmentBatch.Clear();
            while (ChunkAssignmentQueue.TryDequeue(out var request))
            {
                _assignmentBatch.Add(request);
            }
            return _assignmentBatch.Count;
        }

        private int ProcessAssignmentsSequential(World world, List<ChunkAssignmentRequest> requests)
        {
            int processed = 0;
            var span = CollectionsMarshal.AsSpan(requests);
            for (int i = 0; i < span.Length; i++)
            {
                ProcessAssignment(world, span[i]);
                processed++;
            }
            return processed;
        }

        private int ProcessAssignmentsParallel(World world, List<ChunkAssignmentRequest> requests)
        {
            // SIMPLIFIED: Use same pattern as creation batch processing
            // No partitioner, no worker cache, no fallback - just simple parallel foreach
            Parallel.ForEach(requests, request =>
            {
                ProcessAssignment(world, request);
            });

            return requests.Count;
        }

        private Entity TryReuseChunkFromPool(World world, ChunkLocation location)
        {
            if (_chunkManager == null || _chunkPool.Count == 0)
                return Entity.Invalid;

            while (_chunkPool.Count > 0)
            {
                var pooled = _chunkPool.Dequeue();
                if (!world.TryGetEntityLocation(pooled, out var archetype, out var slot))
                    continue;

                var locations = archetype.GetComponentSpan<ChunkLocation>(ChunkLocationTypeId);
                locations[slot] = location;

                var bounds = archetype.GetComponentSpan<ChunkBounds>(ChunkBoundsTypeId);
                bounds[slot] = _chunkManager.ChunkToWorldBounds(location);

                var states = archetype.GetComponentSpan<ChunkState>(ChunkStateTypeId);
                var state = new ChunkState(ChunkLifecycleState.PendingLoad)
                {
                    IsGenerated = true,
                    LastAccessFrame = _chunkManager.CurrentFrame
                };
                states[slot] = state;

                if (archetype.HasComponent(ChunkHashTypeId))
                {
                    var hashes = archetype.GetComponentSpan<ChunkHash>(ChunkHashTypeId);
                    hashes[slot] = new ChunkHash(0, 0);
                }

                _chunkManager.RegisterChunk(pooled, location);
                _chunkManager.TouchChunk(pooled);
                return pooled;
            }

            return Entity.Invalid;
        }

        private void PoolChunkEntity(World world, Entity chunkEntity)
        {
            if (_chunkManager == null)
                return;

            if (!world.TryGetEntityLocation(chunkEntity, out var archetype, out var slot))
                return;

            if (archetype.HasComponent(ChunkStateTypeId))
            {
                var states = archetype.GetComponentSpan<ChunkState>(ChunkStateTypeId);
                var state = states[slot];
                state.Lifecycle = ChunkLifecycleState.Inactive;
                state.EntityCount = 0;
                state.LastAccessFrame = _chunkManager.CurrentFrame;
                states[slot] = state;
            }

            _chunkEntityTracker.Clear(chunkEntity);

            _chunkManager.UnregisterChunk(chunkEntity);

            // SAFETY: Remove UnregisteredChunkTag if present before pooling
            // This prevents stale deferred operations on pooled/reused entities
            if (archetype.HasComponent(UnregisteredChunkTagTypeId))
            {
                world.EnqueueComponentRemove(chunkEntity, UnregisteredChunkTagTypeId);
            }

            _chunkPool.Enqueue(chunkEntity);
        }

        private void EvictStaleChunks(World world)
        {
            if (_chunkManager == null || !SystemSettings.EnableChunkPooling.Value)
                return;

            int cleanupBatch = Math.Max(0, SystemSettings.PoolCleanupBatch.Value);
            int maxChunks = SystemSettings.MaxChunkCount.Value;
            int overBudget = (maxChunks > 0 && _chunkManager.TotalChunks > maxChunks) ? _chunkManager.TotalChunks - maxChunks : 0;
            int idleFrames = SystemSettings.ChunkIdleFrames.Value;

            if (cleanupBatch <= 0 && overBudget <= 0 && idleFrames <= 0)
                return;

            ulong cutoffFrame = idleFrames > 0
                ? (_chunkManager.CurrentFrame > (ulong)idleFrames
                    ? _chunkManager.CurrentFrame - (ulong)idleFrames
                    : 0)
                : ulong.MaxValue;

            int request = Math.Max(cleanupBatch, overBudget);
            if (request <= 0)
                request = cleanupBatch;
            if (request <= 0 && idleFrames <= 0)
                return;

            request = Math.Max(1, request);
            var stale = _chunkManager.CollectStaleChunks(cutoffFrame, request);

            foreach (var entry in stale)
            {
                if (maxChunks > 0 && _chunkManager.TotalChunks <= maxChunks && entry.LastAccess > cutoffFrame)
                    break;

                PoolChunkEntity(world, entry.Entity);
            }
        }

        /// <summary>
        /// Add entity to chunk's entity list for efficient render queries.
        /// Marks chunk as dirty for client visual cache rebuild.
        /// </summary>
        private void TrackEntityInChunk(Entity entity, Entity chunkEntity)
        {
            _chunkEntityTracker.Add(chunkEntity, entity);

            // Mark chunk as dirty so client knows to rebuild visual cache
            _world!.EnqueueComponentAdd(chunkEntity, DirtyChunkTagTypeId, new DirtyChunkTag());
        }

        /// <summary>
        /// Move entity from old chunk to new chunk in tracking.
        /// Marks both chunks as dirty for client visual cache rebuild.
        /// </summary>
        private void MoveEntityBetweenChunks(Entity entity, Entity oldChunkEntity, Entity newChunkEntity)
        {
            if (oldChunkEntity == newChunkEntity)
                return;

            _chunkEntityTracker.Move(entity, oldChunkEntity, newChunkEntity);

            // Mark both chunks as dirty so client knows to rebuild visual caches
            _world!.EnqueueComponentAdd(oldChunkEntity, DirtyChunkTagTypeId, new DirtyChunkTag());
            _world!.EnqueueComponentAdd(newChunkEntity, DirtyChunkTagTypeId, new DirtyChunkTag());
        }

        private void ProcessAssignment(World world, ChunkAssignmentRequest request)
        {
            if (_chunkManager == null)
                return;

            // Skip invalid entities (can happen during mass spawning/destruction)
            if (request.Entity == Entity.Invalid || request.Entity.Index == 0)
                return;

            // SAFETY: Check entity validity before processing
            if (!world.IsEntityValid(request.Entity))
            {
                // CLEANUP: Entity was destroyed - remove from tracking
                if (TryGetOwner(request.Entity, out var destroyedChunk, out _))
                {
                    _chunkEntityTracker.Remove(destroyedChunk, request.Entity);
                    ClearOwner(request.Entity);
                }
                return;
            }

            var chunkEntity = GetOrCreateChunk(world, request.Location);
            if (chunkEntity == Entity.Invalid)
                return;

            _chunkManager.TouchChunk(chunkEntity);

            var owner = new ChunkOwner(chunkEntity, request.Location);

            var hadOwner = TryGetOwner(request.Entity, out var previousChunk, out var previousLocation);

            if (hadOwner)
            {
                // SANITY CHECK: Verify entity is still outside previous chunk bounds
                // Handles case where entity crossed boundary, got queued, then crossed back before processing
                if (world.TryGetEntityLocation(request.Entity, out var entityArch, out var entitySlot) &&
                    entityArch.HasComponent(PosTypeId))
                {
                    var positions = entityArch.GetComponentSpan<Position>(PosTypeId);
                    if ((uint)entitySlot < (uint)positions.Length)
                    {
                        var currentPos = positions[entitySlot];
                        var previousBounds = _chunkManager.ChunkToWorldBounds(previousLocation);
                        if (previousBounds.Contains(currentPos.X, currentPos.Y, currentPos.Z))
                        {
                            // Entity moved back into bounds, no reassignment needed
                            return;
                        }
                    }
                }

                if (previousChunk != chunkEntity)
                {
                    MoveEntityBetweenChunks(request.Entity, previousChunk, chunkEntity);
                }
            }
            else
            {
                TrackEntityInChunk(request.Entity, chunkEntity);
            }

            SetOwner(request.Entity, chunkEntity, request.Location);

            if (world.TryGetEntityLocation(request.Entity, out var archetype, out var slot) &&
                archetype.HasComponent(ChunkOwnerTypeId))
            {
                archetype.SetComponentValue(ChunkOwnerTypeId, slot, owner);
            }
            else
            {
                _world!.EnqueueComponentAdd(request.Entity, ChunkOwnerTypeId, owner);
            }
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
            {
                _chunkManager.TouchChunk(existingChunk);
                return existingChunk;
            }

            if (SystemSettings.EnableChunkPooling.Value)
            {
                var reused = TryReuseChunkFromPool(world, location);
                if (reused != Entity.Invalid)
                    return reused;
            }

            // Check if chunk creation is already pending (FIX FOR INFINITE LOOP BUG)
            if (_pendingChunkCreations.ContainsKey(location))
                return Entity.Invalid; // Already queued, skip

            // Mark as pending to prevent duplicate creation requests
            _pendingChunkCreations.TryAdd(location, 0);

            // Create new chunk entity
            var bounds = _chunkManager.ChunkToWorldBounds(location);
            var state = new ChunkState(ChunkLifecycleState.Active)
            {
                IsGenerated = true
            };

            var entityBuilder = _world!.CreateEntityBuilder()
                .Add(location)
                .Add(bounds)
                .Add(state)
                .Add(new ChunkHash(0, 0)) // Will be computed when terrain/statics are added
                .Add(new UnregisteredChunkTag()); // Mark as unregistered - removed when registered with ChunkManager

            _world!.EnqueueCreateEntity(entityBuilder);

            _chunksQueuedThisFrame++;
            // Detailed per-chunk logging removed to avoid log spam; summary emitted at end of frame.

            return Entity.Invalid; // Will be available next frame
        }

        #region Dirty Chunk Query API (for Client RenderChunkManager)

        /// <summary>
        /// Check if a server spatial chunk has been modified since last visual cache rebuild.
        /// </summary>
        public bool IsChunkDirty(World world, Entity chunkEntity)
        {
            if (world.TryGetEntityLocation(chunkEntity, out var archetype, out _))
            {
                return archetype.HasComponent(DirtyChunkTagTypeId);
            }
            return false;
        }

        /// <summary>
        /// Clear dirty flag after client rebuilds visual cache.
        /// Called by RenderChunkManager after processing chunk.
        /// </summary>
        public void ClearChunkDirty(Entity chunkEntity)
        {
            _world!.EnqueueComponentRemove(chunkEntity, DirtyChunkTagTypeId);
        }

        /// <summary>
        /// Get all dirty chunks (for client to enumerate and rebuild).
        /// Returns list of chunk entities that need visual cache updates.
        /// </summary>
        public List<Entity> GetDirtyChunks(World world)
        {
            var result = new List<Entity>();
            var archetypes = world.QueryArchetypes(typeof(DirtyChunkTag));

            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var entities = archetype.GetEntityArray();
                result.AddRange(entities);
            }

            return result;
        }

        #endregion

        public override void OnShutdown(World world)
        {
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[ChunkSystem] Shutting down - Final stats:\n{_chunkManager?.GetStatistics()}");
            }
        }
    }
}
