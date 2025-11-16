#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

using UltraSim;
using UltraSim.ECS.Threading;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Manages system registration, lifecycle, and parallel batch execution.
    /// Supports declarative dependencies via RequireSystemAttribute.
    /// </summary>
    public sealed partial class SystemManager
    {
        private readonly World _world;
        private readonly List<BaseSystem> _systems = new(32);
        private readonly Dictionary<Type, BaseSystem> _systemMap = new();
        private readonly Dictionary<BaseSystem, SystemAccess> _systemAccess = new();
        private List<List<BaseSystem>> _cachedBatches = new();
        private static readonly ConcurrentDictionary<Type, RequireSystemAttribute[]> _requireCache = new();

        // Deferred operation queues
        private readonly ConcurrentQueue<Type> _registerQueue = new();
        private readonly ConcurrentQueue<Type> _unregisterQueue = new();
        private readonly ConcurrentQueue<Type> _disableQueue = new();
        private readonly ConcurrentQueue<QueuedEnableRequest> _enableQueue = new();
        private readonly ConcurrentBag<HashSet<Type>> _typeSetPool = new();

        private const int MaxRetryAttempts = 3;

        public int Count => _systems.Count;
        public IReadOnlyList<BaseSystem> Systems => _systems;
        public ReadOnlySpan<BaseSystem> SystemsSpan => CollectionsMarshal.AsSpan(_systems);

        public SystemManager(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        private static RequireSystemAttribute[] GetRequiredSystemAttributes(Type type) =>
            _requireCache.GetOrAdd(type, static t =>
                (RequireSystemAttribute[])t.GetCustomAttributes(typeof(RequireSystemAttribute), inherit: true));

        #region Deferred Registration API

        public void EnqueueRegister<T>() where T : BaseSystem => EnqueueRegister(typeof(T));
        public void EnqueueRegister(Type type) => _registerQueue.Enqueue(type);

        public void EnqueueUnregister<T>() where T : BaseSystem => EnqueueUnregister(typeof(T));
        public void EnqueueUnregister(Type type) => _unregisterQueue.Enqueue(type);

        public void EnqueueEnable<T>() where T : BaseSystem => EnqueueEnable(typeof(T));
        public void EnqueueEnable(Type type) => _enableQueue.Enqueue(new QueuedEnableRequest(type, 0));

        public void EnqueueDisable<T>() where T : BaseSystem => EnqueueDisable(typeof(T));
        public void EnqueueDisable(Type type) => _disableQueue.Enqueue(type);

        #endregion

        #region Registration (with dependency resolution)

        private void Register(Type type)
        {
            if (!typeof(BaseSystem).IsAssignableFrom(type))
            {
                Logging.Log($"[SystemManager] Type {type.Name} is not a BaseSystem", LogSeverity.Error);
                return;
            }

            if (_systemMap.ContainsKey(type))
            {
                Logging.Log($"[SystemManager] System {type.Name} already registered", LogSeverity.Warning);
                return;
            }

            var sysInstance = (BaseSystem)Activator.CreateInstance(type)!;

            // CRITICAL: Add to map BEFORE dependency resolution to prevent infinite recursion on circular dependencies
            // If SystemA requires SystemB and SystemB requires SystemA, this prevents stack overflow
            _systems.Add(sysInstance);
            _systemMap[type] = sysInstance;

            // DEPENDENCY RESOLUTION via RequireSystemAttribute
            var requires = type.GetCustomAttributes(typeof(RequireSystemAttribute), inherit: true);
            foreach (RequireSystemAttribute req in requires)
            {
                var depType = Type.GetType(req.RequiredSystem);
                if (depType == null)
                {
                    Logging.Log($"[SystemManager] Required system '{req.RequiredSystem}' not found for {type.Name}", LogSeverity.Warning);
                    continue;
                }

                if (!_systemMap.TryGetValue(depType, out var dep))
                {
                    // Recursively register dependency first
                    // If dependency also requires this system, it will see it in _systemMap and return early
                    Logging.Log($"[SystemManager] Auto-registering dependency: {depType.Name} (required by {type.Name})");
                    Register(depType);
                    dep = _systemMap[depType];
                }

                EventSink.InvokeDependencyResolved(sysInstance, dep);
            }

            RegisterSystemTickScheduling(sysInstance);
            LoadSystemSettings(sysInstance);
            sysInstance.OnInitialize(_world);
            _systemAccess[sysInstance] = BuildSystemAccess(sysInstance);
            ComputeBatches();

            Logging.Log($"[SystemManager] Registered system: {type.Name}");
            EventSink.InvokeSystemRegistered(type);
        }

        private void Unregister(Type type)
        {
            if (_systemMap.TryGetValue(type, out var system))
            {
                if (system.IsEnabled)
                    DisableSystem(system);

                if (system.GetSettings() != null)
                    SaveSystemSettings(system);

                UnregisterSystemTickScheduling(system);

                _systemAccess.Remove(system);
                _systems.Remove(system);
                _systemMap.Remove(type);
                ComputeBatches();

                Logging.Log($"[SystemManager] Unregistered system: {type.Name}");
                EventSink.InvokeSystemUnregistered(type);
            }
        }

        #endregion

        #region Enable/Disable

        private void EnableSystem(BaseSystem system)
        {
            if (!_systemMap.ContainsKey(system.GetType()))
            {
                Logging.Log($"[SystemManager] Attempted to enable unregistered system {system.GetType().Name}", LogSeverity.Warning);
                return;
            }

            if (!system.IsEnabled)
            {
                system.Enable();
                Logging.Log($"[SystemManager] Enabled system: {system.Name}");
                EventSink.InvokeSystemEnabled(system);
            }
        }

        private void DisableSystem(BaseSystem system)
        {
            if (!_systemMap.ContainsKey(system.GetType()))
            {
                Logging.Log($"[SystemManager] Attempted to disable unregistered system {system.GetType().Name}", LogSeverity.Warning);
                return;
            }

            if (system.IsEnabled)
            {
                system.Disable();
                Logging.Log($"[SystemManager] Disabled system: {system.Name}");
                EventSink.InvokeSystemDisabled(system);
            }
        }

        public void EnableAllSystems()
        {
            foreach (var system in CollectionsMarshal.AsSpan(_systems))
                EnableSystem(system);
        }

        public void DisableAllSystems()
        {
            foreach (var system in CollectionsMarshal.AsSpan(_systems))
                DisableSystem(system);
        }

        #endregion

        #region Queue Processing

        public void ProcessQueues()
        {
            ProcessDisableQueue();
            ProcessUnregisterQueue();
            ProcessRegisterQueue();
            ProcessEnableQueue();
        }

        public void Update(World world, double delta)
        {
            ProcessQueues();
            UpdateTicked(world, delta);
            EventSink.InvokeWorldSystemsUpdated(world);
        }

        private void ProcessRegisterQueue()
        {
            if (_registerQueue.IsEmpty)
                return;

            var seen = RentTypeSet();
            try
            {
                while (_registerQueue.TryDequeue(out var type))
                {
                    if (!seen.Add(type))
                        continue;

                    try
                    {
                        Register(type);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[SystemManager] Error registering system {type.Name}: {ex}", LogSeverity.Error);
                    }
                }
            }
            finally
            {
                ReturnTypeSet(seen);
            }
        }

        private void ProcessUnregisterQueue()
        {
            if (_unregisterQueue.IsEmpty)
                return;

            var seen = RentTypeSet();
            try
            {
                while (_unregisterQueue.TryDequeue(out var type))
                {
                    if (!seen.Add(type))
                        continue;

                    try
                    {
                        Unregister(type);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[SystemManager] Error unregistering system {type.Name}: {ex}", LogSeverity.Error);
                    }
                }
            }
            finally
            {
                ReturnTypeSet(seen);
            }
        }

        private void ProcessEnableQueue()
        {
            if (_enableQueue.IsEmpty)
                return;

            var seen = RentTypeSet();
            var retryBuffer = new List<QueuedEnableRequest>();

            try
            {
                while (_enableQueue.TryDequeue(out var request))
                {
                    if (!seen.Add(request.Type))
                        continue;

                    try
                    {
                        if (_systemMap.TryGetValue(request.Type, out var system))
                        {
                            EnableSystem(system);
                        }
                        else
                        {
                            var nextAttempt = request.Attempts + 1;
                            if (nextAttempt >= MaxRetryAttempts)
                            {
                                Logging.Log($"[SystemManager] Dropping enable request for {request.Type.Name} after {nextAttempt} failed attempts", LogSeverity.Warning);
                            }
                            else
                            {
                                retryBuffer.Add(new QueuedEnableRequest(request.Type, nextAttempt));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[SystemManager] Error enabling system {request.Type.Name}: {ex}", LogSeverity.Error);
                    }
                }
            }
            finally
            {
                ReturnTypeSet(seen);
            }

            if (retryBuffer.Count > 0)
            {
                var retrySpan = CollectionsMarshal.AsSpan(retryBuffer);
                foreach (ref readonly var retry in retrySpan)
                {
                    _enableQueue.Enqueue(retry);
                }
            }
        }

        private void ProcessDisableQueue()
        {
            if (_disableQueue.IsEmpty)
                return;

            var seen = RentTypeSet();
            try
            {
                while (_disableQueue.TryDequeue(out var type))
                {
                    if (!seen.Add(type))
                        continue;

                    try
                    {
                        if (_systemMap.TryGetValue(type, out var system))
                        {
                            DisableSystem(system);
                        }
                        else
                        {
                            // System already unregistered
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"[SystemManager] Error disabling system {type.Name}: {ex}", LogSeverity.Error);
                    }
                }
            }
            finally
            {
                ReturnTypeSet(seen);
            }
        }

        #endregion

        #region Queries

        public BaseSystem? GetSystem<T>() where T : BaseSystem
        {
            return _systemMap.TryGetValue(typeof(T), out var system) ? system : null;
        }

        public BaseSystem? GetSystem(Type type)
        {
            return _systemMap.TryGetValue(type, out var system) ? system : null;
        }

        public bool HasSystem<T>() where T : BaseSystem
        {
            return _systemMap.ContainsKey(typeof(T));
        }

        public bool IsSystemEnabled<T>() where T : BaseSystem
        {
            return _systemMap.TryGetValue(typeof(T), out var system) && system.IsEnabled;
        }

        public IReadOnlyList<BaseSystem> GetAllSystems() => _systems;

        #endregion

        #region Update

        public void ProcessSystemUpdates(World world, double delta)
        {
            ParallelSystemScheduler.RunBatches(_cachedBatches, world, delta, null, null);
        }

        #endregion

        #region Batching

        private void ComputeBatches()
        {
            // STEP 1: Topological sort systems based on RequireSystem dependencies
            var sortedSystems = TopologicalSortSystems(_systems);

            // STEP 2: Batch systems for parallel execution (respecting sorted order)
            var batches = new List<List<BaseSystem>>();
            var sortedSpan = CollectionsMarshal.AsSpan(sortedSystems);

            foreach (ref var sys in sortedSpan)
            {
                if (sys.Rate == TickRate.Manual)
                    continue;

                bool placed = false;
                var batchSpan = CollectionsMarshal.AsSpan(batches);

                foreach (ref var batch in batchSpan)
                {
                    if (!ConflictsWithBatch(sys, batch))
                    {
                        batch.Add(sys);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                    batches.Add(new List<BaseSystem> { sys });
            }

            _cachedBatches = batches;

#if USE_DEBUG
            Logging.Log($"[SystemManager] ComputeBatches: Created {batches.Count} batches from {_systems.Count} systems");
            for (int i = 0; i < batches.Count; i++)
            {
                var systemNames = string.Join(", ", batches[i].ConvertAll(s => s.Name));
                Logging.Log($"[SystemManager]   Batch {i}: [{systemNames}]");
            }
#endif
        }

        /// <summary>
        /// Topologically sorts systems based on RequireSystem dependencies.
        /// Systems that are required by others run first.
        /// OPTIMIZATION: Early exit if no systems have RequireSystem attributes.
        /// </summary>
        private List<BaseSystem> TopologicalSortSystems(List<BaseSystem> systems)
        {
            // OPTIMIZATION: Check if ANY system has dependencies
            bool hasDependencies = false;
            foreach (var system in systems)
            {
                if (GetRequiredSystemAttributes(system.GetType()).Length > 0)
                {
                    hasDependencies = true;
                    break;
                }
            }

            // If no dependencies, return original list (no sorting needed)
            if (!hasDependencies)
                return systems;

            var sorted = new List<BaseSystem>(systems.Count);
            var visited = new HashSet<BaseSystem>();
            var visiting = new HashSet<BaseSystem>();

            // Build dependency map: system -> list of systems it depends on
            var dependencies = new Dictionary<BaseSystem, List<BaseSystem>>(systems.Count);
            foreach (var system in systems)
            {
                dependencies[system] = new List<BaseSystem>();

                var requires = GetRequiredSystemAttributes(system.GetType());
                foreach (var req in requires)
                {
                    var depType = Type.GetType(req.RequiredSystem);
                    if (depType != null && _systemMap.TryGetValue(depType, out var depSystem))
                    {
                        dependencies[system].Add(depSystem);
                    }
                }
            }

            // Depth-first search for topological sort
            foreach (var system in systems)
            {
                if (!visited.Contains(system))
                    TopologicalSortVisit(system, dependencies, visited, visiting, sorted);
            }

            return sorted;
        }

        private void TopologicalSortVisit(
            BaseSystem system,
            Dictionary<BaseSystem, List<BaseSystem>> dependencies,
            HashSet<BaseSystem> visited,
            HashSet<BaseSystem> visiting,
            List<BaseSystem> sorted)
        {
            if (visited.Contains(system))
                return;

            if (visiting.Contains(system))
            {
                Logging.Log($"[SystemManager] Circular dependency detected involving system: {system.Name}", LogSeverity.Error);
                return;
            }

            visiting.Add(system);

            // Visit all dependencies first (they must run before this system)
            foreach (var dep in dependencies[system])
            {
                TopologicalSortVisit(dep, dependencies, visited, visiting, sorted);
            }

            visiting.Remove(system);
            visited.Add(system);
            sorted.Add(system); // Add to sorted list AFTER dependencies
        }

        private bool ConflictsWithBatch(BaseSystem s, List<BaseSystem> batch)
        {
            if (batch.Count == 0) return false;

            var access = _systemAccess[s];
            var batchSpan = CollectionsMarshal.AsSpan(batch);

            foreach (ref readonly var other in batchSpan)
            {
                var otherAccess = _systemAccess[other];

                if (HasOverlap(access.WriteSet, otherAccess.WriteLookup) ||
                    HasOverlap(access.WriteSet, otherAccess.ReadLookup))
                    return true;

                if (HasOverlap(otherAccess.WriteSet, access.ReadLookup))
                    return true;
            }

            return false;
        }

        #endregion

        #region Settings Management

        public void InitializeSettings()
        {
            World.Paths.EnsureDirectoriesExist();
        }

        public void LoadSystemSettings(BaseSystem system)
        {
            system.LoadSettings();
        }

        public void SaveSystemSettings(BaseSystem system)
        {
            system.SaveSettings();
        }

        public void SaveAllSettings()
        {
            int savedCount = 0;
            foreach (var system in _systems)
            {
                if (system.GetSettings() != null)
                {
                    system.SaveSettings();
                    savedCount++;
                }
            }

#if USE_DEBUG
            Logging.Log($"[SystemManager] Saved settings for {savedCount} systems");
#endif
        }

        #endregion

        private readonly record struct QueuedEnableRequest(Type Type, int Attempts);

        private sealed class SystemAccess
        {
            public Type[] ReadSet = Array.Empty<Type>();
            public Type[] WriteSet = Array.Empty<Type>();
            public HashSet<Type> ReadLookup = new();
            public HashSet<Type> WriteLookup = new();
        }

        private SystemAccess BuildSystemAccess(BaseSystem system)
        {
            var access = new SystemAccess
            {
                ReadSet = system.ReadSet ?? Array.Empty<Type>(),
                WriteSet = system.WriteSet ?? Array.Empty<Type>()
            };

            foreach (var type in access.ReadSet)
                access.ReadLookup.Add(type);
            foreach (var type in access.WriteSet)
                access.WriteLookup.Add(type);

            return access;
        }

        private static bool HasOverlap(Type[] source, HashSet<Type> lookup)
        {
            if (source.Length == 0 || lookup.Count == 0)
                return false;

            foreach (var type in source)
            {
                if (lookup.Contains(type))
                    return true;
            }

            return false;
        }

        private HashSet<Type> RentTypeSet()
        {
            if (_typeSetPool.TryTake(out var set))
            {
                set.Clear();
                return set;
            }

            return new HashSet<Type>();
        }

        private void ReturnTypeSet(HashSet<Type> set)
        {
            set.Clear();
            _typeSetPool.Add(set);
        }
    }
}
