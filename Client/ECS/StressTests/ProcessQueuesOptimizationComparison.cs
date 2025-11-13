#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.WorldECS;

namespace Client.ECS.StressTests
{
    /// <summary>
    /// Compares 6 different ProcessQueues optimization strategies:
    /// V1: Simple list reuse (baseline optimization)
    /// V2: Adaptive threshold (small=immediate, large=batched)
    /// V3: Chunked processing with modulo
    /// V4: Dynamic threshold (user's suggested queue.count * 0.001)
    /// V5: Parallel.For on builder invocation
    /// V6: Parallel with chunked partitioning
    /// </summary>
    public partial class ProcessQueuesOptimizationComparison : Node, IHost
    {
        private World? _world;
        private readonly Stopwatch _sw = new();

        // IHost implementation
        public RuntimeContext Runtime { get; private set; } = null!;
        public EnvironmentType Environment => EnvironmentType.Hybrid;
        public object? GetRootHandle() => this;
        public void Log(LogEntry entry) => GD.Print($"[{entry.Severity}] {entry.Message}");

        // Test configurations
        private readonly int[] _testSizes = { 10, 100, 1_000, 10_000, 100_000 };
        private int _currentTestIndex = 0;

        private struct TestResult
        {
            public int EntityCount;
            public double V1_TimeMs;
            public double V2_TimeMs;
            public double V3_TimeMs;
            public double V4_TimeMs;
            public double V5_TimeMs;
            public double V6_TimeMs;
            public double V1_PerEntityNs;
            public double V2_PerEntityNs;
            public double V3_PerEntityNs;
            public double V4_PerEntityNs;
            public double V5_PerEntityNs;
            public double V6_PerEntityNs;
            public string Winner;
        }

        private readonly List<TestResult> _results = new();

