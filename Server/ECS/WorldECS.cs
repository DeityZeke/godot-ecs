#nullable enable

using Godot;

using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Systems;
using UltraSim.Logging;

namespace UltraSim.WorldECS
{
    /// <summary>
    /// Main ECS bootstrapper - attach to your scene as the entry point.
    /// Implements IHost to provide Godot-specific services to the ECS framework.
    /// </summary>
    public partial class WorldECS : Node3D, IHost
    {
        [Export] public bool EnableDebugStats = true;
        [Export] public float AutoSaveInterval = 60.0f;

        private World _world = null!;
        private ECSControlPanel _controlPanel = null!;
        private double _accum;
        private double _fpsAccum;
        private int _fpsFrames;
        private int _frameCount = 0;

        #region IHost Implementation

        public object GetRootHandle() => GetTree().Root;

        public UltraSim.IO.IIOProfile? GetIOProfile() => null;

        public void Log(LogEntry entry)
        {
            switch (entry.Severity)
            {
                case LogSeverity.Error:
                    GD.PushError(entry.ToString());
                    break;
                case LogSeverity.Warning:
                    GD.PushWarning(entry.ToString());
                    break;
                default:
                    GD.Print(entry.ToString());
                    break;
            }
        }

        public World GetWorld() => _world;

        #endregion

        #region IEnvironmentInfo Implementation

        public EnvironmentType Environment => EnvironmentType.Hybrid;

        public bool IsDebugBuild
        {
            get
            {
#if DEBUG || USE_DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public string Platform => OS.GetName();

        public string Engine => $"Godot {Godot.Engine.GetVersionInfo()["string"]}";

        public string DotNetVersion => System.Environment.Version.ToString();

        public string? BuildId => null;

        public string ProcessorName => OS.GetProcessorName();

        public int PhysicalCores => OS.GetProcessorCount(); // Godot doesn't distinguish physical vs logical

        public int LogicalCores => OS.GetProcessorCount();

        public SimdSupport MaxSimdSupport
        {
            get
            {
                // Detect SIMD support via .NET intrinsics
                if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported)
                    return SimdSupport.AVX512;
                if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                    return SimdSupport.AVX2;
                if (System.Runtime.Intrinsics.X86.Avx.IsSupported)
                    return SimdSupport.AVX;
                if (System.Runtime.Intrinsics.X86.Sse42.IsSupported)
                    return SimdSupport.SSE3;
                if (System.Runtime.Intrinsics.X86.Sse2.IsSupported)
                    return SimdSupport.SSE2;
                if (System.Runtime.Intrinsics.X86.Sse.IsSupported)
                    return SimdSupport.SSE;
                return SimdSupport.Scalar;
            }
        }

        // Note: Godot doesn't expose total system RAM, so we show current usage and peak
        public long TotalRamMB => (long)(Performance.GetMonitor(Performance.Monitor.MemoryStaticMax) / 1024 / 1024);

        public long AvailableRamMB => (long)(Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024);

        public string GpuName
        {
            get
            {
                var adapter = RenderingServer.GetVideoAdapterName();
                return adapter.Length > 0 ? adapter : "Unknown";
            }
        }

        public string GpuVendor
        {
            get
            {
                var vendor = RenderingServer.GetVideoAdapterVendor();
                return vendor.Length > 0 ? vendor : "Unknown";
            }
        }

        public long TotalVramMB
        {
            get
            {
                // Godot doesn't directly expose VRAM, estimate from texture memory
                var vram = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
                return (long)(vram / 1024 / 1024);
            }
        }

        public string GraphicsAPI
        {
            get
            {
                var apiName = RenderingServer.GetVideoAdapterApiVersion();
                var renderingDevice = RenderingServer.GetRenderingDevice();
                if (renderingDevice != null)
                {
                    return $"Vulkan {apiName}";
                }
                return apiName.Length > 0 ? apiName : "Unknown";
            }
        }

        #endregion

        #region Godot Lifecycle

        public override void _Ready()
        {
            SimContext.Initialize(this);

            GD.Print("========================================");
            GD.Print("      ECS WORLD INITIALIZATION         ");
            GD.Print("========================================");

            _world = new World();

            // Subscribe to world events
            _world.OnInitialized += () => GD.Print("[WorldECS] World initialized.");
            _world.OnFrameComplete += () =>
            {
                if (_frameCount == 1)
                {
                    CreateControlPanel();
                }
            };

            // Register systems
            RegisterSystems();

            // Enable auto-save
            _world.EnableAutoSave(AutoSaveInterval);

            // Initialize world (processes system queues)
            _world.Initialize();

            GD.Print("========================================");
            GD.Print("         ECS WORLD READY                ");
            GD.Print("  Press F12 to open Control Panel      ");
            GD.Print("========================================\n");
        }

        public override void _Process(double delta)
        {
            UltraSim.Logging.Logger.DrainToHost();

            var start = Time.GetTicksUsec();
            _world.Tick(delta);
            var end = Time.GetTicksUsec();

            double frameMs = (end - start) / 1000.0;
            _world.LastTickTimeMs = frameMs; // Update for UI display
            _fpsAccum += frameMs;
            _fpsFrames++;
            _frameCount++;

            if (_accum >= 1.0)
            {
                if (EnableDebugStats)
                {
                    double avg = _fpsAccum / _fpsFrames;
                    GD.Print($"[ECS] Frame: {avg:F3} ms (avg over {_fpsFrames} frames)");
                }
                _accum = 0;
                _fpsAccum = 0;
                _fpsFrames = 0;
            }
            else
            {
                _accum += delta;
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.F12)
                {
                    _controlPanel.Toggle();
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == Key.F11)
                {
                    _world.Save($"manual_{System.DateTime.Now:yyyyMMdd_HHmmss}.sav");
                }
                else if (keyEvent.Keycode == Key.Key9)
                {
                    _world.QuickSave();
                }
                else if (keyEvent.Keycode == Key.Key0)
                {
                    _world.QuickLoad();
                }
            }
        }

        #endregion

        #region Helper Methods

        private void RegisterSystems()
        {
            // Entity spawner (for on-demand entity creation)
            _world.EnqueueSystemCreate(new EntitySpawnerSystem());
            _world.EnqueueSystemEnable<EntitySpawnerSystem>();

            // Movement systems
            _world.EnqueueSystemCreate(new OptimizedMovementSystem());
            _world.EnqueueSystemEnable<OptimizedMovementSystem>();

            _world.EnqueueSystemCreate(new OptimizedPulsingMovementSystem());
            _world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();

            // Rendering system
            _world.EnqueueSystemCreate(new AdaptiveMultiMeshRenderSystem());
            _world.EnqueueSystemEnable<AdaptiveMultiMeshRenderSystem>();
        }

        private void CreateControlPanel()
        {
            var frame = new CanvasLayer();
            GetTree().Root.CallDeferred(MethodName.AddChild, frame);

            _controlPanel = new ECSControlPanel();
            _controlPanel.Initialize(_world);
            frame.CallDeferred(MethodName.AddChild, _controlPanel);
        }

        #endregion
    }
}