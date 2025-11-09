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
    /// SIMD-optimized velocity application operations.
    /// Multiple implementations for different SIMD instruction sets.
    /// </summary>
    public static class ApplyVelocityOperations
    {
        private static bool _scalarLogged = false;
        private static bool _sseLogged = false;
        private static bool _avx2Logged = false;
        private static bool _avx512Logged = false;

        /// <summary>
        /// Scalar implementation: Process 1 entity per iteration.
        /// Baseline implementation for correctness validation and non-SIMD hardware.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ApplyVelocity_Scalar(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            if (!_scalarLogged)
            {
                Logging.Log("[SIMD] ApplyVelocity_Scalar CALLED (1 entity per iteration)");
                _scalarLogged = true;
            }

            int count = Math.Min(positions.Length, velocities.Length);

            for (int i = 0; i < count; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }

        /// <summary>
        /// SSE implementation: Process 4 entities per iteration (4 floats per vector).
        /// Uses separate vectors for X, Y, Z components (SoA-style processing on AoS data).
        /// Falls back to scalar for remainder entities.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ApplyVelocity_SSE(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            // Fallback to scalar if SSE not supported
            if (!Sse.IsSupported)
            {
                if (!_sseLogged)
                {
                    Logging.Log("[SIMD] ApplyVelocity_SSE CALLED but SSE NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _sseLogged = true;
                }
                ApplyVelocity_Scalar(positions, velocities, delta);
                return;
            }

            if (!_sseLogged)
            {
                Logging.Log("[SIMD] ApplyVelocity_SSE CALLED (4 entities per iteration) - SSE hardware confirmed");
                _sseLogged = true;
            }

            int count = Math.Min(positions.Length, velocities.Length);
            int i = 0;

            // SIMD processing: 4 entities at a time
            int simdLength = count - (count % 4);
            Vector128<float> vDelta = Vector128.Create(delta);

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                {
                    for (; i < simdLength; i += 4)
                    {
                        // Load X components from 4 entities
                        Vector128<float> vPosX = Vector128.Create(
                            pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X);
                        Vector128<float> vVelX = Vector128.Create(
                            pVel[i].X, pVel[i + 1].X, pVel[i + 2].X, pVel[i + 3].X);

                        // Load Y components from 4 entities
                        Vector128<float> vPosY = Vector128.Create(
                            pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y);
                        Vector128<float> vVelY = Vector128.Create(
                            pVel[i].Y, pVel[i + 1].Y, pVel[i + 2].Y, pVel[i + 3].Y);

                        // Load Z components from 4 entities
                        Vector128<float> vPosZ = Vector128.Create(
                            pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z);
                        Vector128<float> vVelZ = Vector128.Create(
                            pVel[i].Z, pVel[i + 1].Z, pVel[i + 2].Z, pVel[i + 3].Z);

                        // Compute: pos += vel * delta
                        vPosX = Sse.Add(vPosX, Sse.Multiply(vVelX, vDelta));
                        vPosY = Sse.Add(vPosY, Sse.Multiply(vVelY, vDelta));
                        vPosZ = Sse.Add(vPosZ, Sse.Multiply(vVelZ, vDelta));

                        // Store X components back to 4 entities
                        pPos[i].X = vPosX.GetElement(0);
                        pPos[i + 1].X = vPosX.GetElement(1);
                        pPos[i + 2].X = vPosX.GetElement(2);
                        pPos[i + 3].X = vPosX.GetElement(3);

                        // Store Y components back to 4 entities
                        pPos[i].Y = vPosY.GetElement(0);
                        pPos[i + 1].Y = vPosY.GetElement(1);
                        pPos[i + 2].Y = vPosY.GetElement(2);
                        pPos[i + 3].Y = vPosY.GetElement(3);

                        // Store Z components back to 4 entities
                        pPos[i].Z = vPosZ.GetElement(0);
                        pPos[i + 1].Z = vPosZ.GetElement(1);
                        pPos[i + 2].Z = vPosZ.GetElement(2);
                        pPos[i + 3].Z = vPosZ.GetElement(3);
                    }
                }
            }

            // Process remaining entities with scalar code (0-3 entities)
            for (; i < count; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }

        /// <summary>
        /// AVX2 implementation: Process 8 entities per iteration (8 floats per vector).
        /// Uses separate vectors for X, Y, Z components (SoA-style processing on AoS data).
        /// Falls back to scalar for remainder entities.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ApplyVelocity_AVX2(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            // Fallback to scalar if AVX2 not supported
            if (!Avx2.IsSupported)
            {
                if (!_avx2Logged)
                {
                    Logging.Log("[SIMD] ApplyVelocity_AVX2 CALLED but AVX2 NOT SUPPORTED - falling back to Scalar", LogSeverity.Warning);
                    _avx2Logged = true;
                }
                ApplyVelocity_Scalar(positions, velocities, delta);
                return;
            }

            if (!_avx2Logged)
            {
                Logging.Log("[SIMD] ApplyVelocity_AVX2 CALLED (8 entities per iteration) - AVX2 hardware confirmed");
                _avx2Logged = true;
            }

            int count = Math.Min(positions.Length, velocities.Length);
            int i = 0;

            // SIMD processing: 8 entities at a time
            int simdLength = count - (count % 8);
            Vector256<float> vDelta = Vector256.Create(delta);

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                {
                    for (; i < simdLength; i += 8)
                    {
                        // Load X components from 8 entities
                        Vector256<float> vPosX = Vector256.Create(
                            pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X,
                            pPos[i + 4].X, pPos[i + 5].X, pPos[i + 6].X, pPos[i + 7].X);
                        Vector256<float> vVelX = Vector256.Create(
                            pVel[i].X, pVel[i + 1].X, pVel[i + 2].X, pVel[i + 3].X,
                            pVel[i + 4].X, pVel[i + 5].X, pVel[i + 6].X, pVel[i + 7].X);

                        // Load Y components from 8 entities
                        Vector256<float> vPosY = Vector256.Create(
                            pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y,
                            pPos[i + 4].Y, pPos[i + 5].Y, pPos[i + 6].Y, pPos[i + 7].Y);
                        Vector256<float> vVelY = Vector256.Create(
                            pVel[i].Y, pVel[i + 1].Y, pVel[i + 2].Y, pVel[i + 3].Y,
                            pVel[i + 4].Y, pVel[i + 5].Y, pVel[i + 6].Y, pVel[i + 7].Y);

                        // Load Z components from 8 entities
                        Vector256<float> vPosZ = Vector256.Create(
                            pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z,
                            pPos[i + 4].Z, pPos[i + 5].Z, pPos[i + 6].Z, pPos[i + 7].Z);
                        Vector256<float> vVelZ = Vector256.Create(
                            pVel[i].Z, pVel[i + 1].Z, pVel[i + 2].Z, pVel[i + 3].Z,
                            pVel[i + 4].Z, pVel[i + 5].Z, pVel[i + 6].Z, pVel[i + 7].Z);

                        // Compute: pos += vel * delta
                        vPosX = Avx.Add(vPosX, Avx.Multiply(vVelX, vDelta));
                        vPosY = Avx.Add(vPosY, Avx.Multiply(vVelY, vDelta));
                        vPosZ = Avx.Add(vPosZ, Avx.Multiply(vVelZ, vDelta));

                        // Store X components back to 8 entities
                        pPos[i].X = vPosX.GetElement(0);
                        pPos[i + 1].X = vPosX.GetElement(1);
                        pPos[i + 2].X = vPosX.GetElement(2);
                        pPos[i + 3].X = vPosX.GetElement(3);
                        pPos[i + 4].X = vPosX.GetElement(4);
                        pPos[i + 5].X = vPosX.GetElement(5);
                        pPos[i + 6].X = vPosX.GetElement(6);
                        pPos[i + 7].X = vPosX.GetElement(7);

                        // Store Y components back to 8 entities
                        pPos[i].Y = vPosY.GetElement(0);
                        pPos[i + 1].Y = vPosY.GetElement(1);
                        pPos[i + 2].Y = vPosY.GetElement(2);
                        pPos[i + 3].Y = vPosY.GetElement(3);
                        pPos[i + 4].Y = vPosY.GetElement(4);
                        pPos[i + 5].Y = vPosY.GetElement(5);
                        pPos[i + 6].Y = vPosY.GetElement(6);
                        pPos[i + 7].Y = vPosY.GetElement(7);

                        // Store Z components back to 8 entities
                        pPos[i].Z = vPosZ.GetElement(0);
                        pPos[i + 1].Z = vPosZ.GetElement(1);
                        pPos[i + 2].Z = vPosZ.GetElement(2);
                        pPos[i + 3].Z = vPosZ.GetElement(3);
                        pPos[i + 4].Z = vPosZ.GetElement(4);
                        pPos[i + 5].Z = vPosZ.GetElement(5);
                        pPos[i + 6].Z = vPosZ.GetElement(6);
                        pPos[i + 7].Z = vPosZ.GetElement(7);
                    }
                }
            }

            // Process remaining entities with scalar code (0-7 entities)
            for (; i < count; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }

        /// <summary>
        /// AVX-512 implementation: Process 16 entities per iteration (16 floats per vector).
        /// Uses separate vectors for X, Y, Z components (SoA-style processing on AoS data).
        /// Falls back to AVX2 for remainder entities.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ApplyVelocity_AVX512(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            // Fallback to AVX2 if AVX-512 not supported
            if (!Avx512F.IsSupported)
            {
                if (!_avx512Logged)
                {
                    Logging.Log("[SIMD] ApplyVelocity_AVX512 CALLED but AVX-512 NOT SUPPORTED - falling back to AVX2", LogSeverity.Warning);
                    _avx512Logged = true;
                }
                ApplyVelocity_AVX2(positions, velocities, delta);
                return;
            }

            if (!_avx512Logged)
            {
                Logging.Log("[SIMD] ApplyVelocity_AVX512 CALLED (16 entities per iteration) - AVX-512 hardware confirmed");
                _avx512Logged = true;
            }

            int count = Math.Min(positions.Length, velocities.Length);
            int i = 0;

            // SIMD processing: 16 entities at a time
            int simdLength = count - (count % 16);
            Vector512<float> vDelta = Vector512.Create(delta);

            unsafe
            {
                fixed (Position* pPos = positions)
                fixed (Velocity* pVel = velocities)
                {
                    for (; i < simdLength; i += 16)
                    {
                        // Load X components from 16 entities
                        Vector512<float> vPosX = Vector512.Create(
                            pPos[i].X, pPos[i + 1].X, pPos[i + 2].X, pPos[i + 3].X,
                            pPos[i + 4].X, pPos[i + 5].X, pPos[i + 6].X, pPos[i + 7].X,
                            pPos[i + 8].X, pPos[i + 9].X, pPos[i + 10].X, pPos[i + 11].X,
                            pPos[i + 12].X, pPos[i + 13].X, pPos[i + 14].X, pPos[i + 15].X);
                        Vector512<float> vVelX = Vector512.Create(
                            pVel[i].X, pVel[i + 1].X, pVel[i + 2].X, pVel[i + 3].X,
                            pVel[i + 4].X, pVel[i + 5].X, pVel[i + 6].X, pVel[i + 7].X,
                            pVel[i + 8].X, pVel[i + 9].X, pVel[i + 10].X, pVel[i + 11].X,
                            pVel[i + 12].X, pVel[i + 13].X, pVel[i + 14].X, pVel[i + 15].X);

                        // Load Y components from 16 entities
                        Vector512<float> vPosY = Vector512.Create(
                            pPos[i].Y, pPos[i + 1].Y, pPos[i + 2].Y, pPos[i + 3].Y,
                            pPos[i + 4].Y, pPos[i + 5].Y, pPos[i + 6].Y, pPos[i + 7].Y,
                            pPos[i + 8].Y, pPos[i + 9].Y, pPos[i + 10].Y, pPos[i + 11].Y,
                            pPos[i + 12].Y, pPos[i + 13].Y, pPos[i + 14].Y, pPos[i + 15].Y);
                        Vector512<float> vVelY = Vector512.Create(
                            pVel[i].Y, pVel[i + 1].Y, pVel[i + 2].Y, pVel[i + 3].Y,
                            pVel[i + 4].Y, pVel[i + 5].Y, pVel[i + 6].Y, pVel[i + 7].Y,
                            pVel[i + 8].Y, pVel[i + 9].Y, pVel[i + 10].Y, pVel[i + 11].Y,
                            pVel[i + 12].Y, pVel[i + 13].Y, pVel[i + 14].Y, pVel[i + 15].Y);

                        // Load Z components from 16 entities
                        Vector512<float> vPosZ = Vector512.Create(
                            pPos[i].Z, pPos[i + 1].Z, pPos[i + 2].Z, pPos[i + 3].Z,
                            pPos[i + 4].Z, pPos[i + 5].Z, pPos[i + 6].Z, pPos[i + 7].Z,
                            pPos[i + 8].Z, pPos[i + 9].Z, pPos[i + 10].Z, pPos[i + 11].Z,
                            pPos[i + 12].Z, pPos[i + 13].Z, pPos[i + 14].Z, pPos[i + 15].Z);
                        Vector512<float> vVelZ = Vector512.Create(
                            pVel[i].Z, pVel[i + 1].Z, pVel[i + 2].Z, pVel[i + 3].Z,
                            pVel[i + 4].Z, pVel[i + 5].Z, pVel[i + 6].Z, pVel[i + 7].Z,
                            pVel[i + 8].Z, pVel[i + 9].Z, pVel[i + 10].Z, pVel[i + 11].Z,
                            pVel[i + 12].Z, pVel[i + 13].Z, pVel[i + 14].Z, pVel[i + 15].Z);

                        // Compute: pos += vel * delta
                        vPosX = Avx512F.Add(vPosX, Avx512F.Multiply(vVelX, vDelta));
                        vPosY = Avx512F.Add(vPosY, Avx512F.Multiply(vVelY, vDelta));
                        vPosZ = Avx512F.Add(vPosZ, Avx512F.Multiply(vVelZ, vDelta));

                        // Store X components back to 16 entities
                        pPos[i].X = vPosX.GetElement(0);
                        pPos[i + 1].X = vPosX.GetElement(1);
                        pPos[i + 2].X = vPosX.GetElement(2);
                        pPos[i + 3].X = vPosX.GetElement(3);
                        pPos[i + 4].X = vPosX.GetElement(4);
                        pPos[i + 5].X = vPosX.GetElement(5);
                        pPos[i + 6].X = vPosX.GetElement(6);
                        pPos[i + 7].X = vPosX.GetElement(7);
                        pPos[i + 8].X = vPosX.GetElement(8);
                        pPos[i + 9].X = vPosX.GetElement(9);
                        pPos[i + 10].X = vPosX.GetElement(10);
                        pPos[i + 11].X = vPosX.GetElement(11);
                        pPos[i + 12].X = vPosX.GetElement(12);
                        pPos[i + 13].X = vPosX.GetElement(13);
                        pPos[i + 14].X = vPosX.GetElement(14);
                        pPos[i + 15].X = vPosX.GetElement(15);

                        // Store Y components back to 16 entities
                        pPos[i].Y = vPosY.GetElement(0);
                        pPos[i + 1].Y = vPosY.GetElement(1);
                        pPos[i + 2].Y = vPosY.GetElement(2);
                        pPos[i + 3].Y = vPosY.GetElement(3);
                        pPos[i + 4].Y = vPosY.GetElement(4);
                        pPos[i + 5].Y = vPosY.GetElement(5);
                        pPos[i + 6].Y = vPosY.GetElement(6);
                        pPos[i + 7].Y = vPosY.GetElement(7);
                        pPos[i + 8].Y = vPosY.GetElement(8);
                        pPos[i + 9].Y = vPosY.GetElement(9);
                        pPos[i + 10].Y = vPosY.GetElement(10);
                        pPos[i + 11].Y = vPosY.GetElement(11);
                        pPos[i + 12].Y = vPosY.GetElement(12);
                        pPos[i + 13].Y = vPosY.GetElement(13);
                        pPos[i + 14].Y = vPosY.GetElement(14);
                        pPos[i + 15].Y = vPosY.GetElement(15);

                        // Store Z components back to 16 entities
                        pPos[i].Z = vPosZ.GetElement(0);
                        pPos[i + 1].Z = vPosZ.GetElement(1);
                        pPos[i + 2].Z = vPosZ.GetElement(2);
                        pPos[i + 3].Z = vPosZ.GetElement(3);
                        pPos[i + 4].Z = vPosZ.GetElement(4);
                        pPos[i + 5].Z = vPosZ.GetElement(5);
                        pPos[i + 6].Z = vPosZ.GetElement(6);
                        pPos[i + 7].Z = vPosZ.GetElement(7);
                        pPos[i + 8].Z = vPosZ.GetElement(8);
                        pPos[i + 9].Z = vPosZ.GetElement(9);
                        pPos[i + 10].Z = vPosZ.GetElement(10);
                        pPos[i + 11].Z = vPosZ.GetElement(11);
                        pPos[i + 12].Z = vPosZ.GetElement(12);
                        pPos[i + 13].Z = vPosZ.GetElement(13);
                        pPos[i + 14].Z = vPosZ.GetElement(14);
                        pPos[i + 15].Z = vPosZ.GetElement(15);
                    }
                }
            }

            // Process remaining entities with scalar code (0-15 entities)
            for (; i < count; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }
    }
}
