using Godot;
using System;

namespace UltraSim.ECS
{
    /// <summary>
    /// Static utilities class containing common math functions and constants
    /// used throughout the ECS system.
    /// </summary>
    public static class Utilities
    {
        // Math constants
        public const float PI = 3.14159265359f;
        public const float TWO_PI = 6.28318530718f;
        public const float HALF_PI = 1.57079632679f;

        // Sine lookup table for performance optimization
        private const int LOOKUP_SIZE = 1024;
        public const float LOOKUP_SCALE = LOOKUP_SIZE / TWO_PI;
        private static readonly float[] _sinLookup;

        // Thread-local random for thread-safe random number generation
        [ThreadStatic]
        private static Random _threadRandom;

        /// <summary>
        /// Static constructor to initialize the sine lookup table.
        /// </summary>
        static Utilities()
        {
            // Pre-compute sine lookup table
            _sinLookup = new float[LOOKUP_SIZE];
            for (int i = 0; i < LOOKUP_SIZE; i++)
            {
                _sinLookup[i] = Mathf.Sin(i / LOOKUP_SCALE);
            }
        }

        /// <summary>
        /// Gets a thread-local Random instance for thread-safe random number generation.
        /// </summary>
        private static Random ThreadRandom
        {
            get
            {
                if (_threadRandom == null)
                {
                    _threadRandom = new Random(Guid.NewGuid().GetHashCode());
                }
                return _threadRandom;
            }
        }

        /// <summary>
        /// Fast inverse square root using Quake-style bit manipulation.
        /// Useful for vector normalization and distance calculations.
        /// </summary>
        /// <param name="x">The number to calculate inverse square root for</param>
        /// <returns>Approximate value of 1/sqrt(x)</returns>
        public static float FastInvSqrt(float x)
        {
            float halfx = 0.5f * x;
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            float y = BitConverter.Int32BitsToSingle(i);
            y = y * (1.5f - halfx * y * y);
            return y;
        }

        /// <summary>
        /// Generates a random point uniformly distributed within a sphere.
        /// Uses spherical coordinates with proper uniform distribution.
        /// </summary>
        /// <param name="radius">The radius of the sphere</param>
        /// <returns>A random Vector3 point within the sphere</returns>
        public static Vector3 RandomPointInSphere(float radius)
        {
            Random random = ThreadRandom;

            float u = (float)random.NextDouble();
            float v = (float)random.NextDouble();
            float theta = u * TWO_PI;
            float phi = Mathf.Acos(2f * v - 1f);
            float r = (float)Math.Pow(random.NextDouble(), 1.0 / 3.0) * radius;

            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            float x = r * sinPhi * cosTheta;
            float y = r * sinPhi * sinTheta;
            float z = r * cosPhi;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Fast sine lookup using pre-computed table.
        /// Trades memory for speed with O(1) lookup time.
        /// </summary>
        /// <param name="angle">The angle in radians</param>
        /// <returns>Approximate sine value</returns>
        public static float FastSin(float angle)
        {
            int index = (int)(angle * LOOKUP_SCALE) & (LOOKUP_SIZE - 1);
            return _sinLookup[index];
        }

        /// <summary>
        /// Fast cosine lookup using pre-computed sine table.
        /// Uses the identity: cos(x) = sin(x + PI/2)
        /// </summary>
        /// <param name="angle">The angle in radians</param>
        /// <returns>Approximate cosine value</returns>
        public static float FastCos(float angle)
        {
            return FastSin(angle + HALF_PI);
        }

        /// <summary>
        /// Generates a random float between 0 and 1 using the thread-local Random.
        /// </summary>
        /// <returns>Random float in range [0, 1)</returns>
        public static float RandomFloat()
        {
            return (float)ThreadRandom.NextDouble();
        }

        /// <summary>
        /// Generates a random float between min and max using the thread-local Random.
        /// </summary>
        /// <param name="min">Minimum value (inclusive)</param>
        /// <param name="max">Maximum value (exclusive)</param>
        /// <returns>Random float in range [min, max)</returns>
        public static float RandomRange(float min, float max)
        {
            return min + (float)ThreadRandom.NextDouble() * (max - min);
        }
    }
}
