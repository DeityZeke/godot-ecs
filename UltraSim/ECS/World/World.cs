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

            tickCount++;
        }

        #endregion

        #region Entity Management (Delegates to EntityManager)

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

        /// <summary>
        /// Clears all pending entity creation queues.
        /// Use this when clearing the world to prevent zombie entities from pending creations.
        /// </summary>
        public void ClearEntityCreationQueues() =>
            _entities.ClearCreationQueues();

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

        public void EnqueueComponentAdd<T>(Entity entity, int compId, T value) =>
            _components.EnqueueAdd(entity, compId, value);

        #endregion

        #region Archetype Queries (Delegates to ArchetypeManager)

        public ArchetypeManager.QueryResult QueryArchetypes(params Type[] componentTypes) =>
            _archetypes.Query(componentTypes);

        public IReadOnlyList<Archetype> GetArchetypes() =>
            _archetypes.GetAll();

        internal Archetype GetOrCreateArchetypeInternal(ComponentSignature sig) =>
            _archetypes.GetOrCreate(sig);

        #endregion
    }
}
