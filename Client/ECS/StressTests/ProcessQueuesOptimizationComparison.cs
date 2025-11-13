#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.WorldECS;

namespace Client.ECS.StressTests
{
    /// <summary>
    /// Compares 13 different ProcessQueues optimization strategies:
    /// V1-V6: Original implementations (call ToArray() at site)
    /// V7-V12: "Span" versions (use List overload, ToArray() in World)
    /// V13: TRUE AsSpan version (CollectionsMarshal.AsSpan path)
    ///
    /// V1:  Simple list reuse (baseline)
    /// V2:  Adaptive threshold (<500 immediate, >=500 batched)
    /// V3:  Chunked processing (1000-entity chunks)
    /// V4:  Dynamic threshold (queue.count * 0.001)
    /// V5:  Parallel.For on builder invocation
    /// V6:  Parallel with chunked partitioning
    /// V7-V12: Same as V1-V6 but using List<Entity> overload
    /// V13: V7 + explicit CollectionsMarshal.AsSpan intermediate step
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
            public double[] Times;    // 13 versions
            public double[] PerEntityNs; // 13 versions
            public string Winner;
            public string SpanWinner;  // Winner among V7-V13
        }

        private readonly List<TestResult> _results = new();
        private readonly string[] _versionNames = new[]
        {
            "V1: Reuse",
            "V2: Adaptive",
            "V3: Chunked",
            "V4: Dynamic",
            "V5: Parallel",
            "V6: ParChunk",
            "V7: SpanReuse",
            "V8: SpanAdapt",
            "V9: SpanChunk",
            "V10: SpanDyn",
            "V11: SpanParal",
            "V12: SpanParCh",
            "V13: TrueAsSpan"
        };

        public override void _Ready()
        {
            // Create our own World for testing
            Runtime = new RuntimeContext(HostEnvironment.Capture(), "ProcessQueuesOptimizationComparison");
            Logging.Host = this;
            _world = new World(this);

            GD.Print("╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║     PROCESSQUEUES OPTIMIZATION - FULL COMPARISON (13 VERS)    ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            GD.Print("Testing 13 optimization strategies:");
            GD.Print("  V1:  Simple list reuse (ToArray at site)");
            GD.Print("  V2:  Adaptive threshold (ToArray at site)");
            GD.Print("  V3:  Chunked processing (ToArray at site)");
            GD.Print("  V4:  Dynamic threshold (ToArray at site)");
            GD.Print("  V5:  Parallel.For (ToArray at site)");
            GD.Print("  V6:  Parallel chunked (ToArray at site)");
            GD.Print("  V7-V12: Same as V1-V6 but using List<Entity> overload");
            GD.Print("  V13: TRUE AsSpan (CollectionsMarshal.AsSpan path)");
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

            // Test all 13 versions
            var times = new double[13];
            Action<ConcurrentQueue<Action<Entity>>, List<Entity>>[] testFuncs = new[]
            {
                ProcessQueuesV1, ProcessQueuesV2, ProcessQueuesV3,
                ProcessQueuesV4, ProcessQueuesV5, ProcessQueuesV6,
                ProcessQueuesV7, ProcessQueuesV8, ProcessQueuesV9,
                ProcessQueuesV10, ProcessQueuesV11, ProcessQueuesV12,
                ProcessQueuesV13
            };

            for (int i = 0; i < 13; i++)
            {
                times[i] = TestVersion(entityCount, testFuncs[i]);
                ClearAllEntities();
            }

            // Record results
            var perEntityNs = times.Select(t => (t * 1_000_000.0) / entityCount).ToArray();

            // Determine overall winner
            var minTime = times.Min();
            int winnerIdx = Array.IndexOf(times, minTime);

            // Determine span winner (V7-V13)
            var spanTimes = times.Skip(6).ToArray();
            var spanMinTime = spanTimes.Min();
            int spanWinnerIdx = 6 + Array.IndexOf(spanTimes, spanMinTime);

            var result = new TestResult
            {
                EntityCount = entityCount,
                Times = times,
                PerEntityNs = perEntityNs,
                Winner = $"V{winnerIdx + 1}",
                SpanWinner = $"V{spanWinnerIdx + 1}"
            };

            _results.Add(result);

            // Print results
            for (int i = 0; i < 13; i++)
            {
                string marker = (i == winnerIdx) ? " ⭐" : "";
                GD.Print($"  {_versionNames[i],-15}: {times[i],7:F3} ms ({perEntityNs[i],6:F0} ns/ent){marker}");
            }
            GD.Print($"  Winner: {result.Winner} | Span Winner: {result.SpanWinner}");

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
                int captured = i; // Capture for closure
                queue.Enqueue(entity =>
                {
                    _world!.EnqueueComponentAdd(entity.Index,
                        ComponentManager.GetTypeId<Position>(),
                        new Position { X = captured, Y = captured, Z = captured });
                });
            }

            // Time the ProcessQueues variant
            _sw.Restart();
            processFunc(queue, createdEntities);
            _sw.Stop();

            return _sw.Elapsed.TotalMilliseconds;
        }

        // ═══════════════════════════════════════════════════════════════
        // V1-V6: Original versions (ToArray at call site)
        // ═══════════════════════════════════════════════════════════════

        private void ProcessQueuesV1(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            createdEntities.Clear();
            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();
                createdEntities.Add(entity);
                try { builder?.Invoke(entity); }
                catch (Exception ex) { GD.PrintErr($"[V1] {ex}"); }
            }
            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
        }

        private void ProcessQueuesV2(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int THRESHOLD = 500;
            int queueSize = queue.Count;

            if (queueSize < THRESHOLD)
            {
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V2] {ex}"); }
                }
            }
            else
            {
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V2] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }

        private void ProcessQueuesV3(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int CHUNK_SIZE = 1000;
            int queueSize = queue.Count;
            if (queueSize == 0) return;

            createdEntities.Clear();
            createdEntities.Capacity = Math.Max(createdEntities.Capacity, Math.Min(queueSize, CHUNK_SIZE));

            int chunks = queueSize / CHUNK_SIZE;
            int remainder = queueSize % CHUNK_SIZE;

            for (int c = 0; c < chunks; c++)
            {
                createdEntities.Clear();
                for (int i = 0; i < CHUNK_SIZE && queue.TryDequeue(out var builder); i++)
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V3] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }

            if (remainder > 0)
            {
                createdEntities.Clear();
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V3] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }

        private void ProcessQueuesV4(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            bool shouldBatch = (queueSize * 0.001) > 1;

            if (!shouldBatch)
            {
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V4] {ex}"); }
                }
            }
            else
            {
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V4] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
            }
        }

        private void ProcessQueuesV5(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

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

            Parallel.For(0, createdEntities.Count, i =>
            {
                try { builders[i]?.Invoke(createdEntities[i]); }
                catch (Exception ex) { GD.PrintErr($"[V5] {ex}"); }
            });

            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
        }

        private void ProcessQueuesV6(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

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

            const int CHUNK_SIZE = 1000;
            int chunkCount = (createdEntities.Count + CHUNK_SIZE - 1) / CHUNK_SIZE;

            Parallel.For(0, chunkCount, chunkIdx =>
            {
                int start = chunkIdx * CHUNK_SIZE;
                int end = Math.Min(start + CHUNK_SIZE, createdEntities.Count);
                for (int i = start; i < end; i++)
                {
                    try { builders[i]?.Invoke(createdEntities[i]); }
                    catch (Exception ex) { GD.PrintErr($"[V6] {ex}"); }
                }
            });

            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities.ToArray(), 0, createdEntities.Count);
        }

        // ═══════════════════════════════════════════════════════════════
        // V7-V12: "Span" versions (use List<Entity> overload)
        // ═══════════════════════════════════════════════════════════════

        private void ProcessQueuesV7(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            createdEntities.Clear();
            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();
                createdEntities.Add(entity);
                try { builder?.Invoke(entity); }
                catch (Exception ex) { GD.PrintErr($"[V7] {ex}"); }
            }
            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
        }

        private void ProcessQueuesV8(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int THRESHOLD = 500;
            int queueSize = queue.Count;

            if (queueSize < THRESHOLD)
            {
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V8] {ex}"); }
                }
            }
            else
            {
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V8] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
            }
        }

        private void ProcessQueuesV9(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            const int CHUNK_SIZE = 1000;
            int queueSize = queue.Count;
            if (queueSize == 0) return;

            createdEntities.Clear();
            createdEntities.Capacity = Math.Max(createdEntities.Capacity, Math.Min(queueSize, CHUNK_SIZE));

            int chunks = queueSize / CHUNK_SIZE;
            int remainder = queueSize % CHUNK_SIZE;

            for (int c = 0; c < chunks; c++)
            {
                createdEntities.Clear();
                for (int i = 0; i < CHUNK_SIZE && queue.TryDequeue(out var builder); i++)
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V9] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
            }

            if (remainder > 0)
            {
                createdEntities.Clear();
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V9] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
            }
        }

        private void ProcessQueuesV10(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            bool shouldBatch = (queueSize * 0.001) > 1;

            if (!shouldBatch)
            {
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V10] {ex}"); }
                }
            }
            else
            {
                createdEntities.Clear();
                createdEntities.Capacity = Math.Max(createdEntities.Capacity, queueSize);
                while (queue.TryDequeue(out var builder))
                {
                    var entity = _world!.CreateEntity();
                    createdEntities.Add(entity);
                    try { builder?.Invoke(entity); }
                    catch (Exception ex) { GD.PrintErr($"[V10] {ex}"); }
                }
                if (createdEntities.Count > 0)
                    _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
            }
        }

        private void ProcessQueuesV11(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

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

            Parallel.For(0, createdEntities.Count, i =>
            {
                try { builders[i]?.Invoke(createdEntities[i]); }
                catch (Exception ex) { GD.PrintErr($"[V11] {ex}"); }
            });

            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
        }

        private void ProcessQueuesV12(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            int queueSize = queue.Count;
            if (queueSize == 0) return;

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

            const int CHUNK_SIZE = 1000;
            int chunkCount = (createdEntities.Count + CHUNK_SIZE - 1) / CHUNK_SIZE;

            Parallel.For(0, chunkCount, chunkIdx =>
            {
                int start = chunkIdx * CHUNK_SIZE;
                int end = Math.Min(start + CHUNK_SIZE, createdEntities.Count);
                for (int i = start; i < end; i++)
                {
                    try { builders[i]?.Invoke(createdEntities[i]); }
                    catch (Exception ex) { GD.PrintErr($"[V12] {ex}"); }
                }
            });

            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreated(createdEntities);  // ← List overload!
        }

        // ═══════════════════════════════════════════════════════════════
        // V13: TRUE AsSpan version (CollectionsMarshal.AsSpan path)
        // ═══════════════════════════════════════════════════════════════
        private void ProcessQueuesV13(ConcurrentQueue<Action<Entity>> queue, List<Entity> createdEntities)
        {
            createdEntities.Clear();
            while (queue.TryDequeue(out var builder))
            {
                var entity = _world!.CreateEntity();
                createdEntities.Add(entity);
                try { builder?.Invoke(entity); }
                catch (Exception ex) { GD.PrintErr($"[V13] {ex}"); }
            }
            if (createdEntities.Count > 0)
                _world!.FireEntityBatchCreatedSpanPath(createdEntities);  // ← SpanPath overload!
        }

        private void ClearAllEntities()
        {
            var buffer = new CommandBuffer();
            var archetypes = _world!.QueryArchetypes(typeof(Position));

            foreach (var archetype in archetypes)
            {
                var entities = archetype.GetEntityArray();
                foreach (var entity in entities)
                    buffer.DestroyEntity(entity);
            }

            buffer.Apply(_world);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void PrintFinalComparison()
        {
            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║     PROCESSQUEUES OPTIMIZATION - FINAL COMPARISON             ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            GD.Print("Per-Entity Cost (nanoseconds) - Lower is Better:");
            GD.Print("Count    | V1    | V2    | V3    | V4    | V5    | V6    | V7    | V8    | V9    | V10   | V11   | V12   | V13   | Winner");
            GD.Print("---------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|--------");

            foreach (var result in _results)
            {
                var ns = result.PerEntityNs;
                GD.Print($"{result.EntityCount,8:N0} | {ns[0],5:F0} | {ns[1],5:F0} | {ns[2],5:F0} | {ns[3],5:F0} | {ns[4],5:F0} | {ns[5],5:F0} | {ns[6],5:F0} | {ns[7],5:F0} | {ns[8],5:F0} | {ns[9],5:F0} | {ns[10],5:F0} | {ns[11],5:F0} | {ns[12],5:F0} | {result.Winner}");
            }

            // Count wins
            var wins = new int[13];
            foreach (var result in _results)
            {
                int winnerIdx = int.Parse(result.Winner.Substring(1)) - 1;
                wins[winnerIdx]++;
            }

            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║                    OVERALL WINNER SUMMARY                     ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            for (int i = 0; i < 13; i++)
            {
                GD.Print($"{_versionNames[i],-20}: {wins[i]}/{_results.Count} wins");
            }

            int overallWinnerIdx = Array.IndexOf(wins, wins.Max());
            GD.Print($"\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print($"║  OVERALL WINNER: {_versionNames[overallWinnerIdx],-42} ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝");

            // Compare V1-V6 vs V7-V13
            int origWins = wins.Take(6).Sum();
            int spanWins = wins.Skip(6).Sum();

            GD.Print($"\nV1-V6 (ToArray at site):      {origWins}/{_results.Count} total wins");
            GD.Print($"V7-V13 (List/Span overload):  {spanWins}/{_results.Count} total wins");

            if (spanWins > origWins)
                GD.Print("\n✓ List<Entity> overload provides measurable benefit!");
            else if (spanWins == origWins)
                GD.Print("\n→ List<Entity> overload has no significant impact (same performance)");
            else
                GD.Print("\n✗ List<Entity> overload is actually slower (cache locality issue?)");

            // V7 vs V13 comparison
            int v7Wins = wins[6];
            int v13Wins = wins[12];

            GD.Print($"\nV7 (List overload):           {v7Wins}/{_results.Count} wins");
            GD.Print($"V13 (TRUE AsSpan path):       {v13Wins}/{_results.Count} wins");

            if (v13Wins > v7Wins)
                GD.Print("\n✓ CollectionsMarshal.AsSpan provides additional benefit!");
            else if (v13Wins == v7Wins)
                GD.Print("\n→ AsSpan path has same performance as List overload (no extra benefit)");
            else
                GD.Print("\n✗ AsSpan path is slower than simple List overload");

            GD.Print("");
        }
    }
}
