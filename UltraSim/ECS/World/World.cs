#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using UltraSim;
using UltraSim.Logging;
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

        // Events
        public event Action? OnInitialized;
        public event Action? OnEntitiesSpawned;
        public event Action? OnSystemsUpdated;
        public event Action? OnFrameComplete;

        // Deferred operation queues
        private readonly ConcurrentQueue<BaseSystem> _systemCreateQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemDestroyQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemEnableQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemDisableQueue = new();
        private readonly ConcurrentQueue<Action> _renderQueue = new();

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

        public World()
        {
            Current = this;
            _archetypes = new ArchetypeManager();
            _entities = new EntityManager(this, _archetypes);
            _components = new ComponentManager(this);
            _systems = new SystemManager();
        }

        #region Initialization

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            OnInitialized?.Invoke();
        }

        public void MarkEntitiesSpawned()
        {
            if (_entitiesSpawned) return;
            _entitiesSpawned = true;
            OnEntitiesSpawned?.Invoke();
        }

        #endregion

        #region Frame Pipeline

        int tickCount = 0;
        bool printTickSchedule = true;

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

            // Phase 1: Disable & Destroy
            ProcessSystemDisableQueue();
            ProcessSystemDestructionQueue();

            // Phase 1.5: Process Entity Operations (destroy then create)
            _entities.ProcessQueues();

            // Phase 2: Create & Enable
            ProcessSystemCreationQueue();
            ProcessSystemEnableQueue();

            // Phase 3: Component Operations
            _components.ProcessQueues();

#if DEBUG
            ValidateArchetypes();
#endif

            // Phase 4: Run Systems
#if USE_DEBUG
            _systems.ProfileUpdateAll(this, delta);
#else
            _systems.UpdateTicked(this, delta);
