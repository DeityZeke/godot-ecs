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
        private readonly Stack<uint> _freeIndices = new();
        private readonly List<uint> _entityVersions = new() { 0 }; // Index 0 is reserved for Invalid entity (version 0)
        private readonly Dictionary<uint, (int archetypeIdx, int slot)> _entityLookup = new();
        private uint _nextEntityIndex = 1; // Start at 1 (0 is reserved for Invalid)
        
        // Deferred operation queues
        private readonly ConcurrentQueue<uint> _destroyQueue = new();
        private readonly ConcurrentQueue<Action<Entity>> _createQueue = new();

        public int EntityCount => _entityLookup.Count;

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
            if (_freeIndices.Count > 0)
            {
                // Reuse a recycled index - increment its version
                idx = _freeIndices.Pop();
                _entityVersions[(int)idx]++;
            }
            else
            {
                // Allocate a new index - add version 1 to the list
                idx = _nextEntityIndex++;
                _entityVersions.Add(1); // First version is 1, not 0
            }

            var entity = new Entity(idx, _entityVersions[(int)idx]);

            // Get empty archetype from ArchetypeManager
            var baseArch = _archetypes.GetEmptyArchetype();

            baseArch.AddEntity(entity);
            _entityLookup[idx] = (0, baseArch.Count - 1);

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
            if (_freeIndices.Count > 0)
            {
                // Reuse a recycled index - increment its version
                idx = _freeIndices.Pop();
                _entityVersions[(int)idx]++;
            }
            else
            {
                // Allocate a new index - add version 1 to the list at position idx
                idx = _nextEntityIndex++;
                _entityVersions.Add(1); // First version is 1, not 0
            }

            var entity = new Entity(idx, _entityVersions[(int)idx]);

            // Get or create archetype from ArchetypeManager
            var archetype = _archetypes.GetOrCreate(signature);

            // Add entity to archetype
            archetype.AddEntity(entity);

            // Update lookup
            int archetypeIdx = _archetypes.GetArchetypeIndex(archetype);
            _entityLookup[idx] = (archetypeIdx, archetype.Count - 1);

            return entity;
        }

        /// <summary>
        /// Destroys an entity immediately.
        /// For deferred destruction, use EnqueueDestroy().
        /// </summary>
        public void Destroy(Entity entity)
        {
            if (!_entityLookup.TryGetValue(entity.Index, out var loc))
                return;

            var arch = _archetypes.GetArchetype(loc.archetypeIdx);
            
            arch.RemoveAtSwap(loc.slot);
            _entityLookup.Remove(entity.Index);
            _freeIndices.Push(entity.Index);
            _entityVersions[(int)entity.Index]++;
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
                   _entityLookup.ContainsKey(entity.Index);
        }

        /// <summary>
        /// Gets the archetype and slot for an entity.
        /// Used by command buffers to set component values after creation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetLocation(Entity entity, out Archetype archetype, out int slot)
        {
            if (!_entityLookup.TryGetValue(entity.Index, out var loc))
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
            _entityLookup[entityIndex] = (archetypeIdx, slot);
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

        /// <summary>
        /// Enqueues an entity for creation with an optional builder function.
        /// </summary>
        public void EnqueueCreate(Action<Entity>? builder = null)
        {
            _createQueue.Enqueue(builder ?? (_ => { }));
        }

        /// <summary>
        /// Processes all queued entity operations.
        /// Called during World.Tick() pipeline.
        /// </summary>
        public void ProcessQueues()
        {
            // Process destructions first
            while (_destroyQueue.TryDequeue(out var idx))
            {
                var entity = new Entity(idx, _entityVersions[(int)idx]);
                Destroy(entity);
            }

            // Then process creations
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

        #endregion
    }
}
