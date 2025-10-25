#if USE_DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Godot;

namespace UltraSim.ECS
{
    public partial class SystemManager
    {
        private readonly Dictionary<string, double> _lastSystemTimesMs = new();
        private readonly Dictionary<int, (double min, double avg, double max)> _lastBatchStats = new();

        // Frame-level ECS budget alerts
        private const double ECS_BUDGET_MS = 10.0;
        private const double SYSTEM_BUDGET_MS = 1.0;

        // Memory tracking
        private long _gcBefore, _gcAfter;

        // History for offline export
        private readonly List<FrameData> _frameHistory = new();
        private int _frameIndex = 0;

        #region Profiling

        [Conditional("USE_DEBUG")]
        public void ProfileUpdateAll(World world, double delta)
        {
            _lastSystemTimesMs.Clear();
            _lastBatchStats.Clear();

            _gcBefore = GC.GetTotalMemory(false);
            var frameSw = Stopwatch.StartNew();

            // Per-Batch Execution with Profiling
            for (int batchIndex = 0; batchIndex < _cachedBatches.Count; batchIndex++)
            {
                var batch = _cachedBatches[batchIndex];
                var systemTimes = new ConcurrentBag<double>();

                //foreach (var system in batch)
                foreach (ref var system in CollectionsMarshal.AsSpan(batch))
                {
                    if (!system.IsEnabled) continue;

                    var sw = Stopwatch.StartNew();
                    system.Update(world, delta);
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    _lastSystemTimesMs[system.Name] = ms;
                    systemTimes.Add(ms);

                    if (ms > SYSTEM_BUDGET_MS)
                        GD.PrintErr($"âš ï¸ {system.Name} over per-system budget: {ms:F3} ms");
                }

                // Batch load imbalance stats
                if (systemTimes.Count > 0)
                {
                    double min = systemTimes.Min();
                    double max = systemTimes.Max();
                    double avg = systemTimes.Average();
                    _lastBatchStats[batchIndex] = (min, avg, max);

                    double imbalance = avg > 0 ? max / avg : 0;
                    if (imbalance > 2.5)
                        GD.PrintErr($"âš ï¸ Batch {batchIndex} imbalance: {imbalance:F2}x (min {min:F3}ms, max {max:F3}ms, avg {avg:F3}ms)");
                }
                else
                {
                    _lastBatchStats[batchIndex] = (0, 0, 0);
                }
            }

            frameSw.Stop();
            double totalFrameMs = frameSw.Elapsed.TotalMilliseconds;

            _gcAfter = GC.GetTotalMemory(false);

            // Frame Budget Alerts
            if (totalFrameMs > ECS_BUDGET_MS)
                GD.PrintErr($"âš ï¸ ECS over total frame budget: {totalFrameMs:F3}ms / {ECS_BUDGET_MS}ms");

            if (_frameIndex <= 10 || _frameIndex % 30 == 0)
            {
                GD.Print($"[ECS Debug] Frame {_frameIndex}: ECS update {totalFrameMs:F3} ms | GC Δ {_gcAfter - _gcBefore} bytes");
            }

            // Store frame for offline export
            _frameHistory.Add(new FrameData
            {
                FrameIndex = _frameIndex,
                SystemData = _lastSystemTimesMs.ToDictionary(kv => kv.Key, kv => kv.Value),
                GCDelta = _gcAfter - _gcBefore
            });

            _frameIndex++;
        }

        public int EnabledSystemCount => _systems.Count(s => s.IsEnabled);
        public int DisabledSystemCount => _systems.Count(s => !s.IsEnabled);

        #endregion

        #region Helper Data Structures

        private class FrameData
        {
            public int FrameIndex;
            public Dictionary<string, double> SystemData = new();

            public long GCDelta;
        }

        #endregion
    }
}
#endif