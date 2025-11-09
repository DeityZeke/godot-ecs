#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using UltraSim.ECS;

namespace EntitySignatureBench;

internal static class Program
{
    private const int EntityCount = 5_000_000;
    private const int SignatureCount = 200_000;
    private const int RecycleIterations = 16;
    private const int ComponentsPerSignature = 12;
    private const int MaxComponentId = 1024;
    private const int CommandCount = 6_000_000;
    private const int ComponentOpCount = 4_000_000;
    private const int ToggleOps = 2_000_000;
    private const int ArchetypeShiftOps = 3_000_000;
    private const long AggregateOps =
        EntityCount
        + EntityCount * 2L
        + EntityCount * RecycleIterations
        + SignatureCount * (long)ComponentsPerSignature * 2L
        + CommandCount
        + ComponentOpCount * 2L
        + ToggleOps
        + ArchetypeShiftOps;

    private static readonly int[][] ComponentData = BuildComponentData(SignatureCount, ComponentsPerSignature, MaxComponentId);

    private static ComponentSignature[]? _bitpackedSignatures;
    private static LegacyComponentSignature[]? _legacySignatures;
    private static long _sink;

    static void Main()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine(" Entity & ComponentSignature Performance Tests");
        Console.WriteLine("==============================================");
        Console.WriteLine($"Entities: {EntityCount:N0}");
        Console.WriteLine($"Signatures: {SignatureCount:N0}");
        Console.WriteLine($"Components/Signature: {ComponentsPerSignature}");
        Console.WriteLine();

        var results = new List<BenchmarkResult>
        {
            Benchmark.Measure("Entity.Create", "Bitpacked", EntityCount, () => EntityBench.CreateBitpacked(EntityCount)),
            Benchmark.Measure("Entity.Create", "Legacy", EntityCount, () => EntityBench.CreateLegacy(EntityCount)),
            Benchmark.Measure("Entity.Dictionary", "Bitpacked", EntityCount * 2L, () => EntityBench.DictionaryBitpacked(EntityCount)),
            Benchmark.Measure("Entity.Dictionary", "Legacy", EntityCount * 2L, () => EntityBench.DictionaryLegacy(EntityCount)),
            Benchmark.Measure("Entity.Recycle", "Bitpacked", EntityCount * (long)RecycleIterations, () => EntityBench.RecycleBitpacked(EntityCount, RecycleIterations)),
            Benchmark.Measure("Entity.Recycle", "Legacy", EntityCount * (long)RecycleIterations, () => EntityBench.RecycleLegacy(EntityCount, RecycleIterations)),
            Benchmark.Measure("Signature.Build", "Bitpacked", SignatureCount * ComponentsPerSignature, () => _bitpackedSignatures = SignatureBench.BuildBitpacked(ComponentData)),
            Benchmark.Measure("Signature.Build", "Legacy", SignatureCount * ComponentsPerSignature, () => _legacySignatures = SignatureBench.BuildLegacy(ComponentData)),
            Benchmark.Measure("Signature.Contains", "Bitpacked", SignatureCount * ComponentsPerSignature * 2L, () => SignatureBench.ContainsBitpacked(_bitpackedSignatures ?? throw new InvalidOperationException("Bitpacked signatures not built"), ComponentData)),
            Benchmark.Measure("Signature.Contains", "Legacy", SignatureCount * ComponentsPerSignature * 2L, () => SignatureBench.ContainsLegacy(_legacySignatures ?? throw new InvalidOperationException("Legacy signatures not built"), ComponentData)),
            Benchmark.Measure("CmdBuffer.Commands", "Legacy", CommandCount, () => CommandBench.LegacyScenario(CommandCount)),
            Benchmark.Measure("CmdBuffer.Commands", "Packed", CommandCount, () => CommandBench.PackedScenario(CommandCount)),
            Benchmark.Measure("CompQueue.Enqueue+Drain", "Legacy", ComponentOpCount * 2L, () => ComponentQueueBench.LegacyScenario(ComponentOpCount)),
            Benchmark.Measure("CompQueue.Enqueue+Drain", "Packed", ComponentOpCount * 2L, () => ComponentQueueBench.PackedScenario(ComponentOpCount)),
            Benchmark.Measure("System.Toggle", "Legacy", ToggleOps, () => SystemToggleBench.LegacyScenario(ToggleOps)),
            Benchmark.Measure("System.Toggle", "Packed", ToggleOps, () => SystemToggleBench.PackedScenario(ToggleOps)),
            Benchmark.Measure("Archetype.Shift", "Legacy", ArchetypeShiftOps, () => ArchetypeShiftBench.LegacyScenario(ArchetypeShiftOps)),
            Benchmark.Measure("Archetype.Shift", "Packed", ArchetypeShiftOps, () => ArchetypeShiftBench.PackedScenario(ArchetypeShiftOps)),
            Benchmark.Measure("Aggregate.All", "Legacy", AggregateOps, () => AggregateBench.Run(AggregateBench.Variant.Legacy)),
            Benchmark.Measure("Aggregate.All", "Optimized", AggregateOps, () => AggregateBench.Run(AggregateBench.Variant.Optimized)),
        };

