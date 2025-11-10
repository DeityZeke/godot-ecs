#nullable enable

using Godot;

using Client.Debug;
using Client.ECS;
using Client.ECS.Systems;

using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Systems;
using UltraSim.Server.ECS.Systems;
using UltraSim.WorldECS;

/// <summary>
/// Debug-only bootstrapper that merges the dedicated server and client hosts into one Node3D.
/// Useful for validating hybrid rendering while the gameplay world runs in-process.
/// Attach this to a Node3D inside `_temp/` scenes to spin up a full ECS stack.
/// </summary>
[GlobalClass]
public partial class HybridBootstrapper : WorldHostBase
{
    private bool _renderSystemsConnected;

    [Export] public bool EnableChunkSystems { get; set; } = true;
    [Export] public bool EnableHybridRendering { get; set; } = true;
    [Export] public bool EnableAdaptiveRenderer { get; set; } = false;
    [Export] public bool EnablePulsingMovement { get; set; } = false;
    [Export] public bool EnableChunkDebugOverlay { get; set; } = true;

    protected override string RuntimeProfile => "Hybrid";
    public override EnvironmentType Environment => EnvironmentType.Hybrid;

    private ChunkDebugOverlay? _chunkDebugOverlay;

    protected override HostEnvironment BuildHostEnvironment()
    {
        var env = HostEnvironment.Capture();
        var version = Engine.GetVersionInfo();

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

    protected override void OnWorldCreated(World world)
    {
        base.OnWorldCreated(world);

        if (EnableChunkDebugOverlay)
        {
            _chunkDebugOverlay = new ChunkDebugOverlay
            {
                Name = "ChunkDebugOverlay"
            };
            AddChild(_chunkDebugOverlay);
        }
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

        RegisterServerSystems(world);
        RegisterClientSystems(world);
    }

    protected override void OnWorldFrameProgress(int frameIndex)
    {
        if (!_renderSystemsConnected && EnableChunkSystems && EnableHybridRendering && frameIndex >= 2)
        {
            ConnectHybridRenderSystems();
            _renderSystemsConnected = true;
        }
    }

    private void RegisterServerSystems(World world)
    {
        if (EnableChunkSystems)
        {
            world.EnqueueSystemCreate<ChunkSystem>();
            world.EnqueueSystemEnable<ChunkSystem>();
        }

        world.EnqueueSystemCreate<EntitySpawnerSystem>();
        world.EnqueueSystemEnable<EntitySpawnerSystem>();

        world.EnqueueSystemCreate<OptimizedMovementSystem>();
        world.EnqueueSystemEnable<OptimizedMovementSystem>();

        if (EnablePulsingMovement)
        {
            world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
            world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();
        }
    }

    private void RegisterClientSystems(World world)
    {
        if (EnableHybridRendering)
        {
            world.EnqueueSystemCreate<HybridRenderSystem>();
            world.EnqueueSystemEnable<HybridRenderSystem>();

            world.EnqueueSystemCreate<MeshInstanceBubbleManager>();
            world.EnqueueSystemEnable<MeshInstanceBubbleManager>();

            world.EnqueueSystemCreate<MultiMeshZoneManager>();
            world.EnqueueSystemEnable<MultiMeshZoneManager>();
        }

        if (EnableAdaptiveRenderer)
        {
            world.EnqueueSystemCreate<AdaptiveMultiMeshRenderSystem>();
            world.EnqueueSystemEnable<AdaptiveMultiMeshRenderSystem>();
        }
    }

    private void ConnectHybridRenderSystems()
    {
        if (!EnableHybridRendering)
            return;

        var world = ActiveWorld;
        var chunkSystemBase = world.Systems.GetSystem<ChunkSystem>();

        if (chunkSystemBase is not ChunkSystem chunkSystem)
        {
            GD.PushWarning("[HybridBootstrapper] ChunkSystem not found - hybrid rendering disabled!");
            return;
        }

        var chunkManager = chunkSystem.GetChunkManager();
        if (chunkManager == null)
        {
            GD.PushWarning("[HybridBootstrapper] ChunkManager not initialized - hybrid rendering disabled!");
            return;
        }

        if (world.Systems.GetSystem<HybridRenderSystem>() is HybridRenderSystem hybridRenderSystem)
        {
            hybridRenderSystem.SetChunkManager(chunkManager);
            GD.Print("[HybridBootstrapper] Connected HybridRenderSystem to ChunkManager");
        }

        if (world.Systems.GetSystem<MultiMeshZoneManager>() is MultiMeshZoneManager multiMeshManager)
        {
            multiMeshManager.SetChunkManager(chunkManager, chunkSystem);
            GD.Print("[HybridBootstrapper] Connected MultiMeshZoneManager to ChunkManager + ChunkSystem");
        }

        if (world.Systems.GetSystem<MeshInstanceBubbleManager>() is MeshInstanceBubbleManager bubbleManager)
        {
            bubbleManager.SetChunkManager(chunkManager, chunkSystem);
            GD.Print("[HybridBootstrapper] Connected MeshInstanceBubbleManager to ChunkManager + ChunkSystem");
        }

        if (_chunkDebugOverlay != null)
        {
            _chunkDebugOverlay.SetChunkManager(chunkManager);
            GD.Print("[HybridBootstrapper] Chunk debug overlay attached");
        }
    }
}
