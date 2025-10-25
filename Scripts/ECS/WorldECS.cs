#nullable enable

using Godot;
using System;
using UltraSim.ECS;
using UltraSim.ECS.Systems;

namespace UltraSim
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
    public partial class WorldECS : Node3D
    {
        [Export] public int EntityCount = 100;
        [Export] public bool EnableDebugStats = true;
        [Export] public RendererType Renderer = RendererType.MultiMesh;
        [Export] public float SpawnRadius = 50f;
        [Export] public float MinSpeed = 2f;
        [Export] public float MaxSpeed = 8f;
        [Export] public float PulseFrequencyMin = 0.5f;
        [Export] public float PulseFrequencyMax = 2f;

        private World _world = null!;
        private double _accum;
        private double _fpsAccum;
        private int _fpsFrames;
        private Random _random = new Random();
        private bool _entitiesMarkedAsSpawned = false;
        private int _frameCount = 0;

        public override void _Ready()
        {
            GD.Print("========================================");
            GD.Print("      ECS WORLD INITIALIZATION         ");
            GD.Print("========================================");

            _world = new World();

            // Subscribe to world events
            _world.OnInitialized += () => GD.Print("[WorldECS] World initialized.");
            _world.OnEntitiesSpawned += () => GD.Print("[WorldECS] Entities spawned.");
            _world.OnFrameComplete += () => { /* end-of-frame logic */ };

            // Queue systems (still using queues for systems - that's fine!)
            _world.EnqueueSystemCreate(new OptimizedPulsingMovementSystem());
            _world.EnqueueSystemCreate(new OptimizedMovementSystem());


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

            // Add tick rate test systems
            _world.Systems.Register(new EveryFrameTestSystem());      // Runs every frame
            _world.Systems.Register(new FastTickTestSystem());        // 20 Hz (50ms)
            _world.Systems.Register(new MediumTickTestSystem());      // 10 Hz (100ms)
            _world.Systems.Register(new SlowTickTestSystem());        // 1 Hz (1s)
            _world.Systems.Register(new VerySlowTickTestSystem());    // 0.2 Hz (5s)
            _world.Systems.Register(new ManualTestSystem());          // Manual only
            _world.Systems.Register(new SaveSystem());                // Manual only
            _world.Systems.Register(new BucketedUpdateSystem());      // 10 Hz with internal bucketing
            _world.Systems.Register(new SimulatedAISystem());         // 1 Hz (simulates AI)

            _world.EnqueueSystemEnable<EveryFrameTestSystem>();
            _world.EnqueueSystemEnable<FastTickTestSystem>();
            _world.EnqueueSystemEnable<MediumTickTestSystem>();
            _world.EnqueueSystemEnable<SlowTickTestSystem>();
            _world.EnqueueSystemEnable<VerySlowTickTestSystem>();
            _world.EnqueueSystemEnable<BucketedUpdateSystem>();
            _world.EnqueueSystemEnable<SimulatedAISystem>();
            _world.EnqueueSystemEnable<ManualTestSystem>();
            _world.EnqueueSystemEnable<SaveSystem>();



            // NEW: Use StructuralCommandBuffer for FAST entity creation!


            var buffer = new StructuralCommandBuffer();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            GD.Print($"[WorldECS] Creating {EntityCount} entities with command buffer...");

            for (int i = 0; i < EntityCount; i++)
            {
                // Random position in sphere
                Vector3 randomPos = RandomPointInSphere(SpawnRadius);

                // Random speed and pulse frequency
                float speed = (float)(_random.NextDouble() * (MaxSpeed - MinSpeed) + MinSpeed);
                float frequency = (float)(_random.NextDouble() * (PulseFrequencyMax - PulseFrequencyMin) + PulseFrequencyMin);
                float phaseOffset = (float)(_random.NextDouble() * Math.PI * 2);

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

        private Vector3 RandomPointInSphere(float radius)
        {
            // Uniform distribution in sphere using spherical coordinates
            float u = (float)_random.NextDouble();
            float v = (float)_random.NextDouble();
            float theta = u * 2f * Mathf.Pi;
            float phi = Mathf.Acos(2f * v - 1f);
            float r = (float)Math.Pow(_random.NextDouble(), 1.0 / 3.0) * radius;

            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            return new Vector3(
                r * sinPhi * cosTheta,
                r * sinPhi * sinTheta,
                r * cosPhi
            );
        }

        public override void _Process(double delta)
        {
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

        public World GetWorld() => _world;
    }
}