#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UltraSim.ECS.Testing
{
    /// <summary>
    /// Base class for all stress test modules.
    /// Provides common infrastructure for timing, entity tracking, and result collection.
    /// </summary>
    public abstract class StressTestModule
    {
        protected World world;
        protected StressTestConfig config;
        protected StressTestResult result;
        protected Random random;

        protected List<Entity> trackedEntities = new();
        protected int frameCount = 0;
        protected float elapsedTime = 0f;
        protected List<float> frameTimes = new();

        public abstract StressTestType TestType { get; }
        public abstract string TestName { get; }

        public bool IsComplete { get; protected set; }
        public bool HasFailed { get; protected set; }

        public StressTestModule(World world, StressTestConfig config)
        {
            this.world = world;
            this.config = config;

            result = new StressTestResult
            {
                TestType = TestType,
                Intensity = config.Intensity,
                StartTime = DateTime.UtcNow,
                StartMemoryBytes = GC.GetTotalMemory(false)
            };

            random = new Random();
        }

        public virtual void Initialize()
        {
            GD.Print($"\n╔═══════════════════════════════════════╗");
            GD.Print($"║ STRESS TEST: {TestName,-24} ║");
            GD.Print($"╠═══════════════════════════════════════╣");
            GD.Print($"║ Intensity: {config.Intensity,-26} ║");
            GD.Print($"║ Target: {config.TargetEntityCount,30:N0} ║");
            GD.Print($"╚═══════════════════════════════════════╝");
        }

        public void Update(float deltaTime)
        {
            if (IsComplete || HasFailed) return;

            var frameStart = DateTime.UtcNow;

            try
            {
                UpdateTest(deltaTime);
            }
            catch (Exception ex)
            {
                Fail("Exception during test", ex);
                return;
            }

            float frameTime = (float)(DateTime.UtcNow - frameStart).TotalMilliseconds;
            frameTimes.Add(frameTime);

            // Update stats
            if (frameTimes.Count > 0)
            {
                result.AverageFrameTimeMs = frameTimes.Average();
                result.PeakFrameTimeMs = Math.Max(result.PeakFrameTimeMs, frameTime);
                result.MinFrameTimeMs = Math.Min(result.MinFrameTimeMs, frameTime);
            }

            // Memory tracking
            long currentMemory = GC.GetTotalMemory(false);
            result.PeakMemoryBytes = Math.Max(result.PeakMemoryBytes, currentMemory);

            frameCount++;
            elapsedTime += deltaTime;

            // Check completion
            if (CheckCompletionConditions(deltaTime))
                Complete();

            // Check failure
            if (CheckFailureConditions(frameTime))
                Fail("Performance threshold exceeded");
        }

        protected abstract void UpdateTest(float deltaTime);

        protected virtual bool CheckCompletionConditions(float deltaTime)
        {
            if (elapsedTime >= config.MaxDurationSeconds)
                return true;

            if (frameCount >= config.MaxFrames)
                return true;

            return false;
        }

        protected virtual bool CheckFailureConditions(float frameTime)
        {
            if (config.StopOnSlowFrame && frameTime > config.MaxFrameTimeMs)
                return true;

            return false;
        }

        protected virtual void Complete()
        {
            IsComplete = true;
            result.EndTime = DateTime.UtcNow;
            result.EndMemoryBytes = GC.GetTotalMemory(false);

            GD.Print($"\n[{TestName}] ✅ COMPLETED");
            GD.Print($"Duration: {result.Duration.TotalSeconds:F2}s");
            GD.Print($"Peak Entities: {result.PeakEntityCount:N0}");
            GD.Print($"Avg Frame: {result.AverageFrameTimeMs:F3}ms");
        }

        protected virtual void Fail(string reason, Exception? ex = null)
        {
            HasFailed = true;
            result.Crashed = true;
            result.CrashReason = reason;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;

            GD.PrintErr($"\n[{TestName}] ❌ FAILED: {reason}");
            if (ex != null)
                GD.PrintErr($"Exception: {ex.Message}");
        }

        public StressTestResult GetResults() => result;

        public virtual void Cleanup()
        {
            // Destroy tracked entities
            trackedEntities.Clear();
        }
    }
}
