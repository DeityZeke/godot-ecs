#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages system registration, lifecycle, and parallel batch execution.
    /// </summary>
    public sealed partial class SystemManager
    {
        private readonly List<BaseSystem> _systems = new(32);
        private List<List<BaseSystem>> _cachedBatches = new();
        private readonly Dictionary<Type, BaseSystem> _systemMap = new();

        public IReadOnlyList<BaseSystem> Systems => _systems;
        public ReadOnlySpan<BaseSystem> SystemsSpan => CollectionsMarshal.AsSpan(_systems);

        #region Registration

        /*
                public void Register(BaseSystem system)
                {
                    var type = system.GetType();
                    if (_systemMap.ContainsKey(type))
                    {
                        GD.PrintErr($"[SystemManager] Attempted to register duplicate system: {type.Name}");
                        return;
                    }

                    _systems.Add(system);
                    _systemMap[type] = system;
                    ComputeBatches(); // Recompute batching after adding

                    GD.Print($"[SystemManager] Registered system: {type.Name}");
                }
        */
        public void Register(BaseSystem system)
        {
            var type = system.GetType();
            if (_systemMap.ContainsKey(type))
            {
                GD.PrintErr($"[SystemManager] Attempted to register duplicate system: {type.Name}");
                return;
            }

            _systems.Add(system);
            _systemMap[type] = system;

            // NEW: Register with tick scheduling system
            RegisterSystemTickScheduling(system);

            ComputeBatches(); // Recompute batching after adding

            GD.Print($"[SystemManager] Registered system: {type.Name}");
        }


        /*
        public void Unregister(BaseSystem system)
        {
            var type = system.GetType();
            if (!_systemMap.ContainsKey(type))
                return;

            _systems.Remove(system);
            _systemMap.Remove(type);
            ComputeBatches(); // Recompute batching after removal

            GD.Print($"[SystemManager] Unregistered system: {type.Name}");
        }
        */

        public void Unregister(BaseSystem system)
        {
            var type = system.GetType();
            if (!_systemMap.ContainsKey(type))
                return;

            // NEW: Unregister from tick scheduling system
            UnregisterSystemTickScheduling(system);

            _systems.Remove(system);
            _systemMap.Remove(type);
            ComputeBatches(); // Recompute batching after removal

            GD.Print($"[SystemManager] Unregistered system: {type.Name}");
        }


        public void Unregister<T>() where T : BaseSystem
        {
            if (_systemMap.TryGetValue(typeof(T), out var system))
                Unregister(system);
        }

        #endregion

        #region Initialization

        public void InitializeAll(World world)
        {
            var sysSpan = CollectionsMarshal.AsSpan(_systems);
            foreach (ref var s in sysSpan)
                s.OnInitialize(world);
        }

        #endregion

        #region Enable / Disable

        public void EnableSystem(BaseSystem system)
        {
            if (!system.IsEnabled)
            {
                system.Enable();

                //if (system.SkipBatching)
                    //ComputeBatches();

                GD.Print($"[SystemManager] Enabled system: {system.Name}");
            }
        }

        public void EnableSystem<T>() where T : BaseSystem
        {
            if (_systemMap.TryGetValue(typeof(T), out var system))
                EnableSystem(system);
        }

        public void EnableAllSystems()
        {
            var sysSpan = CollectionsMarshal.AsSpan(_systems);
            foreach (ref var sys in sysSpan)
                EnableSystem(sys);
        }

        public void DisableSystem(BaseSystem system)
        {
            if (system.IsEnabled)
            {
                system.Disable();

                //if (system.SkipBatching)
                    //ComputeBatches();

                GD.Print($"[SystemManager] Disabled system: {system.Name}");
            }
        }

        public void DisableSystem<T>() where T : BaseSystem
        {
            if (_systemMap.TryGetValue(typeof(T), out var system))
                DisableSystem(system);
        }

        public void DisableAllSystems()
        {
            var sysSpan = CollectionsMarshal.AsSpan(_systems);
            foreach (ref var sys in sysSpan)
                DisableSystem(sys);
        }

        public bool IsSystemEnabled<T>() where T : BaseSystem
        {
            return _systemMap.TryGetValue(typeof(T), out var system) && system.IsEnabled;
        }

        #endregion

        #region Getters

        public BaseSystem? GetSystem(Type t)
        {
            _systemMap.TryGetValue(t, out var system);
            return system;
        }

        public BaseSystem? GetSystem<T>() where T : BaseSystem
        {
            return _systemMap.TryGetValue(typeof(T), out var system) ? system : null;
        }

        public bool HasSystem<T>() where T : BaseSystem
        {
            return _systemMap.ContainsKey(typeof(T));
        }
        
        /// <summary>
        /// Gets all registered systems for GUI display and statistics.
        /// </summary>
        public IReadOnlyList<BaseSystem> GetAllSystems()
        {
            return _systems;
        }

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
                //if (!sys.IsEnabled && sys.SkipBatching) continue; // Skip disabled systems

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
            GD.Print($"[SystemManager] ComputeBatches: Created {batches.Count} batches from {_systems.Count} systems");
            for (int i = 0; i < batches.Count; i++)
            {
                var systemNames = string.Join(", ", batches[i].ConvertAll(s => s.Name));
                GD.Print($"[SystemManager]   Batch {i}: [{systemNames}]");
            }
#endif

        }

        private static bool ConflictsWithBatch(BaseSystem s, List<BaseSystem> batch)
        {
            if (batch.Count == 0) return false;

            Span<Type> readSpan = s.ReadSet.AsSpan();
            Span<Type> writeSpan = s.WriteSet.AsSpan();

            var batchSpan = CollectionsMarshal.AsSpan(batch);

            //foreach (var other in batchSpan)
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
    }
}