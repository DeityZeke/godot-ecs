#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UltraSim;
using UltraSim.ECS.Systems;
using UltraSim.ECS.Events;

namespace UltraSim.ECS
{
    /// <summary>
    /// Core ECS World - manages entities, archetypes, and deferred operations.
    /// Uses queue-based pipeline for safe structural changes.
    /// </summary>
    public sealed partial class World
    {
        private readonly EntityManager _entities;
        private readonly ComponentManager _components;
        private readonly ArchetypeManager _archetypes;
        private readonly SystemManager _systems;
        private readonly TimeTracker _time = new();

        private bool _initialized;
        private bool _entitiesSpawned;

        public IHost Host { get; }
        public TimeTracker UpTime => _time;
        public int ArchetypeCount => _archetypes.ArchetypeCount;
        public int EntityCount => _entities.EntityCount;

        // Time tracking (self-contained, no engine dependency)
        public double TotalSeconds => _time.TotalSeconds;
        public long Microseconds => _time.Microseconds;

        // Tick performance tracking
        public double LastTickTimeMs { get; internal set; } = 0.0;

        public static World? Current { get; private set; }

        public SystemManager Systems => _systems;
        private bool _settingsInitialized = false;

        /// <summary>
        /// Event fired after a batch of entities has been created.
        /// Subscribers can perform initial setup/assignment on newly created entities.
        /// </summary>
        public event EntityBatchCreatedHandler? EntityBatchCreated;

        public World(IHost host)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Current = this;
            _archetypes = new ArchetypeManager(this);
            _entities = new EntityManager(this, _archetypes);
            _components = new ComponentManager(this);
            _systems = new SystemManager(this);
        }

