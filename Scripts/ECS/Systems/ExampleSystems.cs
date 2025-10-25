#nullable enable

using System;
using System.Linq;

using UltraSim.ECS;

using Godot;

namespace UltraSim.ECS.Examples
{
    // ====================================================================================
    // EXAMPLE 1: EveryFrame System (Critical Gameplay)
    // ====================================================================================
    
    /// <summary>
    /// Movement system that runs every frame for responsive gameplay.
    /// This is the default tick rate, so no override is strictly necessary,
    /// but we show it here for clarity.
    /// </summary>
    public class MovementSystem : BaseSystem
    {
        public override int SystemId => 1001;
        public override string Name => "MovementSystem";
        public override Type[] ReadSet => new[] { typeof(Position), typeof(Velocity) };
        public override Type[] WriteSet => new[] { typeof(Position) };
        
        private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();
        
        // EveryFrame is the default, but we explicitly declare it for documentation
        public override TickRate Rate => TickRate.EveryFrame;
        
        public override void Update(World world, double delta)
        {
            // Process all entities with Position and Velocity
            foreach (var archetype in world.Query(typeof(Position), typeof(Velocity)))//world.Query<Position, Velocity>())
            {
                var positions = archetype.GetComponentSpan<Position>(PosId);
                var velocities = archetype.GetComponentSpan<Velocity>(VelId);
                
                for (int i = 0; i < positions.Length; i++)
                {
                    // Update position based on velocity
                    positions[i].X += velocities[i].X * (float)delta;
                    positions[i].Y += velocities[i].Y * (float)delta;
                    positions[i].Z += velocities[i].Z * (float)delta;
                }
            }
        }
    }
    
    // ====================================================================================
    // EXAMPLE 2: Tick100ms System (Medium Frequency)
    // ====================================================================================
    
    /// <summary>
    /// Chunk update system that runs 10 times per second.
    /// Perfect for LOD updates, chunk streaming, and other non-critical spatial updates.
    /// </summary>
    public class ChunkUpdateSystem : BaseSystem
    {
        public override int SystemId => 1002;
        public override string Name => "ChunkUpdateSystem";
        public override Type[] ReadSet => new[] { typeof(ChunkComponent), typeof(Position) };
        public override Type[] WriteSet => new[] { typeof(ChunkComponent) };
        
        private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();
        
        // Runs 10 times per second (every 100ms)
        public override TickRate Rate => TickRate.Tick100ms;
        
        public override void Update(World world, double delta)
        {
            // Get camera position (if available)
            var cameraPos =  new CameraPosition { X = 0, Y = 0, Z = 0 };
            
            // Process all chunks
            foreach (var archetype in world.Query(typeof(ChunkComponent), typeof(Position))) //world.Query<ChunkComponent, Position>())
            {
                var chunks = archetype.GetComponentSpan<ChunkComponent>(PosId);
                var positions = archetype.GetComponentSpan<Position>(VelId);
                
                for (int i = 0; i < chunks.Length; i++)
                {
                    // Calculate distance from camera
                    float dx = positions[i].X - cameraPos.X;
                    float dy = positions[i].Y - cameraPos.Y;
                    float dz = positions[i].Z - cameraPos.Z;
                    float distanceSq = dx * dx + dy * dy + dz * dz;
                    
                    // Update LOD based on distance
                    if (distanceSq < 100f)
                        chunks[i].LOD = 0; // High detail
                    else if (distanceSq < 400f)
                        chunks[i].LOD = 1; // Medium detail
                    else if (distanceSq < 1600f)
                        chunks[i].LOD = 2; // Low detail
                    else
                        chunks[i].LOD = 3; // Very low detail
                }
            }
        }
    }
    
    // ====================================================================================
    // EXAMPLE 3: Tick1s System (Slow Updates)
    // ====================================================================================
    
    /// <summary>
    /// AI decision system that runs once per second.
    /// AI agents don't need frame-perfect decisions, making this ideal for
    /// pathfinding, decision trees, and behavior logic.
    /// </summary>
    public class AISystem : BaseSystem
    {
        public override int SystemId => 1003;
        public override string Name => "AISystem";
        public override Type[] ReadSet => new[] { typeof(AIComponent), typeof(Position) };
        public override Type[] WriteSet => new[] { typeof(AIComponent), typeof(Velocity) };

        private static readonly int AI_Id = ComponentTypeRegistry.GetId<AIComponent>();
        private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();
        
