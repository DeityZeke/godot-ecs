namespace UltraSim.ECS.SIMD
{
    /// <summary>
    /// SIMD vectorization modes available for performance optimization.
    /// </summary>
    public enum SimdMode
    {
        /// <summary>Scalar (no SIMD) - baseline performance</summary>
        Scalar = 0,

        /// <summary>128-bit SIMD (SSE/SSE2) - 4 floats at once</summary>
        Simd128 = 128,

        /// <summary>256-bit SIMD (AVX/AVX2) - 8 floats at once</summary>
        Simd256 = 256,

        /// <summary>512-bit SIMD (AVX-512) - 16 floats at once</summary>
        Simd512 = 512
    }

    /// <summary>
    /// Determines which SIMD implementation to use.
    /// </summary>
    public enum SimdCategory
    {
        /// <summary>Core ECS operations (/UltraSim/ECS/)</summary>
        Core,

        /// <summary>Game systems (everything else)</summary>
        Systems
    }
}
