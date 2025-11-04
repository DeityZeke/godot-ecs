#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using UltraSim.Logging;

namespace UltraSim.ECS.SIMD.Core
{
    /// <summary>
    /// SIMD-optimized mathematical operations for batch processing.
    /// Includes sine, cosine, inverse square root, and other common math functions.
    /// </summary>
    public static class MathOperations
    {
        private static bool _sinScalarLogged = false;
        private static bool _sinSseLogged = false;
        private static bool _sinAvx2Logged = false;
        private static bool _sinAvx512Logged = false;

        private static bool _invSqrtScalarLogged = false;
        private static bool _invSqrtSseLogged = false;
        private static bool _invSqrtAvx2Logged = false;
        private static bool _invSqrtAvx512Logged = false;

        // Constants from Utilities.cs
        private const float TWO_PI = 6.28318530718f;
        private const int LOOKUP_SIZE = 1024;
        private const float LOOKUP_SCALE = LOOKUP_SIZE / TWO_PI;

        // Reference to sine lookup table (initialized by Utilities)
        private static float[]? _sinLookup;

        /// <summary>
        /// Initialize math operations with sine lookup table from Utilities.
        /// </summary>
        public static void Initialize(float[] sinLookup)
        {
            _sinLookup = sinLookup;
        }

        #region Batch FastSin Operations

        /// <summary>
        /// Scalar batch sine: Process 1 angle per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BatchFastSin_Scalar(Span<float> angles, Span<float> results)
        {
            if (!_sinScalarLogged)
            {
                Logger.Log("[SIMD] BatchFastSin_Scalar CALLED (1 angle per iteration)");
                _sinScalarLogged = true;
            }

            if (_sinLookup == null)
                throw new InvalidOperationException("MathOperations not initialized with sine lookup table");

            int count = Math.Min(angles.Length, results.Length);

            for (int i = 0; i < count; i++)
            {
                float a = angles[i] % TWO_PI;
                if (a < 0f) a += TWO_PI;
                int idx = (int)(a * LOOKUP_SCALE);
                if ((uint)idx >= LOOKUP_SIZE) idx &= (LOOKUP_SIZE - 1);
                results[i] = _sinLookup[idx];
            }
        }

