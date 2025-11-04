#nullable enable

using System;
using UltraSim.ECS.Components;
using UltraSim.ECS.SIMD.Core;

namespace UltraSim.ECS.SIMD
{
    /// <summary>
    /// Central registry for SIMD-optimized operations.
    /// Delegates are assigned once at startup based on hardware capability.
    /// Prevents runtime switch statements in hot paths.
    /// </summary>
    public static class SimdOperations
    {
        // === MATHEMATICAL OPERATIONS ===

        /// <summary>
        /// Batch FastSin: Processes multiple sine lookups in parallel using SIMD.
        /// </summary>
        public delegate void BatchFastSinDelegate(Span<float> angles, Span<float> results);

        /// <summary>
        /// Active BatchFastSin implementation (assigned based on SIMD mode).
        /// </summary>
        public static BatchFastSinDelegate BatchFastSin { get; private set; } = MathOperations.BatchFastSin_Scalar;

        /// <summary>
        /// Batch FastInvSqrt: Processes multiple inverse square roots in parallel using SIMD.
        /// </summary>
        public delegate void BatchFastInvSqrtDelegate(Span<float> values, Span<float> results);

        /// <summary>
        /// Active BatchFastInvSqrt implementation (assigned based on SIMD mode).
        /// </summary>
        public static BatchFastInvSqrtDelegate BatchFastInvSqrt { get; private set; } = MathOperations.BatchFastInvSqrt_Scalar;

        // === SYSTEM OPERATIONS ===

        /// <summary>
        /// ApplyVelocity: Position += Velocity * delta
        /// Processes Position and Velocity spans in parallel using SIMD.
        /// </summary>
        public delegate void ApplyVelocityDelegate(Span<Position> positions, Span<Velocity> velocities, float delta);

        /// <summary>
        /// Active ApplyVelocity implementation (assigned based on SIMD mode).
        /// </summary>
        public static ApplyVelocityDelegate ApplyVelocity { get; private set; } = ApplyVelocityOperations.ApplyVelocity_Scalar;

        /// <summary>
        /// ProcessPulsing: Position, Velocity, PulseData batch processing with SIMD.
        /// </summary>
        public delegate void ProcessPulsingDelegate(Span<Position> positions, Span<Velocity> velocities, Span<PulseData> pulseData, float delta);

        /// <summary>
        /// Active ProcessPulsing implementation (assigned based on SIMD mode).
        /// </summary>
        public static ProcessPulsingDelegate ProcessPulsing { get; private set; } = ProcessPulsingOperations.ProcessPulsing_Scalar;

        // (Reserved for future batch operations: BatchRandomPoints, etc.)

        /// <summary>
        /// Initialize Core ECS SIMD operations based on selected mode.
        /// Called once at startup and when showcase mode changes.
        /// </summary>
        public static void InitializeCore(SimdMode mode)
        {
            // Core ECS operations (currently none - all moved to Systems)
        }

        /// <summary>
        /// Initialize Mathematical SIMD operations based on selected mode.
        /// Called once at startup and when showcase mode changes.
        /// </summary>
        public static void InitializeMath(SimdMode mode)
        {
            BatchFastSin = mode switch
            {
                SimdMode.Simd512 => MathOperations.BatchFastSin_AVX512,
                SimdMode.Simd256 => MathOperations.BatchFastSin_AVX2,
                SimdMode.Simd128 => MathOperations.BatchFastSin_SSE,
                _ => MathOperations.BatchFastSin_Scalar
            };

            BatchFastInvSqrt = mode switch
            {
                SimdMode.Simd512 => MathOperations.BatchFastInvSqrt_AVX512,
                SimdMode.Simd256 => MathOperations.BatchFastInvSqrt_AVX2,
                SimdMode.Simd128 => MathOperations.BatchFastInvSqrt_SSE,
                _ => MathOperations.BatchFastInvSqrt_Scalar
            };
        }

