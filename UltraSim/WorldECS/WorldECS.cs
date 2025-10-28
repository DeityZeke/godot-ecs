#nullable enable

using System.IO;

using Godot;

using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.ECS.Systems;
using UltraSim.Logging;

namespace UltraSim.WorldECS
{
    /// <summary>
    /// Main ECS manager node - attach to your scene.
    /// </summary>
    public enum RendererType
    {
        NONE,               // No rendering
        IndividualMeshes,  // Original RenderSystem with individual MeshInstance3D nodes
        MultiMesh,         // New MultiMeshRenderSystem with GPU instancing
        Adaptive,          // Future: Adaptive renderer switching based on entity count
    }

    /// <summary>
    /// Main ECS manager node - attach to your scene.
    /// NOW WITH FAST COMMAND BUFFER ENTITY CREATION!
    /// </summary>
    public partial class WorldECS : Node3D, IHost
    {
        [Export] public int EntityCount = 100;
        [Export] public bool EnableDebugStats = true;
        [Export] public RendererType Renderer = RendererType.MultiMesh;
        [Export] public float SpawnRadius = 50f;
        [Export] public float MinSpeed = 2f;
        [Export] public float MaxSpeed = 8f;
        [Export] public float PulseFrequencyMin = 0.5f;
        [Export] public float PulseFrequencyMax = 2f;

        //public static Node RootNode { get; private set; }
        public object GetRootHandle() => GetTree().Root;

        private World _world = null!;
        private ECSControlPanel controlPanel = null!;
        private double _accum;
        private double _fpsAccum;
        private int _fpsFrames;
        private bool _entitiesMarkedAsSpawned = false;
        private int _frameCount = 0;

        public override void _Ready()
        {
            SimContext.Initialize(this);

            GD.Print("========================================");
            GD.Print("      ECS WORLD INITIALIZATION         ");
            GD.Print("========================================");

            //RootNode = GetTree().Root.GetNode<Node>("Main");

            _world = new World();

            // Subscribe to world events
            _world.OnInitialized += () => GD.Print("[WorldECS] World initialized.");
            _world.OnEntitiesSpawned += () => GD.Print("[WorldECS] Entities spawned.");
            _world.OnFrameComplete += () =>
            {
                if (_frameCount == 1)
                {
                    var frame = new CanvasLayer();
                    //CallDeferred(MethodName.AddChild, frame);
                    GetTree().Root.CallDeferred(MethodName.AddChild, frame);

                    controlPanel = new ECSControlPanel();
                    if (controlPanel != null)
                    {
                        controlPanel.Initialize(_world);
                        frame.CallDeferred(MethodName.AddChild, controlPanel);
                    }
                }
            };

            _world.EnqueueSystemCreate(new AdaptiveMultiMeshRenderSystem());
            _world.EnqueueSystemEnable<AdaptiveMultiMeshRenderSystem>();

            _world.EnqueueSystemCreate(new OptimizedPulsingMovementSystem());
            _world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();

            // Queue systems (still using queues for systems - that's fine!)
            //_world.EnqueueSystemCreate(new OptimizedPulsingMovementSystem());
            //_world.EnqueueSystemCreate(new OptimizedMovementSystem());

            /*
            switch (Renderer)
            {

                case RendererType.NONE:
                    GD.Print("[WorldECS] No rendering system selected.");
                    break;
                case RendererType.IndividualMeshes:
                    _world.EnqueueSystemCreate(new RenderSystem());
                    _world.EnqueueSystemEnable<RenderSystem>();
                    break;
                case RendererType.MultiMesh:
                    _world.EnqueueSystemCreate(new MultiMeshRenderSystem());
                    _world.EnqueueSystemEnable<MultiMeshRenderSystem>();
                    break;
                case RendererType.Adaptive:
                    _world.EnqueueSystemCreate(new AdaptiveMultiMeshRenderSystem());
                    _world.EnqueueSystemEnable<AdaptiveMultiMeshRenderSystem>();
                    break;
                default:
                    GD.Print("[WorldECS] Unknown renderer type. No rendering system enabled.");
                    break;
            }
            */

            _world.EnableAutoSave(60f);

            var buffer = new CommandBuffer();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            GD.Print($"[WorldECS] Creating {EntityCount} entities with command buffer...");

            for (int i = 0; i < EntityCount; i++)
            {
                // Random position in sphere
                Vector3 randomPos = Utilities.RandomPointInSphere(SpawnRadius);

                // Random speed and pulse frequency
                float speed = Utilities.RandomRange(MinSpeed, MaxSpeed);
                float frequency = Utilities.RandomRange(PulseFrequencyMin, PulseFrequencyMax);
                float phaseOffset = Utilities.RandomRange(0f, Utilities.TWO_PI);

                // Create entity with ALL components at once (NO archetype thrashing!)
                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position
                    {
                        X = randomPos.X,
                        Y = randomPos.Y,
                        Z = randomPos.Z
                    });

                    builder.Add(new Velocity
                    {
                        X = 0f,
                        Y = 0f,
                        Z = 0f
                    });

                    builder.Add(new PulseData
                    {
                        Speed = speed,
                        Frequency = frequency,
                        Phase = phaseOffset
                    });

                    builder.Add(new RenderTag { });
                    builder.Add(new Visible { });
                });
            }

            sw.Stop();
            GD.Print($"[WorldECS] Queued {EntityCount} entities in {sw.Elapsed.TotalMilliseconds:F3}ms");

            // Initialize world (processes system queues)
            _world.Initialize();

            // Apply the entity creation buffer (FAST - all entities created in 1 frame!)
            sw.Restart();
            buffer.Apply(_world);
            sw.Stop();

            // Mark entities as spawned immediately
            _world.MarkEntitiesSpawned();
            _entitiesMarkedAsSpawned = true;

            GD.Print($"[WorldECS] Applied buffer in {sw.Elapsed.TotalMilliseconds:F3}ms");
            GD.Print($"[WorldECS] Config: Radius={SpawnRadius}, Speed={MinSpeed}-{MaxSpeed}, Freq={PulseFrequencyMin}-{PulseFrequencyMax}Hz");
            GD.Print("========================================");
            GD.Print("         ECS WORLD READY                ");
            GD.Print("========================================\n");
        }

        public override void _Process(double delta)
        {
            UltraSim.Logging.Logger.DrainToHost();

            var start = Time.GetTicksUsec();
            _world.Tick(delta);
            var end = Time.GetTicksUsec();

            double frameMs = (end - start) / 1000.0;
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

                #region Input Handling

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.F12)
                {
                    controlPanel.Toggle();
                    GetViewport().SetInputAsHandled();
                }
                if (keyEvent.Keycode == Key.F11)
                {
                    //InvokeManualSystem<SaveSystem>();
                    _world!.Save($"manual_{System.DateTime.Now:yyyyMMdd_HHmmss}.sav");
                }


                if (keyEvent.Keycode == Key.Key9)
                {
                    _world!.QuickSave();
                }

                if (keyEvent.Keycode == Key.Key0)
                {
                    _world!.QuickLoad();
                }
            }
        }

        #endregion

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

    }
}