        #region Initialization

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EventSink.InvokeWorldInitialized(this);
            EventSink.InvokeWorldStarted(this);
        }

        public void MarkEntitiesSpawned()
        {
            if (_entitiesSpawned) return;
            _entitiesSpawned = true;
            EventSink.InvokeWorldEntitiesSpawned(this);
        }

        #endregion

        #region Frame Pipeline

        public int tickCount { get; private set; } = 0;

        /// <summary>
        /// Main frame tick - processes all deferred operations and runs systems.
        /// </summary>
        public void Tick(double delta)
        {
            // CRITICAL: Advance time tracker FIRST
            _time.Advance((float)delta);
            // Initialize settings on first tick
            if (!_settingsInitialized)
            {
                _settingsInitialized = true;
                _systems.InitializeSettings();
            }

            // Phase 1: Process Entity Operations (destroy then create)
            _entities.ProcessQueues();

            // Phase 2: Component Operations
            _components.ProcessQueues();

#if DEBUG
            _archetypes.ValidateAll();
#endif

            // Phase 3: Run Systems
#if USE_DEBUG
            _systems.ProfileUpdateAll(this, delta);
#else
            _systems.Update(this, delta);
#endif

            //_systems.UpdateAutoSave((float)delta); //Save at the end of frame, just in case
            UpdateAutoSave((float)delta);

            tickCount++;
        }

        #endregion

        #region Entity Management (Delegates to EntityManager)

        public Entity CreateEntity() => _entities.Create();

        public Entity CreateEntityWithSignature(ComponentSignature signature) =>
            _entities.CreateWithSignature(signature);

        public bool TryGetEntityLocation(Entity entity, out Archetype archetype, out int slot) =>
            _entities.TryGetLocation(entity, out archetype, out slot);

        public void DestroyEntity(Entity e) => _entities.Destroy(e);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity e) => _entities.IsAlive(e);

        public void UpdateEntityLookup(uint entityIndex, Archetype arch, int slot) =>
            _entities.UpdateLookup(entityIndex, arch, slot);

        #endregion

        #region Enqueue APIs

        public void EnqueueDestroyEntity(Entity e) => _entities.EnqueueDestroy(e);
        public void EnqueueDestroyEntity(uint entityIndex) => _entities.EnqueueDestroy(entityIndex);

        public void EnqueueCreateEntity(Action<Entity>? builder = null) =>
            _entities.EnqueueCreate(builder);

        // System enqueue APIs - delegate to SystemManager
        public void EnqueueSystemCreate<T>() where T : BaseSystem => _systems.EnqueueRegister<T>();
        public void EnqueueSystemDestroy<T>() where T : BaseSystem => _systems.EnqueueUnregister<T>();
        public void EnqueueSystemEnable<T>() where T : BaseSystem => _systems.EnqueueEnable<T>();
        public void EnqueueSystemDisable<T>() where T : BaseSystem => _systems.EnqueueDisable<T>();

        public void EnqueueComponentRemove(uint entityIndex, int compId) =>
            _components.EnqueueRemove(entityIndex, compId);

        public void EnqueueComponentAdd(uint entityIndex, int compId, object boxedValue) =>
            _components.EnqueueAdd(entityIndex, compId, boxedValue);

        #endregion

        #region Events

        /// <summary>
        /// Fires the EntityBatchCreated event with the specified entities.
        /// Called by EntityManager and CommandBuffer after batch entity creation.
        /// </summary>
        internal void FireEntityBatchCreated(Entity[] entities, int startIndex, int count)
        {
            if (EntityBatchCreated != null && count > 0)
            {
                var args = new EntityBatchCreatedEventArgs(entities, startIndex, count);
                EntityBatchCreated(args);
            }
        }

        /// <summary>
        /// Fires the EntityBatchCreated event with the specified entities from a List.
        /// Convenience overload that avoids ToArray() at call site.
        /// </summary>
        /// <remarks>
        /// NOTE: This still allocates via ToArray() because EntityBatchCreatedEventArgs requires Entity[].
        /// For true zero-allocation, we would need to refactor EventArgs to accept ReadOnlySpan.
        /// This overload measures if there's any cache locality benefit from centralized ToArray().
        /// </remarks>
        internal void FireEntityBatchCreated(List<Entity> entities)
        {
            if (EntityBatchCreated != null && entities.Count > 0)
            {
                // Still needs ToArray() due to EventArgs signature
                // But test will show if calling it here vs at call site makes a difference
                var args = new EntityBatchCreatedEventArgs(entities.ToArray(), 0, entities.Count);
                EntityBatchCreated(args);
            }
        }

        /// <summary>
        /// EXPERIMENTAL: Fires EntityBatchCreated using CollectionsMarshal.AsSpan path.
        /// Tests if using Span as intermediate step provides any benefit.
        /// </summary>
        /// <remarks>
        /// Still allocates via span.ToArray() due to EventArgs requiring Entity[].
        /// This is essentially the same as the List overload above, but explicitly uses
        /// CollectionsMarshal.AsSpan as an intermediate step to measure any potential
        /// JIT optimization differences.
        /// </remarks>
        internal void FireEntityBatchCreatedSpanPath(List<Entity> entities)
        {
            if (EntityBatchCreated != null && entities.Count > 0)
            {
                // Use CollectionsMarshal.AsSpan to get direct access to List's internal buffer
                var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(entities);

                // Still need to convert to array for EventArgs (allocates)
                // But measures if the Span intermediate step has any benefit
                var array = span.ToArray();
                var args = new EntityBatchCreatedEventArgs(array, 0, entities.Count);
                EntityBatchCreated(args);
            }
        }

        #endregion

        #region Component Operations (Internal - called by ComponentManager)

        internal void AddComponentToEntityInternal(uint entityIndex, int componentTypeId, object boxedValue)
        {
            // Get entity location
            var tempEntity = new Entity(entityIndex, 1); // Use version 1 as placeholder since we only need index
            if (!_entities.TryGetLocation(tempEntity, out var sourceArch, out var sourceSlot))
            {
                // Entity was destroyed before deferred component addition was processed (valid race condition)
                return;
            }

            // Calculate new signature and get target archetype
            var newSig = sourceArch.Signature.Add(componentTypeId);
            var targetArch = _archetypes.GetOrCreate(newSig);

            // Move entity to target archetype
            _archetypes.MoveEntity(sourceArch, sourceSlot, targetArch, boxedValue);

            // Update entity lookup
            _entities.UpdateLookup(entityIndex, targetArch, targetArch.Count - 1);
        }

        internal void RemoveComponentFromEntityInternal(uint entityIndex, int componentTypeId)
        {
            // Get entity location
            var tempEntity = new Entity(entityIndex, 1); // Use version 1 as placeholder
            if (!_entities.TryGetLocation(tempEntity, out var oldArch, out var slot))
            {
                // Entity was destroyed before deferred component removal was processed (valid race condition)
                return;
            }

            if (!oldArch.HasComponent(componentTypeId))
                return;

            // Calculate new signature and get target archetype
            var newSig = oldArch.Signature.Remove(componentTypeId);
            var newArch = _archetypes.GetOrCreate(newSig);

            // Move entity to target archetype
            _archetypes.MoveEntity(oldArch, slot, newArch);

            // Update entity lookup
            _entities.UpdateLookup(entityIndex, newArch, newArch.Count - 1);
        }

        #endregion

        #region Archetype Queries (Delegates to ArchetypeManager)

        public IEnumerable<Archetype> QueryArchetypes(params Type[] componentTypes) =>
            _archetypes.Query(componentTypes);

        public IReadOnlyList<Archetype> GetArchetypes() =>
            _archetypes.GetAll();

        internal Archetype GetOrCreateArchetypeInternal(ComponentSignature sig) =>
            _archetypes.GetOrCreate(sig);

        #endregion
    }
}