        public override void _Ready()
        {
            // Create our own World for testing
            Runtime = new RuntimeContext(HostEnvironment.Capture(), "ProcessQueuesOptimizationComparison");
            Logging.Host = this;
            _world = new World(this);

            GD.Print("╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║     PROCESSQUEUES OPTIMIZATION COMPARISON TEST                ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            GD.Print("Testing 6 optimization strategies:");
            GD.Print("  V1: Simple list reuse (baseline)");
            GD.Print("  V2: Adaptive threshold (<500 immediate, >=500 batched)");
            GD.Print("  V3: Chunked processing (1000-entity chunks)");
            GD.Print("  V4: Dynamic threshold (queue.count * 0.001)");
            GD.Print("  V5: Parallel.For on builder invocation");
            GD.Print("  V6: Parallel with chunked partitioning");
            GD.Print("");

            RunNextTest();
        }

        private void RunNextTest()
        {
            if (_currentTestIndex >= _testSizes.Length)
            {
                PrintFinalComparison();
                QueueFree();
                return;
            }

            int entityCount = _testSizes[_currentTestIndex];
            GD.Print($"\n[Test {_currentTestIndex + 1}/{_testSizes.Length}] Testing {entityCount:N0} entities...");

            ClearAllEntities();

            // Test all 6 versions
            var v1Time = TestVersion(entityCount, ProcessQueuesV1);
            ClearAllEntities();

            var v2Time = TestVersion(entityCount, ProcessQueuesV2);
            ClearAllEntities();

            var v3Time = TestVersion(entityCount, ProcessQueuesV3);
            ClearAllEntities();

            var v4Time = TestVersion(entityCount, ProcessQueuesV4);
            ClearAllEntities();

            var v5Time = TestVersion(entityCount, ProcessQueuesV5);
            ClearAllEntities();

            var v6Time = TestVersion(entityCount, ProcessQueuesV6);
            ClearAllEntities();

            // Record results
            var result = new TestResult
            {
                EntityCount = entityCount,
                V1_TimeMs = v1Time,
                V2_TimeMs = v2Time,
                V3_TimeMs = v3Time,
                V4_TimeMs = v4Time,
                V5_TimeMs = v5Time,
                V6_TimeMs = v6Time,
                V1_PerEntityNs = (v1Time * 1_000_000.0) / entityCount,
                V2_PerEntityNs = (v2Time * 1_000_000.0) / entityCount,
                V3_PerEntityNs = (v3Time * 1_000_000.0) / entityCount,
                V4_PerEntityNs = (v4Time * 1_000_000.0) / entityCount,
                V5_PerEntityNs = (v5Time * 1_000_000.0) / entityCount,
                V6_PerEntityNs = (v6Time * 1_000_000.0) / entityCount,
            };

            // Determine winner
            var times = new[] { v1Time, v2Time, v3Time, v4Time, v5Time, v6Time };
            var minTime = times.Min();
            if (Math.Abs(v1Time - minTime) < 0.001) result.Winner = "V1";
            else if (Math.Abs(v2Time - minTime) < 0.001) result.Winner = "V2";
            else if (Math.Abs(v3Time - minTime) < 0.001) result.Winner = "V3";
            else if (Math.Abs(v4Time - minTime) < 0.001) result.Winner = "V4";
            else if (Math.Abs(v5Time - minTime) < 0.001) result.Winner = "V5";
            else result.Winner = "V6";

            _results.Add(result);

            GD.Print($"  V1 (List Reuse):      {v1Time:F3} ms ({result.V1_PerEntityNs:F0} ns/entity)");
            GD.Print($"  V2 (Adaptive):        {v2Time:F3} ms ({result.V2_PerEntityNs:F0} ns/entity)");
            GD.Print($"  V3 (Chunked):         {v3Time:F3} ms ({result.V3_PerEntityNs:F0} ns/entity)");
            GD.Print($"  V4 (Dynamic):         {v4Time:F3} ms ({result.V4_PerEntityNs:F0} ns/entity)");
            GD.Print($"  V5 (Parallel.For):    {v5Time:F3} ms ({result.V5_PerEntityNs:F0} ns/entity)");
            GD.Print($"  V6 (Parallel Chunked):{v6Time:F3} ms ({result.V6_PerEntityNs:F0} ns/entity)");
            GD.Print($"  Winner: {result.Winner}");

            _currentTestIndex++;
            CallDeferred(nameof(RunNextTest));
        }

        private double TestVersion(int count, Action<ConcurrentQueue<Action<Entity>>, List<Entity>> processFunc)
        {
            var queue = new ConcurrentQueue<Action<Entity>>();
            var createdEntities = new List<Entity>(10000);

            // Enqueue entities
            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(entity =>
                {
                    _world!.EnqueueComponentAdd(entity.Index,
                        ComponentManager.GetTypeId<Position>(),
                        new Position { X = i, Y = i, Z = i });
                });
            }

            // Time the ProcessQueues variant
            _sw.Restart();
            processFunc(queue, createdEntities);
            _sw.Stop();

            return _sw.Elapsed.TotalMilliseconds;
        }

