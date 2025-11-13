#nullable enable

using Godot;
using UltraSim;
using UltraSim.ECS.Systems;
using UltraSim.Server.ECS.Systems;
using UltraSim.WorldECS;

namespace Server
{
    /// <summary>
    /// Dedicated server bootstrapper. Uses WorldHostBase for core ECS wiring.
    /// </summary>
    public partial class Main : WorldHostBase
    {
        protected override string RuntimeProfile => "Server";
        public override EnvironmentType Environment => EnvironmentType.Server;

        protected override HostEnvironment BuildHostEnvironment()
        {
            var env = HostEnvironment.Capture();
            var version = Godot.Engine.GetVersionInfo();

            return env with
            {
                Platform = OS.GetName(),
                Engine = $"Godot {version["string"]}",
                DotNetVersion = System.Environment.Version.ToString(),
                ProcessorName = OS.GetProcessorName(),
                PhysicalCores = OS.GetProcessorCount(),
                LogicalCores = OS.GetProcessorCount(),
                TotalRamMB = (long)(Performance.GetMonitor(Performance.Monitor.MemoryStaticMax) / 1024 / 1024),
                AvailableRamMB = (long)(Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024),
                GpuName = RenderingServer.GetVideoAdapterName(),
                GpuVendor = RenderingServer.GetVideoAdapterVendor(),
                TotalVramMB = (long)(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1024 / 1024),
                GraphicsAPI = $"Vulkan {RenderingServer.GetVideoAdapterApiVersion()}",
            };
        }

        protected override void RegisterSystems()
        {
            var world = ActiveWorld;

            // NEW: Simplified chunk system (pure entity tracking, no component manipulation)
            world.EnqueueSystemCreate<SimplifiedChunkSystem>();
            world.EnqueueSystemEnable<SimplifiedChunkSystem>();

            world.EnqueueSystemCreate<EntitySpawnerSystem>();
            world.EnqueueSystemEnable<EntitySpawnerSystem>();

            world.EnqueueSystemCreate<OptimizedMovementSystem>();
            world.EnqueueSystemEnable<OptimizedMovementSystem>();

            world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
            world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();
        }
    }
}
