
#nullable enable

using System;
using Godot;

namespace UltraSim.ECS.Testing
{
    /// <summary>
    /// Tests pure entity creation throughput.
    /// Spawns entities as fast as possible until target reached.
    /// </summary>
    public class SpawnStressTest : StressTestModule
    {
        public override StressTestType TestType => StressTestType.Spawn;
        public override string TestName => "Entity Spawn Test";

        private StructuralCommandBuffer buffer;
        private float spawnRadius = 50f;

        public SpawnStressTest(World world, StressTestConfig config) 
            : base(world, config)
        {
            buffer = new StructuralCommandBuffer();
        }

        protected override void UpdateTest(float deltaTime)
        {
            // Count current entities
            int currentCount = 0;
            foreach (var arch in world.GetArchetypes())
                currentCount += arch.Count;

            result.FinalEntityCount = currentCount;
            result.PeakEntityCount = Math.Max(result.PeakEntityCount, currentCount);

            // Spawn batch if under target
            if (currentCount < config.TargetEntityCount)
            {
                int toSpawn = Math.Min(config.EntitiesPerFrame, 
                                      config.TargetEntityCount - currentCount);

                for (int i = 0; i < toSpawn; i++)
                {
                    // Random position in sphere
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

                    result.TotalEntitiesCreated++;
                }

                buffer.Apply(world);

                // Progress update every 10%
                float progress = (float)currentCount / config.TargetEntityCount * 100f;
                if (frameCount % 60 == 0) // Every second at 60fps
                {
                    GD.Print($"[SpawnTest] Progress: {progress:F1}% ({currentCount:N0}/{config.TargetEntityCount:N0})");
                }
            }
        }

        protected override bool CheckCompletionConditions(float deltaTime)
        {
            // Complete when target reached
            if (result.FinalEntityCount >= config.TargetEntityCount)
                return true;

            return base.CheckCompletionConditions(deltaTime);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            buffer.Clear();
        }
    }
}