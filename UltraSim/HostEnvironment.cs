#nullable enable

using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltraSim
{
    /// <summary>
    /// Immutable snapshot of the host hardware and runtime environment.
    /// </summary>
    public sealed record HostEnvironment
    {
        public string Platform { get; init; } = "Unknown";
        public string Engine { get; init; } = "Unknown";
        public string DotNetVersion { get; init; } = "";
        public string? BuildId { get; init; }
        public string ProcessorName { get; init; } = "Unknown";
        public int PhysicalCores { get; init; }
        public int LogicalCores { get; init; }
        public SimdSupport SimdSupport { get; init; } = SimdSupport.Scalar;
        public long TotalRamMB { get; init; }
        public long AvailableRamMB { get; init; }
        public string GpuName { get; init; } = "Unknown";
        public string GpuVendor { get; init; } = "Unknown";
        public long TotalVramMB { get; init; }
        public string GraphicsAPI { get; init; } = "Unknown";
        public string AppDirectory { get; init; } = "";
        public bool IsDebugBuild { get; init; }
        public string OSDescription { get; init; } = "";
        public Architecture OSArchitecture { get; init; }
        public long ProcessMemoryMB { get; init; }

        public static HostEnvironment Capture()
        {
            var process = Process.GetCurrentProcess();
#if DEBUG || USE_DEBUG
            const bool debugBuild = true;
#else
            const bool debugBuild = false;
#endif

            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            var availableMemory = GC.GetTotalMemory(false) / (1024 * 1024);

            return new HostEnvironment
            {
                Platform = RuntimeInformation.OSDescription,
                Engine = "Unknown",
                DotNetVersion = RuntimeInformation.FrameworkDescription,
                BuildId = null,
                ProcessorName = GetCpuName(),
                PhysicalCores = Environment.ProcessorCount,
                LogicalCores = Environment.ProcessorCount,
                SimdSupport = DetectSimdSupport(),
                TotalRamMB = totalMemory,
                AvailableRamMB = availableMemory,
                GpuName = "Unknown",
                GpuVendor = "Unknown",
                TotalVramMB = 0,
                GraphicsAPI = "Unknown",
                AppDirectory = AppContext.BaseDirectory,
                IsDebugBuild = debugBuild,
                OSDescription = RuntimeInformation.OSDescription,
                OSArchitecture = RuntimeInformation.OSArchitecture,
                ProcessMemoryMB = process.WorkingSet64 / (1024 * 1024)
            };
        }

        private static string GetCpuName()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return System.IO.File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':')[1].Trim() ?? "Unknown CPU";
                return "Unknown CPU";
            }
            catch
            {
                return "Unknown CPU";
            }
        }

        private static SimdSupport DetectSimdSupport()
        {
            if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported) return SimdSupport.AVX512;
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported) return SimdSupport.AVX2;
            if (System.Runtime.Intrinsics.X86.Avx.IsSupported) return SimdSupport.AVX;
            if (System.Runtime.Intrinsics.X86.Sse42.IsSupported) return SimdSupport.SSE3;
            if (System.Runtime.Intrinsics.X86.Sse2.IsSupported) return SimdSupport.SSE2;
            if (System.Runtime.Intrinsics.X86.Sse.IsSupported) return SimdSupport.SSE;
            return SimdSupport.Scalar;
        }
    }

    public enum EnvironmentType
    {
        Client,
        Server,
        Hybrid
    }

    public enum SimdSupport
    {
        Scalar,
        SSE,
        SSE2,
        SSE3,
        AVX,
        AVX2,
        AVX512
    }
}