        PrintResults(results);
    }
    private static class SystemToggleBench
    {
        private const int SystemCount = 512;

        public static void LegacyScenario(int operations)
        {
            var enabled = new HashSet<int>();
            long checksum = 0;

            for (int i = 0; i < operations; i++)
            {
                int systemId = i % SystemCount;
                if (!enabled.Add(systemId))
                    enabled.Remove(systemId);
                checksum += enabled.Count;
            }

            _sink += checksum;
        }

        public static void PackedScenario(int operations)
        {
            var bits = new ulong[(SystemCount + 63) / 64];
            long checksum = 0;
            int enabledCount = 0;

            for (int i = 0; i < operations; i++)
            {
                int systemId = i % SystemCount;
                int word = systemId >> 6;
                int bit = systemId & 63;
                ulong mask = 1UL << bit;
                bool wasSet = (bits[word] & mask) != 0;
                if (wasSet)
                {
                    bits[word] &= ~mask;
                    enabledCount--;
                }
                else
                {
                    bits[word] |= mask;
                    enabledCount++;
                }

                checksum += enabledCount;
            }

            _sink += checksum;
        }
    }

    private static class ArchetypeShiftBench
    {
        public static void LegacyScenario(int iterations)
        {
            var sig = new LegacyComponentSignature();
            long checksum = 0;

            for (int i = 0; i < iterations; i++)
            {
                int comp = i % MaxComponentId;
                if (i % 2 == 0)
                    sig = sig.Add(comp);
                else
                    sig = sig.Remove(comp);

                checksum += sig.Count;
            }

            _sink += checksum;
        }

        public static void PackedScenario(int iterations)
        {
            var sig = new ComponentSignature();
            long checksum = 0;

            for (int i = 0; i < iterations; i++)
            {
                int comp = i % MaxComponentId;
                if (i % 2 == 0)
                    sig = sig.Add(comp);
                else
                    sig = sig.Remove(comp);

                checksum += sig.Count;
            }

            _sink += checksum;
        }
    }

    private static class AggregateBench
    {
        public enum Variant { Legacy, Optimized }

        public static void Run(Variant variant)
        {
            if (variant == Variant.Legacy)
            {
                EntityBench.CreateLegacy(EntityCount);
                EntityBench.DictionaryLegacy(EntityCount);
                EntityBench.RecycleLegacy(EntityCount, RecycleIterations);
                _legacySignatures = SignatureBench.BuildLegacy(ComponentData);
                SignatureBench.ContainsLegacy(_legacySignatures, ComponentData);
                CommandBench.LegacyScenario(CommandCount);
                ComponentQueueBench.LegacyScenario(ComponentOpCount);
                SystemToggleBench.LegacyScenario(ToggleOps);
                ArchetypeShiftBench.LegacyScenario(ArchetypeShiftOps);
            }
            else
            {
                EntityBench.CreateBitpacked(EntityCount);
                EntityBench.DictionaryBitpacked(EntityCount);
                EntityBench.RecycleBitpacked(EntityCount, RecycleIterations);
                _bitpackedSignatures = SignatureBench.BuildBitpacked(ComponentData);
                SignatureBench.ContainsBitpacked(_bitpackedSignatures, ComponentData);
                CommandBench.PackedScenario(CommandCount);
                ComponentQueueBench.PackedScenario(ComponentOpCount);
                SystemToggleBench.PackedScenario(ToggleOps);
                ArchetypeShiftBench.PackedScenario(ArchetypeShiftOps);
            }
        }
    }

    private static class CommandBench
    {
        private const int EntityModulo = 1_000_000;
        private const int ComponentModulo = 4096;

        public static void LegacyScenario(int count)
        {
            var commands = new List<LegacyCommand>(count);

            for (int i = 0; i < count; i++)
            {
                var kind = (CommandKind)(i % 3);
                uint entityIndex = (uint)(i % EntityModulo);
                int componentId = i % ComponentModulo;

                switch (kind)
                {
                    case CommandKind.Add:
                        commands.Add(new LegacyCommand
                        {
                            Type = CommandKind.Add,
                            EntityIndex = entityIndex,
                            ComponentTypeId = componentId,
                            BoxedValue = i
                        });
                        break;
                    case CommandKind.Remove:
                        commands.Add(new LegacyCommand
                        {
                            Type = CommandKind.Remove,
                            EntityIndex = entityIndex,
                            ComponentTypeId = componentId
                        });
                        break;
                    default:
                        commands.Add(new LegacyCommand
                        {
                            Type = CommandKind.Destroy,
                            EntityIndex = entityIndex
                        });
                        break;
                }
            }

            long checksum = 0;
            foreach (var cmd in commands)
            {
                switch (cmd.Type)
                {
                    case CommandKind.Add:
                        checksum += cmd.ComponentTypeId + cmd.EntityIndex + (int)(cmd.BoxedValue ?? 0);
                        break;
                    case CommandKind.Remove:
                        checksum += cmd.ComponentTypeId + cmd.EntityIndex;
                        break;
                    case CommandKind.Destroy:
                        checksum += cmd.EntityIndex;
                        break;
                }
            }

            _sink += checksum;
        }

        public static void PackedScenario(int count)
        {
            var commands = new List<PackedCommand>(count);

            for (int i = 0; i < count; i++)
            {
                var kind = (CommandKind)(i % 3);
                uint entityIndex = (uint)(i % EntityModulo);
                int componentId = i % ComponentModulo;

                switch (kind)
                {
                    case CommandKind.Add:
                        commands.Add(PackedCommand.Create(CommandKind.Add, entityIndex, componentId, i));
                        break;
                    case CommandKind.Remove:
                        commands.Add(PackedCommand.Create(CommandKind.Remove, entityIndex, componentId, null));
                        break;
                    default:
                        commands.Add(PackedCommand.Create(CommandKind.Destroy, entityIndex, 0, null));
                        break;
                }
            }

            long checksum = 0;
            foreach (var cmd in commands)
            {
                switch (cmd.Type)
                {
                    case CommandKind.Add:
                        checksum += cmd.ComponentTypeId + cmd.EntityIndex + (int)(cmd.BoxedValue ?? 0);
                        break;
                    case CommandKind.Remove:
                        checksum += cmd.ComponentTypeId + cmd.EntityIndex;
                        break;
                    case CommandKind.Destroy:
                        checksum += cmd.EntityIndex;
                        break;
                }
            }

            _sink += checksum;
        }

        private enum CommandKind : byte { Add = 0, Remove = 1, Destroy = 2 }

        private struct LegacyCommand
        {
            public CommandKind Type;
            public uint EntityIndex;
            public int ComponentTypeId;
            public object? BoxedValue;
        }

        private readonly struct PackedCommand
        {
            private const int EntityBits = 32;
            private const int ComponentBits = 24;
            private const int TypeBits = 8;
            private const int ComponentShift = EntityBits;
            private const int TypeShift = ComponentShift + ComponentBits;
            private const ulong EntityMask = (1UL << EntityBits) - 1;
            private const ulong ComponentMask = (1UL << ComponentBits) - 1;

            private readonly ulong _header;
            public readonly object? BoxedValue;

            private PackedCommand(ulong header, object? boxedValue)
            {
                _header = header;
                BoxedValue = boxedValue;
            }

            public CommandKind Type => (CommandKind)((_header >> TypeShift) & ((1UL << TypeBits) - 1));
            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)((_header >> ComponentShift) & ComponentMask);

            public static PackedCommand Create(CommandKind type, uint entityIndex, int componentTypeId, object? boxedValue)
            {
                ulong header = ((ulong)type << TypeShift) | (((ulong)componentTypeId & ComponentMask) << ComponentShift) | entityIndex;
                return new PackedCommand(header, boxedValue);
            }
        }
    }

    private static class ComponentQueueBench
    {
        private const int EntityModulo = 1_000_000;
        private const int ComponentModulo = 16_384;

        public static void LegacyScenario(int count)
        {
            var addQueue = new ConcurrentQueue<LegacyAddOp>();
            var removeQueue = new ConcurrentQueue<LegacyRemoveOp>();

            for (int i = 0; i < count; i++)
            {
                uint entityIndex = (uint)(i % EntityModulo);
                int componentId = i % ComponentModulo;
                addQueue.Enqueue(new LegacyAddOp(entityIndex, componentId, i));
                removeQueue.Enqueue(new LegacyRemoveOp(entityIndex, componentId));
            }

            long checksum = 0;
            while (addQueue.TryDequeue(out var op))
                checksum += op.ComponentTypeId + op.EntityIndex + (int)op.Value;

            while (removeQueue.TryDequeue(out var op))
                checksum += op.ComponentTypeId + op.EntityIndex;

            _sink += checksum;
        }

        public static void PackedScenario(int count)
        {
            var addQueue = new ConcurrentQueue<PackedAddOp>();
            var removeQueue = new ConcurrentQueue<PackedRemoveOp>();

            for (int i = 0; i < count; i++)
            {
                uint entityIndex = (uint)(i % EntityModulo);
                int componentId = i % ComponentModulo;
                addQueue.Enqueue(PackedAddOp.Create(entityIndex, componentId, i));
                removeQueue.Enqueue(PackedRemoveOp.Create(entityIndex, componentId));
            }

            long checksum = 0;
            while (addQueue.TryDequeue(out var op))
                checksum += op.ComponentTypeId + op.EntityIndex + (int)op.BoxedValue;

            while (removeQueue.TryDequeue(out var op))
                checksum += op.ComponentTypeId + op.EntityIndex;

            _sink += checksum;
        }

        private readonly struct LegacyAddOp
        {
            public readonly uint EntityIndex;
            public readonly int ComponentTypeId;
            public readonly object Value;

            public LegacyAddOp(uint entityIndex, int componentTypeId, object value)
            {
                EntityIndex = entityIndex;
                ComponentTypeId = componentTypeId;
                Value = value;
            }
        }

        private readonly struct LegacyRemoveOp
        {
            public readonly uint EntityIndex;
            public readonly int ComponentTypeId;

            public LegacyRemoveOp(uint entityIndex, int componentTypeId)
            {
                EntityIndex = entityIndex;
                ComponentTypeId = componentTypeId;
            }
        }

        private readonly struct PackedAddOp
        {
            private const int EntityBits = 32;
            private const int ComponentBits = 32;
            private const int ComponentShift = EntityBits;
            private const ulong EntityMask = (1UL << EntityBits) - 1;

            private readonly ulong _header;
            public readonly object BoxedValue;

            private PackedAddOp(ulong header, object boxedValue)
            {
                _header = header;
                BoxedValue = boxedValue;
            }

            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)(_header >> ComponentShift);

            public static PackedAddOp Create(uint entityIndex, int componentTypeId, object boxedValue) =>
                new(((ulong)componentTypeId << ComponentShift) | entityIndex, boxedValue);
        }

        private readonly struct PackedRemoveOp
        {
            private const int EntityBits = 32;
            private const int ComponentBits = 32;
            private const int ComponentShift = EntityBits;
            private const ulong EntityMask = (1UL << EntityBits) - 1;

            private readonly ulong _header;

            private PackedRemoveOp(ulong header) => _header = header;

            public uint EntityIndex => (uint)(_header & EntityMask);
            public int ComponentTypeId => (int)(_header >> ComponentShift);

            public static PackedRemoveOp Create(uint entityIndex, int componentTypeId) =>
                new(((ulong)componentTypeId << ComponentShift) | entityIndex);
        }
    }

    private static void PrintResults(IEnumerable<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Scenario",-22} {"Variant",-10} {"Ops",-15} {"Time (ms)",-12} {"Ops/s",-15} {"Alloc (MB)",-12} {"Gen0/1/2",-10}");
        Console.WriteLine(new string('-', 96));

        foreach (var result in results)
        {
            var opsPerSecond = result.Operations > 0 && result.Seconds > 0
                ? result.Operations / result.Seconds
                : double.NaN;

            var allocMb = result.AllocBytes / 1024d / 1024d;
            Console.WriteLine($"{result.Scenario,-22} {result.Variant,-10} {result.Operations,15:N0} {result.Milliseconds,12:F2} {opsPerSecond,15:N2} {allocMb,12:F2} {result.Gen0}/{result.Gen1}/{result.Gen2}");
        }

        Console.WriteLine();
        Console.WriteLine("Sink Value (prevents dead-code elimination): " + _sink);
    }

    private static int[][] BuildComponentData(int signatureCount, int componentsPerSignature, int maxComponentId)
    {
        var rng = new Random(1337);
        var data = new int[signatureCount][];
        for (int i = 0; i < signatureCount; i++)
        {
            var ids = new HashSet<int>();
            while (ids.Count < componentsPerSignature)
            {
                ids.Add(rng.Next(0, maxComponentId));
            }
            data[i] = ids.ToArray();
        }
        return data;
    }

    private static class EntityBench
    {
        public static void CreateBitpacked(int count)
        {
            var items = new Entity[count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new Entity((uint)(i + 1), 1);
            }

            _sink += items[^1].Index;
        }

        public static void CreateLegacy(int count)
        {
            var items = new LegacyEntity[count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new LegacyEntity((uint)(i + 1), 1);
            }

            _sink += items[^1].Index;
        }

        public static void DictionaryBitpacked(int count)
        {
            var dict = new Dictionary<Entity, int>(count);
            for (int i = 0; i < count; i++)
            {
                var entity = new Entity((uint)(i + 1), 1);
                dict[entity] = i;
            }

            long sum = 0;
            for (int i = 0; i < count; i++)
            {
                var entity = new Entity((uint)(i + 1), 1);
                sum += dict[entity];
            }

            _sink += sum;
        }

        public static void DictionaryLegacy(int count)
        {
            var dict = new Dictionary<LegacyEntity, int>(count);
            for (int i = 0; i < count; i++)
            {
                var entity = new LegacyEntity((uint)(i + 1), 1);
                dict[entity] = i;
            }

            long sum = 0;
            for (int i = 0; i < count; i++)
            {
                var entity = new LegacyEntity((uint)(i + 1), 1);
                sum += dict[entity];
            }

            _sink += sum;
        }

        public static void RecycleBitpacked(int count, int iterations)
        {
            var versions = new uint[count + 1];
            var free = new Stack<uint>(count);

            for (uint i = 1; i <= count; i++)
            {
                versions[i] = 1;
                free.Push(i);
            }

            long checksum = 0;
            for (int n = 0; n < iterations; n++)
            {
                for (int i = 0; i < count; i++)
                {
                    var idx = free.Pop();
                    versions[idx]++;
                    var entity = new Entity(idx, versions[idx]);
                    checksum += (long)(entity.Packed & 0xFFFFFFFF);
                    free.Push(idx);
                }
            }

            _sink += checksum;
        }

        public static void RecycleLegacy(int count, int iterations)
        {
            var versions = new uint[count + 1];
            var free = new Stack<uint>(count);

            for (uint i = 1; i <= count; i++)
            {
                versions[i] = 1;
                free.Push(i);
            }

            long checksum = 0;
            for (int n = 0; n < iterations; n++)
            {
                for (int i = 0; i < count; i++)
                {
                    var idx = free.Pop();
                    versions[idx]++;
                    var entity = new LegacyEntity(idx, versions[idx]);
                    checksum += entity.Index;
                    free.Push(idx);
                }
            }

            _sink += checksum;
        }
    }

    private static class SignatureBench
    {
        public static ComponentSignature[] BuildBitpacked(int[][] data)
        {
            var signatures = new ComponentSignature[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                var sig = new ComponentSignature();
                var ids = data[i];
                for (int j = 0; j < ids.Length; j++)
                {
                    sig = sig.Add(ids[j]);
                }

                signatures[i] = sig;
            }

            _sink += signatures.Length;
            return signatures;
        }

        public static LegacyComponentSignature[] BuildLegacy(int[][] data)
        {
            var signatures = new LegacyComponentSignature[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                var sig = new LegacyComponentSignature();
                var ids = data[i];
                for (int j = 0; j < ids.Length; j++)
                {
                    sig = sig.Add(ids[j]);
                }

                signatures[i] = sig;
            }

            _sink += signatures.Length;
            return signatures;
        }

        public static void ContainsBitpacked(ComponentSignature[] signatures, int[][] data)
        {
            long hits = 0;
            for (int i = 0; i < signatures.Length; i++)
            {
                var sig = signatures[i];
                var ids = data[i];

                for (int j = 0; j < ids.Length; j++)
                {
                    if (sig.Contains(ids[j]))
                        hits++;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    if (!sig.Contains(ids[j] + MaxComponentId))
                        hits++;
                }
            }

            _sink += hits;
        }

        public static void ContainsLegacy(LegacyComponentSignature[] signatures, int[][] data)
        {
            long hits = 0;
            for (int i = 0; i < signatures.Length; i++)
            {
                var sig = signatures[i];
                var ids = data[i];

                for (int j = 0; j < ids.Length; j++)
                {
                    if (sig.Contains(ids[j]))
                        hits++;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    if (!sig.Contains(ids[j] + MaxComponentId))
                        hits++;
                }
            }

            _sink += hits;
        }
    }
}

