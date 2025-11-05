#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using UltraSim.ECS.Threading;
using UltraSim.Logging;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Tick scheduling extension for SystemManager.
    /// Provides system-level tick rate scheduling with O(1) bucket dispatch.
    /// </summary>
    public partial class SystemManager
    {
        // Tick scheduling data structures
        private readonly Dictionary<TickRate, List<BaseSystem>> _tickBuckets = new();
        private readonly List<(TickRate rate, List<BaseSystem> systems)> _tickBucketsList = new(); // Cached for zero-alloc iteration
        private readonly Dictionary<TickRate, double> _nextRunTime = new();
        private readonly List<BaseSystem> _manualSystems = new();
        private readonly Dictionary<BaseSystem, List<List<BaseSystem>>> _systemBatches = new();

        // Reusable list to avoid per-frame allocation
        private readonly List<BaseSystem> _systemsToRun = new(128);

        /// <summary>
        /// Registers a system and adds it to the appropriate tick bucket.
        /// Automatically detects tick rate and scheduling behavior.
        /// Call this from Register() after adding the system to _systems.
        /// </summary>
        private void RegisterSystemTickScheduling(BaseSystem system)
        {
            var rate = system.Rate;

            // Handle manual systems separately
            if (rate == TickRate.Manual)
            {
                _manualSystems.Add(system);

#if USE_DEBUG
                Logger.Log($"Manual system registered: {system.Name}");
                Logger.Log($"   Call: systemManager.RunManual<{system.GetType().Name}>()");
#endif

                return; // Don't add to tick buckets or batching
            }

            // Add to tick bucket
            if (!_tickBuckets.ContainsKey(rate))
            {
                _tickBuckets[rate] = new List<BaseSystem>();
                _nextRunTime[rate] = 0.0; // Run immediately on first frame
            }

            _tickBuckets[rate].Add(system);

            // Rebuild cached list for zero-alloc iteration
            RebuildTickBucketsCache();

#if USE_DEBUG
            Logger.Log($"System '{system.Name}' registered at {rate} ({rate.ToFrequencyString()})");
#endif
        }

        /// <summary>
        /// Unregisters a system from tick scheduling.
        /// Call this from Unregister() before removing the system from _systems.
        /// </summary>
        private void UnregisterSystemTickScheduling(BaseSystem system)
        {
            var rate = system.Rate;

            if (rate == TickRate.Manual)
            {
                _manualSystems.Remove(system);
                return;
            }

            if (_tickBuckets.TryGetValue(rate, out var bucket))
            {
                bucket.Remove(system);

                // Clean up empty buckets
                if (bucket.Count == 0)
                {
                    _tickBuckets.Remove(rate);
                    _nextRunTime.Remove(rate);
                }
            }

            // Remove from batch cache
            if (_systemBatches.ContainsKey(system))
            {
                _systemBatches.Remove(system);
            }

            // Rebuild cached list for zero-alloc iteration
            RebuildTickBucketsCache();
        }

        /// <summary>
        /// Updates all systems based on their tick rates, using the existing ParallelSystemScheduler.
        /// Call this from your main game loop every frame.
        /// </summary>
        /// <param name="world">The ECS world</param>
        /// <param name="delta">Time since last frame in seconds</param>
        public void UpdateTicked(World world, double delta)
        {
            double currentTime = world.TotalSeconds; // From TimeTracker (self-contained)

            // Collect all systems that should run this frame (reuse list to avoid allocation)
            _systemsToRun.Clear();

#if USE_DEBUG
            int systemsSkipped = 0;
#endif

            // Always include EveryFrame systems (optimized: manual loop instead of LINQ)
            if (_tickBuckets.TryGetValue(TickRate.EveryFrame, out var everyFrameSystems))
            {
                for (int i = 0; i < everyFrameSystems.Count; i++)
                {
                    if (everyFrameSystems[i].IsEnabled)
                        _systemsToRun.Add(everyFrameSystems[i]);
                }
            }

            // Check other tick buckets (optimized: use cached list for zero-alloc iteration)
            var bucketsSpan = CollectionsMarshal.AsSpan(_tickBucketsList);
            for (int i = 0; i < bucketsSpan.Length; i++)
            {
                ref var bucket = ref bucketsSpan[i];
                var rate = bucket.rate;
                var systems = bucket.systems;

                // Skip EveryFrame - already handled
                if (rate == TickRate.EveryFrame)
                    continue;

                // Check if it's time to run this bucket
                if (currentTime >= _nextRunTime[rate])
                {
                    // Update next run time
                    double intervalSeconds = rate.ToSeconds();
                    _nextRunTime[rate] = currentTime + intervalSeconds;

                    // Add enabled systems from this bucket (optimized: manual loop instead of LINQ)
                    for (int j = 0; j < systems.Count; j++)
                    {
                        if (systems[j].IsEnabled)
                            _systemsToRun.Add(systems[j]);
                    }
                }
                else
                {
#if USE_DEBUG
                    // Count skipped systems
                    for (int j = 0; j < systems.Count; j++)
                    {
                        if (systems[j].IsEnabled)
                            systemsSkipped++;
                    }
#endif
                }
            }

            // If no systems need to run, early exit
            if (_systemsToRun.Count == 0)
                return;

            // Get or create batches for the systems that need to run
            var batchesToRun = GetBatchesForSystems(_systemsToRun);

            // Use the existing parallel scheduler!
            ParallelSystemScheduler.RunBatches(batchesToRun, world, delta, null, null);

#if USE_DEBUG
            // Log every 60 frames to avoid spam
            if (World.Current.tickCount % 60 == 0)
            {
                Logger.Log($"[TickScheduler] Frame {World.Current.tickCount}: Ran {_systemsToRun.Count} systems, skipped {systemsSkipped}");
            }
#endif
        }

        /// <summary>
        /// Gets or creates batches for a specific set of systems.
        /// Uses cached batches when possible for performance.
        /// </summary>
        private List<List<BaseSystem>> GetBatchesForSystems(List<BaseSystem> systems)
        {
            // Try to use cached batches for these specific systems
            if (systems.Count == _systems.Count)
            {
                // All systems - use the main cached batches
                return _cachedBatches;
            }

            // For filtered system sets, check if we have a cached batch set
            // Use the first system as a key (systems in same tick bucket)
            var firstSystem = systems[0];
            if (_systemBatches.TryGetValue(firstSystem, out var cachedBatches)
                && cachedBatches.Count > 0)
            {
                // Verify cache is still valid (same systems)
                var cachedSystemCount = cachedBatches.Sum(b => b.Count);
                if (cachedSystemCount == systems.Count)
                    return cachedBatches;
            }

            // Need to create new batches for this system set
            var batches = ComputeBatchesForSystems(systems);

            // Cache it for next time (if it's a tick bucket)
            if (systems.Count > 1) // Don't cache single-system batches
            {
                _systemBatches[firstSystem] = batches;
            }

            return batches;
        }

        /// <summary>
        /// Computes batches for a specific set of systems.
        /// Uses the same conflict detection as the main SystemManager.
        /// </summary>
        private List<List<BaseSystem>> ComputeBatchesForSystems(List<BaseSystem> systems)
        {
            var batches = new List<List<BaseSystem>>();

            foreach (var sys in systems)
            {
                bool placed = false;

                foreach (var batch in batches)
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

            return batches;
        }

        /// <summary>
        /// Manually runs a specific system by type.
        /// Only works with systems that have TickRate.Manual.
        /// </summary>
        public void RunManual<T>(World world) where T : BaseSystem
        {
            if (!_systemMap.TryGetValue(typeof(T), out var system))
            {
                Logger.Log($"[SystemManager] System not found: {typeof(T).Name}", LogSeverity.Error);
                return;
            }

            if (system.Rate != TickRate.Manual)
            {
                Logger.Log($"[SystemManager] Cannot manually run non-Manual system: {system.Name}", LogSeverity.Error);
                Logger.Log($"   System has TickRate: {system.Rate}", LogSeverity.Error);
                return;
            }

#if USE_DEBUG
            Logger.Log($"Running manual system: {system.Name}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            system.UpdateWithTiming(world, 0.0); // Delta doesn't matter for manual systems

#if USE_DEBUG
            sw.Stop();
            Logger.Log($"{system.Name} completed in {sw.Elapsed.TotalMilliseconds:F3}ms");
#endif
        }

        /// <summary>
        /// Manually runs a specific system by instance.
        /// Only works with systems that have TickRate.Manual.
        /// </summary>
        public void RunManual(World world, BaseSystem system)
        {
            if (system.Rate != TickRate.Manual)
            {
                Logger.Log($"[SystemManager] Cannot manually run non-Manual system: {system.Name}", LogSeverity.Error);
                Logger.Log($"   System has TickRate: {system.Rate}", LogSeverity.Error);
                return;
            }

#if USE_DEBUG
            Logger.Log($"Running manual system: {system.Name}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            system.UpdateWithTiming(world, 0.0);

#if USE_DEBUG
            sw.Stop();
            Logger.Log($"{system.Name} completed in {sw.Elapsed.TotalMilliseconds:F3}ms");
#endif
        }

        /// <summary>
        /// Gets detailed information about tick scheduling state.
        /// Useful for debugging and profiling.
        /// </summary>
        public string GetTickSchedulingInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Tick Scheduling Info ===");

            // Sort buckets by tick rate
            var sortedBuckets = _tickBuckets.OrderBy(kvp => (ushort)kvp.Key);

            foreach (var kvp in sortedBuckets)
            {
                var rate = kvp.Key;
                var systems = kvp.Value;
                var enabledCount = systems.Count(s => s.IsEnabled);

                sb.AppendLine($"{rate} ({rate.ToFrequencyString()}): {enabledCount}/{systems.Count} systems");

                foreach (var system in systems)
                {
                    string status = system.IsEnabled ? "[ON]" : "[OFF]";
                    sb.AppendLine($"  {status} {system.Name}");
                }
            }

            // Manual systems
            if (_manualSystems.Count > 0)
            {
                var enabledManualCount = _manualSystems.Count(s => s.IsEnabled);
                sb.AppendLine($"Manual: {enabledManualCount}/{_manualSystems.Count} systems");

                foreach (var system in _manualSystems)
                {
                    string status = system.IsEnabled ? "[ON]" : "[OFF]";
                    sb.AppendLine($"  {status} {system.Name}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets a list of all manual systems.
        /// Useful for UI/debug panels.
        /// </summary>
        public IReadOnlyList<BaseSystem> GetManualSystems()
        {
            return _manualSystems.AsReadOnly();
        }

        /// <summary>
        /// Resets all tick timers. Useful when game is paused or state changes.
        /// </summary>
        public void ResetTickTimers()
        {
            // Optimize: iterate keys directly without ToList allocation
            // Safe because we're only setting values, not modifying keys
            foreach (var rate in _nextRunTime.Keys)
            {
                _nextRunTime[rate] = 0.0; // Will run on next frame
            }

#if USE_DEBUG
            Logger.Log("[TickScheduler] All tick timers reset");
#endif
        }

        /// <summary>
        /// Rebuilds the cached tick buckets list for zero-allocation iteration.
        /// Called whenever buckets are added or removed.
        /// </summary>
        private void RebuildTickBucketsCache()
        {
            _tickBucketsList.Clear();
            foreach (var kvp in _tickBuckets)
            {
                _tickBucketsList.Add((kvp.Key, kvp.Value));
            }
        }
    }
}