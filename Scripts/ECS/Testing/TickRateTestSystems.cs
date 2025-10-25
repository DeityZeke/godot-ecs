#nullable enable

using System;
using Godot;
using UltraSim.ECS;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Test system that runs every frame (default behavior).
    /// Should see "Frame tick!" message every frame.
    /// </summary>
    public class EveryFrameTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 0;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "EveryFrameTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.EveryFrame;
        
        private int frameCount = 0;
        
        public override void Update(World world, double delta)
        {
            frameCount++;
            
            // Only print occasionally to avoid spam
            if (frameCount % 60 == 0)
            {
                GD.Print($"[EveryFrameTest] âœ… Frame {frameCount} (should print ~every second)");
            }
        }
    }
    
    /// <summary>
    /// Test system that runs 20 times per second (every 50ms).
    /// Should see "Fast tick!" message 20x per second.
    /// </summary>
    public class FastTickTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 100;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "FastTickTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();

        public override TickRate Rate => TickRate.Tick33ms;//TickRate.Tick50ms; // 20 Hz
        
        private int tickCount = 0;
        private double lastTickTime = 0;
        
        public override void Update(World world, double delta)
        {
            tickCount++;
            double currentTime = Time.GetTicksMsec() / 1000.0;
            double deltaTime = currentTime - lastTickTime;
            lastTickTime = currentTime;
            
            // Print every 20 ticks (once per second)
            if (tickCount % 20 == 0)
            {
                GD.Print($"""[FastTickTest] âœ… Tick {tickCount} (Î" {deltaTime*1000:F1}ms, should be ~50ms)""");
            }
        }
    }
    
    /// <summary>
    /// Test system that runs 10 times per second (every 100ms).
    /// Should see "Medium tick!" message 10x per second.
    /// </summary>
    public class MediumTickTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 200;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "MediumTickTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Tick100ms; // 10 Hz
        
        private int tickCount = 0;
        private double lastTickTime = 0;
        
        public override void Update(World world, double delta)
        {
            tickCount++;
            double currentTime = Time.GetTicksMsec() / 1000.0;
            double deltaTime = currentTime - lastTickTime;
            lastTickTime = currentTime;
            
            // Print every 10 ticks (once per second)
            if (tickCount % 10 == 0)
            {
                GD.Print($"""[MediumTickTest] âœ… Tick {tickCount} (Î" {deltaTime*1000:F1}ms, should be ~100ms)""");
            }
        }
    }
    
    /// <summary>
    /// Test system that runs once per second.
    /// Should see "Slow tick!" message exactly once per second.
    /// </summary>
    public class SlowTickTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 300;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "SlowTickTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Tick1s; // 1 Hz
        
        private int tickCount = 0;
        private double lastTickTime = 0;
        
        public override void Update(World world, double delta)
        {
            tickCount++;
            double currentTime = Time.GetTicksMsec() / 1000.0;
            double deltaTime = currentTime - lastTickTime;
            lastTickTime = currentTime;
            
            GD.Print($"""[SlowTickTest] âœ… Tick {tickCount} (Î" {deltaTime*1000:F0}ms, should be ~1000ms)""");
        }
    }
    
    /// <summary>
    /// Test system that runs every 5 seconds.
    /// Should see "Very slow tick!" message every 5 seconds.
    /// </summary>
    public class VerySlowTickTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 400;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "VerySlowTickTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Tick5s; // 0.2 Hz
        
        private int tickCount = 0;
        private double lastTickTime = 0;
        
        public override void Update(World world, double delta)
        {
            tickCount++;
            double currentTime = Time.GetTicksMsec() / 1000.0;
            double deltaTime = currentTime - lastTickTime;
            lastTickTime = currentTime;
            
            GD.Print($"""[VerySlowTickTest] âœ… Tick {tickCount} (Î" {deltaTime:F1}s, should be ~5.0s)""");
        }
    }
    
    /// <summary>
    /// Manual system that must be explicitly invoked.
    /// Should NEVER run automatically - only when triggered by hotkey.
    /// </summary>
    public class ManualTestSystem : BaseSystem
    {
        private static int _systemIdCounter = 500;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "ManualTest";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Manual;
        
        private int invokeCount = 0;
        
        public override void Update(World world, double delta)
        {
            invokeCount++;
            GD.Print($"[ManualTest] ðŸ'¥ MANUALLY INVOKED #{invokeCount} (should only happen when F10 pressed)");
        }
    }
    
    /// <summary>
    /// Another manual system for save operations.
    /// </summary>
    public class SaveSystem : BaseSystem
    {
        private static int _systemIdCounter = 600;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "SaveSystem";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Manual;
        
        private int saveCount = 0;
        
        public override void Update(World world, double delta)
        {
            saveCount++;
            
            GD.Print($"""â•"â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•""");
            GD.Print($"â•'  SAVE SYSTEM INVOKED #{saveCount}");
            GD.Print($"â•'  Entities: {CountEntities(world):N0}");
            GD.Print($"â•'  Archetypes: {world.GetArchetypes().Count}");
            GD.Print("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
        }
        
        private int CountEntities(World world)
        {
            int count = 0;
            foreach (var arch in world.GetArchetypes())
                count += arch.Count;
            return count;
        }
    }
    
    /// <summary>
    /// System that demonstrates internal bucketing for spreading work across frames.
    /// Runs at Tick100ms (10 Hz), but internally processes different chunks each time.
    /// </summary>
    public class BucketedUpdateSystem : BaseSystem
    {
        private static int _systemIdCounter = 700;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "BucketedUpdate";
        public override Type[] ReadSet => new[] { typeof(Position) };
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Tick100ms; // 10 Hz
        
        private const int TOTAL_BUCKETS = 10;
        private int currentBucket = 0;
        private int tickCount = 0;
        
        public override void Update(World world, double delta)
        {
            tickCount++;
            
            // Process 1/10th of entities each tick
            // Net result: Each entity updated once per second, but load is spread evenly
            int startBucket = currentBucket;
            int endBucket = currentBucket + 1;
            
            int processedCount = 0;

            var query = world.Query(typeof(Position)); //world.Query<Position>();
            foreach (var arch in query)
            {
                if (!arch.TryGetComponentSpan<Position>(out var positions))
                    continue;
                
                for (int i = 0; i < positions.Length; i++)
                {
                    // Bucket assignment based on entity index
                    int bucket = i % TOTAL_BUCKETS;
                    
                    if (bucket >= startBucket && bucket < endBucket)
                    {
                        // Process this entity
                        processedCount++;
                    }
                }
            }
            
            // Only print occasionally
            if (tickCount % 10 == 0)
            {
                GD.Print($"[BucketedUpdate] âœ… Tick {tickCount} processed bucket {currentBucket} ({processedCount:N0} entities)");
            }
            
            // Advance to next bucket
            currentBucket = (currentBucket + 1) % TOTAL_BUCKETS;
        }
    }
    
    /// <summary>
    /// Practical example: AI system that runs once per second.
    /// Simulates expensive AI decision-making that doesn't need to run every frame.
    /// </summary>
    public class SimulatedAISystem : BaseSystem
    {
        private static int _systemIdCounter = 800;
        public override int SystemId { get; } = _systemIdCounter++;
        public override string Name => "SimulatedAI";
        public override Type[] ReadSet => new[] { typeof(Position) };
        public override Type[] WriteSet => Array.Empty<Type>();
        
        public override TickRate Rate => TickRate.Tick1s; // 1 Hz - AI updates once per second
        
        private int aiUpdateCount = 0;
        
        public override void Update(World world, double delta)
        {
            aiUpdateCount++;
            
            int entityCount = 0;
            var query = world.Query(typeof(Position)); //world.Query<Position>();
            foreach (var arch in query)
                entityCount += arch.Count;
            
            // Simulate expensive AI work
            int decisionsProcessed = entityCount;
            
            GD.Print($"[SimulatedAI] ðŸ§  AI Update #{aiUpdateCount}: Processed {decisionsProcessed:N0} decision trees");
        }
    }
}
