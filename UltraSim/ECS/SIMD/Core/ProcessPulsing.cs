#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using UltraSim.ECS.Components;
using UltraSim;

namespace UltraSim.ECS.SIMD.Core
{
    /// <summary>
    /// SIMD-optimized pulsing movement processing operations.
    /// Processes Phase updates, sine lookups, distance calculations, and velocity updates.
    /// </summary>
    public static class ProcessPulsingOperations
    {
        private static bool _scalarLogged = false;
        private static bool _sseLogged = false;
        private static bool _avx2Logged = false;
        private static bool _avx512Logged = false;

        private const float TWO_PI = 6.28318530718f;
        private const float MIN_DIST_SQ = 0.0001f;

        #region Scalar Implementation

        /// <summary>
        /// Scalar pulsing: Process 1 entity per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessPulsing_Scalar(
            Span<Position> positions,
            Span<Velocity> velocities,
            Span<PulseData> pulseData,
            float delta)
        {
            if (!_scalarLogged)
            {
                Logging.Log("[SIMD] ProcessPulsing_Scalar CALLED (1 entity per iteration)");
                _scalarLogged = true;
            }

            int count = Math.Min(Math.Min(positions.Length, velocities.Length), pulseData.Length);

            for (int i = 0; i < count; i++)
            {
                ref var p = ref positions[i];
                ref var v = ref velocities[i];
                ref var pd = ref pulseData[i];

                // Update phase
                pd.Phase += pd.Frequency * delta;
                if (pd.Phase > TWO_PI)
                    pd.Phase -= TWO_PI;

                float pulseDirection = Utilities.FastSin(pd.Phase);

                // Calculate distance squared
                float distSq = p.X * p.X + p.Y * p.Y + p.Z * p.Z;

                if (distSq > MIN_DIST_SQ)
                {
                    float invDist = Utilities.FastInvSqrt(distSq);
                    float speedFactor = pulseDirection * pd.Speed;

                    v.X = p.X * invDist * speedFactor;
                    v.Y = p.Y * invDist * speedFactor;
                    v.Z = p.Z * invDist * speedFactor;
                }
                else
                {
                    // Random velocity for near-zero distance
                    v.X = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Y = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Z = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                }
            }
        }

        #endregion

        #region SSE (128-bit) Implementation

