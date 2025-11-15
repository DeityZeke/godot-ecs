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
                GraphicsAPI = $"Vulkan {RenderingServer.GetVideoAdapterApiVersion()}"
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

            // Server-side systems
            // NEW: Simplified chunk system (pure entity tracking, no component manipulation)
            world.EnqueueSystemCreate<SimplifiedChunkSystem>();
            world.EnqueueSystemEnable<SimplifiedChunkSystem>();

            world.EnqueueSystemCreate<EntitySpawnerSystem>();
            world.EnqueueSystemEnable<EntitySpawnerSystem>();

            world.EnqueueSystemCreate<OptimizedMovementSystem>();
            world.EnqueueSystemEnable<OptimizedMovementSystem>();

            world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
            world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();

            // NEW: Clean architecture rendering systems (design doc compliant)
            // Each system has SINGLE RESPONSIBILITY:
            // 1. RenderChunkManager: Window + zone tagging
            // 2. RenderVisibilitySystem: Frustum culling
            // 3-5. Zone systems: Build visuals for their assigned zones

            world.EnqueueSystemCreate<RenderChunkManager>();
            world.EnqueueSystemEnable<RenderChunkManager>();

            world.EnqueueSystemCreate<RenderVisibilitySystem>();
            world.EnqueueSystemEnable<RenderVisibilitySystem>();

            world.EnqueueSystemCreate<DynamicEntityRenderSystem>();
            world.EnqueueSystemEnable<DynamicEntityRenderSystem>();

            world.EnqueueSystemCreate<StaticEntityRenderSystem>();
            world.EnqueueSystemEnable<StaticEntityRenderSystem>();

            world.EnqueueSystemCreate<BillboardEntityRenderSystem>();
            world.EnqueueSystemEnable<BillboardEntityRenderSystem>();

            world.EnqueueSystemCreate<SaveSystem>();
            world.EnqueueSystemEnable<SaveSystem>();
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
            var chunkSystemBase = world.Systems.GetSystem<SimplifiedChunkSystem>();

            if (chunkSystemBase is not SimplifiedChunkSystem chunkSystem)
            {
                GD.PushWarning("[ClientHost] SimplifiedChunkSystem not found - hybrid rendering disabled!");
                return;
            }

            var chunkManager = chunkSystem.GetChunkManager();
            if (chunkManager == null)
            {
                GD.PushWarning("[ClientHost] ChunkManager not initialized - hybrid rendering disabled!");
                return;
            }

            // NOTE: Render systems are NOT compatible with SimplifiedChunkSystem/SimplifiedChunkManager
            // The simplified systems use a different architecture (SpatialChunk vs chunk entities)
            // Render systems are disabled when using simplified chunk system
            GD.PrintRich("[color=yellow][ClientHost] Render systems skipped - using SimplifiedChunkSystem (incompatible architecture)[/color]");

            // TODO: Create new render systems compatible with SimplifiedChunkSystem
            // Or refactor SimplifiedChunkSystem to be compatible with existing render systems
    }
    }
}
