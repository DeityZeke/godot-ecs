#nullable enable

using System;
using System.Linq;
using Godot;

namespace UltraSim.ECS.Testing
{
    /// <summary>
    /// Tests rapid create/destroy cycles.
    /// Creates N entities, then every frame destroys X% and creates X% new ones.
    /// This stresses archetype management, memory fragmentation, and free-list efficiency.
    /// </summary>
    public class ChurnStressTest : StressTestModule
    {
        public override StressTestType TestType => StressTestType.Churn;
        public override string TestName => "Entity Churn Test";

        private StructuralCommandBuffer buffer;
        private float spawnRadius = 50f;
        private float churnPercentage = 0.10f; // 10% churn per frame

        public ChurnStressTest(World world, StressTestConfig config) 
            : base(world, config)
        {
            buffer = new StructuralCommandBuffer();
        }

        public override void Initialize()
        {
            base.Initialize();

            // Initial spawn to reach target
            #if USE_DEBUG
            GD.Print($"[ChurnTest] Pre-spawning {config.TargetEntityCount:N0} entities...");
            #endif // USE_DEBUG

            for (int i = 0; i < config.TargetEntityCount; i++)
            {
                SpawnEntity();
            }

            buffer.Apply(world);
            result.TotalEntitiesCreated = config.TargetEntityCount;

            #if USE_DEBUG
            GD.Print($"[ChurnTest] âœ… Initial spawn complete");
            #endif // USE_DEBUG
        }

        protected override void UpdateTest(float deltaTime)
        {
            // Count current entities
            int currentCount = 0;
            foreach (var arch in world.GetArchetypes())
            {
                foreach (var entity in arch.GetEntityArray())
                {
                    trackedEntities.Add(entity);
                }
                currentCount += arch.Count;
            }

            result.FinalEntityCount = currentCount;
            result.PeakEntityCount = Math.Max(result.PeakEntityCount, currentCount);

            // Calculate churn amount
            int churnCount = Math.Max(1, (int)(currentCount * churnPercentage));
            churnCount = Math.Min(churnCount, config.OperationsPerFrame / 2); // Limit to config

            // Destroy random entities
            for (int i = 0; i < churnCount && trackedEntities.Count > 0; i++)
            {
                int idx = random.Next(trackedEntities.Count);
                var entity = trackedEntities[idx];
                
                if (world.IsEntityValid(entity))
                {
                    buffer.DestroyEntity(entity);
                    result.TotalEntitiesDestroyed++;
                }
                
                trackedEntities.RemoveAt(idx);
            }

            // Create new entities to replace them
            for (int i = 0; i < churnCount; i++)
            {
                SpawnEntity();
                result.TotalEntitiesCreated++;
            }

            buffer.Apply(world);
            trackedEntities.Clear(); // Will rebuild next frame

            // Progress update every second
            if (frameCount % 60 == 0)
            {
                float churnRate = (result.TotalEntitiesCreated + result.TotalEntitiesDestroyed) / elapsedTime;
                #if USE_DEBUG
                GD.Print($"[ChurnTest] Frame {frameCount}: Entities={currentCount:N0}, ChurnRate={churnRate:F0} ops/sec");
                #endif // USE_DEBUG
            }
        }

        private void SpawnEntity()
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
                builder.Add(new RenderTag { });
                builder.Add(new Visible { });
            });
        }

        public override void Cleanup()
        {
            base.Cleanup();
            
            // Note: This leaves entities in the world intentionally
            // If you want to clean up ALL test entities, uncomment below:
            /*
            var cleanupBuffer = new StructuralCommandBuffer();
            foreach (var arch in world.GetArchetypes())
            {
                foreach (var entity in arch.GetEntityArray())
                {
                    cleanupBuffer.DestroyEntity(entity);
                }
            }
            cleanupBuffer.Apply(world);
            cleanupBuffer.Clear();
            */
            
            buffer.Clear();
        }
    }
}