        // ═══════════════════════════════════════════════════════════════
        // V1: SIMPLE LIST REUSE (Baseline Optimization)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV1(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            createdEntities.Clear(); // Reuse list instead of allocating

            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();
                createdEntities.Add(entity);
                try
                {
                    builder?.Invoke(entity);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V1] Entity builder exception: {ex}");
                }
            }

            if (createdEntities.Count > 0)
            {
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // V2: ADAPTIVE THRESHOLD (<500 = immediate, >=500 = batched)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV2(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int BATCH_THRESHOLD = 500;
            int queueSize = queue.Count;

            if (queueSize < BATCH_THRESHOLD)
            {
                // Small batch: Process immediately without tracking
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.Print($"[V2] Entity builder exception: {ex}", LogSeverity.Error);
                    }
                }
            }
            else
            {
                // Large batch: Collect and fire event once
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);

                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V2] Entity builder exception: {ex}");
                    }
                }

                if (createdEntities.Count > 0)
                {
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // V3: CHUNKED PROCESSING (1000-entity chunks + remainder)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV3(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int CHUNK_SIZE = 1000;
            int queueSize = queue.Count;

            if (queueSize == 0) return;

            createdEntities.Clear();
            createdEntities.Capacity = Math.Max(createdEntities.Capacity, Math.Min(queueSize, CHUNK_SIZE));

            int chunks = queueSize / CHUNK_SIZE;
            int remainder = queueSize % CHUNK_SIZE;

            // Process full chunks
            for (int c = 0; c < chunks; c++)
            {
                createdEntities.Clear();

                for (int i = 0; i < CHUNK_SIZE && queue.TryDequeue(out var builder); i++)
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V3] Entity builder exception: {ex}");
                    }
                }

                if (createdEntities.Count > 0)
                {
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
                }
            }

            // Process remainder
            if (remainder > 0)
            {
                createdEntities.Clear();

                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V3] Entity builder exception: {ex}");
                    }
                }

                if (createdEntities.Count > 0)
                {
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // V4: DYNAMIC THRESHOLD (queue.count * 0.001)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV4(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;

            // User's suggested threshold: queue.count * 0.001 > 1 → batch
            // This means batch if queueSize > 1000
            bool shouldBatch = (queueSize * 0.001) > 1;

            if (!shouldBatch)
            {
                // Immediate spawn (no tracking)
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V4] Entity builder exception: {ex}");
                    }
                }
            }
            else
            {
                // Batch spawn (collect and fire event)
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);

                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try
                    {
                        builder?.Invoke(entity);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V4] Entity builder exception: {ex}");
                    }
                }

                if (createdEntities.Count > 0)
                {
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
                }
            }
        }

        private void ClearAllEntities()
        {
            var buffer = new CommandBuffer();
            var archetypes = _world!.QueryArchetypes(typeof(Position));

            foreach (var archetype in archetypes)
            {
                var entities = archetype.GetEntityArray();
                foreach (var entity in entities)
                {
                    buffer.DestroyEntity(entity);
                }
            }

            buffer.Apply(_world);

            // Let GC settle
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void PrintFinalComparison()
        {
            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║     PROCESSQUEUES OPTIMIZATION - FINAL COMPARISON             ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            GD.Print("Entity Count | V1: Reuse | V2: Adaptive | V3: Chunked | V4: Dynamic | V5: Parallel | V6: ParChunk | Winner");
            GD.Print("-------------|-----------|--------------|-------------|-------------|--------------|-------------|--------");

            foreach (var result in _results)
            {
                string winner = result.Winner;
                GD.Print($"{result.EntityCount,12:N0} | {result.V1_TimeMs,8:F3}ms | {result.V2_TimeMs,11:F3}ms | {result.V3_TimeMs,10:F3}ms | {result.V4_TimeMs,10:F3}ms | {result.V5_TimeMs,11:F3}ms | {result.V6_TimeMs,10:F3}ms | {winner}");
            }

            GD.Print("\nPer-Entity Cost (nanoseconds):");
            GD.Print("Entity Count | V1: Reuse | V2: Adaptive | V3: Chunked | V4: Dynamic | V5: Parallel | V6: ParChunk | Winner");
            GD.Print("-------------|-----------|--------------|-------------|-------------|--------------|-------------|--------");

            foreach (var result in _results)
            {
                string winner = result.Winner;
                GD.Print($"{result.EntityCount,12:N0} | {result.V1_PerEntityNs,8:F0}ns | {result.V2_PerEntityNs,11:F0}ns | {result.V3_PerEntityNs,10:F0}ns | {result.V4_PerEntityNs,10:F0}ns | {result.V5_PerEntityNs,11:F0}ns | {result.V6_PerEntityNs,10:F0}ns | {winner}");
            }

            // Count wins for each version
            var wins = new Dictionary<string, int>
            {
                ["V1"] = _results.Count(r => r.Winner == "V1"),
                ["V2"] = _results.Count(r => r.Winner == "V2"),
                ["V3"] = _results.Count(r => r.Winner == "V3"),
                ["V4"] = _results.Count(r => r.Winner == "V4"),
                ["V5"] = _results.Count(r => r.Winner == "V5"),
                ["V6"] = _results.Count(r => r.Winner == "V6")
            };

            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║                    OVERALL WINNER SUMMARY                     ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            GD.Print($"V1 (List Reuse):        {wins["V1"]}/{_results.Count} wins");
            GD.Print($"V2 (Adaptive):          {wins["V2"]}/{_results.Count} wins");
            GD.Print($"V3 (Chunked):           {wins["V3"]}/{_results.Count} wins");
            GD.Print($"V4 (Dynamic):           {wins["V4"]}/{_results.Count} wins");
            GD.Print($"V5 (Parallel.For):      {wins["V5"]}/{_results.Count} wins");
            GD.Print($"V6 (Parallel Chunked):  {wins["V6"]}/{_results.Count} wins");

            var overallWinner = wins.OrderByDescending(kv => kv.Value).First();

            GD.Print($"\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print($"║  OVERALL WINNER: {overallWinner.Key,-45} ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝");

            // Recommendation
            GD.Print("\nRECOMMENDATION:");
            if (overallWinner.Key == "V1")
            {
                GD.Print("  → Use V1 (Simple List Reuse)");
                GD.Print("  → Simplest implementation with best overall performance");
                GD.Print("  → One-line change: reuse _createdEntitiesCache.Clear()");
            }
            else if (overallWinner.Key == "V2")
            {
                GD.Print("  → Use V2 (Adaptive Threshold)");
                GD.Print("  → Optimizes small batches by skipping event tracking");
                GD.Print("  → Best balance of simplicity and performance");
            }
            else if (overallWinner.Key == "V3")
            {
                GD.Print("  → Use V3 (Chunked Processing)");
                GD.Print("  → Best for very large batches (10k+ entities)");
                GD.Print("  → Prevents memory spikes with chunked events");
            }
            else if (overallWinner.Key == "V4")
            {
                GD.Print("  → Use V4 (Dynamic Threshold)");
                GD.Print("  → User's suggested approach performs best");
                GD.Print("  → Threshold automatically scales with queue size");
            }
            else if (overallWinner.Key == "V5")
            {
                GD.Print("  → Use V5 (Parallel.For)");
                GD.Print("  → Parallel builder invocation wins");
                GD.Print("  → Best for systems with multi-core CPUs");
            }
            else
            {
                GD.Print("  → Use V6 (Parallel Chunked)");
                GD.Print("  → Parallel with chunked partitioning wins");
                GD.Print("  → Best cache locality with parallelism");
            }

            GD.Print("");
        }

        // ═══════════════════════════════════════════════════════════════
        // V5: PARALLEL.FOR (Parallel builder invocation)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV5(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

            // Pre-allocate entities sequentially (EntityManager.Create is NOT thread-safe)
            var builders = new Action<Entity>[queueSize];
            createdEntities.Clear();
            createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);

            int index = 0;
            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();  // Sequential allocation
                createdEntities.Add(entity);
                builders[index++] = builder;
            }

            // Parallel invoke builders (EnqueueComponentAdd uses ConcurrentQueue - thread-safe)
            Parallel.For(0, createdEntities.Count, i =>
            {
                try
                {
                    builders[i]?.Invoke(createdEntities[i]);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V5] Entity builder exception: {ex}");
                }
            });

            // Fire event once
            if (createdEntities.Count > 0)
            {
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // V6: PARALLEL WITH CHUNKED PARTITIONING (Cache-friendly)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV6(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

            // Pre-allocate entities sequentially
            var builders = new Action<Entity>[queueSize];
            createdEntities.Clear();
            createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);

            int index = 0;
            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();
                createdEntities.Add(entity);
                builders[index++] = builder;
            }

            // Parallel with chunked partitioning (better cache locality)
            const int CHUNK_SIZE = 1000;
            int chunkCount = (createdEntities.Count + CHUNK_SIZE - 1) / CHUNK_SIZE;

            Parallel.For(0, chunkCount, chunkIdx =>
            {
                int start = chunkIdx * CHUNK_SIZE;
                int end = Math.Min(start + CHUNK_SIZE, createdEntities.Count);

                for (int i = start; i < end; i++)
                {
                    try
                    {
                        builders[i]?.Invoke(createdEntities[i]);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V6] Entity builder exception: {ex}");
                    }
                }
            });

            // Fire event once
            if (createdEntities.Count > 0)
            {
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }
    }
}
