#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;

namespace ChunkBench;

internal static class Program
{
    public static void Main()
    {
        Console.WriteLine("============================================");
        Console.WriteLine(" Chunk & Entity Assignment Performance Tests");
        Console.WriteLine("============================================");

        RunChunkRegistrationBenchmark(chunkGridSize: 64);
        RunEntityAssignmentBenchmark(entityCount: 200_000, steps: 60);

        var windowDataset = ChunkWindowDataset.Create(
            gridX: 24,
            gridY: 8,
            gridZ: 24,
            entityCount: 100_000,
            seed: 2025);

        RunFullScanVsChunkWindowBenchmark(windowDataset);
        RunChunkWindowScalingBenchmark(windowDataset);
    }

    private static void RunChunkRegistrationBenchmark(int chunkGridSize)
    {
        var manager = new ChunkManager();
        var sw = Stopwatch.StartNew();

        uint nextEntityIndex = 1;
        for (int x = -chunkGridSize; x < chunkGridSize; x++)
        {
            for (int z = -chunkGridSize; z < chunkGridSize; z++)
            {
                var entity = new Entity(nextEntityIndex++, 1);
                manager.RegisterChunk(entity, new ChunkLocation(x, z, 0));
            }
        }

        sw.Stop();
        int totalChunks = (chunkGridSize * 2) * (chunkGridSize * 2);
        Console.WriteLine($"[ChunkBench] Registered {totalChunks:N0} chunks in {sw.Elapsed.TotalMilliseconds:F2} ms");
    }