internal static class Benchmark
{
    public static BenchmarkResult Measure(string scenario, string variant, long operations, Action action)
    {
        ForceGc();

        long beforeMem = GC.GetTotalMemory(true);
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();

        long afterMem = GC.GetTotalMemory(true);
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        return new BenchmarkResult(
            scenario,
            variant,
            operations,
            sw.Elapsed.TotalMilliseconds,
            sw.Elapsed.TotalSeconds,
            afterMem - beforeMem,
            gen0After - gen0Before,
            gen1After - gen1Before,
            gen2After - gen2Before);
    }

    private static void ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}

internal sealed record BenchmarkResult(
    string Scenario,
    string Variant,
    long Operations,
    double Milliseconds,
    double Seconds,
    long AllocBytes,
    int Gen0,
    int Gen1,
    int Gen2);

internal readonly struct LegacyEntity : IEquatable<LegacyEntity>
{
    public readonly uint Index;
    public readonly uint Version;

    public static readonly LegacyEntity Invalid = new(0, 0);

    public LegacyEntity(uint index, uint version)
    {
        Index = index;
        Version = version;
    }

    public bool IsValid => Index > 0 && Version > 0;

    public bool Equals(LegacyEntity other) => Index == other.Index && Version == other.Version;

    public override bool Equals(object? obj) => obj is LegacyEntity entity && Equals(entity);

    public override int GetHashCode() => HashCode.Combine(Index, Version);

    public override string ToString() => $"LegacyEntity({Index},v{Version})";

    public static bool operator ==(LegacyEntity left, LegacyEntity right) => left.Equals(right);
    public static bool operator !=(LegacyEntity left, LegacyEntity right) => !left.Equals(right);
}

internal sealed class LegacyComponentSignature
{
    private readonly HashSet<int> _ids = new();

    public int Count => _ids.Count;

    public LegacyComponentSignature() { }

    private LegacyComponentSignature(HashSet<int> ids)
    {
        _ids = new HashSet<int>(ids);
    }

    public LegacyComponentSignature Add(int id)
    {
        var clone = new HashSet<int>(_ids) { id };
        return new LegacyComponentSignature(clone);
    }

    public LegacyComponentSignature Remove(int id)
    {
        var clone = new HashSet<int>(_ids);
        clone.Remove(id);
        return new LegacyComponentSignature(clone);
    }

    public bool Contains(int id) => _ids.Contains(id);

    public bool Equals(LegacyComponentSignature other) => _ids.SetEquals(other._ids);

    public override bool Equals(object? obj) => obj is LegacyComponentSignature sig && Equals(sig);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var id in _ids.OrderBy(x => x))
            hash = hash * 31 + id;
        return hash;
    }

    public override string ToString() => $"LegacySignature[{string.Join(",", _ids.OrderBy(x => x))}]";
}
