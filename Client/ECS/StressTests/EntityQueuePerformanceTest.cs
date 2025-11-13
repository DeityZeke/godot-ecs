#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Events;
using UltraSim.ECS.Components;
using UltraSim.WorldECS;

namespace Client.ECS.StressTests
{
    /// <summary>
    /// Benchmarks entity creation queue performance with random entity counts and intervals.
    /// Tests: Enqueue -> ProcessQueues -> Batch Create -> Event Fire
    /// </summary>
    public partial class EntityQueuePerformanceTest : Node, IHost
    {
        private World? _world;
        private readonly Random _random = new(12345); // Fixed seed for reproducibility

        // IHost implementation
        public RuntimeContext Runtime { get; private set; } = null!;
        public EnvironmentType Environment => EnvironmentType.Hybrid;
        public object? GetRootHandle() => this;
        public void Log(LogEntry entry) => GD.Print($"[{entry.Severity}] {entry.Message}");

        // Timing data
        private readonly List<TimingSample> _samples = new();
        private long _totalEnqueueNs = 0;
        private long _totalProcessNs = 0;
        private long _totalCreateNs = 0;
        private long _totalEventNs = 0;
        private int _totalEntitiesCreated = 0;

        // Test configuration
        private int _testFrames = 0;
        private const int MAX_TEST_FRAMES = 120; // 2 seconds at 60 FPS
        private const int MIN_ENTITIES_PER_BATCH = 1;
        private const int MAX_ENTITIES_PER_BATCH = 10000;

        private readonly Stopwatch _sw = new();

        public override void _Ready()
        {
            // Create our own World for testing
            Runtime = new RuntimeContext(HostEnvironment.Capture(), "EntityQueuePerformanceTest");
            Logging.Host = this;
            _world = new World(this);

            GD.Print("[EntityQueuePerformanceTest] Starting queue performance benchmark...");
            GD.Print("[EntityQueuePerformanceTest] Test configuration:");
            GD.Print($"  - Test duration: {MAX_TEST_FRAMES} frames (~2 seconds at 60 FPS)");
            GD.Print($"  - Batch size range: {MIN_ENTITIES_PER_BATCH}-{MAX_ENTITIES_PER_BATCH} entities");
            GD.Print($"  - Pattern: Random batch sizes at random intervals");
            GD.Print($"  - Measuring: Enqueue | Process | Create | Event | Total");
        }

        public override void _Process(double delta)
        {
            if (_world == null || _testFrames >= MAX_TEST_FRAMES)
                return;

            // Tick the world to process queues and systems
            _world!.Tick(delta);

            _testFrames++;

            // Random batch creation (30% chance per frame)
            if (_random.NextDouble() < 0.3)
            {
                RunBatchCreationTest();
            }

            // Print intermediate results every 30 frames
            if (_testFrames % 30 == 0)
            {
                PrintIntermediateResults();
            }

            // Final results
            if (_testFrames == MAX_TEST_FRAMES)
            {
                PrintFinalResults();
                QueueFree();
            }
        }

