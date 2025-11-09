#nullable enable

using Godot;
using UltraSim;
using Client.ECS;
using Client.ECS.Systems;
using UltraSim.ECS.Systems;
using UltraSim.Server.ECS.Systems;
using UltraSim.WorldECS;

namespace Client
{
    /// <summary>
    /// Client bootstrapper that hosts the ECS world for rendering/input.
    /// </summary>
    public partial class Main : WorldHostBase
    {
        protected override string RuntimeProfile => "Client";
        public override EnvironmentType Environment => EnvironmentType.Client;
        private bool _renderSystemsConnected;

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

        protected override void BeforeWorldTick(double delta)
        {
            var camera = GetViewport()?.GetCamera3D();
            if (camera != null)
            {
                CameraCache.Position = camera.GlobalPosition;
                CameraCache.FrustumPlanes = camera.GetFrustum();
                CameraCache.IsValid = true;
            }
            else
            {
                CameraCache.IsValid = false;
                CameraCache.FrustumPlanes = null;
            }
        }

        protected override void RegisterSystems()
        {
            var world = ActiveWorld;

            world.EnqueueSystemCreate<ChunkSystem>();
            world.EnqueueSystemEnable<ChunkSystem>();

            world.EnqueueSystemCreate<EntitySpawnerSystem>();
            world.EnqueueSystemEnable<EntitySpawnerSystem>();

            world.EnqueueSystemCreate<OptimizedMovementSystem>();
            world.EnqueueSystemEnable<OptimizedMovementSystem>();

            world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
            world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();

            world.EnqueueSystemCreate<HybridRenderSystem>();
            world.EnqueueSystemEnable<HybridRenderSystem>();

            world.EnqueueSystemCreate<MeshInstanceBubbleManager>();
            world.EnqueueSystemEnable<MeshInstanceBubbleManager>();

            world.EnqueueSystemCreate<MultiMeshZoneManager>();
            world.EnqueueSystemEnable<MultiMeshZoneManager>();
        }

        protected override void OnWorldFrameProgress(int frameIndex)
        {
            if (!_renderSystemsConnected && frameIndex >= 2)
            {
                ConnectHybridRenderSystems();
                _renderSystemsConnected = true;
            }
        }

        private void ConnectHybridRenderSystems()
        {
            var world = ActiveWorld;
            var chunkSystemBase = world.Systems.GetSystem<ChunkSystem>();

            if (chunkSystemBase is not ChunkSystem chunkSystem)
            {
                GD.PushWarning("[ClientHost] ChunkSystem not found - hybrid rendering disabled!");
                return;
            }

            var chunkManager = chunkSystem.GetChunkManager();
            if (chunkManager == null)
            {
                GD.PushWarning("[ClientHost] ChunkManager not initialized - hybrid rendering disabled!");
                return;
            }

            if (world.Systems.GetSystem<HybridRenderSystem>() is HybridRenderSystem hybridRenderSystem)
            {
                hybridRenderSystem.SetChunkManager(chunkManager);
                GD.Print("[ClientHost] Connected HybridRenderSystem to ChunkManager");
            }

            if (world.Systems.GetSystem<MultiMeshZoneManager>() is MultiMeshZoneManager multiMeshManager)
            {
                multiMeshManager.SetChunkManager(chunkManager);
                GD.Print("[ClientHost] Connected MultiMeshZoneManager to ChunkManager");
            }
        }
    }
}