        // === OPTIMAL SIMD MODES (per-operation configuration) ===

        /// <summary>
        /// Optimal SIMD mode for ApplyVelocity (Movement System).
        /// SSE provides best performance (27% faster than Scalar).
        /// </summary>
        public static SimdMode ApplyVelocityOptimalMode => SimdMode.Simd128;

        /// <summary>
        /// Optimal SIMD mode for ProcessPulsing (Pulsing Movement System).
        /// Scalar is fastest - SIMD overhead exceeds benefits due to complex branching.
        /// </summary>
        public static SimdMode ProcessPulsingOptimalMode => SimdMode.Scalar;

        /// <summary>
        /// Select the best available SIMD mode, falling back through cascade.
        /// Example: If optimal is AVX-512 but hardware only supports AVX2, use AVX2.
        /// </summary>
        private static SimdMode SelectBestAvailableMode(SimdMode optimal, SimdMode hardwareMax)
        {
            // If optimal mode is supported, use it
            if (optimal <= hardwareMax)
                return optimal;

            // Cascade down to best available mode
            if (hardwareMax >= SimdMode.Simd512 && optimal > SimdMode.Simd512)
                return SimdMode.Simd512;
            if (hardwareMax >= SimdMode.Simd256 && optimal > SimdMode.Simd256)
                return SimdMode.Simd256;
            if (hardwareMax >= SimdMode.Simd128 && optimal > SimdMode.Simd128)
                return SimdMode.Simd128;

            return SimdMode.Scalar;
        }

        /// <summary>
        /// Initialize Systems SIMD operations based on selected mode.
        /// Called once at startup and when showcase mode changes.
        ///
        /// In showcase mode: Uses requested mode for testing/demonstration.
        /// In production mode: Uses optimal mode for each operation (with hardware fallback).
        /// </summary>
        public static void InitializeSystems(SimdMode mode, bool showcaseMode = false, SimdMode hardwareMax = SimdMode.Simd512)
        {
            // Math operations used by systems
            InitializeMath(mode);

            // Determine effective mode for each operation
            SimdMode applyVelocityMode = showcaseMode ? mode : SelectBestAvailableMode(ApplyVelocityOptimalMode, hardwareMax);
            SimdMode processPulsingMode = showcaseMode ? mode : SelectBestAvailableMode(ProcessPulsingOptimalMode, hardwareMax);

            // Movement system operations
            ApplyVelocity = applyVelocityMode switch
            {
                SimdMode.Simd512 => ApplyVelocityOperations.ApplyVelocity_AVX512,
                SimdMode.Simd256 => ApplyVelocityOperations.ApplyVelocity_AVX2,
                SimdMode.Simd128 => ApplyVelocityOperations.ApplyVelocity_SSE,
                _ => ApplyVelocityOperations.ApplyVelocity_Scalar
            };

            // Pulsing movement system operations
            ProcessPulsing = processPulsingMode switch
            {
                SimdMode.Simd512 => ProcessPulsingOperations.ProcessPulsing_AVX512,
                SimdMode.Simd256 => ProcessPulsingOperations.ProcessPulsing_AVX2,
                SimdMode.Simd128 => ProcessPulsingOperations.ProcessPulsing_SSE,
                _ => ProcessPulsingOperations.ProcessPulsing_Scalar
            };

#if USE_DEBUG
            // Log effective modes in debug builds (only when not in showcase mode)
            if (!showcaseMode)
            {
                Logging.Logger.Log($"[SIMD] Systems initialized with optimal modes:");
                Logging.Logger.Log($"  - ApplyVelocity: {applyVelocityMode} (optimal: {ApplyVelocityOptimalMode})");
                Logging.Logger.Log($"  - ProcessPulsing: {processPulsingMode} (optimal: {ProcessPulsingOptimalMode})");
            }
#endif

            // Reserved for future system operations
            // BatchRandomPoints, etc.
        }
    }
}
