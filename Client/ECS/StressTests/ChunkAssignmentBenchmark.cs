#nullable enable

using UltraSim.ECS;
using UltraSim;
using UltraSim.ECS.Components;
using UltraSim.Server.ECS.Systems;
using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace Client.ECS.StressTests
{
    /// <summary>
    /// Benchmarks chunk assignment performance comparing synchronous vs deferred processing.
    /// Tests:
    /// 1. Initial chunk assignment for newly created entities
    /// 2. Chunk reassignment during movement
    /// 3. Parallel batch processing efficiency
    /// 4. Event handler overhead
    /// </summary>
    public class ChunkAssignmentBenchmark
    {
        private World _world;
        private ChunkSystem? _chunkSystem;
        private OptimizedMovementSystem? _movementSystem;

        public ChunkAssignmentBenchmark(World world)
        {
            _world = world;
            _chunkSystem = world.Systems.GetSystem<ChunkSystem>() as ChunkSystem;
            _movementSystem = world.Systems.GetSystem<OptimizedMovementSystem>() as OptimizedMovementSystem;
        }

        /// <summary>
        /// Run complete benchmark suite and return results.
        /// </summary>
        public BenchmarkResults RunBenchmarks()
        {
            var results = new BenchmarkResults();

            Logging.Log("=================================================================");
            Logging.Log("  CHUNK ASSIGNMENT BENCHMARK SUITE");
            Logging.Log("=================================================================");

            // Test 1: Entity creation with deferred batch processing
            Logging.Log("\n[Test 1] Entity Creation + Initial Chunk Assignment (Deferred)");
            SetChunkSystemMode(deferred: true, parallel: true);
            var deferredCreationResult = BenchmarkEntityCreation(100000);
            results.DeferredCreationTimeMs = deferredCreationResult.TotalTimeMs;
            results.DeferredCreationThroughput = deferredCreationResult.EntitiesPerSecond;

            CleanupEntities();
            System.Threading.Thread.Sleep(100); // Give GC a moment

            // Test 2: Entity creation with synchronous processing
            Logging.Log("\n[Test 2] Entity Creation + Initial Chunk Assignment (Synchronous)");
            SetChunkSystemMode(deferred: false, parallel: false);
            var syncCreationResult = BenchmarkEntityCreation(100000);
            results.SyncCreationTimeMs = syncCreationResult.TotalTimeMs;
            results.SyncCreationThroughput = syncCreationResult.EntitiesPerSecond;

            CleanupEntities();
            System.Threading.Thread.Sleep(100);

            // Test 3: Movement with deferred batch processing
            Logging.Log("\n[Test 3] Movement + Chunk Reassignment (Deferred Parallel)");
            SetChunkSystemMode(deferred: true, parallel: true);
            var deferredMovementResult = BenchmarkMovementProcessing(500000, frames: 60);
            results.DeferredMovementAvgMs = deferredMovementResult.AverageFrameTimeMs;
            results.DeferredMovementPeakMs = deferredMovementResult.PeakFrameTimeMs;

            CleanupEntities();
            System.Threading.Thread.Sleep(100);

            // Test 4: Movement with synchronous processing
            Logging.Log("\n[Test 4] Movement + Chunk Reassignment (Synchronous)");
            SetChunkSystemMode(deferred: false, parallel: false);
            var syncMovementResult = BenchmarkMovementProcessing(500000, frames: 60);
            results.SyncMovementAvgMs = syncMovementResult.AverageFrameTimeMs;
            results.SyncMovementPeakMs = syncMovementResult.PeakFrameTimeMs;

            CleanupEntities();
            System.Threading.Thread.Sleep(100);

            // Calculate improvements
            results.CalculateImprovements();

            // Print summary
            PrintBenchmarkSummary(results);

            // Restore default settings
            SetChunkSystemMode(deferred: true, parallel: true);

            return results;
        }

        private void SetChunkSystemMode(bool deferred, bool parallel)
        {
            if (_chunkSystem == null) return;

            _chunkSystem.SystemSettings.EnableDeferredBatchProcessing.Value = deferred;
            _chunkSystem.SystemSettings.ParallelBatchProcessing.Value = parallel;
            _chunkSystem.SystemSettings.ParallelBatchThreshold.Value = 2;

            Logging.Log($"  Mode: Deferred={deferred}, Parallel={parallel}");
        }

        private CreationBenchmarkResult BenchmarkEntityCreation(int entityCount)
        {
            var result = new CreationBenchmarkResult();
            var buffer = new CommandBuffer();
            var sw = Stopwatch.StartNew();

            // Create entities
            for (int i = 0; i < entityCount; i++)
            {
                float x = (i % 100) * 10f;
                float y = 0f;
                float z = (i / 100) * 10f;

                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position { X = x, Y = y, Z = z });
                    builder.Add(new Velocity { X = 1f, Y = 0f, Z = 1f });
                    builder.Add(new RenderTag());
                });
            }

            var creationTime = sw.ElapsedMilliseconds;
            buffer.Apply(_world);
            var applyTime = sw.ElapsedMilliseconds - creationTime;

            // Process World tick to trigger events and chunk assignments
            _world.Tick(0.016);

            sw.Stop();
            result.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
            result.CreationTimeMs = creationTime;
            result.ApplyTimeMs = applyTime;
            result.AssignmentTimeMs = result.TotalTimeMs - creationTime - applyTime;
            result.EntitiesPerSecond = entityCount / (result.TotalTimeMs / 1000.0);

            Logging.Log($"  Created: {entityCount:N0} entities");
            Logging.Log($"  Total Time: {result.TotalTimeMs:F2}ms");
            Logging.Log($"  - Creation: {result.CreationTimeMs:F2}ms");
            Logging.Log($"  - Apply: {result.ApplyTimeMs:F2}ms");
            Logging.Log($"  - Assignment: {result.AssignmentTimeMs:F2}ms");
            Logging.Log($"  Throughput: {result.EntitiesPerSecond:F0} entities/sec");

            return result;
        }

        private MovementBenchmarkResult BenchmarkMovementProcessing(int entityCount, int frames)
        {
            var result = new MovementBenchmarkResult();

            // Create entities first
            Logging.Log($"  Setting up {entityCount:N0} entities...");
            var buffer = new CommandBuffer();
            for (int i = 0; i < entityCount; i++)
            {
                float x = (i % 100) * 10f;
                float y = 0f;
                float z = (i / 100) * 10f;

                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position { X = x, Y = y, Z = z });
                    builder.Add(new Velocity { X = 1f, Y = 0f, Z = 1f });
                    builder.Add(new RenderTag());
                });
            }
            buffer.Apply(_world);
            _world.Tick(0.016); // Initial tick for chunk assignment

            Logging.Log($"  Running {frames} frames of movement...");

            var frameTimes = new List<double>();
            var sw = Stopwatch.StartNew();

            // Run movement frames
            for (int frame = 0; frame < frames; frame++)
            {
                var frameStart = sw.Elapsed.TotalMilliseconds;
                _world.Tick(0.016);
                var frameEnd = sw.Elapsed.TotalMilliseconds;

                double frameTime = frameEnd - frameStart;
                frameTimes.Add(frameTime);

                if (frame % 10 == 0)
                {
                    Logging.Log($"  Frame {frame}/{frames}: {frameTime:F2}ms");
                }
            }

            sw.Stop();

            result.TotalFrames = frames;
            result.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
            result.AverageFrameTimeMs = frameTimes.Count > 0
                ? frameTimes.ToArray().Average()
                : 0;
            result.PeakFrameTimeMs = frameTimes.Count > 0
                ? frameTimes.Max()
                : 0;
            result.MinFrameTimeMs = frameTimes.Count > 0
                ? frameTimes.Min()
                : 0;

            Logging.Log($"  Completed {frames} frames");
            Logging.Log($"  Avg Frame: {result.AverageFrameTimeMs:F2}ms");
            Logging.Log($"  Peak Frame: {result.PeakFrameTimeMs:F2}ms");
            Logging.Log($"  Min Frame: {result.MinFrameTimeMs:F2}ms");

            return result;
        }

        private void CleanupEntities()
        {
            Logging.Log("  Cleaning up entities...");
            int count = 0;
            foreach (var arch in _world.GetArchetypes())
            {
                count += arch.Count;
            }

            // Destroy all non-chunk entities
            var buffer = new CommandBuffer();
            foreach (var arch in _world.GetArchetypes())
            {
                var entities = arch.GetEntityArray();
                foreach (var entity in entities)
                {
                    buffer.DestroyEntity(entity);
                }
            }
            buffer.Apply(_world);
            _world.Tick(0.016);

            Logging.Log($"  Cleaned {count:N0} entities");
        }

        private void PrintBenchmarkSummary(BenchmarkResults results)
        {
            Logging.Log("\n=================================================================");
            Logging.Log("  BENCHMARK RESULTS SUMMARY");
            Logging.Log("=================================================================");
            Logging.Log("\n--- ENTITY CREATION ---");
            Logging.Log($"  Deferred:     {results.DeferredCreationTimeMs:F2}ms  ({results.DeferredCreationThroughput:F0} ent/s)");
            Logging.Log($"  Synchronous:  {results.SyncCreationTimeMs:F2}ms  ({results.SyncCreationThroughput:F0} ent/s)");
            Logging.Log($"  Improvement:  {results.CreationImprovement:F1}%");

            Logging.Log("\n--- MOVEMENT PROCESSING (Avg Frame Time) ---");
            Logging.Log($"  Deferred:     {results.DeferredMovementAvgMs:F2}ms  (peak: {results.DeferredMovementPeakMs:F2}ms)");
            Logging.Log($"  Synchronous:  {results.SyncMovementAvgMs:F2}ms  (peak: {results.SyncMovementPeakMs:F2}ms)");
            Logging.Log($"  Improvement:  {results.MovementImprovement:F1}%");

            Logging.Log("\n--- STABILITY ANALYSIS ---");
            Logging.Log($"  Deferred is:  {(results.CreationImprovement > 0 ? "FASTER" : "SLOWER")} for creation");
            Logging.Log($"  Deferred is:  {(results.MovementImprovement > 0 ? "FASTER" : "SLOWER")} for movement");

            if (results.CreationImprovement > 10 && results.MovementImprovement > 10)
            {
                Logging.Log("\n  ✓ CONCLUSION: Deferred processing shows significant improvements");
            }
            else if (results.CreationImprovement < -10 || results.MovementImprovement < -10)
            {
                Logging.Log("\n  ✗ CONCLUSION: Deferred processing may have regressions");
            }
            else
            {
                Logging.Log("\n  ~ CONCLUSION: Performance is similar between modes");
            }

            Logging.Log("=================================================================\n");
        }

        public class CreationBenchmarkResult
        {
            public double TotalTimeMs;
            public double CreationTimeMs;
            public double ApplyTimeMs;
            public double AssignmentTimeMs;
            public double EntitiesPerSecond;
        }

        public class MovementBenchmarkResult
        {
            public int TotalFrames;
            public double TotalTimeMs;
            public double AverageFrameTimeMs;
            public double PeakFrameTimeMs;
            public double MinFrameTimeMs;
        }

        public class BenchmarkResults
        {
            public double DeferredCreationTimeMs;
            public double SyncCreationTimeMs;
            public double DeferredCreationThroughput;
            public double SyncCreationThroughput;

            public double DeferredMovementAvgMs;
            public double SyncMovementAvgMs;
            public double DeferredMovementPeakMs;
            public double SyncMovementPeakMs;

            public double CreationImprovement;
            public double MovementImprovement;

            public void CalculateImprovements()
            {
                CreationImprovement = ((SyncCreationTimeMs - DeferredCreationTimeMs) / SyncCreationTimeMs) * 100.0;
                MovementImprovement = ((SyncMovementAvgMs - DeferredMovementAvgMs) / SyncMovementAvgMs) * 100.0;
            }
        }
    }
}
