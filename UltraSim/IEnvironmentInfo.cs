#nullable enable

using System;

namespace UltraSim
{
    /// <summary>
    /// Defines the execution environment (client-side, server-side, or hybrid).
    /// </summary>
    public enum EnvironmentType
    {
        /// <summary>Client-only (rendering, input, UI)</summary>
        Client,

        /// <summary>Server-only (headless simulation, no rendering)</summary>
        Server,

        /// <summary>Hybrid (both client and server in same process)</summary>
        Hybrid
    }

    /// <summary>
    /// SIMD instruction set support levels for vectorized math operations.
    /// Used to determine optimal math implementations at runtime.
    /// </summary>
    public enum SimdSupport
    {
        /// <summary>No SIMD support - use scalar operations</summary>
        Scalar,

        /// <summary>SSE (Streaming SIMD Extensions) - 128-bit operations</summary>
        SSE,

        /// <summary>SSE2 (Streaming SIMD Extensions 2) - 128-bit operations</summary>
        SSE2,

        /// <summary>SSE3/SSSE3/SSE4 - Extended 128-bit operations</summary>
        SSE3,

        /// <summary>AVX (Advanced Vector Extensions) - 256-bit operations</summary>
        AVX,

        /// <summary>AVX2 (Advanced Vector Extensions 2) - 256-bit operations</summary>
        AVX2,

        /// <summary>AVX-512 - 512-bit operations</summary>
        AVX512
    }

    /// <summary>
    /// Provides comprehensive information about the execution environment and hardware capabilities.
    /// Implemented by the host (e.g., WorldECS) to expose build, runtime, and hardware information.
    /// Systems can query this to make runtime optimization decisions (SIMD, threading, etc.).
    /// </summary>
    public interface IEnvironmentInfo
    {
        #region Build & Environment

        /// <summary>Current execution environment type</summary>
        EnvironmentType Environment { get; }

        /// <summary>Is this a debug build?</summary>
        bool IsDebugBuild { get; }

        /// <summary>Platform name (e.g., "Windows", "Linux", "Android")</summary>
        string Platform { get; }

        /// <summary>Engine name and version (e.g., "Godot 4.5")</summary>
        string Engine { get; }

        /// <summary>.NET runtime version</summary>
        string DotNetVersion { get; }

        /// <summary>Optional custom build identifier</summary>
        string? BuildId { get; }

        #endregion

        #region Hardware - CPU

        /// <summary>Processor name/model</summary>
        string ProcessorName { get; }

        /// <summary>Number of physical CPU cores</summary>
        int PhysicalCores { get; }

        /// <summary>Number of logical CPU cores (including hyperthreading)</summary>
        int LogicalCores { get; }

        /// <summary>Maximum supported SIMD instruction set</summary>
        SimdSupport MaxSimdSupport { get; }

        #endregion

        #region Hardware - Memory

        /// <summary>Total system RAM in MB</summary>
        long TotalRamMB { get; }

        /// <summary>Available system RAM in MB (updated at query time)</summary>
        long AvailableRamMB { get; }

        #endregion

        #region Hardware - GPU

        /// <summary>GPU name/model</summary>
        string GpuName { get; }

        /// <summary>GPU vendor (e.g., "NVIDIA", "AMD", "Intel")</summary>
        string GpuVendor { get; }

        /// <summary>Total VRAM in MB</summary>
        long TotalVramMB { get; }

        /// <summary>Graphics API (e.g., "Vulkan 1.3", "OpenGL 4.6")</summary>
        string GraphicsAPI { get; }

        #endregion
    }
}
