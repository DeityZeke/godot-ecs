#nullable enable

using System;
using System.Diagnostics;
using Godot;
using UltraSim;
using UltraSim.ECS;
using Server.ECS.Components;

namespace Client.ECS.StressTests
{
    /// <summary>
    /// Direct comparison: Queue-based creation vs CommandBuffer creation.
    /// Tests identical workloads to isolate queue overhead.
    /// </summary>
    public partial class QueueVsCommandBufferComparison : Node
    {
        private World? _world;
        private readonly Stopwatch _sw = new();

        // Test configurations
        private readonly int[] _testSizes = { 10, 100, 1_000, 10_000, 100_000 };
        private int _currentTestIndex = 0;

        private struct TestResult
        {
            public int EntityCount;
            public double QueueTimeMs;
            public double BufferTimeMs;
            public double QueuePerEntityNs;
            public double BufferPerEntityNs;
            public double SlowdownFactor;
        }

        private readonly System.Collections.Generic.List<TestResult> _results = new();

        public override void _Ready()
        {
            Logging.Log("[QueueVsCommandBufferComparison] Starting comparison test...");

            _world = GetNode<Server.ECS.WorldECS>("/root/WorldECS").World;

            if (_world == null)
            {
                Logging.Log("[QueueVsCommandBufferComparison] ERROR: World not found!", LogSeverity.Error);
                return;
            }

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
            Logging.Log($"\n[Test {_currentTestIndex + 1}/{_testSizes.Length}] Testing {entityCount:N0} entities...");

            // Clear any existing entities
            ClearAllEntities();

            // Run both tests
            var queueTime = TestQueueCreation(entityCount);
            ClearAllEntities();

            var bufferTime = TestCommandBufferCreation(entityCount);
            ClearAllEntities();

            // Record results
            var result = new TestResult
            {
                EntityCount = entityCount,
                QueueTimeMs = queueTime,
                BufferTimeMs = bufferTime,
                QueuePerEntityNs = (queueTime * 1_000_000.0) / entityCount,
                BufferPerEntityNs = (bufferTime * 1_000_000.0) / entityCount,
                SlowdownFactor = queueTime / bufferTime
            };

            _results.Add(result);

            Logging.Log($"  Queue:         {queueTime:F3} ms ({result.QueuePerEntityNs:F0} ns/entity)");
            Logging.Log($"  CommandBuffer: {bufferTime:F3} ms ({result.BufferPerEntityNs:F0} ns/entity)");
            Logging.Log($"  Slowdown:      {result.SlowdownFactor:F2}x");

            _currentTestIndex++;
            CallDeferred(nameof(RunNextTest));
        }

        private double TestQueueCreation(int count)
        {
            _sw.Restart();

            // Enqueue entities
            for (int i = 0; i < count; i++)
            {
                _world.EnqueueCreateEntity(entity =>
                {
                    _world.EnqueueComponentAdd(entity.Index,
                        ComponentManager.GetTypeId<Position>(),
                        new Position { X = i, Y = i, Z = i });
                });
            }

            // Process queues (this is where the work happens)
            _world.Entities.ProcessQueues();
            _world.Components.ProcessQueues();

            _sw.Stop();
            return _sw.Elapsed.TotalMilliseconds;
        }

        private double TestCommandBufferCreation(int count)
        {
            var buffer = new CommandBuffer();

            _sw.Restart();

            // Queue entities in buffer
            for (int i = 0; i < count; i++)
            {
                buffer.CreateEntity(e => e.Add(new Position { X = i, Y = i, Z = i }));
            }

            // Apply buffer (this is where the work happens)
            buffer.Apply(_world);

            _sw.Stop();
            return _sw.Elapsed.TotalMilliseconds;
        }

        private void ClearAllEntities()
        {
            var buffer = new CommandBuffer();
            var archetypes = _world.QueryArchetypes(typeof(Position));

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
            Logging.Log("\n╔════════════════════════════════════════════════════════════════╗");
            Logging.Log("║         QUEUE vs COMMANDBUFFER - FINAL COMPARISON             ║");
            Logging.Log("╚════════════════════════════════════════════════════════════════╝\n");

            Logging.Log("Entity Count | Queue Time | Buffer Time | Queue/Entity | Buffer/Entity | Slowdown");
            Logging.Log("-------------|------------|-------------|--------------|---------------|----------");

            foreach (var result in _results)
            {
                Logging.Log($"{result.EntityCount,12:N0} | {result.QueueTimeMs,9:F2}ms | {result.BufferTimeMs,10:F2}ms | {result.QueuePerEntityNs,11:F0}ns | {result.BufferPerEntityNs,12:F0}ns | {result.SlowdownFactor,7:F2}x");
            }

            // Overall analysis
            var avgSlowdown = _results.Count > 0
                ? _results.Select(r => r.SlowdownFactor).Average()
                : 0;

            var worstSlowdown = _results.Count > 0
                ? _results.Max(r => r.SlowdownFactor)
                : 0;

            Logging.Log($"\nSummary:");
            Logging.Log($"  Average slowdown: {avgSlowdown:F2}x");
            Logging.Log($"  Worst slowdown:   {worstSlowdown:F2}x");

            // Find the bottleneck
            var largeTest = _results.Find(r => r.EntityCount >= 10_000);
            if (largeTest.EntityCount > 0)
            {
                Logging.Log($"\nBottleneck Analysis (10k+ entities):");

                var queueOverheadMs = largeTest.QueueTimeMs - largeTest.BufferTimeMs;
                var queueOverheadPercent = (queueOverheadMs / largeTest.QueueTimeMs) * 100;

                Logging.Log($"  Queue overhead:   {queueOverheadMs:F2} ms ({queueOverheadPercent:F1}%)");
                Logging.Log($"  Per-entity cost:  {(queueOverheadMs * 1_000_000.0) / largeTest.EntityCount:F0} ns");

                if (queueOverheadPercent < 10)
                {
                    Logging.Log($"  ✓ Queue overhead is negligible (<10%)");
                }
                else if (queueOverheadPercent < 50)
                {
                    Logging.Log($"  ⚠ Queue overhead is moderate (10-50%)");
                }
                else
                {
                    Logging.Log($"  ✗ Queue overhead is significant (>50%)");
                }
            }

            // Verdict
            Logging.Log($"\n╔════════════════════════════════════════════════════════════════╗");
            if (avgSlowdown < 1.2)
            {
                Logging.Log("║  VERDICT: Queue is COMPETITIVE with CommandBuffer             ║");
                Logging.Log("║  Recommendation: Use queues for explicit ordering              ║");
            }
            else if (avgSlowdown < 2.0)
            {
                Logging.Log("║  VERDICT: Queue is SLOWER but ACCEPTABLE                       ║");
                Logging.Log("║  Recommendation: Use queues unless perf-critical               ║");
            }
            else if (avgSlowdown < 5.0)
            {
                Logging.Log("║  VERDICT: Queue has MEASURABLE overhead                        ║");
                Logging.Log("║  Recommendation: Investigate queue implementation              ║");
            }
            else
            {
                Logging.Log("║  VERDICT: Queue has SEVERE overhead                            ║");
                Logging.Log("║  Recommendation: Fix queue or use CommandBuffer                ║");
            }
            Logging.Log("╚════════════════════════════════════════════════════════════════╝\n");
        }
    }
}
