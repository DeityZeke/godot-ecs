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

        // Apply-phase sub-timings
        private double _mergeMs, _compressMs, _applyValuesMs, _structuralMs;

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
                        GD.PrintErr($"⚠️ {system.Name} over per-system budget: {ms:F3} ms");
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
                        GD.PrintErr($"⚠️ Batch {batchIndex} imbalance: {imbalance:F2}x (min {min:F3}ms, max {max:F3}ms, avg {avg:F3}ms)");
                }
                else
                {
                    _lastBatchStats[batchIndex] = (0, 0, 0);
                }
            }

            // Apply Phase Sub-Timing (placeholder - integrate with World)
            var mergeSw = Stopwatch.StartNew();
            // world.ApplyMergePhase();
            mergeSw.Stop();
            _mergeMs = mergeSw.Elapsed.TotalMilliseconds;

            var compressSw = Stopwatch.StartNew();
            // world.ApplyCompressPhase();
            compressSw.Stop();
            _compressMs = compressSw.Elapsed.TotalMilliseconds;

            var applyValuesSw = Stopwatch.StartNew();
            // world.ApplyValueChangesPhase();
            applyValuesSw.Stop();
            _applyValuesMs = applyValuesSw.Elapsed.TotalMilliseconds;

            var structuralSw = Stopwatch.StartNew();
            // world.ApplyStructuralPhase();
            structuralSw.Stop();
            _structuralMs = structuralSw.Elapsed.TotalMilliseconds;

            frameSw.Stop();
            double totalFrameMs = frameSw.Elapsed.TotalMilliseconds;

            _gcAfter = GC.GetTotalMemory(false);

            // Frame Budget Alerts
            if (totalFrameMs > ECS_BUDGET_MS)
                GD.PrintErr($"⚠️ ECS over total frame budget: {totalFrameMs:F3}ms / {ECS_BUDGET_MS}ms");

            if (_frameIndex <= 10 || _frameIndex % 30 == 0)
            {
                GD.Print($"[ECS Debug] Frame {_frameIndex}: ECS update {totalFrameMs:F3} ms | Merge {_mergeMs:F2} ms | Compress {_compressMs:F2} ms | ApplyValues {_applyValuesMs:F2} ms | Structural {_structuralMs:F2} ms | GC Δ {_gcAfter - _gcBefore} bytes");
            }

            // Store frame for offline export
            _frameHistory.Add(new FrameData
            {
                FrameIndex = _frameIndex,
                SystemData = _lastSystemTimesMs.ToDictionary(kv => kv.Key, kv => kv.Value),
                MergeMs = _mergeMs,
                CompressMs = _compressMs,
                ApplyValuesMs = _applyValuesMs,
                StructuralMs = _structuralMs,
                GCDelta = _gcAfter - _gcBefore
            });

            _frameIndex++;
        }

        [Conditional("USE_DEBUG")]
        public void DumpLastSystemTimes()
        {
            GD.Print("[ECS Debug] System times (ms):");
            foreach (var kv in _lastSystemTimesMs)
                GD.Print($" - {kv.Key}: {kv.Value:F3} ms");
        }

        [Conditional("USE_DEBUG")]
        public void DumpLastBatchStats()
        {
            GD.Print("[ECS Debug] Batch stats:");
            foreach (var kv in _lastBatchStats)
            {
                var (min, avg, max) = kv.Value;
                GD.Print($" - Batch {kv.Key}: min {min:F3} ms | avg {avg:F3} ms | max {max:F3} ms");
            }
        }

        [Conditional("USE_DEBUG")]
        public void DumpSystemStates()
        {
            GD.Print($"[SystemManager] {_systems.Count} systems registered:");
            //foreach (var s in _systems)
            foreach (ref var s in CollectionsMarshal.AsSpan(_systems))
                GD.Print($" - {s.Name,-24} | Enabled: {s.IsEnabled}");
        }

        [Conditional("USE_DEBUG")]
        public void DumpSystemOrder()
        {
            GD.Print("[SystemManager] Execution Order:");
            //for (int i = 0; i < _systems.Count; i++)
            for (int i = 0; i < CollectionsMarshal.AsSpan(_systems).Length; i++)
                GD.Print($" {i,3}. {_systems[i].Name}");
        }

        [Conditional("USE_DEBUG")]
        public void ValidateSystems()
        {
            //foreach (var s in _systems)
            foreach (ref var s in CollectionsMarshal.AsSpan(_systems))
            {
                if (!s.IsValid)
                    GD.PrintErr($"[SystemManager] ❌ System invalid: {s.Name}");
            }
        }

        public int EnabledSystemCount => _systems.Count(s => s.IsEnabled);
        public int DisabledSystemCount => _systems.Count(s => !s.IsEnabled);

        #endregion

        #region Offline Export

        [Conditional("USE_DEBUG")]
        public void ExportFrameDataCSV(string path)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Frame,System,TimeMs,MergeMs,CompressMs,ApplyValuesMs,StructuralMs,GCDeltaBytes");

                //foreach (var frame in _frameHistory)
                foreach (var frame in CollectionsMarshal.AsSpan(_frameHistory))
                {
                    foreach (var kv in frame.SystemData)
                    {
                        sb.AppendLine($"{frame.FrameIndex},{kv.Key},{kv.Value:F3},{frame.MergeMs:F3},{frame.CompressMs:F3},{frame.ApplyValuesMs:F3},{frame.StructuralMs:F3},{frame.GCDelta}");
                    }
                }

                File.WriteAllText(path, sb.ToString());
                GD.Print($"[ECS Debug] Exported frame profiling CSV to {path}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[ECS Debug] Failed to export profiling CSV: {e}");
            }
        }

        #endregion

        #region Helper Data Structures

        private class FrameData
        {
            public int FrameIndex;
            public Dictionary<string, double> SystemData = new();
            public double MergeMs, CompressMs, ApplyValuesMs, StructuralMs;
            public long GCDelta;
        }

        #endregion
    }
}
#endif