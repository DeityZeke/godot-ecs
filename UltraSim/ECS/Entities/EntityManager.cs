#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

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

        // Lock for thread-safe entity allocation (only entity ID allocation needs sync, archetype ops are already safe)
        private readonly object _entityAllocationLock = new object();

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
        private readonly ConcurrentQueue<Entity> _destroyQueue = new();
        private readonly ConcurrentQueue<Action<Entity>> _createQueue = new();
        private readonly ConcurrentQueue<EntityBuilder> _createWithBuilderQueue = new();

        // Reusable list for batch entity creation (V8 optimization)
        private readonly List<Entity> _createdEntitiesCache = new(1000);

        // Reusable list for batch entity destruction (V8 optimization, mirrors creation pattern)
        private readonly List<Entity> _destroyedEntitiesCache = new(1000);
        private byte[] _destroyProcessedFlags = Array.Empty<byte>();

        // Reusable list for draining EntityBuilder queue (zero-allocation)
        private readonly List<EntityBuilder> _builderBatchCache = new(1000);

        // Component list pooling for EntityBuilder reuse (zero-allocation)
        private readonly ConcurrentBag<List<ComponentInit>> _componentListPool = new();

        // Signature cache for EntityBuilder queue (avoids rebuilding identical signatures)
        // Key: Hash of TypeId pattern, Value: Cached ComponentSignature
        // Dramatically speeds up batches with many entities sharing the same components
        private readonly Dictionary<int, List<SignatureCacheEntry>> _signatureCache = new();

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

            int slot = baseArch.AddEntity(entity);
            SetLookup(idx, 0, slot);
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

            // Add entity to archetype and get the actual slot (thread-safe)
            int slot = archetype.AddEntity(entity);

            // Update lookup
            int archetypeIdx = _archetypes.GetArchetypeIndex(archetype);
            SetLookup(idx, archetypeIdx, slot);
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
            {
                Logging.Log($"Destroy failed: entity {entity} has no lookup", LogSeverity.Warning);
                return;
            }

            var arch = _archetypes.GetArchetype(loc.archetypeIdx);
            arch.RemoveAtSwap(loc.slot, entity);
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
                _destroyQueue.Enqueue(entity);
        }

        public void EnqueueDestroy(uint entityIndex)
        {
            if (entityIndex > 0 &&
                entityIndex < _entityArchetypeIndices.Count &&
                _entityArchetypeIndices[(int)entityIndex] >= 0)
            {
                var version = entityIndex < _entityVersions.Count ? _entityVersions[(int)entityIndex] : 0;
                _destroyQueue.Enqueue(new Entity(entityIndex, version));
            }
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
        /// Clears all pending entity creation queues.
        /// Use this when you want to cancel all pending entity creations (e.g., when clearing the world).
        /// </summary>
        public void ClearCreationQueues()
        {
            int builderQueueCleared = 0;
            int createQueueCleared = 0;

            while (_createWithBuilderQueue.TryDequeue(out _))
                builderQueueCleared++;

            while (_createQueue.TryDequeue(out _))
                createQueueCleared++;

            if (builderQueueCleared > 0 || createQueueCleared > 0)
            {
                Logging.Log($"[EntityManager.ClearCreationQueues] Cleared {builderQueueCleared} builders and {createQueueCleared} simple creates from creation queues", LogSeverity.Info);
            }
        }

        /// <summary>
        /// Processes all queued entity operations.
        /// Called during World.Tick() pipeline.
        /// </summary>
        public void ProcessQueues()
        {
            // Clear signature cache from previous frame (prevent unbounded growth)
            _signatureCache.Clear();

            // Process in order: destroy first, then create (prevents use-after-free issues)
            ProcessEntityDestructionQueue();
            ProcessEntityBuilderCreationQueue();
            ProcessEntityCreationQueue();
        }

        /// <summary>
        /// Processes queued entity destructions and fires batch events.
        /// Fires EntityDestroyRequest BEFORE destruction (components accessible),
        /// then fires EntityDestroyed AFTER destruction (components removed).
        /// </summary>
        private void ProcessEntityDestructionQueue()
        {
            _destroyedEntitiesCache.Clear();

            // PHASE 1: Collect all entities to be destroyed (don't destroy yet!)
            while (_destroyQueue.TryDequeue(out var entity))
            {
                _destroyedEntitiesCache.Add(entity);
            }

            int queuedCount = _destroyedEntitiesCache.Count;
            if (queuedCount == 0)
                return;

            EnsureDestroyFlagCapacity(queuedCount);
            Array.Clear(_destroyProcessedFlags, 0, queuedCount);

            // PHASE 2: Fire EntityDestroyRequest event (entities still alive, components accessible)
            var requestArgs = new EntityBatchDestroyRequestEventArgs(_destroyedEntitiesCache);
            EventSink.InvokeEntityBatchDestroyRequest(requestArgs);

            // PHASE 3: Actually destroy the entities
            // Use multi-pass approach to handle stale lookups from swap-and-pop operations
            int destroyedCount = 0;
            int passCount = 0;
            int remainingToDestroy = queuedCount;
            var archetypeCounts = new Dictionary<int, int>();
            var flags = _destroyProcessedFlags;

            while (remainingToDestroy > 0 && passCount < 100)  // Safety limit to prevent infinite loop
            {
                passCount++;
                int destroyedThisPass = 0;

                for (int i = 0; i < _destroyedEntitiesCache.Count; i++)
                {
                    if (flags[i] != 0)
                        continue;

                    var entity = _destroyedEntitiesCache[i];

                    if (!entity.IsValid || !IsAlive(entity))
                    {
                        flags[i] = 1;
                        destroyedCount++;
                        destroyedThisPass++;
                        continue;
                    }

                    if (!TryGetLocation(entity, out var archetype, out var slot))
                    {
                        flags[i] = 1;
                        destroyedCount++;
                        destroyedThisPass++;
                        continue;
                    }

                    // Resolve slot if lookup became stale due to swap cascades
                    if (slot >= archetype.Count)
                    {
                        slot = archetype.FindEntitySlot(entity);
                        if (slot < 0)
                        {
                            // Entity not found anymore - treat as processed
                            flags[i] = 1;
                            destroyedCount++;
                            destroyedThisPass++;
                            continue;
                        }
                    }

                    if (!archetype.RemoveAtSwap(slot, entity))
                    {
                        // Retry by searching for the entity's current slot (it may have moved again)
                        slot = archetype.FindEntitySlot(entity);
                        if (slot < 0 || !archetype.RemoveAtSwap(slot, entity))
                        {
                            // Could not destroy this pass; try again next loop
                            continue;
                        }
                    }

                    var archIndex = _archetypes.GetArchetypeIndex(archetype);
                    if (archIndex >= 0)
                    {
                        if (!archetypeCounts.ContainsKey(archIndex))
                            archetypeCounts[archIndex] = 0;
                        archetypeCounts[archIndex]++;
                    }

                    _freeHandles.Push(entity.Packed + VersionIncrement);
                    _entityVersions[(int)entity.Index]++;
                    _packedVersions[(int)entity.Index] += VersionIncrement;
                    ClearLookup(entity.Index);
                    _liveEntityCount--;
                    flags[i] = 1;
                    destroyedCount++;
                    destroyedThisPass++;
                }

                remainingToDestroy = queuedCount - destroyedCount;

                // If we made no progress this pass, stop (shouldn't happen but safety check)
                if (destroyedThisPass == 0)
                {
                    Logging.Log($"[EntityManager] Multi-pass destroy stalled! Destroyed {destroyedCount}/{queuedCount}, {remainingToDestroy} entities still have lookups but couldn't be destroyed", LogSeverity.Error);
                    break;
                }
            }

            if (archetypeCounts.Count > 0 && queuedCount > 1000)
            {
                foreach (var kvp in archetypeCounts)
                {
                    Logging.Log($"[EntityManager] Destroyed {kvp.Value} entities from archetype {kvp.Key}", LogSeverity.Info);
                }
            }

            // Log results
            if (destroyedCount != queuedCount)
            {
                Logging.Log($"[EntityManager.ProcessEntityDestructionQueue] Queued: {queuedCount}, Destroyed: {destroyedCount}, Failed: {queuedCount - destroyedCount}, Passes: {passCount}", LogSeverity.Warning);
            }
            else if (queuedCount > 1000)
            {
                Logging.Log($"[EntityManager.ProcessEntityDestructionQueue] Successfully destroyed {destroyedCount} entities in {passCount} pass(es)", LogSeverity.Info);
            }

            // PHASE 4: Fire EntityDestroyed event (entities destroyed, components removed)
            if (_destroyedEntitiesCache.Count > 0)
            {
                var args = new EntityBatchDestroyedEventArgs(_destroyedEntitiesCache);
                EventSink.InvokeEntityBatchDestroyed(args);
            }
        }

        /// <summary>
        /// Processes EntityBuilder queue with archetype grouping to avoid thrashing.
        /// </summary>
        private void ProcessEntityBuilderCreationQueue()
        {
            const int ADAPTIVE_THRESHOLD = 500;

            int builderQueueSize = _createWithBuilderQueue.Count;
            if (builderQueueSize <= 0)
                return;

            _builderBatchCache.Clear();
            _builderBatchCache.Capacity = Math.Max(_builderBatchCache.Capacity, builderQueueSize);
            while (_createWithBuilderQueue.TryDequeue(out var builder))
            {
                _builderBatchCache.Add(builder);
            }

            bool trackForEvent = builderQueueSize >= ADAPTIVE_THRESHOLD;
            if (trackForEvent)
            {
                _createdEntitiesCache.Clear();
                _createdEntitiesCache.Capacity = Math.Max(_createdEntitiesCache.Capacity, builderQueueSize);
            }

            var signatureGroups = new Dictionary<ComponentSignature, List<EntityBuilder>>(ReferenceEqualityComparer.Instance);
            Span<int> typeIdBuffer = stackalloc int[32];

            foreach (ref readonly var builder in CollectionsMarshal.AsSpan(_builderBatchCache))
            {
                try
                {
                    var components = builder.GetComponents();
                    var componentSpan = CollectionsMarshal.AsSpan(components);
                    int componentCount = Math.Min(componentSpan.Length, typeIdBuffer.Length);

                    for (int i = 0; i < componentCount; i++)
                    {
                        typeIdBuffer[i] = componentSpan[i].TypeId;
                    }

                    var typeIdSlice = typeIdBuffer.Slice(0, componentCount);
                    int signatureHash = ComputeTypeIdHash(typeIdSlice);

                    if (!TryGetCachedSignature(signatureHash, typeIdSlice, out var signature))
                    {
                        signature = ComponentSignature.FromTypeIds(typeIdSlice);
                        CacheSignature(signatureHash, typeIdSlice, signature);
                    }

                    if (!signatureGroups.TryGetValue(signature, out var group))
                    {
                        group = new List<EntityBuilder>();
                        signatureGroups.Add(signature, group);
                    }

                    group.Add(builder);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[EntityManager] EntityBuilder grouping exception: {ex}", LogSeverity.Error);
                }
            }

            foreach (var kvp in signatureGroups)
            {
                var signature = kvp.Key;
                var builders = kvp.Value;

                foreach (var builder in builders)
                {
                    try
                    {
                        var components = builder.GetComponents();
                        var componentSpan = CollectionsMarshal.AsSpan(components);

                        var entity = CreateWithSignature(signature);

                        if (TryGetLocation(entity, out var archetype, out var slot))
                        {
                            foreach (ref readonly var comp in componentSpan)
                            {
                                archetype.SetComponentValueBoxed(comp.TypeId, slot, comp.Value);
                            }
                        }

                        if (trackForEvent)
                        {
                            _createdEntitiesCache.Add(entity);
                        }

                        ReturnComponentList(components);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[EntityManager] EntityBuilder creation exception: {ex}", LogSeverity.Error);
                    }
                }
            }

            if (trackForEvent && _createdEntitiesCache.Count > 0)
            {
                _world.FireEntityBatchCreated(_createdEntitiesCache);
            }
        }

        /// <summary>
        /// Processes Action&lt;Entity&gt; creation queue with adaptive threshold optimization.
        /// Small batches skip event tracking for minimal overhead.
        /// </summary>
        /// <remarks>
        /// Adaptive Strategy:
        /// - Small batches (&lt;500): Skip event tracking for minimal overhead
        /// - Large batches (≥500): Batch efficiently with zero-allocation events
        /// </remarks>
        private void ProcessEntityCreationQueue()
        {
            const int ADAPTIVE_THRESHOLD = 500;

            int queueSize = _createQueue.Count;

            if (queueSize < ADAPTIVE_THRESHOLD)
            {
                // Small batch: Process immediately without event tracking
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

        private bool TryGetCachedSignature(int signatureHash, ReadOnlySpan<int> typeIds, out ComponentSignature signature)
        {
            if (_signatureCache.TryGetValue(signatureHash, out var entries))
            {
                var span = CollectionsMarshal.AsSpan(entries);
                for (int i = 0; i < span.Length; i++)
                {
                    var entry = span[i];
                    if (entry.ComponentCount != typeIds.Length)
                        continue;

                    if (SignatureMatches(entry.Signature, typeIds))
                    {
                        signature = entry.Signature;
                        return true;
                    }
                }
            }

            signature = null!;
            return false;
        }

        private void CacheSignature(int signatureHash, ReadOnlySpan<int> typeIds, ComponentSignature signature)
        {
            if (!_signatureCache.TryGetValue(signatureHash, out var entries))
            {
                entries = new List<SignatureCacheEntry>();
                _signatureCache[signatureHash] = entries;
            }

            entries.Add(new SignatureCacheEntry(signature, typeIds.Length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SignatureMatches(ComponentSignature signature, ReadOnlySpan<int> typeIds)
        {
            foreach (var id in typeIds)
            {
                if (!signature.Contains(id))
                    return false;
            }

            return true;
        }

        private readonly struct SignatureCacheEntry
        {
            public readonly ComponentSignature Signature;
            public readonly int ComponentCount;

            public SignatureCacheEntry(ComponentSignature signature, int componentCount)
            {
                Signature = signature;
                ComponentCount = componentCount;
            }
        }

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
        private void EnsureDestroyFlagCapacity(int count)
        {
            if (_destroyProcessedFlags.Length >= count)
                return;

            int newSize = _destroyProcessedFlags.Length == 0 ? 128 : _destroyProcessedFlags.Length * 2;
            if (newSize < count)
                newSize = count;

            Array.Resize(ref _destroyProcessedFlags, newSize);
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

        /// <summary>
        /// Computes a hash code from a span of TypeIds for signature caching.
        /// Uses FNV-1a hash algorithm for speed and good distribution.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeTypeIdHash(ReadOnlySpan<int> typeIds)
        {
            const uint FNV_OFFSET_BASIS = 2166136261;
            const uint FNV_PRIME = 16777619;

            uint hash = FNV_OFFSET_BASIS;
            foreach (var id in typeIds)
            {
                hash ^= (uint)id;
                hash *= FNV_PRIME;
            }

            return (int)hash;
        }
    }
}
