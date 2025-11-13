#nullable enable

using UltraSim.ECS;
using UltraSim;

using System;
using System.Collections.Generic;
using UltraSim.ECS.Components;

namespace Client.ECS.StressTests
{
    // Test components for archetype transitions
    public struct Temperature
    {
        public float Value;
    }

    public struct Health
    {
        public int Value;
    }

    public struct Lifetime
    {
        public float RemainingSeconds;
    }

    /// <summary>
    /// Tests rapid component add/remove operations.
    /// This stresses archetype transition performance and archetype proliferation.
    /// </summary>
    public class ArchetypeTransitionTest : StressTestBase
    {
        public override StressTestType TestType => StressTestType.Archetype;
        public override string TestName => "Archetype Transition Test";

        private CommandBuffer buffer;
        private List<Entity> testEntities = new();
        private float spawnRadius = 50f;

        private int totalComponentAdds = 0;
        private int totalComponentRemoves = 0;
        private int peakArchetypeCount = 0;

        public ArchetypeTransitionTest(World world, StressTestConfig config)
            : base(world, config)
        {
            buffer = new CommandBuffer();
        }

        public override void Initialize()
        {
            base.Initialize();

            // Spawn initial entities with base components
            Logging.Log($"[ArchetypeTest] Spawning {config.TargetEntityCount:N0} base entities...");

            for (int i = 0; i < config.TargetEntityCount; i++)
            {
                var pos = Utilities.RandomPointInSphere(spawnRadius);

                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position { X = pos.X, Y = pos.Y, Z = pos.Z });
                    builder.Add(new Velocity
                    {
                        X = Utilities.RandomRange(-1f, 1f),
                        Y = Utilities.RandomRange(-1f, 1f),
                        Z = Utilities.RandomRange(-1f, 1f)
                    });
                });
            }

            buffer.Apply(world);

            // Collect entity references
            foreach (var arch in world.GetArchetypes())
            {
                foreach (var entity in arch.GetEntityArray())
                {
                    testEntities.Add(entity);
                }
            }

            result.TotalEntitiesCreated = testEntities.Count;
            Logging.Log($"[ArchetypeTest] Spawned {testEntities.Count:N0} entities");
        }

        protected override void UpdateTest(float deltaTime)
        {
            // Track archetype count
            int archetypeCount = world.GetArchetypes().Count;
            peakArchetypeCount = Math.Max(peakArchetypeCount, archetypeCount);

            // Perform random component operations
            int operationsPerFrame = Math.Min(config.OperationsPerFrame, testEntities.Count);

            for (int i = 0; i < operationsPerFrame; i++)
            {
                if (testEntities.Count == 0) break;

                var entity = testEntities[random.Next(testEntities.Count)];
                if (!world.IsEntityValid(entity)) continue;

                // Randomly choose an operation
                int operation = random.Next(6);

                switch (operation)
                {
                    case 0: // Add Temperature
                        world.EnqueueComponentAdd(entity,
                            ComponentManager.GetTypeId<Temperature>(),
                            new Temperature { Value = (float)(random.NextDouble() * 100) });
                        totalComponentAdds++;
                        break;

                    case 1: // Remove Temperature
                        world.EnqueueComponentRemove(entity,
                            ComponentManager.GetTypeId<Temperature>());
                        totalComponentRemoves++;
                        break;

                    case 2: // Add Health
                        world.EnqueueComponentAdd(entity,
                            ComponentManager.GetTypeId<Health>(),
                            new Health { Value = random.Next(1, 100) });
                        totalComponentAdds++;
                        break;

                    case 3: // Remove Health
                        world.EnqueueComponentRemove(entity,
                            ComponentManager.GetTypeId<Health>());
                        totalComponentRemoves++;
                        break;

                    case 4: // Add Lifetime
                        world.EnqueueComponentAdd(entity,
                            ComponentManager.GetTypeId<Lifetime>(),
                            new Lifetime { RemainingSeconds = (float)(random.NextDouble() * 10) });
                        totalComponentAdds++;
                        break;

                    case 5: // Remove Lifetime
                        world.EnqueueComponentRemove(entity,
                            ComponentManager.GetTypeId<Lifetime>());
                        totalComponentRemoves++;
                        break;
                }
            }

            result.FinalEntityCount = testEntities.Count;
            result.PeakEntityCount = Math.Max(result.PeakEntityCount, testEntities.Count);

            // Progress update every second
            if (frameCount % 60 == 0)
            {
                float opsPerSec = (totalComponentAdds + totalComponentRemoves) / elapsedTime;
                Logging.Log($"[ArchetypeTest] Frame {frameCount}: Archetypes={archetypeCount}, Peak={peakArchetypeCount}, Ops/sec={opsPerSec:F0}");
            }
        }

        protected override void Complete()
        {
            base.Complete();
            Logging.Log($"[ArchetypeTest] Total Component Adds: {totalComponentAdds:N0}");
            Logging.Log($"[ArchetypeTest] Total Component Removes: {totalComponentRemoves:N0}");
            Logging.Log($"[ArchetypeTest] Peak Archetype Count: {peakArchetypeCount}");
        }

        public override void Cleanup()
        {
            base.Cleanup();
            testEntities.Clear();
            buffer.Clear();
        }
    }
}