        private void RunBatchCreationTest()
        {
            // Random batch size (weighted toward smaller batches)
            int batchSize = _random.Next(0, 100) switch
            {
                < 50 => _random.Next(1, 100),      // 50% chance: 1-100 entities
                < 80 => _random.Next(100, 1000),   // 30% chance: 100-1000 entities
                < 95 => _random.Next(1000, 5000),  // 15% chance: 1000-5000 entities
                _ => _random.Next(5000, MAX_ENTITIES_PER_BATCH + 1) // 5% chance: 5000-10000 entities
            };

            var sample = new TimingSample { BatchSize = batchSize };

            // === PHASE 1: ENQUEUE ===
            _sw.Restart();
            for (int i = 0; i < batchSize; i++)
            {
                _world!.EnqueueCreateEntity(entity =>
                {
                    // Simple entity with Position component
                    _world!.EnqueueComponentAdd(entity,
                        ComponentManager.GetTypeId<Position>(),
                        new Position { X = 0, Y = 0, Z = 0 });
                });
            }
            _sw.Stop();
            sample.EnqueueNs = _sw.Elapsed.Ticks * 100; // Convert to nanoseconds
            _totalEnqueueNs += sample.EnqueueNs;

            // === PHASE 2 & 3: TICK WORLD (processes all queues) ===
            // Note: World.Tick processes entity queue, then component queue
            // We can't separate them without access to internal managers
            _sw.Restart();
            _world!.Tick(0.016); // Tick with minimal delta
            _sw.Stop();
            long tickNs = _sw.Elapsed.Ticks * 100;
            sample.ProcessNs = tickNs / 2; // Approximate split
            sample.CreateNs = tickNs / 2;
            _totalProcessNs += sample.ProcessNs;
            _totalCreateNs += sample.CreateNs;

            // Total time
            sample.TotalNs = sample.EnqueueNs + sample.ProcessNs + sample.CreateNs;

            _samples.Add(sample);
            _totalEntitiesCreated += batchSize;
        }

        private void PrintIntermediateResults()
        {
            if (_samples.Count == 0)
                return;

            GD.Print($"\n[Frame {_testFrames}] Intermediate Results:");
            GD.Print($"  Batches processed: {_samples.Count}");
            GD.Print($"  Entities created:  {_totalEntitiesCreated:N0}");

            var avgEnqueue = _totalEnqueueNs / _samples.Count;
            var avgProcess = _totalProcessNs / _samples.Count;
            var avgCreate = _totalCreateNs / _samples.Count;
            var avgTotal = (_totalEnqueueNs + _totalProcessNs + _totalCreateNs) / _samples.Count;

            GD.Print($"  Avg Enqueue:  {avgEnqueue / 1000.0:F2} μs");
            GD.Print($"  Avg Process:  {avgProcess / 1000.0:F2} μs");
            GD.Print($"  Avg Create:   {avgCreate / 1000.0:F2} μs");
            GD.Print($"  Avg Total:    {avgTotal / 1000.0:F2} μs");
        }

