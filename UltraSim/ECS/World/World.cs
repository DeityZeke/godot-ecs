#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UltraSim;
using UltraSim.ECS.Systems;

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

            // Phase 2.5: Compact archetypes to remove dead slots before systems run
            _archetypes.CompactAll();

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

        /// <summary>
        /// Creates an EntityBuilder for fluent component definition.
        /// Use this to define all components before enqueueing creation to avoid archetype thrashing.
        /// </summary>
        /// <example>
        /// var builder = world.CreateEntityBuilder()
        ///     .Add(new Position { X = 0, Y = 0, Z = 0 })
        ///     .Add(new Velocity { X = 1, Y = 0, Z = 0 });
        /// world.EnqueueCreateEntity(builder);
        /// </example>
        public EntityBuilder CreateEntityBuilder()
        {
            var componentList = _entities.GetComponentList();
            return new EntityBuilder(componentList);
        }

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

        /// <summary>
        /// Enqueues an entity for creation with all components pre-defined in EntityBuilder.
        /// This is the PREFERRED method for creating entities with multiple components.
        /// Avoids archetype thrashing by creating the entity directly in the final archetype.
        /// </summary>
        /// <example>
        /// var builder = CreateEntityBuilder()
        ///     .Add(new Position { X = 0, Y = 0, Z = 0 })
        ///     .Add(new Velocity { X = 1, Y = 0, Z = 0 });
        /// world.EnqueueCreateEntity(builder);
        /// </example>
        public void EnqueueCreateEntity(EntityBuilder builder) =>
            _entities.EnqueueCreate(builder);

        // System enqueue APIs - delegate to SystemManager
        public void EnqueueSystemCreate<T>() where T : BaseSystem => _systems.EnqueueRegister<T>();
        public void EnqueueSystemDestroy<T>() where T : BaseSystem => _systems.EnqueueUnregister<T>();
        public void EnqueueSystemEnable<T>() where T : BaseSystem => _systems.EnqueueEnable<T>();
        public void EnqueueSystemDisable<T>() where T : BaseSystem => _systems.EnqueueDisable<T>();

        /// <summary>
        /// Enqueues a component removal for deferred processing.
        /// Stores full entity (index + version) for proper validation.
        /// </summary>
        public void EnqueueComponentRemove(Entity entity, int compId) =>
            _components.EnqueueRemove(entity, compId);

        /// <summary>
        /// Enqueues a component addition for deferred processing.
        /// Stores full entity (index + version) for proper validation.
        /// </summary>
        public void EnqueueComponentAdd(Entity entity, int compId, object boxedValue) =>
            _components.EnqueueAdd(entity, compId, boxedValue);

        #endregion

        #region Events

        /// <summary>
        /// Fires the EntityBatchCreated event with the specified entities.
        /// Called by EntityManager and CommandBuffer after batch entity creation.
        /// </summary>
        internal void FireEntityBatchCreated(Entity[] entities, int startIndex, int count)
        {
            if (count > 0)
            {
                var args = new EntityBatchCreatedEventArgs(entities, startIndex, count);
                EventSink.InvokeEntityBatchCreated(args);
            }
        }

        /// <summary>
        /// Fires the EntityBatchCreated event with the specified entities from a List.
        /// ZERO ALLOCATION - Passes List directly to EventArgs without ToArray().
        /// </summary>
        /// <remarks>
        /// EventArgs now stores the List internally and uses CollectionsMarshal.AsSpan
        /// for zero-allocation iteration via GetSpan().
        /// Subscribers should use args.GetSpan() for best performance.
        /// </remarks>
        internal void FireEntityBatchCreated(List<Entity> entities)
        {
            if (entities.Count > 0)
            {
                // TRUE zero-allocation: Pass List directly!
                var args = new EntityBatchCreatedEventArgs(entities);
                EventSink.InvokeEntityBatchCreated(args);
            }
        }

        /// <summary>
        /// DEPRECATED: Use FireEntityBatchCreated(List&lt;Entity&gt;) instead.
        /// This overload is kept for testing purposes only.
        /// </summary>
        internal void FireEntityBatchCreatedSpanPath(List<Entity> entities)
        {
            // Just call the main overload - no difference anymore
            FireEntityBatchCreated(entities);
        }

        /// <summary>
        /// Fires the EntityBatchDestroyed event with the specified entities.
        /// Called by EntityManager after batch entity destruction.
        /// </summary>
        internal void FireEntityBatchDestroyed(Entity[] entities, int startIndex, int count)
        {
            if (count > 0)
            {
                var args = new EntityBatchDestroyedEventArgs(entities, startIndex, count);
                EventSink.InvokeEntityBatchDestroyed(args);
            }
        }

        /// <summary>
        /// Fires the EntityBatchDestroyed event with the specified entities from a List.
        /// ZERO ALLOCATION - Passes List directly to EventArgs without ToArray().
        /// Mirrors FireEntityBatchCreated for consistency.
        /// </summary>
        internal void FireEntityBatchDestroyed(List<Entity> entities)
        {
            if (entities.Count > 0)
            {
                // TRUE zero-allocation: Pass List directly!
                var args = new EntityBatchDestroyedEventArgs(entities);
                EventSink.InvokeEntityBatchDestroyed(args);
            }
        }

        #endregion

        #region Component Operations (Internal - called by ComponentManager)

        /// <summary>
        /// Adds a component to an entity (called during queue processing).
        /// OPTION 2: Validates entity version before applying operation.
        /// If entity was destroyed/recycled, operation is safely skipped.
        /// </summary>
        internal void AddComponentToEntityInternal(Entity entity, int componentTypeId, object boxedValue)
        {
            // OPTION 2: Proper version validation - uses entity.Version from queue
            if (!_entities.IsAlive(entity))
            {
                // Entity was destroyed/recycled before queue processing - skip operation
                // This is NOT an error - it's a valid race condition
                return;
            }

            // Get entity location (we know it's alive, so this should succeed)
            if (!_entities.TryGetLocation(entity, out var sourceArch, out var sourceSlot))
            {
                // This shouldn't happen if IsAlive returned true, but be defensive
                return;
            }

            // Calculate new signature and get target archetype
            var newSig = sourceArch.Signature.Add(componentTypeId);
            var targetArch = _archetypes.GetOrCreate(newSig);

            // Move entity to target archetype
            _archetypes.MoveEntity(sourceArch, sourceSlot, targetArch, boxedValue);

            // Update entity lookup
            _entities.UpdateLookup(entity.Index, targetArch, targetArch.Count - 1);
        }

        /// <summary>
        /// Removes a component from an entity (called during queue processing).
        /// OPTION 2: Validates entity version before applying operation.
        /// If entity was destroyed/recycled, operation is safely skipped.
        /// </summary>
        internal void RemoveComponentFromEntityInternal(Entity entity, int componentTypeId)
        {
            // OPTION 2: Proper version validation - uses entity.Version from queue
            if (!_entities.IsAlive(entity))
            {
                // Entity was destroyed/recycled before queue processing - skip operation
                // This is NOT an error - it's a valid race condition
                return;
            }

            // Get entity location (we know it's alive, so this should succeed)
            if (!_entities.TryGetLocation(entity, out var oldArch, out var slot))
            {
                // This shouldn't happen if IsAlive returned true, but be defensive
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
            _entities.UpdateLookup(entity.Index, newArch, newArch.Count - 1);
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
