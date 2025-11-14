#nullable enable

using System;
using System.Collections.Generic;
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
    /// Benchmark testing parallel-by-archetype optimization with diverse entity types.
    /// Compares homogeneous vs diverse entity creation to demonstrate parallel speedup.
    ///
    /// Test Scenarios:
    /// 1. Homogeneous: All entities have same components (Position+Velocity+RenderTag)
    ///    - Expected: Sequential path (signatureGroups.Count == 1)
    ///    - Baseline performance
    ///
    /// 2. Diverse (3 types): Enemies, Projectiles, Pickups
    ///    - Expected: Parallel path (signatureGroups.Count == 3)
    ///    - Should be 2-3x faster due to parallel processing
    ///
    /// 3. Diverse (6 types): Multiple entity categories
    ///    - Expected: Maximum parallelization (signatureGroups.Count == 6)
    ///    - Should show best parallel speedup
    /// </summary>
    public partial class DiverseEntityCreationBenchmark : Node, IHost
    {
        private World? _world;

        // IHost implementation
        public RuntimeContext Runtime { get; private set; } = null!;
        public EnvironmentType Environment => EnvironmentType.Hybrid;
        public object? GetRootHandle() => this;
        public void Log(LogEntry entry) => GD.Print($"[{entry.Severity}] {entry.Message}");

        // Test configuration
        private readonly int[] _batchSizes = { 10000, 50000, 100000, 500000, 1000000 };
        private int _currentTestIndex = 0;
        private int _currentScenarioIndex = 0;
        private readonly string[] _scenarioNames = { "Homogeneous (1 type)", "Diverse (3 types)", "Diverse (6 types)" };

        // Timing results
        private readonly List<TestResult> _results = new();

        // Event tracking
        private int _eventsFired = 0;
        private int _entitiesInEvent = 0;

        private readonly Stopwatch _sw = new();

        // Warmup control
        private int _frameCounter = 0;
        private bool _warmupComplete = false;
        private bool _testsStarted = false;

        // Custom component types for diverse entities
        private struct EnemyTag { public int Level; }
        private struct ProjectileTag { public float Damage; }
        private struct PickupTag { public int ItemId; }
        private struct BuildingTag { public int Health; }
        private struct ParticleTag { public float Lifetime; }
        private struct NPCTag { public string Name; }

        public override void _Ready()
        {
            Runtime = new RuntimeContext(HostEnvironment.Capture(), "DiverseEntityCreationBenchmark");
            Logging.Host = this;
            _world = new World(this);

            // Subscribe to entity creation events
            UltraSim.EventSink.EntityBatchCreated += OnEntityBatchCreated;

            GD.Print("╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║        DIVERSE ENTITY CREATION PARALLEL BENCHMARK             ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");
            GD.Print("Testing EntityBuilder Queue with different entity diversity levels:\n");
            GD.Print("  1. Homogeneous (1 type):  All entities have Position+Velocity+RenderTag");
            GD.Print("                            → Sequential processing (baseline)\n");
            GD.Print("  2. Diverse (3 types):     Enemies, Projectiles, Pickups");
            GD.Print("                            → Parallel processing (3 signature groups)\n");
            GD.Print("  3. Diverse (6 types):     Enemies, Projectiles, Pickups, Buildings, Particles, NPCs");
            GD.Print("                            → Maximum parallelization (6 signature groups)\n");
            GD.Print($"Batch sizes: {string.Join(", ", _batchSizes.Select(x => x >= 1000000 ? $"{x / 1000000}M" : x >= 1000 ? $"{x / 1000}K" : x.ToString()))}\n");
            GD.Print("Expected: 2-3x speedup for diverse scenarios vs homogeneous\n");
            GD.Print("Warming up (60 frames)...\n");
        }

        public override void _Process(double delta)
        {
            if (_world == null)
                return;

            // Warmup phase: Run for 60 frames before starting tests
            if (!_warmupComplete)
            {
                _frameCounter++;
                if (_frameCounter >= 60)
                {
                    _warmupComplete = true;
                    GD.Print("Warmup complete! Starting tests...\n");
                }
                return;
            }

            // Tests complete
            if (_currentTestIndex >= _batchSizes.Length)
            {
                if (!_testsStarted)
                    return;

                if (_results.Count > 0)
                {
                    PrintFinalResults();
                    _results.Clear();
                    _currentTestIndex++; // Prevent printing again
                }
                return;
            }

            _testsStarted = true;

            int batchSize = _batchSizes[_currentTestIndex];
            string scenarioName = _scenarioNames[_currentScenarioIndex];

            string batchSizeStr = batchSize >= 1000000 ? $"{batchSize / 1000000}M" : batchSize >= 1000 ? $"{batchSize / 1000}K" : batchSize.ToString();
            GD.Print($"\n[Test {_currentTestIndex * 3 + _currentScenarioIndex + 1}/{_batchSizes.Length * 3}] {scenarioName} - {batchSizeStr} entities");

            // Reset event tracking
            _eventsFired = 0;
            _entitiesInEvent = 0;

            // Run the test
            var result = _currentScenarioIndex switch
            {
                0 => TestHomogeneous(batchSize),
                1 => TestDiverse3Types(batchSize),
                2 => TestDiverse6Types(batchSize),
                _ => throw new InvalidOperationException()
            };

            result.MethodName = scenarioName;
            result.BatchSize = batchSize;
            result.EventsFired = _eventsFired;
            result.EntitiesInEvent = _entitiesInEvent;
            _results.Add(result);

            GD.Print($"  Enqueue:  {result.EnqueueMs:F3}ms");
            GD.Print($"  Process:  {result.ProcessMs:F3}ms");
            GD.Print($"  Total:    {result.TotalMs:F3}ms");
            GD.Print($"  Per-ent:  {result.NsPerEntity:F0}ns ({1_000_000_000.0 / result.NsPerEntity:F0} entities/sec)");
            GD.Print($"  Events:   {result.EventsFired} fired, {result.EntitiesInEvent} entities");

            // Clean up entities before next test
            CleanupEntities();

            // Move to next test
            _currentScenarioIndex++;
            if (_currentScenarioIndex >= _scenarioNames.Length)
            {
                _currentScenarioIndex = 0;
                _currentTestIndex++;
            }
        }

        private TestResult TestHomogeneous(int count)
        {
            // All entities have same components: Position + Velocity + RenderTag
            // This triggers sequential path (signatureGroups.Count == 1)
            _sw.Restart();

            for (int i = 0; i < count; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 1, Y = 0, Z = 0 });
                builder.Add(new RenderTag());
                _world.EnqueueCreateEntity(builder);
            }

            double enqueueMs = _sw.Elapsed.TotalMilliseconds;

            _sw.Restart();
            _world!.Tick(0.016);
            double processMs = _sw.Elapsed.TotalMilliseconds;

            return new TestResult
            {
                EnqueueMs = enqueueMs,
                ProcessMs = processMs,
                TotalMs = enqueueMs + processMs,
                NsPerEntity = (enqueueMs + processMs) * 1_000_000.0 / count
            };
        }

        private TestResult TestDiverse3Types(int count)
        {
            // 3 entity types with different component signatures:
            // - 50% Enemies: Position + Velocity + EnemyTag
            // - 30% Projectiles: Position + Velocity + ProjectileTag
            // - 20% Pickups: Position + PickupTag
            // This triggers parallel path (signatureGroups.Count == 3)

            int enemyCount = (int)(count * 0.5);
            int projectileCount = (int)(count * 0.3);
            int pickupCount = count - enemyCount - projectileCount;

            _sw.Restart();

            // Enqueue enemies
            for (int i = 0; i < enemyCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 1, Y = 0, Z = 0 });
                builder.Add(new EnemyTag { Level = 1 });
                _world.EnqueueCreateEntity(builder);
            }

            // Enqueue projectiles
            for (int i = 0; i < projectileCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 5, Y = 0, Z = 0 });
                builder.Add(new ProjectileTag { Damage = 10.0f });
                _world.EnqueueCreateEntity(builder);
            }

            // Enqueue pickups
            for (int i = 0; i < pickupCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new PickupTag { ItemId = 100 });
                _world.EnqueueCreateEntity(builder);
            }

            double enqueueMs = _sw.Elapsed.TotalMilliseconds;

            _sw.Restart();
            _world!.Tick(0.016);
            double processMs = _sw.Elapsed.TotalMilliseconds;

            return new TestResult
            {
                EnqueueMs = enqueueMs,
                ProcessMs = processMs,
                TotalMs = enqueueMs + processMs,
                NsPerEntity = (enqueueMs + processMs) * 1_000_000.0 / count
            };
        }

        private TestResult TestDiverse6Types(int count)
        {
            // 6 entity types with different component signatures:
            // - 30% Enemies: Position + Velocity + EnemyTag
            // - 20% Projectiles: Position + Velocity + ProjectileTag
            // - 15% Pickups: Position + PickupTag
            // - 15% Buildings: Position + BuildingTag
            // - 10% Particles: Position + Velocity + ParticleTag
            // - 10% NPCs: Position + Velocity + NPCTag
            // This triggers maximum parallelization (signatureGroups.Count == 6)

            int enemyCount = (int)(count * 0.30);
            int projectileCount = (int)(count * 0.20);
            int pickupCount = (int)(count * 0.15);
            int buildingCount = (int)(count * 0.15);
            int particleCount = (int)(count * 0.10);
            int npcCount = count - enemyCount - projectileCount - pickupCount - buildingCount - particleCount;

            _sw.Restart();

            // Enqueue all entity types
            for (int i = 0; i < enemyCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 1, Y = 0, Z = 0 });
                builder.Add(new EnemyTag { Level = 1 });
                _world.EnqueueCreateEntity(builder);
            }

            for (int i = 0; i < projectileCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 5, Y = 0, Z = 0 });
                builder.Add(new ProjectileTag { Damage = 10.0f });
                _world.EnqueueCreateEntity(builder);
            }

            for (int i = 0; i < pickupCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new PickupTag { ItemId = 100 });
                _world.EnqueueCreateEntity(builder);
            }

            for (int i = 0; i < buildingCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new BuildingTag { Health = 1000 });
                _world.EnqueueCreateEntity(builder);
            }

            for (int i = 0; i < particleCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 0, Y = 1, Z = 0 });
                builder.Add(new ParticleTag { Lifetime = 2.0f });
                _world.EnqueueCreateEntity(builder);
            }

            for (int i = 0; i < npcCount; i++)
            {
                var builder = _world!.CreateEntityBuilder();
                builder.Add(new Position { X = i, Y = 0, Z = 0 });
                builder.Add(new Velocity { X = 0.5f, Y = 0, Z = 0 });
                builder.Add(new NPCTag { Name = "NPC" });
                _world.EnqueueCreateEntity(builder);
            }

            double enqueueMs = _sw.Elapsed.TotalMilliseconds;

            _sw.Restart();
            _world!.Tick(0.016);
            double processMs = _sw.Elapsed.TotalMilliseconds;

            return new TestResult
            {
                EnqueueMs = enqueueMs,
                ProcessMs = processMs,
                TotalMs = enqueueMs + processMs,
                NsPerEntity = (enqueueMs + processMs) * 1_000_000.0 / count
            };
        }

        private void CleanupEntities()
        {
            // Query all possible component types used in tests and destroy entities
            var componentTypes = new[]
            {
                typeof(RenderTag), typeof(EnemyTag), typeof(ProjectileTag),
                typeof(PickupTag), typeof(BuildingTag), typeof(ParticleTag), typeof(NPCTag)
            };

            foreach (var componentType in componentTypes)
            {
                var archetypes = _world!.QueryArchetypes(componentType);
                foreach (var archetype in archetypes)
                {
                    var entities = archetype.GetEntityArray();
                    foreach (var entity in entities)
                    {
                        _world.EnqueueDestroyEntity(entity);
                    }
                }
            }

            _world!.Tick(0.016); // Process destruction queue
        }

        private void OnEntityBatchCreated(EntityBatchCreatedEventArgs args)
        {
            _eventsFired++;
            _entitiesInEvent += args.Count;
        }

        private void PrintFinalResults()
        {
            GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
            GD.Print("║                    FINAL RESULTS                               ║");
            GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

            // Group results by batch size
            foreach (int batchSize in _batchSizes)
            {
                var batchResults = _results.FindAll(r => r.BatchSize == batchSize);
                if (batchResults.Count == 0) continue;

                string batchSizeStr = batchSize >= 1000000 ? $"{batchSize / 1000000}M" : batchSize >= 1000 ? $"{batchSize / 1000}K" : batchSize.ToString();
                GD.Print($"\n━━━ Batch Size: {batchSizeStr} entities ({batchSize:N0}) ━━━\n");

                // Find homogeneous baseline
                var batchHomogeneousResults = batchResults.FindAll(r => r.MethodName.Contains("Homogeneous"));
                var batchDiverse3Results = batchResults.FindAll(r => r.MethodName.Contains("3 types"));
                var batchDiverse6Results = batchResults.FindAll(r => r.MethodName.Contains("6 types"));

                if (batchHomogeneousResults.Count > 0)
                {
                    var homogeneous = batchHomogeneousResults[0];
                    GD.Print($"Homogeneous (1 type) - BASELINE:");
                    GD.Print($"  Total:    {homogeneous.TotalMs:F3}ms");
                    GD.Print($"  Enqueue:  {homogeneous.EnqueueMs:F3}ms");
                    GD.Print($"  Process:  {homogeneous.ProcessMs:F3}ms");
                    GD.Print($"  Per-ent:  {homogeneous.NsPerEntity:F0}ns ({1_000_000_000.0 / homogeneous.NsPerEntity:F0} entities/sec)");
                    GD.Print($"  Path:     Sequential (1 signature group)");
                }

                if (batchDiverse3Results.Count > 0 && batchHomogeneousResults.Count > 0)
                {
                    var homogeneous = batchHomogeneousResults[0];
                    var diverse3 = batchDiverse3Results[0];
                    double speedup = homogeneous.TotalMs / diverse3.TotalMs;
                    string marker = speedup >= 1.0 ? $"{speedup:F2}x FASTER" : $"{1.0 / speedup:F2}x slower";

                    GD.Print($"\nDiverse (3 types):");
                    GD.Print($"  Total:    {diverse3.TotalMs:F3}ms ({marker})");
                    GD.Print($"  Enqueue:  {diverse3.EnqueueMs:F3}ms");
                    GD.Print($"  Process:  {diverse3.ProcessMs:F3}ms");
                    GD.Print($"  Per-ent:  {diverse3.NsPerEntity:F0}ns ({1_000_000_000.0 / diverse3.NsPerEntity:F0} entities/sec)");
                    GD.Print($"  Path:     Parallel (3 signature groups → 3 threads)");
                    GD.Print($"  Speedup:  {speedup:F2}x vs homogeneous");
                }

                if (batchDiverse6Results.Count > 0 && batchHomogeneousResults.Count > 0)
                {
                    var homogeneous = batchHomogeneousResults[0];
                    var diverse6 = batchDiverse6Results[0];
                    double speedup = homogeneous.TotalMs / diverse6.TotalMs;
                    string marker = speedup >= 1.0 ? $"{speedup:F2}x FASTER" : $"{1.0 / speedup:F2}x slower";

                    GD.Print($"\nDiverse (6 types):");
                    GD.Print($"  Total:    {diverse6.TotalMs:F3}ms ({marker})");
                    GD.Print($"  Enqueue:  {diverse6.EnqueueMs:F3}ms");
                    GD.Print($"  Process:  {diverse6.ProcessMs:F3}ms");
                    GD.Print($"  Per-ent:  {diverse6.NsPerEntity:F0}ns ({1_000_000_000.0 / diverse6.NsPerEntity:F0} entities/sec)");
                    GD.Print($"  Path:     Parallel (6 signature groups → 6 threads)");
                    GD.Print($"  Speedup:  {speedup:F2}x vs homogeneous");
                }
            }

            // Overall summary
            var homogeneousResults = _results.FindAll(r => r.MethodName.Contains("Homogeneous"));
            var diverse3Results = _results.FindAll(r => r.MethodName.Contains("3 types"));
            var diverse6Results = _results.FindAll(r => r.MethodName.Contains("6 types"));

            if (homogeneousResults.Count > 0 && diverse3Results.Count > 0 && diverse6Results.Count > 0)
            {
                double avgHomogeneous = homogeneousResults.Average(r => r.NsPerEntity);
                double avgDiverse3 = diverse3Results.Average(r => r.NsPerEntity);
                double avgDiverse6 = diverse6Results.Average(r => r.NsPerEntity);

                GD.Print("\n╔════════════════════════════════════════════════════════════════╗");
                GD.Print("║                      SUMMARY                                   ║");
                GD.Print("╚════════════════════════════════════════════════════════════════╝\n");

                GD.Print("Average performance (ns/entity across all batch sizes):");
                GD.Print($"  Homogeneous (1 type):  {avgHomogeneous:F0}ns (baseline)");
                GD.Print($"  Diverse (3 types):     {avgDiverse3:F0}ns ({avgHomogeneous / avgDiverse3:F2}x speedup)");
                GD.Print($"  Diverse (6 types):     {avgDiverse6:F0}ns ({avgHomogeneous / avgDiverse6:F2}x speedup)\n");

                GD.Print("Parallel efficiency:");
                GD.Print($"  3 signature groups:  {(avgHomogeneous / avgDiverse3 / 3.0) * 100:F1}% of ideal 3x speedup");
                GD.Print($"  6 signature groups:  {(avgHomogeneous / avgDiverse6 / 6.0) * 100:F1}% of ideal 6x speedup\n");

                GD.Print("Conclusion:");
                if (avgHomogeneous / avgDiverse3 >= 1.5)
                    GD.Print("  ✓ Parallel-by-archetype optimization is WORKING!");
                else
                    GD.Print("  ⚠ Parallel speedup lower than expected (check thread pool)");
            }

            GD.Print("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

            // Auto-cleanup
            GetTree().Quit();
        }

        private struct TestResult
        {
            public string MethodName;
            public int BatchSize;
            public double EnqueueMs;
            public double ProcessMs;
            public double TotalMs;
            public double NsPerEntity;
            public int EventsFired;
            public int EntitiesInEvent;
        }
    }
}
