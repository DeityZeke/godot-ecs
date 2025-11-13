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
            public BoolSetting EnableAutoAssignment { get; private set; }
            public IntSetting AssignmentFrequency { get; private set; }
            public BoolSetting UseDirtyAssignmentQueue { get; private set; }
            public BoolSetting FallbackToFullScan { get; private set; }
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
                EnableAutoAssignment = RegisterBool("Auto-Assign Entities", true,
                    tooltip: "Automatically assign entities to chunks based on Position");

                AssignmentFrequency = RegisterInt("Assignment Frequency", 60,
                    min: 1, max: 600, step: 1,
                    tooltip: "Update entity assignments every N frames (60 = ~1 per second at 60fps)");

                UseDirtyAssignmentQueue = RegisterBool("Use Dirty Assignment Queue", true,
                    tooltip: "Process chunk assignments from the dirty queue before running full scans.");

                FallbackToFullScan = RegisterBool("Fallback To Full Scan", true,
                    tooltip: "Run a full assignment scan even if the dirty queue processed assignments this frame.");

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
        private CommandBuffer _buffer = new();
        private int _frameCounter = 0;
        private int _chunksQueuedThisFrame = 0;
        private int _chunksRegisteredThisFrame = 0;

        // Deferred batch processing queues
        private readonly ConcurrentQueue<EntityBatchProcessedEventArgs> _movementBatchQueue = new();
        private readonly ConcurrentQueue<EntityBatchCreatedEventArgs> _creationBatchQueue = new();

        // Track chunks that are pending creation (to avoid duplicate creation requests)
        private readonly HashSet<ChunkLocation> _pendingChunkCreations = new();
        private readonly Dictionary<ChunkLocation, Entity> _chunkEntityCache = new();
        private readonly List<ChunkAssignmentRequest> _assignmentBatch = new(capacity: 2048);
        private readonly List<ChunkAssignmentRequest> _parallelFallback = new();
        private readonly object _fallbackLock = new();
        private static readonly ThreadLocal<WorkerCache> _workerCache = new(() => new WorkerCache());
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

        public override void OnInitialize(World world)
        {
            _world = world;
            _cachedQuery = world.QueryArchetypes(typeof(Position));
            _chunkManager = new ChunkManager(chunkSizeXZ: 64, chunkSizeY: 32);

            // Subscribe to World's entity batch created event for initial chunk assignment
            EventSink.EntityBatchCreated += OnEntityBatchCreated;
            Logging.Log($"[ChunkSystem] Subscribed to World entity creation events");

            // Subscribe to MovementSystem's entity batch processed event
            UltraSim.Server.EventSink.EntityBatchProcessed += OnEntityBatchProcessed;
            Logging.Log($"[ChunkSystem] Subscribed to MovementSystem batch events");

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
        /// Event handler for MovementSystem's EntityBatchProcessed event.
        /// Always processes immediately with smart chunk boundary checking.
        /// Only enqueues entities that actually crossed chunk boundaries.
        /// </summary>
        private void OnEntityBatchProcessed(EntityBatchProcessedEventArgs args)
        {
            if (_chunkManager == null || _world == null)
                return;

            // ALWAYS process movement immediately with smart filtering
            // Deferred batching is bad for continuous movement - it accumulates 6 frames of checks
            // when most entities haven't crossed chunk boundaries yet.
            ProcessMovementBatchSmart(args);
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
        /// Smart movement batch processing - only enqueues entities that crossed chunk boundaries.
        /// This is much faster than deferred batching for continuous movement because:
        /// 1. Most entities don't cross chunk boundaries every frame (chunks are 64x32x64 units)
        /// 2. Processing immediately avoids accumulating 6+ frames of checks
        /// 3. Early filtering reduces ChunkAssignmentQueue size dramatically
        /// </summary>
        private void ProcessMovementBatchSmart(EntityBatchProcessedEventArgs args)
        {
            var entitySpan = args.GetSpan();

            for (int i = 0; i < entitySpan.Length; i++)
            {
                var entity = entitySpan[i];

                // SAFETY: Skip entities that were destroyed during movement processing
                if (!_world!.IsEntityValid(entity))
                    continue;

                if (!_world!.TryGetEntityLocation(entity, out var archetype, out var slot))
                    continue;

                // Need both Position and ChunkOwner to check boundaries
                if (!archetype.HasComponent(PosTypeId) || !archetype.HasComponent(ChunkOwnerTypeId))
                    continue;

                var positions = archetype.GetComponentSpan<Position>(PosTypeId);
                var owners = archetype.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);

                if ((uint)slot >= (uint)positions.Length || (uint)slot >= (uint)owners.Length)
                    continue;

                var position = positions[slot];
                var currentOwner = owners[slot];

                // SMART CHECK: Only calculate chunk location if entity has an owner
                // (Entities without owners will be caught by creation event or periodic scan)
                if (!currentOwner.IsAssigned)
                    continue;

                // Calculate which chunk this position should be in
                var targetChunkLoc = _chunkManager!.WorldToChunk(position.X, position.Y, position.Z);

                // CRITICAL OPTIMIZATION: Only enqueue if chunk actually changed
                // Most entities stay in the same chunk for many frames (64x32x64 unit chunks)
                if (!currentOwner.Location.Equals(targetChunkLoc))
                {
                    ChunkAssignmentQueue.Enqueue(entity, targetChunkLoc);
                }
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

            // === AUTO-ASSIGNMENT ===
            if (SystemSettings.EnableAutoAssignment.Value)
            {
                int frequency = SystemSettings.AssignmentFrequency.Value;
                bool runFullScan = _frameCounter % frequency == 0 &&
                    (!SystemSettings.UseDirtyAssignmentQueue.Value ||
                     !queueProcessed ||
                     SystemSettings.FallbackToFullScan.Value);

                if (runFullScan)
                {
                    int reassignedCount = AssignEntitiesToChunks(world);

                    if (SystemSettings.EnableDebugLogs.Value && reassignedCount > 0)
                    {
                        Logging.Log($"[ChunkSystem] Reassigned {reassignedCount} entities to chunks");
                    }
                }
            }

            EvictStaleChunks(world);

            // Apply all component changes
            _buffer.Apply(world);

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
                    _pendingChunkCreations.Remove(location);

                    // Remove UnregisteredChunkTag - chunk is now registered!
                    // Using CommandBuffer to defer component removal (safe during Update)
                    _buffer.RemoveComponent(entity, UnregisteredChunkTagTypeId);

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
            if (_chunkManager == null)
                return 0;

            _parallelFallback.Clear();
            int processed = 0;
            int batchSize = Math.Max(64, SystemSettings.ParallelBatchSize.Value);

            int count = requests.Count;
            Parallel.ForEach(Partitioner.Create(0, count, batchSize), range =>
            {
                var cache = _workerCache.Value!;
                cache.Reset();

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    var request = requests[i];
                    if (TryProcessAssignmentFast(world, request, cache))
                    {
                        Interlocked.Increment(ref processed);
                    }
                    else
                    {
                        lock (_fallbackLock)
                        {
                            _parallelFallback.Add(request);
                        }
                    }
                }
            });

            if (_parallelFallback.Count > 0)
            {
                processed += ProcessAssignmentsSequential(world, _parallelFallback);
                _parallelFallback.Clear();
            }

            return processed;
        }

        private bool TryProcessAssignmentFast(World world, ChunkAssignmentRequest request, WorkerCache cache)
        {
            if (_chunkManager == null)
                return false;

            // Skip invalid entities (can happen during mass spawning/destruction)
            if (request.Entity == Entity.Invalid || request.Entity.Index == 0)
                return true;

            // SAFETY: Check entity validity before processing
            if (!world.IsEntityValid(request.Entity))
                return true;

            var chunkEntity = cache.GetChunk(_chunkManager, request.Location);
            if (chunkEntity == Entity.Invalid)
                return false;

            _chunkManager.TouchChunk(chunkEntity);

            if (!world.TryGetEntityLocation(request.Entity, out var archetype, out var slot))
                return true;

            if (archetype.HasComponent(ChunkOwnerTypeId))
            {
                var owners = archetype.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                if ((uint)slot < (uint)owners.Length)
                {
                    ref var owner = ref owners[slot];
                    if (owner.IsAssigned &&
                        owner.ChunkEntity == chunkEntity &&
                        owner.Location.Equals(request.Location))
                    {
                        return true;
                    }

                    owner = new ChunkOwner(chunkEntity, request.Location);
                    return true;
                }
            }

            _buffer.AddComponent(request.Entity, ChunkOwnerTypeId, new ChunkOwner(chunkEntity, request.Location));
            return true;
        }

        private sealed class WorkerCache
        {
            private readonly Dictionary<ChunkLocation, Entity> _chunkLookup = new(128);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset() => _chunkLookup.Clear();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Entity GetChunk(ChunkManager manager, ChunkLocation location)
            {
                if (_chunkLookup.TryGetValue(location, out var entity))
                    return entity;

                entity = manager.GetChunk(location);
                _chunkLookup[location] = entity;
                return entity;
            }
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
            // This prevents stale CommandBuffer operations on pooled/reused entities
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
        /// </summary>
        private void TrackEntityInChunk(Entity entity, Entity chunkEntity)
        {
            _chunkEntityTracker.Add(chunkEntity, entity);
        }

        /// <summary>
        /// Move entity from old chunk to new chunk in tracking.
        /// </summary>
        private void MoveEntityBetweenChunks(Entity entity, Entity oldChunkEntity, Entity newChunkEntity)
        {
            if (oldChunkEntity == newChunkEntity)
                return;

            _chunkEntityTracker.Move(entity, oldChunkEntity, newChunkEntity);
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
                return;

            var chunkEntity = GetOrCreateChunk(world, request.Location);
            if (chunkEntity == Entity.Invalid)
                return;

            _chunkManager.TouchChunk(chunkEntity);

            var owner = new ChunkOwner(chunkEntity, request.Location);

            var hadOwner = TryGetOwner(request.Entity, out var previousChunk, out _);

            if (hadOwner)
            {
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
                _buffer.AddComponent(request.Entity, ChunkOwnerTypeId, owner);
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
            _chunkEntityCache.Clear();
            var archetypes = world.QueryArchetypes(typeof(Position));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                // Skip chunk entities themselves (they have ChunkLocation)
                if (arch.HasComponent(ChunkLocationTypeId))
                    continue;

                var positions = arch.GetComponentSpan<Position>(PosTypeId);
                var entities = arch.GetEntityArray();

                bool hasChunkOwnerComponent = arch.HasComponent(ChunkOwnerTypeId);

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var pos = positions[i];

                    // Calculate chunk location for this position
                    var chunkLoc = _chunkManager.WorldToChunk(pos.X, pos.Y, pos.Z);

                    if (!_chunkEntityCache.TryGetValue(chunkLoc, out var chunkEntity) || chunkEntity == Entity.Invalid)
                    {
                        chunkEntity = GetOrCreateChunk(world, chunkLoc);
                        if (chunkEntity == Entity.Invalid)
                            continue;

                        _chunkEntityCache[chunkLoc] = chunkEntity;
                    }

                    _chunkManager.TouchChunk(chunkEntity);

                    var hasOwner = TryGetOwner(entity, out var previousChunk, out var previousLocation);
                    if (hasOwner && previousChunk == chunkEntity && previousLocation.Equals(chunkLoc))
                        continue;

                    var owner = new ChunkOwner(chunkEntity, chunkLoc);

                    if (hasOwner)
                    {
                        if (previousChunk != chunkEntity && previousChunk != Entity.Invalid)
                        {
                            MoveEntityBetweenChunks(entity, previousChunk, chunkEntity);
                        }
                    }
                    else
                    {
                        TrackEntityInChunk(entity, chunkEntity);
                    }

                    SetOwner(entity, chunkEntity, chunkLoc);

                    if (hasChunkOwnerComponent)
                    {
                        arch.SetComponentValue(ChunkOwnerTypeId, i, owner);
                    }
                    else
                    {
                        _buffer.AddComponent(entity, ChunkOwnerTypeId, owner);
                    }

                    assignedCount++;
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
                builder.Add(new UnregisteredChunkTag()); // Mark as unregistered - removed when registered with ChunkManager
            });

            _chunksQueuedThisFrame++;
            // Detailed per-chunk logging removed to avoid log spam; summary emitted at end of frame.

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
