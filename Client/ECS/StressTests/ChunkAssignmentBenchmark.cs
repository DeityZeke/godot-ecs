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
    /// Benchmarks chunk assignment performance for baseline comparison.
    /// Tests:
    /// 1. Entity creation + initial chunk assignment
    /// 2. Chunk reassignment during movement simulation
    ///
    /// BASELINE VERSION: Works with current synchronous ChunkSystem
    /// </summary>
    public class ChunkAssignmentBenchmark
    {
        private World _world;
        private ChunkSystem? _chunkSystem;

        public ChunkAssignmentBenchmark(World world)
        {
            _world = world;
            _chunkSystem = world.Systems.GetSystem<ChunkSystem>() as ChunkSystem;
        }

        /// <summary>
        /// Run baseline benchmark suite and return results.
        /// </summary>
        public BenchmarkResults RunBenchmarks()
        {
            var results = new BenchmarkResults();

            Logging.Log("=================================================================");
            Logging.Log("  CHUNK ASSIGNMENT BENCHMARK SUITE (BASELINE)");
            Logging.Log("=================================================================");

            // Test 1: Entity creation benchmark
            Logging.Log("\n[Test 1] Entity Creation + Initial Chunk Assignment");
            var creationResult = BenchmarkEntityCreation(100000);
            results.BaselineCreationTimeMs = creationResult.TotalTimeMs;
            results.BaselineCreationThroughput = creationResult.EntitiesPerSecond;

            CleanupEntities();
            System.Threading.Thread.Sleep(100); // Give GC a moment

            // Test 2: Movement processing benchmark
            Logging.Log("\n[Test 2] Movement + Chunk Reassignment");
            var movementResult = BenchmarkMovementProcessing(100000, frames: 60);
            results.BaselineMovementAvgMs = movementResult.AverageFrameTimeMs;
            results.BaselineMovementPeakMs = movementResult.PeakFrameTimeMs;

            CleanupEntities();
            System.Threading.Thread.Sleep(100);

            // Print summary
            PrintBenchmarkSummary(results);

            return results;
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

            // Process World tick to trigger chunk assignments
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
            result.FrameTimesMs = new List<double>();

            Logging.Log($"  Setting up {entityCount:N0} entities...");
            SetupMovingEntities(entityCount);

            Logging.Log($"  Running {frames} frames of movement...");
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < frames; frame++)
            {
                var frameStart = sw.Elapsed.TotalMilliseconds;
                _world.Tick(0.016);
                var frameTime = sw.Elapsed.TotalMilliseconds - frameStart;
                result.FrameTimesMs.Add(frameTime);

                if (frame % 10 == 0)
                    Logging.Log($"  Frame {frame}/{frames}: {frameTime:F2}ms");
            }

            sw.Stop();

            result.CalculateStats();

            Logging.Log($"  Completed {frames} frames");
            Logging.Log($"  Avg Frame: {result.AverageFrameTimeMs:F2}ms");
            Logging.Log($"  Peak Frame: {result.PeakFrameTimeMs:F2}ms");
            Logging.Log($"  Min Frame: {result.MinFrameTimeMs:F2}ms");

            return result;
        }

        private void SetupMovingEntities(int entityCount)
        {
            var buffer = new CommandBuffer();

            for (int i = 0; i < entityCount; i++)
            {
                float x = (i % 100) * 10f;
                float y = 0f;
                float z = (i / 100) * 10f;

                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position { X = x, Y = y, Z = z });
                    builder.Add(new Velocity { X = 0.5f, Y = 0f, Z = 0.5f }); // Moving entities
                    builder.Add(new RenderTag());
                });
            }

            buffer.Apply(_world);
            _world.Tick(0.016); // Initial chunk assignment
        }

        private void CleanupEntities()
        {
            // Destroy all entities with Position component
            var archetypes = _world.QueryArchetypes(typeof(Position));
            var buffer = new CommandBuffer();

            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0) continue;

                var entities = archetype.GetEntityArray();
                foreach (var entity in entities)
                {
                    buffer.DestroyEntity(entity);
                }
            }

            buffer.Apply(_world);
            _world.Tick(0.016);
        }

        private void PrintBenchmarkSummary(BenchmarkResults results)
        {
            Logging.Log("\n=================================================================");
            Logging.Log("  BENCHMARK RESULTS SUMMARY (BASELINE)");
            Logging.Log("=================================================================");
            Logging.Log("");
            Logging.Log("--- ENTITY CREATION ---");
            Logging.Log($"  Time:       {results.BaselineCreationTimeMs:F2}ms");
            Logging.Log($"  Throughput: {results.BaselineCreationThroughput:F0} ent/s");
            Logging.Log("");
            Logging.Log("--- MOVEMENT PROCESSING ---");
            Logging.Log($"  Avg Frame:  {results.BaselineMovementAvgMs:F2}ms");
            Logging.Log($"  Peak Frame: {results.BaselineMovementPeakMs:F2}ms");
            Logging.Log("=================================================================");
        }

        public class BenchmarkResults
        {
            public double BaselineCreationTimeMs;
            public double BaselineCreationThroughput;
            public double BaselineMovementAvgMs;
            public double BaselineMovementPeakMs;
        }

        private class CreationBenchmarkResult
        {
            public double TotalTimeMs;
            public double CreationTimeMs;
            public double ApplyTimeMs;
            public double AssignmentTimeMs;
            public double EntitiesPerSecond;
        }

        private class MovementBenchmarkResult
        {
            public List<double> FrameTimesMs = new();
            public double AverageFrameTimeMs;
            public double PeakFrameTimeMs;
            public double MinFrameTimeMs;

            public void CalculateStats()
            {
                if (FrameTimesMs.Count == 0) return;

                double sum = 0;
                PeakFrameTimeMs = double.MinValue;
                MinFrameTimeMs = double.MaxValue;

                foreach (var time in FrameTimesMs)
                {
                    sum += time;
                    if (time > PeakFrameTimeMs) PeakFrameTimeMs = time;
                    if (time < MinFrameTimeMs) MinFrameTimeMs = time;
                }

                AverageFrameTimeMs = sum / FrameTimesMs.Count;
            }
        }
    }
}