        // Runs once per second
        public override TickRate Rate => TickRate.Tick1s;
        
        private Random _random = new Random();
        
        public override void Update(World world, double delta)
        {
            // Process all AI entities
            foreach (var archetype in world.Query(typeof(AIComponent), typeof(Position), typeof(Velocity))) //world.Query<AIComponent, Position, Velocity>())
            {
                var aiComponents = archetype.GetComponentSpan<AIComponent>(AI_Id);
                var positions = archetype.GetComponentSpan<Position>(PosId);
                var velocities = archetype.GetComponentSpan<Velocity>(VelId);
                
                for (int i = 0; i < aiComponents.Length; i++)
                {
                    // Skip inactive AI
                    if (!aiComponents[i].IsActive)
                        continue;
                    
                    // Simple AI: Random walk
                    // In a real system, this would be pathfinding, decision trees, etc.
                    velocities[i].X = (float)(_random.NextDouble() * 2.0 - 1.0) * 5f;
                    velocities[i].Z = (float)(_random.NextDouble() * 2.0 - 1.0) * 5f;
                    
                    // Update AI state
                    aiComponents[i].TimeSinceLastDecision = 0f;
                    aiComponents[i].CurrentState = AIState.Moving;
                }
            }
        }
    }
    
    // ====================================================================================
    // EXAMPLE 4: Manual System (Event-Driven)
    // ====================================================================================
    
    /// <summary>
    /// Save system that only runs when explicitly triggered.
    /// Perfect for save/load operations, debug commands, or other event-driven logic.
    /// 
    /// Call with: systemManager.RunManual<SaveSystem>(world);
    /// </summary>
    public class SaveSystem : BaseSystem
    {
        public override int SystemId => 1004;
        public override string Name => "SaveSystem";
        public override Type[] ReadSet => new[] { typeof(Position), typeof(Velocity) };
        public override Type[] WriteSet => Array.Empty<Type>();
        
        // Manual - does not run automatically
        public override TickRate Rate => TickRate.Manual;
        
        private string _saveFilePath = "user://save_game.dat";
        
        public override void Update(World world, double delta)
        {
            GD.Print($"[SaveSystem] Starting save to {_saveFilePath}...");
            
            int entitiesSaved = 0;
            
            // Save all entities with position and velocity
            // In a real system, you'd serialize to disk
            foreach (var archetype in world.Query(typeof(Position), typeof(Velocity))) //world.Query<Position, Velocity>())
            {
                entitiesSaved += archetype.Count;
            }
            
            GD.Print($"[SaveSystem] Saved {entitiesSaved} entities successfully!");
        }
    }
    
    // ====================================================================================
    // EXAMPLE 5: Tick10s System (Very Slow Background Tasks)
    // ====================================================================================
    
    /// <summary>
    /// Autosave system that runs every 10 seconds.
    /// Perfect for periodic background tasks that don't need frequent execution.
    /// </summary>
    public class AutosaveSystem : BaseSystem
    {
        public override int SystemId => 1005;
        public override string Name => "AutosaveSystem";
        public override Type[] ReadSet => new[] { typeof(Position), typeof(Velocity) };
        public override Type[] WriteSet => Array.Empty<Type>();
        
        // Runs every 10 seconds
        public override TickRate Rate => TickRate.Tick10s;
        
        private int _autosaveCount = 0;
        
        public override void Update(World world, double delta)
        {
            _autosaveCount++;
            
            GD.Print($"[AutosaveSystem] Autosave #{_autosaveCount} triggered...");

            // Perform autosave logic here
            int entityCount = world.Query(typeof(Position), typeof(Velocity)).Sum(a => a.Count); //world.Query<Position, Velocity>().Sum(a => a.Count);
            
            GD.Print($"[AutosaveSystem] Autosaved {entityCount} entities");
        }
    }
    
    // ====================================================================================
    // Example Component Definitions
    // ====================================================================================
    
    public struct Position
    {
        public float X, Y, Z;
    }
    
    public struct Velocity
    {
        public float X, Y, Z;
    }
    
    public struct ChunkComponent
    {
        public int LOD;
        public bool IsActive;
    }
    
    public struct CameraPosition
    {
        public float X, Y, Z;
    }
    
    public struct AIComponent
    {
        public bool IsActive;
        public float TimeSinceLastDecision;
        public AIState CurrentState;
    }
    
    public enum AIState
    {
        Idle,
        Moving,
        Attacking,
        Fleeing
    }
}
