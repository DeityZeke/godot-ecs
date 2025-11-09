#if USE_DEBUG

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

using UltraSim;
using UltraSim.ECS.SIMD;

namespace UltraSim.ECS.Systems
{
    public partial class SystemManager
    {
        private readonly Dictionary<string, double> _lastSystemTimesMs = new();
        private readonly Dictionary<int, (double min, double avg, double max)> _lastBatchStats = new();

        // Frame-level ECS budget alerts
        // ECS_BUDGET_MS: Total time budget for all ECS systems per frame (60 FPS = 16.67ms total)
        private const double ECS_BUDGET_MS = 10.0;

        // SYSTEM_BUDGET_MS: Per-system warning threshold
        // For 100k+ entities, 2-3ms per system is normal for parallel workloads
        // Only alert on truly excessive times (3ms+ suggests poor optimization)
        private const double SYSTEM_BUDGET_MS = 3.0;

        // Memory tracking
        private long _gcBefore, _gcAfter;

        // History for offline export
        private readonly List<FrameData> _frameHistory = new();
        private int _frameIndex = 0;

        // Performance tracking
        private readonly Stopwatch _frameSw = new();

        #region Profiling

        [Conditional("USE_DEBUG")]
        public void ProfileUpdateAll(World world, double delta)
        {
            _lastSystemTimesMs.Clear();
            _lastBatchStats.Clear();

            // DEBUG: Verify this method is being called
            if (_frameIndex == 0)
                Logging.Log("[SystemManager.Debug] ProfileUpdateAll() ACTIVE - Debug profiling enabled", LogSeverity.Info);

            _gcBefore = GC.GetTotalMemory(false);
            _frameSw.Restart();

            // Use the tick scheduling system (same as production)
            // This calls UpdateWithTiming() which populates EMA statistics
            Update(world, delta);

            _frameSw.Stop();
            double totalFrameMs = _frameSw.Elapsed.TotalMilliseconds;
            _gcAfter = GC.GetTotalMemory(false);

            // Collect statistics from EMA-based SystemStatistics
            // This replaces manual timing - we now use the built-in statistics
            foreach (var system in _systems)
            {
                if (system.IsEnabled && system.Statistics.UpdateCount > 0)
                {
                    // Use the EMA average from BaseSystem statistics
                    double avgMs = system.Statistics.AverageUpdateTimeMs;
                    _lastSystemTimesMs[system.Name] = avgMs;

                    // Alert on systems over budget (using average, not last)
                    if (avgMs > SYSTEM_BUDGET_MS)
                        Logging.Log($"WARNING: {system.Name} over per-system budget: {avgMs:F3} ms avg", LogSeverity.Error);
                }
            }

            // Compute batch statistics from the systems that ran
            ComputeBatchStats();

            // Frame Budget Alerts
            if (totalFrameMs > ECS_BUDGET_MS)
                Logging.Log($"WARNING: ECS over total frame budget: {totalFrameMs:F3}ms / {ECS_BUDGET_MS}ms", LogSeverity.Error);

            // Periodic logging
            if (_frameIndex <= 10 || _frameIndex % 30 == 0)
            {
                string simdMode = SimdManager.ShowcaseEnabled
                    ? $"Showcase:{SimdManager.GetMode(SimdCategory.Systems)}"
                    : "Optimal";

                Logging.Log($"[ECS Debug] Frame {_frameIndex}: ECS {totalFrameMs:F3}ms | Entities {world.EntityCount} | SIMD {simdMode} | GC Δ {_gcAfter - _gcBefore} bytes", LogSeverity.Info);
            }

            // Store frame for offline export
            _frameHistory.Add(new FrameData
            {
                FrameIndex = _frameIndex,
                SystemData = _lastSystemTimesMs.ToDictionary(kv => kv.Key, kv => kv.Value),
                GCDelta = _gcAfter - _gcBefore,
                EntityCount = world.EntityCount,
                SimdMode = SimdManager.ShowcaseEnabled
                    ? $"Showcase:{SimdManager.GetMode(SimdCategory.Systems)}"
                    : "Optimal",
                TotalFrameMs = totalFrameMs
            });

            _frameIndex++;
        }

        /// <summary>
        /// Computes batch statistics from the enabled systems.
        /// Since we're using tick scheduling, we reconstruct batch stats from active systems.
        /// </summary>
        private void ComputeBatchStats()
        {
            _lastBatchStats.Clear();

            for (int batchIndex = 0; batchIndex < _cachedBatches.Count; batchIndex++)
            {
                var batch = _cachedBatches[batchIndex];
                var systemTimes = new List<double>();

                // Collect times for enabled systems in this batch
                foreach (var system in batch)
                {
                    if (system.IsEnabled && system.Statistics.UpdateCount > 0)
                    {
                        systemTimes.Add(system.Statistics.AverageUpdateTimeMs);
                    }
                }

                // Compute batch imbalance stats
                if (systemTimes.Count > 0)
                {
                    double min = systemTimes.Min();
                    double max = systemTimes.Max();
                    double avg = systemTimes.Average();
                    _lastBatchStats[batchIndex] = (min, avg, max);

                    double imbalance = avg > 0 ? max / avg : 0;
                    if (imbalance > 2.5)
                        Logging.Log($"WARNING: Batch {batchIndex} imbalance: {imbalance:F2}x (min {min:F3}ms, max {max:F3}ms, avg {avg:F3}ms)", LogSeverity.Error);
                }
                else
                {
                    _lastBatchStats[batchIndex] = (0, 0, 0);
                }
            }
        }

        public int EnabledSystemCount => _systems.Count(s => s.IsEnabled);
        public int DisabledSystemCount => _systems.Count(s => !s.IsEnabled);

        /// <summary>
        /// Exports profiling data to CSV for offline analysis.
        /// </summary>
        public void ExportProfilingData(string path)
        {
            try
            {
                using var writer = new StreamWriter(path);

                // Write header
                var systemNames = _frameHistory
                    .SelectMany(f => f.SystemData.Keys)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                writer.Write("Frame,TotalMs,EntityCount,SimdMode,GCDelta");
                foreach (var sysName in systemNames)
                    writer.Write($",{sysName}");
                writer.WriteLine();

                // Write data rows
                foreach (var frame in _frameHistory)
                {
                    writer.Write($"{frame.FrameIndex},{frame.TotalFrameMs:F3},{frame.EntityCount},{frame.SimdMode},{frame.GCDelta}");
                    foreach (var sysName in systemNames)
                    {
                        if (frame.SystemData.TryGetValue(sysName, out var time))
                            writer.Write($",{time:F3}");
                        else
                            writer.Write(",0");
                    }
                    writer.WriteLine();
                }

                Logging.Log($"[ECS Debug] Exported {_frameHistory.Count} frames to {path}");
            }
            catch (Exception ex)
            {
                Logging.Log($"[ECS Debug] Failed to export profiling data: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Clears profiling history.
        /// </summary>
        public void ClearProfilingHistory()
        {
            _frameHistory.Clear();
            _frameIndex = 0;
            Logging.Log("[ECS Debug] Profiling history cleared");
        }

        #endregion

        #region Helper Data Structures

        private class FrameData
        {
            public int FrameIndex;
            public Dictionary<string, double> SystemData = new();
            public long GCDelta;
            public int EntityCount;
            public string SimdMode = "Unknown";
            public double TotalFrameMs;
        }

        #endregion
    }
}
#endif
