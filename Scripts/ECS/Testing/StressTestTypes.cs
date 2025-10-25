#nullable enable

using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Testing
{
    /// <summary>
    /// Types of stress tests available.
    /// </summary>
    public enum StressTestType
    {
        Spawn,      // Pure creation speed
        Churn,      // Create/destroy cycles
        Archetype,  // Component add/remove
        System,     // System execution stress
        Rendering   // Visualization stress
    }

    /// <summary>
    /// Intensity levels for stress tests.
    /// </summary>
    public enum StressIntensity
    {
        Light,      // 1K entities
        Medium,     // 10K entities
        Heavy,      // 50K entities
        Extreme,    // 100K+ entities
        UntilCrash  // Push until failure
    }

    /// <summary>
    /// Configuration for a stress test run.
    /// </summary>
    public class StressTestConfig
    {
        public StressTestType Type;
        public StressIntensity Intensity;

        public float MaxDurationSeconds = 60f;
        public int MaxFrames = 3600;
        public int TargetEntityCount = 10000;
        public int EntitiesPerFrame = 100;
        public int OperationsPerFrame = 100;

        public float MaxFrameTimeMs = 100f;
        public bool StopOnSlowFrame = false;

        /// <summary>
        /// Creates a preset configuration based on test type and intensity.
        /// </summary>
        public static StressTestConfig CreatePreset(StressTestType type, StressIntensity intensity)
        {
            var config = new StressTestConfig
            {
                Type = type,
                Intensity = intensity
            };

            switch (intensity)
            {
                case StressIntensity.Light:
                    config.TargetEntityCount = 1000;
                    config.EntitiesPerFrame = 10;
                    config.OperationsPerFrame = 10;
                    break;

                case StressIntensity.Medium:
                    config.TargetEntityCount = 10000;
                    config.EntitiesPerFrame = 100;
                    config.OperationsPerFrame = 100;
                    break;

                case StressIntensity.Heavy:
                    config.TargetEntityCount = 50000;
                    config.EntitiesPerFrame = 500;
                    config.OperationsPerFrame = 1000;
                    break;

                case StressIntensity.Extreme:
                    config.TargetEntityCount = 100000;
                    config.EntitiesPerFrame = 1000;
                    config.OperationsPerFrame = 5000;
                    break;

                case StressIntensity.UntilCrash:
                    config.TargetEntityCount = int.MaxValue;
                    config.MaxDurationSeconds = float.MaxValue;
                    config.StopOnSlowFrame = true;
                    config.MaxFrameTimeMs = 33.33f; // 30 FPS limit
                    break;
            }

            return config;
        }
    }

    /// <summary>
    /// Results from a completed stress test.
    /// </summary>
    public class StressTestResult
    {
        public StressTestType TestType;
        public StressIntensity Intensity;
        public DateTime StartTime;
        public DateTime EndTime;
        public TimeSpan Duration => EndTime - StartTime;

        // Entity stats
        public int PeakEntityCount;
        public int TotalEntitiesCreated;
        public int TotalEntitiesDestroyed;
        public int FinalEntityCount;

        // Performance stats
        public float AverageFrameTimeMs;
        public float PeakFrameTimeMs;
        public float MinFrameTimeMs = float.MaxValue;

        // Memory
        public long StartMemoryBytes;
        public long PeakMemoryBytes;
        public long EndMemoryBytes;

        // Failure info
        public bool Crashed;
        public string? CrashReason;
        public Exception? Exception;

        /// <summary>
        /// Generates a summary report of the test results.
        /// </summary>
        public string GenerateReport()
        {
            var status = Crashed ? "❌ FAILED" : "✅ PASSED";
            var report = $@"
╔══════════════════════════════════════════╗
║         STRESS TEST RESULTS              ║
╠══════════════════════════════════════════╣
║ Test: {TestType,-34} ║
║ Intensity: {Intensity,-29} ║
║ Status: {status,-32} ║
╠══════════════════════════════════════════╣
║ TIMING                                   ║
║ Duration: {Duration.TotalSeconds,29:F2}s ║
║ Avg Frame: {AverageFrameTimeMs,27:F3}ms ║
║ Peak Frame: {PeakFrameTimeMs,26:F3}ms ║
║ Min Frame: {MinFrameTimeMs,27:F3}ms ║
╠══════════════════════════════════════════╣
║ ENTITIES                                 ║
║ Peak Count: {PeakEntityCount,28:N0} ║
║ Total Created: {TotalEntitiesCreated,25:N0} ║
║ Total Destroyed: {TotalEntitiesDestroyed,23:N0} ║
║ Final Count: {FinalEntityCount,27:N0} ║
╠══════════════════════════════════════════╣
║ MEMORY                                   ║
║ Start: {StartMemoryBytes / 1024.0 / 1024.0,31:F2} MB ║
║ Peak: {PeakMemoryBytes / 1024.0 / 1024.0,32:F2} MB ║
║ End: {EndMemoryBytes / 1024.0 / 1024.0,33:F2} MB ║
║ Delta: {(EndMemoryBytes - StartMemoryBytes) / 1024.0 / 1024.0,31:F2} MB ║
╚══════════════════════════════════════════╝";

            if (Crashed)
            {
                report += $"\n\n❌ CRASH REASON: {CrashReason}";
                if (Exception != null)
                    report += $"\n   Exception: {Exception.Message}";
            }

            return report;
        }
    }
}
