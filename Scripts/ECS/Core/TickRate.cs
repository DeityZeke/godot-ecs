#nullable enable

using System;

namespace UltraSim.ECS
{
    /// <summary>
    /// Standardized tick rates for system scheduling.
    /// Values represent milliseconds between updates.
    /// 
    /// Systems declare their update frequency by overriding BaseSystem.Rate.
    /// The SystemManager automatically schedules systems to run at their
    /// specified intervals, minimizing wasted CPU cycles.
    /// </summary>
    public enum TickRate : ushort
    {
        /// <summary>
        /// Manual systems do not run automatically. They must be explicitly invoked
        /// via systemManager.RunManual<T>(). Use sparingly for:
        /// - Save/load operations
        /// - Debug/profiling tools  
        /// - Event-driven logic triggered by external inputs
        /// 
        /// WARNING: Manual systems should be leaf systems with no dependencies.
        /// Consider using command components or state-based gating instead for
        /// most "conditional execution" needs.
        /// </summary>
        Manual = 0,
        
        /// <summary>
        /// Runs every frame. Use for critical gameplay systems:
        /// - Movement/physics
        /// - Input handling
        /// - Camera updates
        /// - Animation
        /// (~16ms at 60fps, ~8ms at 120fps)
        /// </summary>
        EveryFrame = 1,
        
        /// <summary>
        /// Runs at 100 Hz. Use for high-frequency but non-critical updates:
        /// - Secondary physics passes
        /// - Fast-paced AI reactions
        /// </summary>
        Tick10ms = 10,
        
        /// <summary>
        /// Runs at ~60 Hz (frame-aligned at 60fps). Use for:
        /// - Visual effects that don't need to be frame-perfect
        /// - UI animations
        /// </summary>
        Tick16ms = 16,
        
        /// <summary>
        /// Runs at ~30 Hz. Use for:
        /// - Non-critical visual updates
        /// - Particle system cleanup
        /// </summary>
        Tick33ms = 33,
        
        /// <summary>
        /// Runs at 10 Hz. Use for:
        /// - Chunk streaming logic
        /// - LOD updates
        /// - Network sync packets (client prediction)
        /// </summary>
        Tick100ms = 100,
        
        /// <summary>
        /// Runs at 4 Hz. Use for:
        /// - Environmental updates (wind, ambient sounds)
        /// - Pathfinding cache refresh
        /// </summary>
        Tick250ms = 250,
        
        /// <summary>
        /// Runs at 2 Hz. Use for:
        /// - UI updates (health bars, minimaps)
        /// - Slow environmental effects
        /// </summary>
        Tick500ms = 500,
        
        /// <summary>
        /// Runs at 1 Hz. Use for:
        /// - AI decision trees
        /// - Spawn systems
        /// - Slow gameplay mechanics (hunger, thirst)
        /// </summary>
        Tick1s = 1000,
        
        /// <summary>
        /// Runs at 0.5 Hz. Use for:
        /// - Background simulation (weather)
        /// - Periodic cleanup
        /// </summary>
        Tick2s = 2000,
        
        /// <summary>
        /// Runs at 0.2 Hz. Use for:
        /// - Global economy updates
        /// - World events
        /// </summary>
        Tick5s = 5000,
        
        /// <summary>
        /// Runs at 0.1 Hz. Use for:
        /// - Autosave
        /// - Long-term statistics
        /// - Slow background tasks
        /// </summary>
        Tick10s = 10000
    }
    
    /// <summary>
    /// Extension methods for TickRate enum.
    /// </summary>
    public static class TickRateExtensions
    {
        /// <summary>
        /// Converts tick rate to seconds.
        /// </summary>
        public static double ToSeconds(this TickRate rate)
        {
            return (ushort)rate / 1000.0;
        }
        
        /// <summary>
        /// Converts tick rate to milliseconds.
        /// </summary>
        public static int ToMilliseconds(this TickRate rate)
        {
            return (ushort)rate;
        }
        
        /// <summary>
        /// Returns a human-readable description of the tick rate.
        /// </summary>
        public static string ToFrequencyString(this TickRate rate)
        {
            if (rate == TickRate.Manual) return "Manual";
            if (rate == TickRate.EveryFrame) return "Every Frame";
            
            double hz = 1000.0 / (ushort)rate;
            if (hz >= 1.0)
                return $"{hz:F1} Hz";
            else
                return $"{hz:F2} Hz";
        }
    }
}
