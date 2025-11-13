#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using UltraSim;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages entity lifecycle - allocation, recycling, lookup, and deferred operations.
    /// Owns entity versioning and free list to prevent ID reuse bugs.
    /// </summary>
    public sealed class EntityManager
    {
        private readonly World _world;
        private readonly ArchetypeManager _archetypes;

        // Entity storage
        private const ulong VersionIncrement = 1UL << 32;

        private readonly Stack<ulong> _freeHandles = new();
        private readonly List<uint> _entityVersions = new() { 0 }; // Index 0 is reserved for Invalid entity (version 0)
        private readonly List<ulong> _packedVersions = new() { 0 }; // Cached (version << 32) per entity index
        private readonly List<int> _entityArchetypeIndices = new() { -1 };
        private readonly List<int> _entitySlots = new() { -1 };
        private uint _nextEntityIndex = 1; // Start at 1 (0 is reserved for Invalid)
        private int _liveEntityCount = 0;
        
        // Deferred operation queues
        private readonly ConcurrentQueue<uint> _destroyQueue = new();
        private readonly ConcurrentQueue<Action<Entity>> _createQueue = new();
        private readonly ConcurrentQueue<EntityBuilder> _createWithBuilderQueue = new();

        // Reusable list for batch entity creation (V8 optimization)
        private readonly List<Entity> _createdEntitiesCache = new(1000);

        // Reusable list for batch entity destruction (V8 optimization, mirrors creation pattern)
        private readonly List<Entity> _destroyedEntitiesCache = new(1000);

        // Component list pooling for EntityBuilder reuse (zero-allocation)
        private readonly ConcurrentBag<List<ComponentInit>> _componentListPool = new();

        public int EntityCount => _liveEntityCount;

        public EntityManager(World world, ArchetypeManager archetypes)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
        }

        #region Entity Lifecycle

        /// <summary>
        /// Creates a new entity in the empty archetype.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity Create()
        {
            uint idx;
            ulong packed;
            if (_freeHandles.Count > 0)
            {
                // Reuse a recycled handle - cached packed version already incremented once
                packed = _freeHandles.Pop();
                idx = (uint)packed;
                _entityVersions[(int)idx]++;
                _packedVersions[(int)idx] += VersionIncrement;
                packed += VersionIncrement;
            }
            else
            {
                // Allocate a new index - add version 1 to the list
                idx = _nextEntityIndex++;
                _entityVersions.Add(1); // First version is 1, not 0
                _packedVersions.Add(VersionIncrement);
                packed = VersionIncrement | idx;
            }

            EnsureLookupCapacity(idx);
            var entity = new Entity(packed);

            // Get empty archetype from ArchetypeManager
            var baseArch = _archetypes.GetEmptyArchetype();

            baseArch.AddEntity(entity);
            SetLookup(idx, 0, baseArch.Count - 1);
            _liveEntityCount++;

            return entity;
        }

        /// <summary>
        /// Creates an entity directly in an archetype with the given signature.
        /// This is THE KEY to avoiding archetype thrashing - entity starts in correct archetype!
        /// </summary>
        public Entity CreateWithSignature(ComponentSignature signature)
        {
            // Allocate entity ID
            uint idx;
            ulong packed;
            if (_freeHandles.Count > 0)
            {
                // Reuse a recycled handle - cached packed version already incremented once
                packed = _freeHandles.Pop();
                idx = (uint)packed;
                _entityVersions[(int)idx]++;
                _packedVersions[(int)idx] += VersionIncrement;
                packed += VersionIncrement;
            }
            else
            {
                // Allocate a new index - add version 1 to the list at position idx
                idx = _nextEntityIndex++;
                _entityVersions.Add(1); // First version is 1, not 0
                _packedVersions.Add(VersionIncrement);
                packed = VersionIncrement | idx;
            }

            EnsureLookupCapacity(idx);
            var entity = new Entity(packed);

            // Get or create archetype from ArchetypeManager
            var archetype = _archetypes.GetOrCreate(signature);

            // Add entity to archetype
            archetype.AddEntity(entity);

            // Update lookup
            int archetypeIdx = _archetypes.GetArchetypeIndex(archetype);
            SetLookup(idx, archetypeIdx, archetype.Count - 1);
            _liveEntityCount++;

            return entity;
        }

        /// <summary>
        /// Destroys an entity immediately.
        /// For deferred destruction, use EnqueueDestroy().
        /// </summary>
        public void Destroy(Entity entity)
        {
            if (!TryGetLookup(entity.Index, out var loc))
                return;

            var arch = _archetypes.GetArchetype(loc.archetypeIdx);
            
            arch.RemoveAtSwap(loc.slot);
            _freeHandles.Push(entity.Packed + VersionIncrement);
            _entityVersions[(int)entity.Index]++;
            _packedVersions[(int)entity.Index] += VersionIncrement;
            ClearLookup(entity.Index);
            _liveEntityCount--;
        }

        /// <summary>
        /// Checks if an entity is still alive (valid ID and version).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(Entity entity)
        {
            return entity.Index > 0 &&
                   entity.Index < _entityVersions.Count &&
                   _entityVersions[(int)entity.Index] == entity.Version &&
                   GetArchetypeIndex(entity.Index) >= 0;
        }

        /// <summary>
        /// Gets the archetype and slot for an entity.
        /// Used by command buffers to set component values after creation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetLocation(Entity entity, out Archetype archetype, out int slot)
        {
            if (!TryGetLookup(entity.Index, out var loc))
            {
                archetype = null!;
                slot = -1;
                return false;
            }

            archetype = _archetypes.GetArchetype(loc.archetypeIdx);
            slot = loc.slot;
            return true;
        }

        /// <summary>
        /// Updates the lookup table when an entity moves between archetypes.
        /// Called by World during component add/remove operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateLookup(uint entityIndex, Archetype archetype, int slot)
        {
            int archetypeIdx = _archetypes.GetArchetypeIndex(archetype);
            EnsureLookupCapacity(entityIndex);
            SetLookup(entityIndex, archetypeIdx, slot);
        }

        #endregion

        #region Deferred Operations

        /// <summary>
        /// Enqueues an entity for destruction at the start of the next frame.
        /// </summary>
        public void EnqueueDestroy(Entity entity)
        {
            if (IsAlive(entity))
                _destroyQueue.Enqueue(entity.Index);
        }

        public void EnqueueDestroy(uint entityIndex)
        {
            if (entityIndex > 0 && entityIndex < _entityArchetypeIndices.Count && _entityArchetypeIndices[(int)entityIndex] >= 0)
                _destroyQueue.Enqueue(entityIndex);
        }

        /// <summary>
        /// Enqueues an entity for creation with an optional builder function.
        /// </summary>
        public void EnqueueCreate(Action<Entity>? builder = null)
        {
            _createQueue.Enqueue(builder ?? (_ => { }));
        }

        /// <summary>
        /// Enqueues an entity for creation with all components pre-defined in EntityBuilder.
        /// This AVOIDS archetype thrashing by creating the entity directly in the final archetype.
        /// Significantly faster than adding components one-by-one (10x improvement for large batches).
        /// </summary>
        public void EnqueueCreate(EntityBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            _createWithBuilderQueue.Enqueue(builder);
        }

        /// <summary>
        /// Processes all queued entity operations.
        /// Called during World.Tick() pipeline.
        /// Uses V8 optimization: Adaptive threshold with zero-allocation events.
        /// </summary>
        /// <remarks>
        /// V8 Optimization Strategy:
        /// - Small batches (&lt;500): Skip event tracking for minimal overhead
        /// - Large batches (≥500): Batch efficiently with zero-allocation events
        /// - Reuses _createdEntitiesCache to avoid List allocation
        /// - Uses World.FireEntityBatchCreated(List) for zero-allocation event firing
        /// Performance: 79 ns/entity at 100k entities (12.6M entities/sec)
        /// </remarks>
        public void ProcessQueues()
        {
            const int ADAPTIVE_THRESHOLD = 500;

            // Process destructions first - collect entities before destroying for event firing
            _destroyedEntitiesCache.Clear();

            while (_destroyQueue.TryDequeue(out var idx))
            {
                var entity = CreateEntityHandle(idx);
                _destroyedEntitiesCache.Add(entity);
                Destroy(entity);
            }

            // Fire EntityBatchDestroyed event if any entities were destroyed
            if (_destroyedEntitiesCache.Count > 0)
            {
                _world.FireEntityBatchDestroyed(_destroyedEntitiesCache);
            }

            // Process EntityBuilder queue (component-batched creation, NO archetype thrashing)
            int builderQueueSize = _createWithBuilderQueue.Count;

            if (builderQueueSize > 0)
            {
                // Determine if we need event tracking
                bool trackForEvent = builderQueueSize >= ADAPTIVE_THRESHOLD;

                if (trackForEvent)
                {
                    _createdEntitiesCache.Clear();
                    _createdEntitiesCache.Capacity = Math.Max(_createdEntitiesCache.Capacity, builderQueueSize);
                }

                while (_createWithBuilderQueue.TryDequeue(out var builder))
                {
                    try
                    {
                        var components = builder.GetComponents();

                        // Build component signature
                        var signature = new ComponentSignature();
                        foreach (var comp in components)
                        {
                            signature = signature.Add(comp.TypeId);
                        }

                        // Create entity directly in target archetype (NO thrashing!)
                        var entity = CreateWithSignature(signature);

                        // Set all component values
                        if (TryGetLocation(entity, out var archetype, out var slot))
                        {
                            foreach (var comp in components)
                            {
                                archetype.SetComponentValueBoxed(comp.TypeId, slot, comp.Value);
                            }
                        }

                        // Track for event if needed
                        if (trackForEvent)
                        {
                            _createdEntitiesCache.Add(entity);
                        }

                        // Return component list to pool
                        ReturnComponentList(components);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[EntityManager] EntityBuilder creation exception: {ex}", LogSeverity.Error);
                    }
                }

                // Fire event if we tracked entities
                if (trackForEvent && _createdEntitiesCache.Count > 0)
                {
                    _world.FireEntityBatchCreated(_createdEntitiesCache);
                }
            }

            // Adaptive entity creation based on batch size (regular Action<Entity> queue)
            int queueSize = _createQueue.Count;

            if (queueSize < ADAPTIVE_THRESHOLD)
            {
                // Small batch: Process immediately without event tracking
                // This skips the overhead of List management and event firing for small batches
                while (_createQueue.TryDequeue(out var builder))
                {
                    var entity = Create();
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[EntityManager] Entity builder exception: {ex}", LogSeverity.Error);
                    }
                }
            }
            else
            {
                // Large batch: Collect entities and fire zero-allocation batch event
                _createdEntitiesCache.Clear();
                _createdEntitiesCache.Capacity = Math.Max(_createdEntitiesCache.Capacity, queueSize);

                while (_createQueue.TryDequeue(out var builder))
                {
                    var entity = Create();
                    _createdEntitiesCache.Add(entity);
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[EntityManager] Entity builder exception: {ex}", LogSeverity.Error);
                    }
                }

                // Fire zero-allocation event: Passes List directly without ToArray()
                if (_createdEntitiesCache.Count > 0)
                {
                    _world.FireEntityBatchCreated(_createdEntitiesCache);
                }
            }
        }

        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureLookupCapacity(uint idx)
        {
            while (_entityArchetypeIndices.Count <= idx)
            {
                _entityArchetypeIndices.Add(-1);
                _entitySlots.Add(-1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetLookup(uint idx, int archetypeIdx, int slot)
        {
            _entityArchetypeIndices[(int)idx] = archetypeIdx;
            _entitySlots[(int)idx] = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearLookup(uint idx)
        {
            if (idx < _entityArchetypeIndices.Count)
            {
                _entityArchetypeIndices[(int)idx] = -1;
                _entitySlots[(int)idx] = -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetArchetypeIndex(uint idx) =>
            idx < _entityArchetypeIndices.Count ? _entityArchetypeIndices[(int)idx] : -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetLookup(uint idx, out (int archetypeIdx, int slot) loc)
        {
            if (idx >= _entityArchetypeIndices.Count)
            {
                loc = (-1, -1);
                return false;
            }

            var archetypeIdx = _entityArchetypeIndices[(int)idx];
            if (archetypeIdx < 0)
            {
                loc = (-1, -1);
                return false;
            }

            loc = (archetypeIdx, _entitySlots[(int)idx]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Entity CreateEntityHandle(uint idx)
        {
            var packed = _packedVersions[(int)idx] | idx;
            return new Entity(packed);
        }

        /// <summary>
        /// Gets a pooled List for EntityBuilder or creates a new one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal List<ComponentInit> GetComponentList()
        {
            if (_componentListPool.TryTake(out var list))
            {
                list.Clear();
                return list;
            }
            return new List<ComponentInit>(16); // Default capacity for typical entities
        }

        /// <summary>
        /// Returns a List to the pool for reuse.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReturnComponentList(List<ComponentInit> list)
        {
            if (list != null)
            {
                list.Clear();
                _componentListPool.Add(list);
            }
        }
    }
}