#endif

            // Phase 5: Rendering
            ProcessRenderQueue();

            //_systems.UpdateAutoSave((float)delta); //Save at the end of frame, just in case
            UpdateAutoSave((float)delta);

            // Phase 6: Events
            OnSystemsUpdated?.Invoke();
            OnFrameComplete?.Invoke();
        }

        #endregion

        #region Queue Processing

        private void ProcessSystemDisableQueue()
        {
            while (_systemDisableQueue.TryDequeue(out var s))
            {
                try { s.Disable(); }
                catch (Exception ex) { Logger.Log(($"Error disabling system {s.Name}: {ex}, World"), LogSeverity.Error); }
            }
        }

        private void ProcessSystemDestructionQueue()
        {
            while (_systemDestroyQueue.TryDequeue(out var s))
            {
                try
                {
                    if (s.IsEnabled) s.Disable();
                    _systems.Unregister(s);
                }
                catch (Exception ex) { Logger.Log($"[World] Error destroying system {s.Name}: {ex}", LogSeverity.Error); }
            }
        }

        private void ProcessSystemCreationQueue()
        {
            while (_systemCreateQueue.TryDequeue(out var s))
            {
                try
                {
                    _systems.Register(s);
                    s.OnInitialize(this);
                }
                catch (Exception ex) { Logger.Log($"[World] Error creating system {s.Name}: {ex}", LogSeverity.Error); }
            }
        }

        private void ProcessSystemEnableQueue()
        {
            while (_systemEnableQueue.TryDequeue(out var s))
            {
                try { s.Enable(); }
                catch (Exception ex) { Logger.Log($"[World] Error enabling system {s.Name}: {ex}", LogSeverity.Error); }
            }
        }

        private void ProcessRenderQueue()
        {
            while (_renderQueue.TryDequeue(out var action))
            {
                try { action.Invoke(); }
                catch (Exception ex) { Logger.Log($"[World] Render action error: {ex}", LogSeverity.Error); }
            }
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

        public void UpdateEntityLookup(int entityIndex, Archetype arch, int slot) =>
            _entities.UpdateLookup(entityIndex, arch, slot);

        #endregion

        #region Enqueue APIs

        public void EnqueueDestroyEntity(Entity e) => _entities.EnqueueDestroy(e);

        public void EnqueueCreateEntity(Action<Entity>? builder = null) =>
            _entities.EnqueueCreate(builder);

        public void EnqueueSystemCreate(BaseSystem s) => _systemCreateQueue.Enqueue(s);

        public void EnqueueSystemDestroy(BaseSystem s) => _systemDestroyQueue.Enqueue(s);

        public void EnqueueSystemDestroy<T>() where T : BaseSystem
        {
            var sys = FindSystemInQueuesOrRegistry<T>();

            if (sys == null)
            {
                Logger.Log($"[World] ÃƒÂ¢Ã…Â¡Ã‚Â  System {typeof(T).Name} not found", LogSeverity.Error);
                return;
            }

            _systemDestroyQueue.Enqueue(sys);
        }

        public void EnqueueSystemEnable(BaseSystem s) => _systemEnableQueue.Enqueue(s);

        public void EnqueueSystemEnable<T>() where T : BaseSystem
        {
            var sys = FindSystemInQueuesOrRegistry<T>();

            if (sys == null)
            {
                Logger.Log($"[World] ÃƒÂ¢Ã…Â¡Ã‚Â  System {typeof(T).Name} not found", LogSeverity.Error);
                return;
            }

            _systemEnableQueue.Enqueue(sys);

        }

        public void EnqueueSystemDisable(BaseSystem s) => _systemDisableQueue.Enqueue(s);

        public void EnqueueSystemDisable<T>() where T : BaseSystem
        {

            var sys = FindSystemInQueuesOrRegistry<T>();

            if (sys == null)
            {
                Logger.Log($"[World] ÃƒÂ¢Ã…Â¡Ã‚Â  System {typeof(T).Name} not found", LogSeverity.Error);
                return;
            }

            _systemDisableQueue.Enqueue(sys);
        }

        public void EnqueueComponentRemove(int entityIndex, int compId) =>
            _components.EnqueueRemove(entityIndex, compId);

        public void EnqueueComponentAdd(int entityIndex, int compId, object boxedValue) =>
            _components.EnqueueAdd(entityIndex, compId, boxedValue);

        public void EnqueueRenderAction(Action action) => _renderQueue.Enqueue(action);

        #endregion

        #region Component Operations (Internal - called by ComponentManager)

        internal void AddComponentToEntityInternal(int entityIndex, int componentTypeId, object boxedValue)
        {
            // Get entity location
            var tempEntity = new Entity(entityIndex, 0);
            if (!_entities.TryGetLocation(tempEntity, out var sourceArch, out var sourceSlot))
            {
                Logger.Log($"[World] Tried to add component to invalid entity {entityIndex}", LogSeverity.Error);
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

        internal void RemoveComponentFromEntityInternal(int entityIndex, int componentTypeId)
        {
            // Get entity location
            var tempEntity = new Entity(entityIndex, 0);
            if (!_entities.TryGetLocation(tempEntity, out var oldArch, out var slot))
            {
                Logger.Log($"[World] Tried to remove component from invalid entity {entityIndex}", LogSeverity.Error);
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

        public IEnumerable<Archetype> Query(params Type[] componentTypes) =>
            _archetypes.Query(componentTypes);

        public IReadOnlyList<Archetype> GetArchetypes() =>
            _archetypes.GetAll();

        public Archetype GetOrCreateArchetype(ComponentSignature sig) =>
            _archetypes.GetOrCreate(sig);

        #endregion

        #region System Queries

        private BaseSystem? FindSystemInQueuesOrRegistry<T>() where T : BaseSystem
        {
            var sysType = typeof(T);

            // Check if already registered
            var sys = _systems.GetSystem(sysType);
            if (sys != null) return sys;

            // Check creation queue
            foreach (var queuedSys in _systemCreateQueue)
            {
                if (queuedSys.GetType() == sysType)
                    return queuedSys;
            }

            return null;
        }

        #endregion

        #region Validation

        private void ValidateArchetypes()
        {
            _archetypes.ValidateAll();
        }

        #endregion
    }
}