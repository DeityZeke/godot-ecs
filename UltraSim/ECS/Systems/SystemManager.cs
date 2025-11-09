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
        private List<List<BaseSystem>> _cachedBatches = new();

        // Deferred operation queues (Type, retry count)
        private readonly ConcurrentDictionary<Type, int> _registerQueue = new();
        private readonly ConcurrentDictionary<Type, int> _unregisterQueue = new();
        private readonly ConcurrentDictionary<Type, int> _enableQueue = new();
        private readonly ConcurrentDictionary<Type, int> _disableQueue = new();

        private const int MaxRetryAttempts = 3;

        public int Count => _systems.Count;
        public IReadOnlyList<BaseSystem> Systems => _systems;
        public ReadOnlySpan<BaseSystem> SystemsSpan => CollectionsMarshal.AsSpan(_systems);

        public SystemManager(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        #region Deferred Registration API

        public void EnqueueRegister<T>() where T : BaseSystem => EnqueueRegister(typeof(T));
        public void EnqueueRegister(Type type) => _registerQueue.TryAdd(type, 0);

        public void EnqueueUnregister<T>() where T : BaseSystem => EnqueueUnregister(typeof(T));
        public void EnqueueUnregister(Type type) => _unregisterQueue.TryAdd(type, 0);

        public void EnqueueEnable<T>() where T : BaseSystem => EnqueueEnable(typeof(T));
        public void EnqueueEnable(Type type) => _enableQueue.TryAdd(type, 0);

        public void EnqueueDisable<T>() where T : BaseSystem => EnqueueDisable(typeof(T));
        public void EnqueueDisable(Type type) => _disableQueue.TryAdd(type, 0);

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
                    Logging.Log($"[SystemManager] Auto-registering dependency: {depType.Name} (required by {type.Name})");
                    Register(depType);
                    dep = _systemMap[depType];
                }

                EventSink.InvokeDependencyResolved(sysInstance, dep);
            }

            _systems.Add(sysInstance);
            _systemMap[type] = sysInstance;

            RegisterSystemTickScheduling(sysInstance);
            LoadSystemSettings(sysInstance);
            sysInstance.OnInitialize(_world);
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
            foreach (var (type, _) in _registerQueue)
            {
                try
                {
                    Register(type);
                    _registerQueue.TryRemove(type, out _);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[SystemManager] Error registering system {type.Name}: {ex}", LogSeverity.Error);
                    _registerQueue.TryRemove(type, out _);
                }
            }
        }

        private void ProcessUnregisterQueue()
        {
            foreach (var (type, _) in _unregisterQueue)
            {
                try
                {
                    Unregister(type);
                    _unregisterQueue.TryRemove(type, out _);
                }
                catch (Exception ex)
                {
                    Logging.Log($"[SystemManager] Error unregistering system {type.Name}: {ex}", LogSeverity.Error);
                    _unregisterQueue.TryRemove(type, out _);
                }
            }
        }

        private void ProcessEnableQueue()
        {
            foreach (var (type, attempts) in _enableQueue)
            {
                try
                {
                    if (_systemMap.TryGetValue(type, out var system))
                    {
                        EnableSystem(system);
                        _enableQueue.TryRemove(type, out _);
                    }
                    else
                    {
                        if (attempts + 1 >= MaxRetryAttempts)
                        {
                            _enableQueue.TryRemove(type, out _);
                            Logging.Log($"[SystemManager] Dropping enable request for {type.Name} after {attempts + 1} failed attempts", LogSeverity.Warning);
                        }
                        else
                        {
                            _enableQueue[type] = attempts + 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log($"[SystemManager] Error enabling system {type.Name}: {ex}", LogSeverity.Error);
                    _enableQueue.TryRemove(type, out _);
                }
            }
        }

        private void ProcessDisableQueue()
        {
            foreach (var (type, attempts) in _disableQueue)
            {
                try
                {
                    if (_systemMap.TryGetValue(type, out var system))
                    {
                        DisableSystem(system);
                        _disableQueue.TryRemove(type, out _);
                    }
                    else
                    {
                        _disableQueue.TryRemove(type, out _);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log($"[SystemManager] Error disabling system {type.Name}: {ex}", LogSeverity.Error);
                    _disableQueue.TryRemove(type, out _);
                }
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
            var batches = new List<List<BaseSystem>>();
            var sysSpan = CollectionsMarshal.AsSpan(_systems);

            foreach (ref var sys in sysSpan)
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

        private static bool ConflictsWithBatch(BaseSystem s, List<BaseSystem> batch)
        {
            if (batch.Count == 0) return false;

            Span<Type> readSpan = s.ReadSet.AsSpan();
            Span<Type> writeSpan = s.WriteSet.AsSpan();
            var batchSpan = CollectionsMarshal.AsSpan(batch);

            foreach (ref readonly var other in batchSpan)
            {
                var oRead = other.ReadSet;
                var oWrite = other.WriteSet;

                // Write/Write conflicts
                foreach (var wt in writeSpan)
                {
                    foreach (var owt in oWrite)
                        if (wt == owt) return true;

                    foreach (var ort in oRead)
                        if (wt == ort) return true;
                }

                // Read/Write conflicts (reversed)
                foreach (var otw in oWrite)
                {
                    foreach (var srt in readSpan)
                        if (srt == otw) return true;
                }
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
    }
}
