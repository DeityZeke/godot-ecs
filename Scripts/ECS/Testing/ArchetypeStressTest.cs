#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace UltraSim.ECS.Testing
{
    // Additional test components
    public struct Temperature { public float Value; }
    public struct Health { public int Value; }
    public struct Lifetime { public float RemainingSeconds; }

    /// <summary>
    /// Tests rapid component add/remove operations.
    /// This stresses archetype transition performance and archetype proliferation.
    /// </summary>
    public class ArchetypeStressTest : StressTestModule
    {
        public override StressTestType TestType => StressTestType.Archetype;
        public override string TestName => "Archetype Transition Test";

        private StructuralCommandBuffer buffer;
        private List<Entity> testEntities = new();
        private float spawnRadius = 50f;
        
        private int totalComponentAdds = 0;
        private int totalComponentRemoves = 0;
        private int peakArchetypeCount = 0;

        public ArchetypeStressTest(World world, StressTestConfig config) 
            : base(world, config)
        {
            buffer = new StructuralCommandBuffer();
        }

        public override void Initialize()
        {
            base.Initialize();

            // Spawn initial entities with base components
            GD.Print($"[ArchetypeTest] Spawning {config.TargetEntityCount:N0} base entities...");

            for (int i = 0; i < config.TargetEntityCount; i++)
            {
                float theta = (float)(random.NextDouble() * Math.PI * 2);
                float phi = (float)(Math.Acos(2 * random.NextDouble() - 1));
                float r = (float)(Math.Pow(random.NextDouble(), 1.0 / 3.0) * spawnRadius);

                float x = r * MathF.Sin(phi) * MathF.Cos(theta);
                float y = r * MathF.Sin(phi) * MathF.Sin(theta);
                float z = r * MathF.Cos(phi);

                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position { X = x, Y = y, Z = z });
                    builder.Add(new Velocity 
                    { 
                        X = (float)(random.NextDouble() * 2 - 1),
                        Y = (float)(random.NextDouble() * 2 - 1),
                        Z = (float)(random.NextDouble() * 2 - 1)
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
            GD.Print($"[ArchetypeTest] ✅ Spawned {testEntities.Count:N0} entities");
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
                        world.EnqueueComponentAdd(entity.Index, 
                            ComponentTypeRegistry.GetId<Temperature>(), 
                            new Temperature { Value = (float)(random.NextDouble() * 100) });
                        totalComponentAdds++;
                        break;

                    case 1: // Remove Temperature
                        world.EnqueueComponentRemove(entity.Index, 
                            ComponentTypeRegistry.GetId<Temperature>());
                        totalComponentRemoves++;
                        break;

                    case 2: // Add Health
                        world.EnqueueComponentAdd(entity.Index, 
                            ComponentTypeRegistry.GetId<Health>(), 
                            new Health { Value = random.Next(1, 100) });
                        totalComponentAdds++;
                        break;

                    case 3: // Remove Health
                        world.EnqueueComponentRemove(entity.Index, 
                            ComponentTypeRegistry.GetId<Health>());
                        totalComponentRemoves++;
                        break;

                    case 4: // Add Lifetime
                        world.EnqueueComponentAdd(entity.Index, 
                            ComponentTypeRegistry.GetId<Lifetime>(), 
                            new Lifetime { RemainingSeconds = (float)(random.NextDouble() * 10) });
                        totalComponentAdds++;
                        break;

                    case 5: // Remove Lifetime
                        world.EnqueueComponentRemove(entity.Index, 
                            ComponentTypeRegistry.GetId<Lifetime>());
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
                GD.Print($"[ArchetypeTest] Frame {frameCount}: Archetypes={archetypeCount}, Peak={peakArchetypeCount}, Ops/sec={opsPerSec:F0}");
            }
        }

        protected override void Complete()
        {
            base.Complete();
            GD.Print($"[ArchetypeTest] Total Component Adds: {totalComponentAdds:N0}");
            GD.Print($"[ArchetypeTest] Total Component Removes: {totalComponentRemoves:N0}");
            GD.Print($"[ArchetypeTest] Peak Archetype Count: {peakArchetypeCount}");
        }

        public override void Cleanup()
        {
            base.Cleanup();
            testEntities.Clear();
            buffer.Clear();
        }
    }
}
