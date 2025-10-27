#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Godot;

using UltraSim.ECS.Threading;

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
        private readonly Dictionary<TickRate, double> _nextRunTime = new();
        private readonly List<BaseSystem> _manualSystems = new();
        private readonly Dictionary<BaseSystem, List<List<BaseSystem>>> _systemBatches = new();

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
                GD.Print($"⏸️ Manual system registered: {system.Name}");
                GD.Print($"   Call: systemManager.RunManual<{system.GetType().Name}>()");
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

#if USE_DEBUG
            GD.Print($"✓ System '{system.Name}' registered at {rate} ({rate.ToFrequencyString()})");
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
        }

        /// <summary>
        /// Updates all systems based on their tick rates, using the existing ParallelSystemScheduler.
        /// Call this from your main game loop every frame.
        /// </summary>
        /// <param name="world">The ECS world</param>
        /// <param name="delta">Time since last frame in seconds</param>
        public void UpdateTicked(World world, double delta)
        {
            double currentTime = Time.GetTicksMsec() / 1000.0; // Current time in seconds

            // Collect all systems that should run this frame
            var systemsToRun = new List<BaseSystem>();

#if USE_DEBUG
            int systemsSkipped = 0;
#endif

            // Always include EveryFrame systems
            if (_tickBuckets.TryGetValue(TickRate.EveryFrame, out var everyFrameSystems))
            {
                systemsToRun.AddRange(everyFrameSystems.Where(s => s.IsEnabled));
            }

            // Check other tick buckets
            foreach (var kvp in _tickBuckets)
            {
                var rate = kvp.Key;
                var systems = kvp.Value;

                // Skip EveryFrame - already handled
                if (rate == TickRate.EveryFrame)
                    continue;

                // Check if it's time to run this bucket
                if (currentTime >= _nextRunTime[rate])
                {
                    // Update next run time
                    double intervalSeconds = rate.ToSeconds();
                    _nextRunTime[rate] = currentTime + intervalSeconds;

                    // Add enabled systems from this bucket
                    systemsToRun.AddRange(systems.Where(s => s.IsEnabled));
                }
                else
                {
#if USE_DEBUG
                    systemsSkipped += systems.Count(s => s.IsEnabled);
#endif
                }
            }

            // If no systems need to run, early exit
            if (systemsToRun.Count == 0)
                return;

            // Get or create batches for the systems that need to run
            var batchesToRun = GetBatchesForSystems(systemsToRun);

            // Use the existing parallel scheduler!
            ParallelSystemScheduler.RunBatches(batchesToRun, world, delta, null, null);

#if USE_DEBUG
            // Log every 60 frames to avoid spam
            if (Engine.GetProcessFrames() % 60 == 0)
            {
                GD.Print($"[TickScheduler] Frame {Engine.GetProcessFrames()}: Ran {systemsToRun.Count} systems, skipped {systemsSkipped}");
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


        /*

        /// <summary>
        /// Checks if a system conflicts with any system in a batch.
        /// Copied from SystemManager for consistency.
        /// </summary>
        private static bool ConflictsWithBatch(BaseSystem s, List<BaseSystem> batch)
        {
            if (batch.Count == 0) return false;

            Span<Type> readSpan = s.ReadSet.AsSpan();
            Span<Type> writeSpan = s.WriteSet.AsSpan();

            foreach (var other in batch)
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
        */

        /// <summary>
        /// Manually runs a specific system by type.
        /// Only works with systems that have TickRate.Manual.
        /// </summary>
        public void RunManual<T>(World world) where T : BaseSystem
        {
            if (!_systemMap.TryGetValue(typeof(T), out var system))
            {
                GD.PrintErr($"[SystemManager] System not found: {typeof(T).Name}");
                return;
            }

            if (system.Rate != TickRate.Manual)
            {
                GD.PrintErr($"[SystemManager] Cannot manually run non-Manual system: {system.Name}");
                GD.PrintErr($"   System has TickRate: {system.Rate}");
                return;
            }

#if USE_DEBUG
            GD.Print($"▶️ Running manual system: {system.Name}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            system.UpdateWithTiming(world, 0.0); // Delta doesn't matter for manual systems

#if USE_DEBUG
            sw.Stop();
            GD.Print($"✓ {system.Name} completed in {sw.Elapsed.TotalMilliseconds:F3}ms");
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
                GD.PrintErr($"[SystemManager] Cannot manually run non-Manual system: {system.Name}");
                GD.PrintErr($"   System has TickRate: {system.Rate}");
                return;
            }

#if USE_DEBUG
            GD.Print($"▶️ Running manual system: {system.Name}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            system.UpdateWithTiming(world, 0.0);

#if USE_DEBUG
            sw.Stop();
            GD.Print($"✓ {system.Name} completed in {sw.Elapsed.TotalMilliseconds:F3}ms");
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
                    string status = system.IsEnabled ? "✓" : "✗";
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
                    string status = system.IsEnabled ? "✓" : "✗";
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
            var keys = _nextRunTime.Keys.ToList();
            foreach (var rate in keys)
            {
                _nextRunTime[rate] = 0.0; // Will run on next frame
            }

#if USE_DEBUG
            GD.Print("[TickScheduler] All tick timers reset");
#endif
        }
    }
}