        /// <summary>
        /// SSE batch sine: Process 4 angles per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastSin_SSE(Span<float> angles, Span<float> results)
        {
            if (!Sse.IsSupported)
            {
                if (!_sinSseLogged)
                {
                    Logger.Log("[SIMD] BatchFastSin_SSE CALLED but SSE NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _sinSseLogged = true;
                }
                BatchFastSin_Scalar(angles, results);
                return;
            }

            if (!_sinSseLogged)
            {
                Logger.Log("[SIMD] BatchFastSin_SSE CALLED (4 angles per iteration) - SSE hardware confirmed");
                _sinSseLogged = true;
            }

            if (_sinLookup == null)
                throw new InvalidOperationException("MathOperations not initialized with sine lookup table");

            int count = Math.Min(angles.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 4);

            Vector128<float> vTwoPi = Vector128.Create(TWO_PI);
            Vector128<float> vZero = Vector128<float>.Zero;
            Vector128<float> vLookupScale = Vector128.Create(LOOKUP_SCALE);

            for (; i < simdLength; i += 4)
            {
                // Load 4 angles
                Vector128<float> vAngles = Vector128.Create(angles[i], angles[i + 1], angles[i + 2], angles[i + 3]);

                // Normalize angles (scalar fallback for modulo - SIMD modulo is complex)
                // For now, process individually for correctness
                float a0 = angles[i] % TWO_PI;
                float a1 = angles[i + 1] % TWO_PI;
                float a2 = angles[i + 2] % TWO_PI;
                float a3 = angles[i + 3] % TWO_PI;

                if (a0 < 0f) a0 += TWO_PI;
                if (a1 < 0f) a1 += TWO_PI;
                if (a2 < 0f) a2 += TWO_PI;
                if (a3 < 0f) a3 += TWO_PI;

                // Compute indices
                int idx0 = (int)(a0 * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
                int idx1 = (int)(a1 * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
                int idx2 = (int)(a2 * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
                int idx3 = (int)(a3 * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);

                // Gather lookups and store
                results[i] = _sinLookup[idx0];
                results[i + 1] = _sinLookup[idx1];
                results[i + 2] = _sinLookup[idx2];
                results[i + 3] = _sinLookup[idx3];
            }

            // Process remaining angles
            for (; i < count; i++)
            {
                float a = angles[i] % TWO_PI;
                if (a < 0f) a += TWO_PI;
                int idx = (int)(a * LOOKUP_SCALE);
                if ((uint)idx >= LOOKUP_SIZE) idx &= (LOOKUP_SIZE - 1);
                results[i] = _sinLookup[idx];
            }
        }

        /// <summary>
        /// AVX2 batch sine: Process 8 angles per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastSin_AVX2(Span<float> angles, Span<float> results)
        {
            if (!Avx2.IsSupported)
            {
                if (!_sinAvx2Logged)
                {
                    Logger.Log("[SIMD] BatchFastSin_AVX2 CALLED but AVX2 NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _sinAvx2Logged = true;
                }
                BatchFastSin_Scalar(angles, results);
                return;
            }

            if (!_sinAvx2Logged)
            {
                Logger.Log("[SIMD] BatchFastSin_AVX2 CALLED (8 angles per iteration) - AVX2 hardware confirmed");
                _sinAvx2Logged = true;
            }

            if (_sinLookup == null)
                throw new InvalidOperationException("MathOperations not initialized with sine lookup table");

            int count = Math.Min(angles.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 8);

            // Process 8 angles at a time
            for (; i < simdLength; i += 8)
            {
                // Normalize and lookup (scalar for correctness)
                for (int j = 0; j < 8; j++)
                {
                    float a = angles[i + j] % TWO_PI;
                    if (a < 0f) a += TWO_PI;
                    int idx = (int)(a * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
                    results[i + j] = _sinLookup[idx];
                }
            }

            // Process remaining angles
            for (; i < count; i++)
            {
                float a = angles[i] % TWO_PI;
                if (a < 0f) a += TWO_PI;
                int idx = (int)(a * LOOKUP_SCALE);
                if ((uint)idx >= LOOKUP_SIZE) idx &= (LOOKUP_SIZE - 1);
                results[i] = _sinLookup[idx];
            }
        }

        /// <summary>
        /// AVX-512 batch sine: Process 16 angles per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastSin_AVX512(Span<float> angles, Span<float> results)
        {
            if (!Avx512F.IsSupported)
            {
                if (!_sinAvx512Logged)
                {
                    Logger.Log("[SIMD] BatchFastSin_AVX512 CALLED but AVX-512 NOT SUPPORTED - falling back to AVX2", LogSeverity.Warning);
                    _sinAvx512Logged = true;
                }
                BatchFastSin_AVX2(angles, results);
                return;
            }

            if (!_sinAvx512Logged)
            {
                Logger.Log("[SIMD] BatchFastSin_AVX512 CALLED (16 angles per iteration) - AVX-512 hardware confirmed");
                _sinAvx512Logged = true;
            }

            if (_sinLookup == null)
                throw new InvalidOperationException("MathOperations not initialized with sine lookup table");

            int count = Math.Min(angles.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 16);

            // Process 16 angles at a time
            for (; i < simdLength; i += 16)
            {
                // Normalize and lookup (scalar for correctness)
                for (int j = 0; j < 16; j++)
                {
                    float a = angles[i + j] % TWO_PI;
                    if (a < 0f) a += TWO_PI;
                    int idx = (int)(a * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
                    results[i + j] = _sinLookup[idx];
                }
            }

            // Process remaining angles
            for (; i < count; i++)
            {
                float a = angles[i] % TWO_PI;
                if (a < 0f) a += TWO_PI;
                int idx = (int)(a * LOOKUP_SCALE);
                if ((uint)idx >= LOOKUP_SIZE) idx &= (LOOKUP_SIZE - 1);
                results[i] = _sinLookup[idx];
            }
        }

        #endregion

        #region Batch FastInvSqrt Operations

        /// <summary>
        /// Scalar batch inverse square root: Process 1 value per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BatchFastInvSqrt_Scalar(Span<float> values, Span<float> results)
        {
            if (!_invSqrtScalarLogged)
            {
                Logger.Log("[SIMD] BatchFastInvSqrt_Scalar CALLED (1 value per iteration)");
                _invSqrtScalarLogged = true;
            }

            int count = Math.Min(values.Length, results.Length);

            for (int i = 0; i < count; i++)
            {
                // Quake-style fast inverse square root
                float x = values[i];
                float xhalf = 0.5f * x;
                int i32 = BitConverter.SingleToInt32Bits(x);
                i32 = 0x5f3759df - (i32 >> 1);
                x = BitConverter.Int32BitsToSingle(i32);
                x *= (1.5f - xhalf * x * x); // Newton-Raphson iteration
                results[i] = x;
            }
        }

        /// <summary>
        /// SSE batch inverse square root: Process 4 values per iteration using hardware rsqrt.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastInvSqrt_SSE(Span<float> values, Span<float> results)
        {
            if (!Sse.IsSupported)
            {
                if (!_invSqrtSseLogged)
                {
                    Logger.Log("[SIMD] BatchFastInvSqrt_SSE CALLED but SSE NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _invSqrtSseLogged = true;
                }
                BatchFastInvSqrt_Scalar(values, results);
                return;
            }

            if (!_invSqrtSseLogged)
            {
                Logger.Log("[SIMD] BatchFastInvSqrt_SSE CALLED (4 values per iteration) - SSE hardware confirmed");
                _invSqrtSseLogged = true;
            }

            int count = Math.Min(values.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 4);

            for (; i < simdLength; i += 4)
            {
                // Load 4 values
                Vector128<float> vValues = Vector128.Create(values[i], values[i + 1], values[i + 2], values[i + 3]);

                // Hardware reciprocal square root (fast approximation)
                Vector128<float> vResults = Sse.ReciprocalSqrt(vValues);

                // Store results
                results[i] = vResults.GetElement(0);
                results[i + 1] = vResults.GetElement(1);
                results[i + 2] = vResults.GetElement(2);
                results[i + 3] = vResults.GetElement(3);
            }

            // Process remaining values
            for (; i < count; i++)
            {
                float x = values[i];
                float xhalf = 0.5f * x;
                int i32 = BitConverter.SingleToInt32Bits(x);
                i32 = 0x5f3759df - (i32 >> 1);
                x = BitConverter.Int32BitsToSingle(i32);
                x *= (1.5f - xhalf * x * x);
                results[i] = x;
            }
        }

        /// <summary>
        /// AVX2 batch inverse square root: Process 8 values per iteration using hardware rsqrt.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastInvSqrt_AVX2(Span<float> values, Span<float> results)
        {
            if (!Avx.IsSupported)
            {
                if (!_invSqrtAvx2Logged)
                {
                    Logger.Log("[SIMD] BatchFastInvSqrt_AVX2 CALLED but AVX NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _invSqrtAvx2Logged = true;
                }
                BatchFastInvSqrt_Scalar(values, results);
                return;
            }

            if (!_invSqrtAvx2Logged)
            {
                Logger.Log("[SIMD] BatchFastInvSqrt_AVX2 CALLED (8 values per iteration) - AVX hardware confirmed");
                _invSqrtAvx2Logged = true;
            }

            int count = Math.Min(values.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 8);

            for (; i < simdLength; i += 8)
            {
                // Load 8 values
                Vector256<float> vValues = Vector256.Create(
                    values[i], values[i + 1], values[i + 2], values[i + 3],
                    values[i + 4], values[i + 5], values[i + 6], values[i + 7]);

                // Hardware reciprocal square root
                Vector256<float> vResults = Avx.ReciprocalSqrt(vValues);

                // Store results
                results[i] = vResults.GetElement(0);
                results[i + 1] = vResults.GetElement(1);
                results[i + 2] = vResults.GetElement(2);
                results[i + 3] = vResults.GetElement(3);
                results[i + 4] = vResults.GetElement(4);
                results[i + 5] = vResults.GetElement(5);
                results[i + 6] = vResults.GetElement(6);
                results[i + 7] = vResults.GetElement(7);
            }

            // Process remaining values
            for (; i < count; i++)
            {
                float x = values[i];
                float xhalf = 0.5f * x;
                int i32 = BitConverter.SingleToInt32Bits(x);
                i32 = 0x5f3759df - (i32 >> 1);
                x = BitConverter.Int32BitsToSingle(i32);
                x *= (1.5f - xhalf * x * x);
                results[i] = x;
            }
        }

        /// <summary>
        /// AVX-512 batch inverse square root: Process 16 values per iteration using hardware rsqrt.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void BatchFastInvSqrt_AVX512(Span<float> values, Span<float> results)
        {
            if (!Avx512F.IsSupported)
            {
                if (!_invSqrtAvx512Logged)
                {
                    Logger.Log("[SIMD] BatchFastInvSqrt_AVX512 CALLED but AVX-512 NOT SUPPORTED - falling back to AVX2", LogSeverity.Warning);
                    _invSqrtAvx512Logged = true;
                }
                BatchFastInvSqrt_AVX2(values, results);
                return;
            }

            if (!_invSqrtAvx512Logged)
            {
                Logger.Log("[SIMD] BatchFastInvSqrt_AVX512 CALLED (16 values per iteration) - AVX-512 hardware confirmed");
                _invSqrtAvx512Logged = true;
            }

            int count = Math.Min(values.Length, results.Length);
            int i = 0;
            int simdLength = count - (count % 16);

            for (; i < simdLength; i += 16)
            {
                // Load 16 values
                Vector512<float> vValues = Vector512.Create(
                    values[i], values[i + 1], values[i + 2], values[i + 3],
                    values[i + 4], values[i + 5], values[i + 6], values[i + 7],
                    values[i + 8], values[i + 9], values[i + 10], values[i + 11],
                    values[i + 12], values[i + 13], values[i + 14], values[i + 15]);

                // Hardware reciprocal square root (AVX-512 has native rsqrt14)
                Vector512<float> vResults = Avx512F.ReciprocalSqrt14(vValues);

                // Store results
                results[i] = vResults.GetElement(0);
                results[i + 1] = vResults.GetElement(1);
                results[i + 2] = vResults.GetElement(2);
                results[i + 3] = vResults.GetElement(3);
                results[i + 4] = vResults.GetElement(4);
                results[i + 5] = vResults.GetElement(5);
                results[i + 6] = vResults.GetElement(6);
                results[i + 7] = vResults.GetElement(7);
                results[i + 8] = vResults.GetElement(8);
                results[i + 9] = vResults.GetElement(9);
                results[i + 10] = vResults.GetElement(10);
                results[i + 11] = vResults.GetElement(11);
                results[i + 12] = vResults.GetElement(12);
                results[i + 13] = vResults.GetElement(13);
                results[i + 14] = vResults.GetElement(14);
                results[i + 15] = vResults.GetElement(15);
            }

            // Process remaining values
            for (; i < count; i++)
            {
                float x = values[i];
                float xhalf = 0.5f * x;
                int i32 = BitConverter.SingleToInt32Bits(x);
                i32 = 0x5f3759df - (i32 >> 1);
                x = BitConverter.Int32BitsToSingle(i32);
                x *= (1.5f - xhalf * x * x);
                results[i] = x;
            }
        }

        #endregion
    }
}