    private static void RunEntityAssignmentBenchmark(int entityCount, int steps)
    {
        var manager = new ChunkManager();
        var random = new Random(1337);
        var owners = new ChunkOwner[entityCount];
        var positions = new Position[entityCount];
        var velocities = new Vector3[entityCount];
        var chunkCache = new Dictionary<ChunkLocation, Entity>();
        uint nextChunkEntityIndex = 1;

        for (int i = 0; i < entityCount; i++)
        {
            positions[i] = new Position
            {
                X = random.NextSingle() * 512f - 256f,
                Y = random.NextSingle() * 32f,
                Z = random.NextSingle() * 512f - 256f,
            };
            velocities[i] = new Vector3
            (
                random.NextSingle() - 0.5f,
                0,
                random.NextSingle() - 0.5f
            );
        }

        var sw = Stopwatch.StartNew();
        long totalAssignments = 0;

        for (int step = 0; step < steps; step++)
        {
            for (int i = 0; i < entityCount; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Z += velocities[i].Z;

                var chunkLoc = manager.WorldToChunk(positions[i].X, positions[i].Y, positions[i].Z);
                ref var owner = ref owners[i];
                if (!owner.IsAssigned || owner.Location != chunkLoc)
                {
                    var chunkEntity = EnsureChunk(manager, chunkCache, chunkLoc, ref nextChunkEntityIndex);
                    owner = new ChunkOwner(chunkEntity, chunkLoc);
                    totalAssignments++;
                }
            }
        }

        sw.Stop();
        double assignmentsPerSecond = totalAssignments / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"[ChunkBench] Entity assignment: {totalAssignments:N0} updates in {sw.Elapsed.TotalMilliseconds:F1} ms ({assignmentsPerSecond / 1_000_000:F2} M ops/sec)");
    }

    private static Entity EnsureChunk(ChunkManager manager, Dictionary<ChunkLocation, Entity> cache, ChunkLocation location, ref uint nextIndex)
    {
        if (cache.TryGetValue(location, out var existing))
            return existing;

        var entity = new Entity(nextIndex++, 1);
        manager.RegisterChunk(entity, location);
        cache[location] = entity;
        return entity;
    }

    private static void RunFullScanVsChunkWindowBenchmark(ChunkWindowDataset dataset)
    {
        var coreWindow = BuildWindowIndices(dataset, radius: 1);
        var nearWindow = BuildWindowIndices(dataset, radius: 3);

        var coreLookup = new HashSet<int>(coreWindow);
        var nearLookup = new HashSet<int>(nearWindow);

        // Full scan approach (iterate every entity)
        var sw = Stopwatch.StartNew();
        int coreFullCount = 0;
        int nearFullCount = 0;
        foreach (var chunkIndex in dataset.EntityChunks)
        {
            if (coreLookup.Contains(chunkIndex))
                coreFullCount++;
            if (nearLookup.Contains(chunkIndex))
                nearFullCount++;
        }
        sw.Stop();
        double fullScanMs = sw.Elapsed.TotalMilliseconds;

        // Chunk-window approach (iterate only chunks in the window)
        sw.Restart();
        int coreWindowCount = SumEntitiesInChunks(dataset, coreWindow);
        int nearWindowCount = SumEntitiesInChunks(dataset, nearWindow);
        sw.Stop();
        double windowScanMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine("[ChunkBench] Chunk window iteration benchmark");
        Console.WriteLine($"  Full scan:  core={coreFullCount:N0}, near={nearFullCount:N0}, time={fullScanMs:F2} ms");
        Console.WriteLine($"  Chunk window: core={coreWindowCount:N0}, near={nearWindowCount:N0}, time={windowScanMs:F2} ms");
    }

    private static void RunChunkWindowScalingBenchmark(ChunkWindowDataset dataset)
    {
        Console.WriteLine("[ChunkBench] Chunk window scaling benchmark");
        var radii = new[] { 1, 2, 3, 4 };

        foreach (var radius in radii)
        {
            var window = BuildWindowIndices(dataset, radius);
            var sw = Stopwatch.StartNew();
            int entityCount = SumEntitiesInChunks(dataset, window);
            sw.Stop();

            Console.WriteLine($"  Radius {radius,2} -> {window.Length,4} chunks, {entityCount,7:N0} entities, iteration {sw.Elapsed.TotalMilliseconds:F3} ms");
        }
    }

    private static int[] BuildWindowIndices(ChunkWindowDataset dataset, int radius)
    {
        int centerX = dataset.GridX / 2;
        int centerY = dataset.GridY / 2;
        int centerZ = dataset.GridZ / 2;

        var indices = new List<int>();

        for (int y = Math.Max(0, centerY - radius); y <= Math.Min(dataset.GridY - 1, centerY + radius); y++)
        {
            for (int z = Math.Max(0, centerZ - radius); z <= Math.Min(dataset.GridZ - 1, centerZ + radius); z++)
            {
                for (int x = Math.Max(0, centerX - radius); x <= Math.Min(dataset.GridX - 1, centerX + radius); x++)
                {
                    indices.Add(dataset.FlattenIndex(x, y, z));
                }
            }
        }

        return indices.ToArray();
    }

    private static int SumEntitiesInChunks(ChunkWindowDataset dataset, int[] chunkIndices)
    {
        int total = 0;
        foreach (var chunkIndex in chunkIndices)
        {
            total += dataset.ChunkToEntities[chunkIndex].Count;
        }
        return total;
    }

    private sealed class ChunkWindowDataset
    {
        public int GridX { get; }
        public int GridY { get; }
        public int GridZ { get; }
        public int[] EntityChunks { get; }
        public List<int>[] ChunkToEntities { get; }

        private ChunkWindowDataset(int gridX, int gridY, int gridZ, int entityCount)
        {
            GridX = gridX;
            GridY = gridY;
            GridZ = gridZ;
            EntityChunks = new int[entityCount];
            ChunkToEntities = new List<int>[gridX * gridY * gridZ];
            for (int i = 0; i < ChunkToEntities.Length; i++)
            {
                ChunkToEntities[i] = new List<int>(capacity: 64);
            }
        }

        public static ChunkWindowDataset Create(int gridX, int gridY, int gridZ, int entityCount, int seed)
        {
            var dataset = new ChunkWindowDataset(gridX, gridY, gridZ, entityCount);
            var random = new Random(seed);
            int chunkCount = dataset.ChunkToEntities.Length;

            for (int entity = 0; entity < entityCount; entity++)
            {
                int chunkIndex = random.Next(chunkCount);
                dataset.EntityChunks[entity] = chunkIndex;
                dataset.ChunkToEntities[chunkIndex].Add(entity);
            }

            return dataset;
        }

        public int FlattenIndex(int x, int y, int z)
        {
            return (y * GridZ + z) * GridX + x;
        }
    }
}
