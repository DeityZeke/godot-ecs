#nullable enable

using System;
using System.Diagnostics;
using UltraSim;
using UltraSim.ECS;

namespace EntityCreationBench;

internal static class Program
{
    private const int DefaultEntityCount = 100_000;

    private static void Main()
    {
        Console.WriteLine("============================================");
        Console.WriteLine(" Entity Builder Creation Benchmarks");
        Console.WriteLine("============================================");

        RunBenchmark("SingleSignature", DefaultEntityCount, BuildSingleSignatureEntity);
        RunBenchmark("MixedSignatures (4 archetypes)", DefaultEntityCount, BuildMixedSignatureEntity);

        Console.WriteLine();
    }

    private static BenchmarkResult RunBenchmark(
        string name,
        int entityCount,
        Func<World, int, EntityBuilder> builderFactory)
    {
        // Fresh world per benchmark to avoid cross-run interference
        var host = new BenchmarkHost(name);
        Logging.Host = host;
        Logging.MinSeverity = LogSeverity.Error;
        Logging.Clear();

        var world = new World(host);
        world.Initialize();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < entityCount; i++)
        {
            var builder = builderFactory(world, i);
            world.EnqueueCreateEntity(builder);
        }

        // Process the queue once (no systems registered)
        world.Tick(0.001);

        sw.Stop();

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        var throughput = entityCount / sw.Elapsed.TotalSeconds;
        var nsPerEntity = (elapsedMs * 1_000_000.0) / entityCount;

        Console.WriteLine($"[{name}] {entityCount:N0} entities in {elapsedMs:F2} ms  ({throughput / 1_000_000:F2} M/s, {nsPerEntity:F1} ns each)");
        Console.WriteLine($"         Archetypes: {world.ArchetypeCount}, Entities Alive: {world.EntityCount:N0}");

        return new BenchmarkResult(name, entityCount, elapsedMs, throughput);
    }

    private static EntityBuilder BuildSingleSignatureEntity(World world, int index)
    {
        float pos = index * 0.01f;
        return world.CreateEntityBuilder()
            .Add(new BenchPosition(pos, pos * 0.5f, pos * 0.25f))
            .Add(new BenchVelocity(0.1f, 0.2f, 0.3f))
            .Add(new BenchTag(index));
    }

    private static EntityBuilder BuildMixedSignatureEntity(World world, int index)
    {
        var builder = world.CreateEntityBuilder()
            .Add(new BenchPosition(index * 0.01f, 0, 0));

        switch (index % 4)
        {
            case 0:
                builder.Add(new BenchVelocity(0.5f, 0.1f, 0.0f))
                       .Add(new BenchPayloadA(index));
                break;
            case 1:
                builder.Add(new BenchVelocity(0.2f, -0.3f, 0.1f))
                       .Add(new BenchPayloadB(index * 2, -index));
                break;
            case 2:
                builder.Add(new BenchPayloadC { Scale = 1.0f + (index % 10) * 0.1f })
                       .Add(new BenchTag(index));
                break;
            default:
                builder.Add(new BenchVelocity(-0.2f, 0.0f, 0.4f))
                       .Add(new BenchPayloadD(index & 0xFF));
                break;
        }

        return builder;
    }

    private sealed class BenchmarkHost : IHost
    {
        public RuntimeContext Runtime { get; }
        public EnvironmentType Environment => EnvironmentType.Server;

        public BenchmarkHost(string name)
        {
            Runtime = new RuntimeContext(HostEnvironment.Capture(), $"Bench-{name}");
        }

        public object? GetRootHandle() => null;

        public void Log(LogEntry entry)
        {
            Console.WriteLine(entry.ToString());
        }
    }

    private readonly record struct BenchmarkResult(string Name, int Count, double TotalMs, double EntitiesPerSecond);

    private readonly record struct BenchPosition(float X, float Y, float Z);
    private readonly record struct BenchVelocity(float X, float Y, float Z);
    private readonly record struct BenchTag(int Id);
    private readonly record struct BenchPayloadA(int Seed);
    private readonly record struct BenchPayloadB(int A, int B);

    private struct BenchPayloadC
    {
        public float Scale;
    }

    private readonly record struct BenchPayloadD(int Value);
}
