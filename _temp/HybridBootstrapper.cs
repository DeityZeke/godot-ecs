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

    protected override void AfterWorldTick(double delta)
    {
        // No main thread operations needed for new clean architecture
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
            world.EnqueueSystemCreate<SimplifiedChunkSystem>();
            world.EnqueueSystemEnable<SimplifiedChunkSystem>();
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
        // NOTE: Render systems are DISABLED when using SimplifiedChunkSystem
        // They require the old ChunkManager architecture which is incompatible
        // TODO: Create new render systems compatible with SimplifiedChunkManager/SpatialChunk

        if (EnableHybridRendering)
        {
            GD.PrintRich("[color=yellow][HybridBootstrapper] Render systems disabled - incompatible with SimplifiedChunkSystem[/color]");
            // OLD CODE (incompatible with SimplifiedChunkSystem):
            /*
            // NEW: Clean architecture rendering systems (strategy-based split)
            // Each system has SINGLE RESPONSIBILITY:
            // 1. RenderChunkManager: Window + zone tagging + pooling
            // 2. RenderVisibilitySystem: Frustum culling
            // 3. DynamicEntityRenderSystem: Dynamic entities with MeshInstance3D (Near zone)
            // 4. StaticEntityRenderSystem: Static entities with MultiMesh (Near + Mid zones)
            // 5. BillboardEntityRenderSystem: Billboard entities (Far zone)

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
            */
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

        // NOTE: Render systems are NOT compatible with SimplifiedChunkSystem
        // The simplified system uses a different architecture (SpatialChunk vs chunk entities)
        // Render systems are disabled when using SimplifiedChunkSystem
        GD.PrintRich("[color=yellow][HybridBootstrapper] Render systems skipped - using SimplifiedChunkSystem (incompatible architecture)[/color]");

        // TODO: Create new render systems compatible with SimplifiedChunkSystem
        // Or refactor SimplifiedChunkSystem to be compatible with existing render systems
        return;

        // OLD CODE (incompatible with SimplifiedChunkSystem):
        /*
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

        // NEW: Connect clean architecture systems (strategy-based split)

        if (world.Systems.GetSystem<RenderChunkManager>() is RenderChunkManager renderChunkManager)
        {
            renderChunkManager.SetChunkManager(chunkManager);
            GD.Print("[HybridBootstrapper] Connected RenderChunkManager to ChunkManager");
        }

        if (world.Systems.GetSystem<DynamicEntityRenderSystem>() is DynamicEntityRenderSystem dynamicSystem)
        {
            dynamicSystem.SetChunkManager(chunkManager, chunkSystem);
            GD.Print("[HybridBootstrapper] Connected DynamicEntityRenderSystem to ChunkManager + ChunkSystem");
        }

        if (world.Systems.GetSystem<StaticEntityRenderSystem>() is StaticEntityRenderSystem staticSystem)
        {
            staticSystem.SetChunkManager(chunkManager, chunkSystem);
            GD.Print("[HybridBootstrapper] Connected StaticEntityRenderSystem to ChunkManager + ChunkSystem");
        }

        if (_chunkDebugOverlay != null)
        {
            _chunkDebugOverlay.SetChunkManager(chunkManager);
            GD.Print("[HybridBootstrapper] Chunk debug overlay attached");
        }

        GD.Print("[HybridBootstrapper] Clean architecture rendering systems connected successfully!");
        */
    }
}
