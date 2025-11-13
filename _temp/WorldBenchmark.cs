#nullable enable

using System;
using System.Diagnostics;
using System.Runtime;
using Godot;
using UltraSim.ECS;
using UltraSim;
using UltraSim.ECS.SIMD;
using UltraSim.IO;
using UltraSim.Server.ECS.Systems;
using Client.ECS.StressTests;

[GlobalClass]
public partial class WorldBenchmark : Node, IHost
{
    [Export] public int WarmupTicks = 100;
    [Export] public int BenchmarkTicks = 1000;
    [Export] public bool GCCollectBefore = true;
    [Export] public bool RunChunkBenchmark = true;

    private World _world = null!;
    private Stopwatch _watch = new Stopwatch();
    public RuntimeContext Runtime { get; private set; } = null!;

    public override void _Ready()
    {
        Runtime = new RuntimeContext(HostEnvironment.Capture(), "Benchmark");
        UltraSim.Logging.Host = this;
        SimdManager.Initialize(Runtime.Environment.SimdSupport);
        _world = new World(this);

        // Register ChunkSystem for chunk assignment benchmark
        if (RunChunkBenchmark)
        {
            _world.EnqueueSystemCreate<ChunkSystem>();
            _world.EnqueueSystemEnable<ChunkSystem>();
            _world.Tick(0.016); // Initialize systems
        }

        RunBenchmark();
    }

    private void RunBenchmark()
    {
        if (RunChunkBenchmark)
        {
            GD.Print($"[WorldBenchmark] Running ChunkAssignmentBenchmark (POST-MERGE)");
            var benchmark = new ChunkAssignmentBenchmark(_world);
            var results = benchmark.RunBenchmarks();

            // Print summary to console
            GD.Print("=================================================================");
            GD.Print("  POST-MERGE BENCHMARK RESULTS");
            GD.Print("=================================================================");
            GD.Print($"Entity Creation: {results.BaselineCreationTimeMs:F2}ms ({results.BaselineCreationThroughput:F0} ent/s)");
            GD.Print($"Movement Avg: {results.BaselineMovementAvgMs:F2}ms, Peak: {results.BaselineMovementPeakMs:F2}ms");
            GD.Print("=================================================================");

            // Exit after benchmark completes
            GD.Print($"[WorldBenchmark] Benchmark complete. Exiting in 1 second...");
            System.Threading.Thread.Sleep(1000); // Give output time to flush
            GetTree().Quit();
            return;
        }

        GD.Print($"[WorldBenchmark] Running standard benchmark for {_world.GetType().Name}");
        if (GCCollectBefore) GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.TryStartNoGCRegion(128 * 1024 * 1024); // optional: lock out GC for more consistent timings

        // Warmup
        for (int i = 0; i < WarmupTicks; i++)
            _world.Tick(0.016f);

        // Timed run
        _watch.Restart();
        for (int i = 0; i < BenchmarkTicks; i++)
            _world.Tick(0.016f);
        _watch.Stop();

        if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion)
            GC.EndNoGCRegion();

        double avgMs = _watch.Elapsed.TotalMilliseconds / BenchmarkTicks;
        double avgUs = avgMs * 1000.0;

        GD.Print($"[WorldBenchmark] Avg Tick: {avgMs:F6} ms ({avgUs:F1} µs) over {BenchmarkTicks} ticks");

        // Optional: check allocations
        long memBefore = GC.GetTotalMemory(false);
        for (int i = 0; i < 100; i++) _world.Tick(0.016f);
        long memAfter = GC.GetTotalMemory(false);
        GD.Print($"[WorldBenchmark] Avg alloc per tick: {(memAfter - memBefore) / 100.0:F1} bytes");

        // Optional: per-system profiling
        ProfileSystems(_world);
    }

    private void ProfileSystems(World world)
    {
        var systems = world.Systems.SystemsSpan;
        if (systems == null || systems.Length == 0)
        {
            GD.Print("[WorldBenchmark] No systems to profile.");
            return;
        }

        GD.Print($"[WorldBenchmark] Profiling {systems.Length} systems...");
        foreach (var sys in systems)
        {
            _watch.Restart();
            for (int i = 0; i < 1000; i++)
                sys.Update(_world, (double)0.016f);
            _watch.Stop();
            GD.Print($"  {sys.GetType().Name,-30} : {_watch.Elapsed.TotalMilliseconds:F6} ms total (~{_watch.Elapsed.TotalMilliseconds / 1000.0:F6} ms per tick)");
        }
    }

    #region IHost

    public EnvironmentType Environment => EnvironmentType.Server;

    public object? GetRootHandle() => GetTree()?.Root;

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

    public IIOProfile? GetIOProfile() => null;

    #endregion
}
