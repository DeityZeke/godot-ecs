#nullable enable

using System;
using System.Diagnostics;

namespace UltraSim.ECS
{
    /// <summary>
    /// Self-contained time tracking for the ECS.
    /// Does NOT rely on engine APIs - accumulates delta time from Update() calls.
    /// </summary>
    public sealed class TimeTracker
    {
        private double _totalSeconds = 0.0;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        
        /// <summary>
        /// Total elapsed time in seconds since ECS initialization.
        /// Accumulated from delta time passed to Update().
        /// </summary>
        public double TotalSeconds => _totalSeconds;

        /// <summary>
        /// High-precision timestamp in microseconds.
        /// Uses System.Diagnostics.Stopwatch (engine-independent).
        /// </summary>
        public long Microseconds => _stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;

        /// <summary>
        /// Update the time tracker with delta time from the engine's frame loop.
        /// Call this at the start of World.Update(deltaTime).
        /// </summary>
        public void Advance(float deltaTime)
        {
            _totalSeconds += deltaTime;
        }

        /// <summary>
        /// Reset time tracking (useful for tests or reinitialization).
        /// </summary>
        public void Reset()
        {
            _totalSeconds = 0.0;
            _stopwatch.Restart();
        }
    }
}