        /// <summary>
        /// SSE pulsing: Process 4 entities per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ProcessPulsing_SSE(
            Span<Position> positions,
            Span<Velocity> velocities,
            Span<PulseData> pulseData,
            float delta)
        {
            if (!Sse.IsSupported)
            {
                if (!_sseLogged)
                {
                    Logging.Log("[SIMD] ProcessPulsing_SSE CALLED but SSE NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _sseLogged = true;
                }
                ProcessPulsing_Scalar(positions, velocities, pulseData, delta);
                return;
            }

            if (!_sseLogged)
            {
                Logging.Log("[SIMD] ProcessPulsing_SSE CALLED (4 entities per iteration) - SSE hardware confirmed");
                _sseLogged = true;
            }

            int count = Math.Min(Math.Min(positions.Length, velocities.Length), pulseData.Length);
            int i = 0;
            int simdLength = count - (count % 4);

            Vector128<float> vDelta = Vector128.Create(delta);
            Vector128<float> vTwoPi = Vector128.Create(TWO_PI);
            Vector128<float> vMinDistSq = Vector128.Create(MIN_DIST_SQ);

            // Temporary arrays for batch operations (reused across iterations)
            Span<float> phases = stackalloc float[4];
            Span<float> distSqValues = stackalloc float[4];

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                fixed (PulseData* pPulse = pulseData)
                {
                    for (; i < simdLength; i += 4)
                    {
                        // === Phase Updates ===
                        // Load frequencies and current phases
                        Vector128<float> vFreq = Vector128.Create(
                            pPulse[i].Frequency, pPulse[i + 1].Frequency,
                            pPulse[i + 2].Frequency, pPulse[i + 3].Frequency);

                        Vector128<float> vPhase = Vector128.Create(
                            pPulse[i].Phase, pPulse[i + 1].Phase,
                            pPulse[i + 2].Phase, pPulse[i + 3].Phase);

                        // phase += frequency * delta
                        vPhase = Sse.Add(vPhase, Sse.Multiply(vFreq, vDelta));

                        // Wrap phase (scalar for correctness)
                        phases[0] = vPhase.GetElement(0);
                        phases[1] = vPhase.GetElement(1);
                        phases[2] = vPhase.GetElement(2);
                        phases[3] = vPhase.GetElement(3);

                        for (int j = 0; j < 4; j++)
                        {
                            if (phases[j] > TWO_PI)
                                phases[j] -= TWO_PI;
                        }

                        // Store updated phases and calculate sine inline
                        pPulse[i].Phase = phases[0];
                        pPulse[i + 1].Phase = phases[1];
                        pPulse[i + 2].Phase = phases[2];
                        pPulse[i + 3].Phase = phases[3];

                        // === Inline Sine Lookups (avoid batch call overhead) ===
                        float sin0 = Utilities.FastSin(phases[0]);
                        float sin1 = Utilities.FastSin(phases[1]);
                        float sin2 = Utilities.FastSin(phases[2]);
                        float sin3 = Utilities.FastSin(phases[3]);

                        Vector128<float> vPulseDir = Vector128.Create(sin0, sin1, sin2, sin3);

                        // === Distance Squared ===
                        Vector128<float> vPosX = Vector128.Create(pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X);
                        Vector128<float> vPosY = Vector128.Create(pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y);
                        Vector128<float> vPosZ = Vector128.Create(pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z);

                        Vector128<float> vDistSq = Sse.Add(
                            Sse.Add(Sse.Multiply(vPosX, vPosX), Sse.Multiply(vPosY, vPosY)),
                            Sse.Multiply(vPosZ, vPosZ));

                        distSqValues[0] = vDistSq.GetElement(0);
                        distSqValues[1] = vDistSq.GetElement(1);
                        distSqValues[2] = vDistSq.GetElement(2);
                        distSqValues[3] = vDistSq.GetElement(3);

                        // === Inline Inverse Square Root (avoid batch call overhead) ===
                        float invSqrt0 = distSqValues[0] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[0]) : 0f;
                        float invSqrt1 = distSqValues[1] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[1]) : 0f;
                        float invSqrt2 = distSqValues[2] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[2]) : 0f;
                        float invSqrt3 = distSqValues[3] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[3]) : 0f;

                        // === Velocity Calculation ===
                        for (int j = 0; j < 4; j++)
                        {
                            int idx = i + j;
                            float distSq = distSqValues[j];

                            if (distSq > MIN_DIST_SQ)
                            {
                                float invDist = j switch { 0 => invSqrt0, 1 => invSqrt1, 2 => invSqrt2, _ => invSqrt3 };
                                float sinVal = j switch { 0 => sin0, 1 => sin1, 2 => sin2, _ => sin3 };
                                float speedFactor = sinVal * pPulse[idx].Speed;

                                pVel[idx].X = pPos[idx].X * invDist * speedFactor;
                                pVel[idx].Y = pPos[idx].Y * invDist * speedFactor;
                                pVel[idx].Z = pPos[idx].Z * invDist * speedFactor;
                            }
                            else
                            {
                                // Random velocity (scalar fallback)
                                pVel[idx].X = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Y = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Z = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                            }
                        }
                    }
                }
            }

            // Process remaining entities
            for (; i < count; i++)
            {
                ref var p = ref positions[i];
                ref var v = ref velocities[i];
                ref var pd = ref pulseData[i];

                pd.Phase += pd.Frequency * delta;
                if (pd.Phase > TWO_PI)
                    pd.Phase -= TWO_PI;

                float pulseDirection = Utilities.FastSin(pd.Phase);
                float distSq = p.X * p.X + p.Y * p.Y + p.Z * p.Z;

                if (distSq > MIN_DIST_SQ)
                {
                    float invDist = Utilities.FastInvSqrt(distSq);
                    float speedFactor = pulseDirection * pd.Speed;

                    v.X = p.X * invDist * speedFactor;
                    v.Y = p.Y * invDist * speedFactor;
                    v.Z = p.Z * invDist * speedFactor;
                }
                else
                {
                    v.X = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Y = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Z = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                }
            }
        }

        #endregion

        #region AVX2 (256-bit) Implementation

        /// <summary>
        /// AVX2 pulsing: Process 8 entities per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ProcessPulsing_AVX2(
            Span<Position> positions,
            Span<Velocity> velocities,
            Span<PulseData> pulseData,
            float delta)
        {
            if (!Avx2.IsSupported)
            {
                if (!_avx2Logged)
                {
                    Logging.Log("[SIMD] ProcessPulsing_AVX2 CALLED but AVX2 NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _avx2Logged = true;
                }
                ProcessPulsing_Scalar(positions, velocities, pulseData, delta);
                return;
            }

            if (!_avx2Logged)
            {
                Logging.Log("[SIMD] ProcessPulsing_AVX2 CALLED (8 entities per iteration) - AVX2 hardware confirmed");
                _avx2Logged = true;
            }

            int count = Math.Min(Math.Min(positions.Length, velocities.Length), pulseData.Length);
            int i = 0;
            int simdLength = count - (count % 8);

            Vector256<float> vDelta = Vector256.Create(delta);
            Vector256<float> vTwoPi = Vector256.Create(TWO_PI);
            Vector256<float> vMinDistSq = Vector256.Create(MIN_DIST_SQ);

            // Temporary arrays for batch operations (reused across iterations)
            Span<float> phases = stackalloc float[8];
            Span<float> distSqValues = stackalloc float[8];
            Span<float> sineResults = stackalloc float[8];
            Span<float> invSqrtResults = stackalloc float[8];

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                fixed (PulseData* pPulse = pulseData)
                {
                    for (; i < simdLength; i += 8)
                    {
                        // === Phase Updates ===
                        Vector256<float> vFreq = Vector256.Create(
                            pPulse[i].Frequency, pPulse[i + 1].Frequency, pPulse[i + 2].Frequency, pPulse[i + 3].Frequency,
                            pPulse[i + 4].Frequency, pPulse[i + 5].Frequency, pPulse[i + 6].Frequency, pPulse[i + 7].Frequency);

                        Vector256<float> vPhase = Vector256.Create(
                            pPulse[i].Phase, pPulse[i + 1].Phase, pPulse[i + 2].Phase, pPulse[i + 3].Phase,
                            pPulse[i + 4].Phase, pPulse[i + 5].Phase, pPulse[i + 6].Phase, pPulse[i + 7].Phase);

                        // phase += frequency * delta
                        vPhase = Avx.Add(vPhase, Avx.Multiply(vFreq, vDelta));

                        // Extract and wrap phases
                        for (int j = 0; j < 8; j++)
                        {
                            phases[j] = vPhase.GetElement(j);
                            if (phases[j] > TWO_PI)
                                phases[j] -= TWO_PI;
                        }

                        // Store updated phases and calculate sine inline
                        for (int j = 0; j < 8; j++)
                        {
                            pPulse[i + j].Phase = phases[j];
                            sineResults[j] = Utilities.FastSin(phases[j]);
                        }

                        // === Distance Squared ===
                        Vector256<float> vPosX = Vector256.Create(
                            pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X,
                            pPos[i + 4].X, pPos[i + 5].X, pPos[i + 6].X, pPos[i + 7].X);
                        Vector256<float> vPosY = Vector256.Create(
                            pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y,
                            pPos[i + 4].Y, pPos[i + 5].Y, pPos[i + 6].Y, pPos[i + 7].Y);
                        Vector256<float> vPosZ = Vector256.Create(
                            pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z,
                            pPos[i + 4].Z, pPos[i + 5].Z, pPos[i + 6].Z, pPos[i + 7].Z);

                        Vector256<float> vDistSq = Avx.Add(
                            Avx.Add(Avx.Multiply(vPosX, vPosX), Avx.Multiply(vPosY, vPosY)),
                            Avx.Multiply(vPosZ, vPosZ));

                        for (int j = 0; j < 8; j++)
                        {
                            distSqValues[j] = vDistSq.GetElement(j);
                            // Inline inverse square root
                            invSqrtResults[j] = distSqValues[j] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[j]) : 0f;
                        }

                        // === Velocity Calculation ===
                        for (int j = 0; j < 8; j++)
                        {
                            int idx = i + j;
                            float distSq = distSqValues[j];

                            if (distSq > MIN_DIST_SQ)
                            {
                                float invDist = invSqrtResults[j];
                                float speedFactor = sineResults[j] * pPulse[idx].Speed;

                                pVel[idx].X = pPos[idx].X * invDist * speedFactor;
                                pVel[idx].Y = pPos[idx].Y * invDist * speedFactor;
                                pVel[idx].Z = pPos[idx].Z * invDist * speedFactor;
                            }
                            else
                            {
                                pVel[idx].X = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Y = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Z = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                            }
                        }
                    }
                }
            }

            // Process remaining entities
            for (; i < count; i++)
            {
                ref var p = ref positions[i];
                ref var v = ref velocities[i];
                ref var pd = ref pulseData[i];

                pd.Phase += pd.Frequency * delta;
                if (pd.Phase > TWO_PI)
                    pd.Phase -= TWO_PI;

                float pulseDirection = Utilities.FastSin(pd.Phase);
                float distSq = p.X * p.X + p.Y * p.Y + p.Z * p.Z;

                if (distSq > MIN_DIST_SQ)
                {
                    float invDist = Utilities.FastInvSqrt(distSq);
                    float speedFactor = pulseDirection * pd.Speed;

                    v.X = p.X * invDist * speedFactor;
                    v.Y = p.Y * invDist * speedFactor;
                    v.Z = p.Z * invDist * speedFactor;
                }
                else
                {
                    v.X = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Y = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Z = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                }
            }
        }

        #endregion

        #region AVX-512 (512-bit) Implementation

        /// <summary>
        /// AVX-512 pulsing: Process 16 entities per iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ProcessPulsing_AVX512(
            Span<Position> positions,
            Span<Velocity> velocities,
            Span<PulseData> pulseData,
            float delta)
        {
            if (!Avx512F.IsSupported)
            {
                if (!_avx512Logged)
                {
                    Logging.Log("[SIMD] ProcessPulsing_AVX512 CALLED but AVX-512 NOT SUPPORTED - falling back to AVX2", LogSeverity.Warning);
                    _avx512Logged = true;
                }
                ProcessPulsing_AVX2(positions, velocities, pulseData, delta);
                return;
            }

            if (!_avx512Logged)
            {
                Logging.Log("[SIMD] ProcessPulsing_AVX512 CALLED (16 entities per iteration) - AVX-512 hardware confirmed");
                _avx512Logged = true;
            }

            int count = Math.Min(Math.Min(positions.Length, velocities.Length), pulseData.Length);
            int i = 0;
            int simdLength = count - (count % 16);

            Vector512<float> vDelta = Vector512.Create(delta);
            Vector512<float> vTwoPi = Vector512.Create(TWO_PI);
            Vector512<float> vMinDistSq = Vector512.Create(MIN_DIST_SQ);

            // Temporary arrays for batch operations (reused across iterations)
            Span<float> phases = stackalloc float[16];
            Span<float> distSqValues = stackalloc float[16];
            Span<float> sineResults = stackalloc float[16];
            Span<float> invSqrtResults = stackalloc float[16];

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                fixed (PulseData* pPulse = pulseData)
                {
                    for (; i < simdLength; i += 16)
                    {
                        // === Phase Updates ===
                        Vector512<float> vFreq = Vector512.Create(
                            pPulse[i].Frequency, pPulse[i + 1].Frequency, pPulse[i + 2].Frequency, pPulse[i + 3].Frequency,
                            pPulse[i + 4].Frequency, pPulse[i + 5].Frequency, pPulse[i + 6].Frequency, pPulse[i + 7].Frequency,
                            pPulse[i + 8].Frequency, pPulse[i + 9].Frequency, pPulse[i + 10].Frequency, pPulse[i + 11].Frequency,
                            pPulse[i + 12].Frequency, pPulse[i + 13].Frequency, pPulse[i + 14].Frequency, pPulse[i + 15].Frequency);

                        Vector512<float> vPhase = Vector512.Create(
                            pPulse[i].Phase, pPulse[i + 1].Phase, pPulse[i + 2].Phase, pPulse[i + 3].Phase,
                            pPulse[i + 4].Phase, pPulse[i + 5].Phase, pPulse[i + 6].Phase, pPulse[i + 7].Phase,
                            pPulse[i + 8].Phase, pPulse[i + 9].Phase, pPulse[i + 10].Phase, pPulse[i + 11].Phase,
                            pPulse[i + 12].Phase, pPulse[i + 13].Phase, pPulse[i + 14].Phase, pPulse[i + 15].Phase);

                        // phase += frequency * delta
                        vPhase = Avx512F.Add(vPhase, Avx512F.Multiply(vFreq, vDelta));

                        // Extract and wrap phases
                        for (int j = 0; j < 16; j++)
                        {
                            phases[j] = vPhase.GetElement(j);
                            if (phases[j] > TWO_PI)
                                phases[j] -= TWO_PI;
                        }

                        // Store updated phases and calculate sine inline
                        for (int j = 0; j < 16; j++)
                        {
                            pPulse[i + j].Phase = phases[j];
                            sineResults[j] = Utilities.FastSin(phases[j]);
                        }

                        // === Distance Squared ===
                        Vector512<float> vPosX = Vector512.Create(
                            pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X,
                            pPos[i + 4].X, pPos[i + 5].X, pPos[i + 6].X, pPos[i + 7].X,
                            pPos[i + 8].X, pPos[i + 9].X, pPos[i + 10].X, pPos[i + 11].X,
                            pPos[i + 12].X, pPos[i + 13].X, pPos[i + 14].X, pPos[i + 15].X);
                        Vector512<float> vPosY = Vector512.Create(
                            pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y,
                            pPos[i + 4].Y, pPos[i + 5].Y, pPos[i + 6].Y, pPos[i + 7].Y,
                            pPos[i + 8].Y, pPos[i + 9].Y, pPos[i + 10].Y, pPos[i + 11].Y,
                            pPos[i + 12].Y, pPos[i + 13].Y, pPos[i + 14].Y, pPos[i + 15].Y);
                        Vector512<float> vPosZ = Vector512.Create(
                            pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z,
                            pPos[i + 4].Z, pPos[i + 5].Z, pPos[i + 6].Z, pPos[i + 7].Z,
                            pPos[i + 8].Z, pPos[i + 9].Z, pPos[i + 10].Z, pPos[i + 11].Z,
                            pPos[i + 12].Z, pPos[i + 13].Z, pPos[i + 14].Z, pPos[i + 15].Z);

                        Vector512<float> vDistSq = Avx512F.Add(
                            Avx512F.Add(Avx512F.Multiply(vPosX, vPosX), Avx512F.Multiply(vPosY, vPosY)),
                            Avx512F.Multiply(vPosZ, vPosZ));

                        for (int j = 0; j < 16; j++)
                        {
                            distSqValues[j] = vDistSq.GetElement(j);
                            // Inline inverse square root
                            invSqrtResults[j] = distSqValues[j] > MIN_DIST_SQ ? Utilities.FastInvSqrt(distSqValues[j]) : 0f;
                        }

                        // === Velocity Calculation ===
                        for (int j = 0; j < 16; j++)
                        {
                            int idx = i + j;
                            float distSq = distSqValues[j];

                            if (distSq > MIN_DIST_SQ)
                            {
                                float invDist = invSqrtResults[j];
                                float speedFactor = sineResults[j] * pPulse[idx].Speed;

                                pVel[idx].X = pPos[idx].X * invDist * speedFactor;
                                pVel[idx].Y = pPos[idx].Y * invDist * speedFactor;
                                pVel[idx].Z = pPos[idx].Z * invDist * speedFactor;
                            }
                            else
                            {
                                pVel[idx].X = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Y = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                                pVel[idx].Z = Utilities.RandomRange(-0.5f, 0.5f) * pPulse[idx].Speed;
                            }
                        }
                    }
                }
            }

            // Process remaining entities
            for (; i < count; i++)
            {
                ref var p = ref positions[i];
                ref var v = ref velocities[i];
                ref var pd = ref pulseData[i];

                pd.Phase += pd.Frequency * delta;
                if (pd.Phase > TWO_PI)
                    pd.Phase -= TWO_PI;

                float pulseDirection = Utilities.FastSin(pd.Phase);
                float distSq = p.X * p.X + p.Y * p.Y + p.Z * p.Z;

                if (distSq > MIN_DIST_SQ)
                {
                    float invDist = Utilities.FastInvSqrt(distSq);
                    float speedFactor = pulseDirection * pd.Speed;

                    v.X = p.X * invDist * speedFactor;
                    v.Y = p.Y * invDist * speedFactor;
                    v.Z = p.Z * invDist * speedFactor;
                }
                else
                {
                    v.X = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Y = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Z = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                }
            }
        }

        #endregion
    }
}