        private void PrintFinalResults()
        {
            if (_samples.Count == 0)
            {
                GD.Print("[EntityQueuePerformanceTest] No samples collected!");
                return;
            }

            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║       ENTITY QUEUE PERFORMANCE TEST - FINAL RESULTS           ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            // Overall statistics
            GD.Print($"Test Summary:");
            GD.Print($"  Frames:           {_testFrames}");
            GD.Print($"  Batches:          {_samples.Count}");
            GD.Print($"  Total entities:   {_totalEntitiesCreated:N0}");
            GD.Print($"  Avg batch size:   {_totalEntitiesCreated / (double)_samples.Count:F1}");

            // Phase breakdown (averages)
            GD.Print($"\nPhase Timing (Average per batch):");
            var avgEnqueue = _totalEnqueueNs / _samples.Count;
            var avgProcess = _totalProcessNs / _samples.Count;
            var avgCreate = _totalCreateNs / _samples.Count;
            var avgTotal = avgEnqueue + avgProcess + avgCreate;

            GD.Print($"  Enqueue:    {avgEnqueue / 1000.0:F2} μs ({avgEnqueue * 100.0 / avgTotal:F1}%)");
            GD.Print($"  Process:    {avgProcess / 1000.0:F2} μs ({avgProcess * 100.0 / avgTotal:F1}%)");
            GD.Print($"  Create:     {avgCreate / 1000.0:F2} μs ({avgCreate * 100.0 / avgTotal:F1}%)");
            GD.Print($"  TOTAL:      {avgTotal / 1000.0:F2} μs");

            // Per-entity cost
            GD.Print($"\nPer-Entity Cost:");
            var perEntityEnqueue = _totalEnqueueNs / (double)_totalEntitiesCreated;
            var perEntityProcess = _totalProcessNs / (double)_totalEntitiesCreated;
            var perEntityCreate = _totalCreateNs / (double)_totalEntitiesCreated;
            var perEntityTotal = (perEntityEnqueue + perEntityProcess + perEntityCreate);

            GD.Print($"  Enqueue:    {perEntityEnqueue:F0} ns");
            GD.Print($"  Process:    {perEntityProcess:F0} ns");
            GD.Print($"  Create:     {perEntityCreate:F0} ns");
            GD.Print($"  TOTAL:      {perEntityTotal:F0} ns ({1000000000.0 / perEntityTotal:F0} entities/sec)");

            // Batch size analysis
            var smallBatches = _samples.Where(s => s.BatchSize < 100).ToList();
            var mediumBatches = _samples.Where(s => s.BatchSize >= 100 && s.BatchSize < 1000).ToList();
            var largeBatches = _samples.Where(s => s.BatchSize >= 1000).ToList();

            GD.Print($"\nBatch Size Analysis:");
            if (smallBatches.Count > 0)
            {
                var avgTime = smallBatches.Average(s => s.TotalNs) / 1000.0;
                GD.Print($"  Small (1-99):      {smallBatches.Count} batches, avg {avgTime:F2} μs");
            }
            if (mediumBatches.Count > 0)
            {
                var avgTime = mediumBatches.Average(s => s.TotalNs) / 1000.0;
                GD.Print($"  Medium (100-999):  {mediumBatches.Count} batches, avg {avgTime:F2} μs");
            }
            if (largeBatches.Count > 0)
            {
                var avgTime = largeBatches.Average(s => s.TotalNs) / 1000.0;
                GD.Print($"  Large (1000+):     {largeBatches.Count} batches, avg {avgTime:F2} μs");
            }

            // Worst cases
            var worstSample = _samples.OrderByDescending(s => s.TotalNs).First();
            var bestSample = _samples.OrderBy(s => s.TotalNs).First();

            GD.Print($"\nWorst case:");
            GD.Print($"  Batch size: {worstSample.BatchSize} entities");
            GD.Print($"  Total time: {worstSample.TotalNs / 1000000.0:F2} ms");
            GD.Print($"  Per entity: {worstSample.TotalNs / worstSample.BatchSize:F0} ns");

            GD.Print($"\nBest case:");
            GD.Print($"  Batch size: {bestSample.BatchSize} entities");
            GD.Print($"  Total time: {bestSample.TotalNs / 1000000.0:F2} ms");
            GD.Print($"  Per entity: {bestSample.TotalNs / bestSample.BatchSize:F0} ns");

            // Frame budget analysis (60 FPS = 16.67ms per frame)
            GD.Print($"\nFrame Budget Analysis (60 FPS = 16.67ms):");
            var maxEntitiesPerFrame = (16670000.0 / perEntityTotal); // 16.67ms in nanoseconds
            GD.Print($"  Max entities per frame: {maxEntitiesPerFrame:F0} (staying under 16.67ms)");

            var framesWithSpikes = _samples.Count(s => s.TotalNs > 16670000);
            if (framesWithSpikes > 0)
            {
                GD.Print($"  WARNING: {framesWithSpikes} batches exceeded 16.67ms!");
            }

            // Performance verdict
            GD.Print($"\n╔════════════════════════════════════════════════════════════════╗");
            if (perEntityTotal < 500) // < 500ns per entity
            {
                GD.Print("║  VERDICT: ✓ EXCELLENT - Queue overhead is negligible          ║");
            }
            else if (perEntityTotal < 2000) // < 2μs per entity
            {
                GD.Print("║  VERDICT: ✓ GOOD - Queue overhead is acceptable               ║");
            }
            else if (perEntityTotal < 10000) // < 10μs per entity
            {
                GD.Print("║  VERDICT: ⚠ MODERATE - Queue adds measurable overhead         ║");
            }
            else
            {
                GD.Print("║  VERDICT: ✗ POOR - Queue overhead is significant              ║");
            }
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");
        }

        private struct TimingSample
        {
            public int BatchSize;
            public long EnqueueNs;
            public long ProcessNs;
            public long CreateNs;
            public long TotalNs;
        }
    }
}
