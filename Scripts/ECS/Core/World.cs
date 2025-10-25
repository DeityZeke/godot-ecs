#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Core ECS World - manages entities, archetypes, and deferred operations.
    /// Uses queue-based pipeline for safe structural changes.
    /// </summary>
    public sealed class World
    {
        private readonly List<Archetype> _archetypes = new();
        private readonly Dictionary<int, (int archetypeIdx, int slot)> _entityLookup = new();
        private readonly Stack<int> _freeIndices = new();
        private readonly List<int> _entityVersions = new();
        private readonly SystemManager _systems;

        private bool _initialized;
        private bool _entitiesSpawned;

        // Events
        public event Action? OnInitialized;
        public event Action? OnEntitiesSpawned;
        public event Action? OnSystemsUpdated;
        public event Action? OnFrameComplete;

        // Deferred operation queues
        private readonly ConcurrentQueue<int> _entityDestroyQueue = new();
        private readonly ConcurrentQueue<Action<Entity>> _entityCreateQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemCreateQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemDestroyQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemEnableQueue = new();
        private readonly ConcurrentQueue<BaseSystem> _systemDisableQueue = new();
        private readonly ConcurrentQueue<(int entityIndex, int componentTypeId)> _componentRemoveQueue = new();
        private readonly ConcurrentQueue<(int entityIndex, int componentTypeId, object boxedValue)> _componentAddQueue = new();
        private readonly ConcurrentQueue<Action> _renderQueue = new();

        public static World? Current { get; private set; }

        public SystemManager Systems => _systems;

        public World()
        {
            Current = this;
            _systems = new SystemManager();
            _archetypes.Add(new Archetype()); // Empty archetype
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
            // Phase 1: Disable & Destroy
            ProcessSystemDisableQueue();
            ProcessSystemDestructionQueue();
            ProcessEntityDestructionQueue();

            // Phase 2: Create & Enable
            ProcessSystemCreationQueue();
            ProcessSystemEnableQueue();
            ProcessEntityCreationQueue();

            // Phase 3: Component Operations
            ProcessComponentRemovalQueue();
            ProcessComponentAddQueue();

#if DEBUG
            ValidateArchetypes();
#endif

            // Phase 4: Run Systems
#if USE_DEBUG
            _systems.ProfileUpdateAll(this, delta);
#else
            //_systems.ProcessSystemUpdates(this, delta);
            _systems.UpdateTicked(this, delta);
#endif

            // Phase 5: Apply Deferred Changes
            ApplyDeferredChanges();

            // Phase 6: Rendering
            ProcessRenderQueue();

            if (tickCount > 300 && printTickSchedule)
            {
                GD.Print(_systems.GetTickSchedulingInfo());
                printTickSchedule = false;
            }

            tickCount++;

            // Phase 7: Events
            OnSystemsUpdated?.Invoke();
            OnFrameComplete?.Invoke();
        }

        #endregion

        #region Queue Processing

        private void ProcessEntityDestructionQueue()
        {
            while (_entityDestroyQueue.TryDequeue(out var idx))
            {
                var e = new Entity(idx, _entityVersions[idx]);
                DestroyEntity(e);
            }
        }

        private void ProcessEntityCreationQueue()
        {
            while (_entityCreateQueue.TryDequeue(out var builder))
            {
                var e = CreateEntity();
                try { builder?.Invoke(e); }
                catch (Exception ex) { GD.PrintErr($"[World] Entity builder exception: {ex}"); }
            }
        }

        private void ProcessSystemDisableQueue()
        {
            while (_systemDisableQueue.TryDequeue(out var s))
            {
                try { s.Disable(); }
                catch (Exception ex) { GD.PrintErr($"[World] Error disabling system {s.Name}: {ex}"); }
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
                catch (Exception ex) { GD.PrintErr($"[World] Error destroying system {s.Name}: {ex}"); }
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
                catch (Exception ex) { GD.PrintErr($"[World] Error creating system {s.Name}: {ex}"); }
            }
        }

        private void ProcessSystemEnableQueue()
        {
            while (_systemEnableQueue.TryDequeue(out var s))
            {
                try { s.Enable(); }
                catch (Exception ex) { GD.PrintErr($"[World] Error enabling system {s.Name}: {ex}"); }
            }
        }

        private void ProcessComponentRemovalQueue()
        {
            while (_componentRemoveQueue.TryDequeue(out var component))
            {
                RemoveComponentFromEntity(component.entityIndex, component.componentTypeId);
            }
        }

        private void ProcessComponentAddQueue()
        {
            while (_componentAddQueue.TryDequeue(out var component))
            {
                AddComponentToEntity(component.entityIndex, component.componentTypeId, component.boxedValue);
            }
        }

        private void ProcessRenderQueue()
        {
            while (_renderQueue.TryDequeue(out var action))
            {
                try { action.Invoke(); }
                catch (Exception ex) { GD.PrintErr($"[World] Render action error: {ex}"); }
            }
        }

        private void ApplyDeferredChanges()
        {
            // TODO: Integrate command buffers here
            // MergePhase(); CompressPhase(); ApplyValueChanges(); ApplyStructuralChanges();
        }

        #endregion

        #region Entity Management

        public Entity CreateEntity()
        {
            int idx = _freeIndices.Count > 0 ? _freeIndices.Pop() : _entityVersions.Count;
            if (idx == _entityVersions.Count)
                _entityVersions.Add(0);

            var e = new Entity(idx, _entityVersions[idx]);
            var baseArch = _archetypes[0];
            baseArch.AddEntity(e);
            _entityLookup[idx] = (0, baseArch.Count - 1);
            return e;
        }

        /// <summary>
        /// Creates an entity directly in an archetype with the given signature.
        /// This is THE KEY to avoiding archetype thrashing - entity starts in correct archetype!
        /// </summary>
        public Entity CreateEntityWithSignature(ComponentSignature signature)
        {
            // Allocate entity ID
            int idx = _freeIndices.Count > 0 ? _freeIndices.Pop() : _entityVersions.Count;
            if (idx == _entityVersions.Count)
                _entityVersions.Add(0);

            var entity = new Entity(idx, _entityVersions[idx]);

            // Find or create archetype with this signature
            var archetype = GetOrCreateArchetype(signature);

            // Ensure component lists exist for all types in signature
            foreach (var typeId in signature.GetIds())
            {
                var type = ComponentTypeRegistry.GetTypeById(typeId);
                var method = typeof(Archetype).GetMethod(nameof(Archetype.EnsureComponentList))
                                              ?.MakeGenericMethod(type);
                method?.Invoke(archetype, new object[] { typeId });
            }

            // Add entity to archetype
            archetype.AddEntity(entity);

            // Update lookup
            _entityLookup[idx] = (_archetypes.IndexOf(archetype), archetype.Count - 1);

            return entity;
        }

        /// <summary>
        /// Gets the archetype and slot for an entity.
        /// Used by command buffers to set component values after creation.
        /// </summary>
        public bool TryGetEntityLocation(Entity entity, out Archetype archetype, out int slot)
        {
            if (!_entityLookup.TryGetValue(entity.Index, out var loc))
            {
                archetype = null!;
                slot = -1;
                return false;
            }

            archetype = _archetypes[loc.archetypeIdx];
            slot = loc.slot;
            return true;
        }

        public void DestroyEntity(Entity e)
        {
            if (!_entityLookup.TryGetValue(e.Index, out var loc))
                return;

            var arch = _archetypes[loc.archetypeIdx];
            arch.RemoveAtSwap(loc.slot);
            _entityLookup.Remove(e.Index);
            _freeIndices.Push(e.Index);
            _entityVersions[e.Index]++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity e)
        {
            return e.Index >= 0 &&
                   e.Index < _entityVersions.Count &&
                   _entityVersions[e.Index] == e.Version &&
                   _entityLookup.ContainsKey(e.Index);
        }

        public void UpdateEntityLookup(int entityIndex, Archetype arch, int slot)
        {
            _entityLookup[entityIndex] = (_archetypes.IndexOf(arch), slot);
        }

        #endregion

        #region Enqueue APIs

        public void EnqueueDestroyEntity(Entity e)
        {
            if (IsEntityValid(e))
                _entityDestroyQueue.Enqueue(e.Index);
        }

        public void EnqueueCreateEntity(Action<Entity>? builder = null)
        {
            _entityCreateQueue.Enqueue(builder ?? (_ => { }));
        }

        public void EnqueueSystemCreate(BaseSystem s) => _systemCreateQueue.Enqueue(s);

        public void EnqueueSystemCreate<T>() where T : BaseSystem
        {
            var sys = FindSystemInQueuesOrRegistry<T>();

            if (sys == null)
            {
                GD.PrintErr($"[World] ⚠ System {typeof(T).Name} not found");
                return;
            }

            _systemCreateQueue.Enqueue(sys);
        }

        public void EnqueueSystemDestroy(BaseSystem s) => _systemDestroyQueue.Enqueue(s);

        public void EnqueueSystemDestroy<T>() where T : BaseSystem
        {
            var sys = FindSystemInQueuesOrRegistry<T>();

            if (sys == null)
            {
                GD.PrintErr($"[World] ⚠ System {typeof(T).Name} not found");
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
                GD.PrintErr($"[World] ⚠ System {typeof(T).Name} not found");
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
                GD.PrintErr($"[World] ⚠ System {typeof(T).Name} not found");
                return;
            }

            _systemEnableQueue.Enqueue(sys);
        }

        public void EnqueueComponentRemove(int entityIndex, int compId) =>
            _componentRemoveQueue.Enqueue((entityIndex, compId));

        public void EnqueueComponentAdd(int entityIndex, int compId, object boxedValue) =>
            _componentAddQueue.Enqueue((entityIndex, compId, boxedValue));

        public void EnqueueRenderAction(Action action) => _renderQueue.Enqueue(action);

        #endregion

        #region Component Operations

        private void AddComponentToEntity(int entityIndex, int componentTypeId, object boxedValue)
        {
            if (!_entityLookup.TryGetValue(entityIndex, out var loc))
            {
                GD.PrintErr($"[World] Tried to add component to invalid entity {entityIndex}");
                return;
            }

            var sourceArch = _archetypes[loc.archetypeIdx];
            var sourceSlot = loc.slot;

            var newSig = sourceArch.Signature.Add(componentTypeId);

            // Find or create target archetype
            Archetype? targetArch = null;
            //foreach (var arch in _archetypes)
            foreach (ref var arch in CollectionsMarshal.AsSpan(_archetypes))
            {
                if (arch.Signature.Equals(newSig))
                {
                    targetArch = arch;
                    break;
                }
            }

            if (targetArch == null)
            {
                targetArch = new Archetype(newSig);

                // Ensure component lists for all IDs in signature
                foreach (var id in newSig.GetIds())
                {
                    var type = ComponentTypeRegistry.GetTypeById(id);
                    var method = typeof(Archetype).GetMethod(nameof(Archetype.EnsureComponentList))
                                                  ?.MakeGenericMethod(type);
                    method?.Invoke(targetArch, new object[] { id });
                }

                _archetypes.Add(targetArch);
            }

            // Move entity to target archetype
            sourceArch.MoveEntityTo(sourceSlot, targetArch, boxedValue);

            // Update lookup
            _entityLookup[entityIndex] = (_archetypes.IndexOf(targetArch), targetArch.Count - 1);
        }

        private void RemoveComponentFromEntity(int entityIndex, int componentTypeId)
        {
            if (!_entityLookup.TryGetValue(entityIndex, out var loc))
            {
                GD.PrintErr($"[World] Tried to remove component from invalid entity {entityIndex}");
                return;
            }

            var oldArch = _archetypes[loc.archetypeIdx];
            var slot = loc.slot;

            if (!oldArch.HasComponent(componentTypeId))
                return;

            var newSig = oldArch.Signature.Remove(componentTypeId);
            var newArch = GetOrCreateArchetype(newSig);

            oldArch.MoveEntityTo(slot, newArch);
            _entityLookup[entityIndex] = (_archetypes.IndexOf(newArch), newArch.Count - 1);
        }

        public Archetype GetOrCreateArchetype(ComponentSignature sig)
        {
            //foreach (var arch in _archetypes)
            foreach (ref var arch in CollectionsMarshal.AsSpan(_archetypes))
            {
                if (arch.Signature.Equals(sig))
                    return arch;
            }

            var newArch = new Archetype(sig);
            _archetypes.Add(newArch);
            return newArch;
        }

        #endregion

        #region Queries

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

        public IEnumerable<Archetype> Query(params Type[] componentTypes)
        {
            foreach (var arch in _archetypes)
            {
                if (arch.Matches(componentTypes))
                    yield return arch;
            }
        }

        public IReadOnlyList<Archetype> GetArchetypes() => _archetypes;

        #endregion

        #region Validation

        private void ValidateArchetypes()
        {
            foreach (var arch in _archetypes)
            {
                foreach (var error in arch.DebugValidate())
                    GD.PrintErr($"[World Validate] {error}");
            }
        }

        #endregion
    }
}