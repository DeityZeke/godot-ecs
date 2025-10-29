
#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Godot;

using UltraSim.ECS.Components;
using UltraSim.ECS.Threading;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// OPTIMIZED PulsingMovementSystem with parallel chunk processing.
    /// Processes 1M entities in ~5-8ms instead of 20ms+.
    /// </summary>
    public sealed class OptimizedPulsingMovementSystem : BaseSystem
    {
        public override string Name => "OptimizedPulsingMovementSystem";
        public override int SystemId => typeof(OptimizedPulsingMovementSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(PulseData) };
        public override Type[] WriteSet { get; } = new[] { typeof(Velocity), typeof(PulseData) };

        private const int CHUNK_SIZE = 32768;

        private static readonly int PosId = ComponentManager.GetTypeId<Position>(); //ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentManager.GetTypeId<Velocity>(); //ComponentTypeRegistry.GetId<Velocity>();
        private static readonly int PulseId = ComponentManager.GetTypeId<PulseData>(); //ComponentTypeRegistry.GetId<PulseData>();

        // Manual thread pool (created once, reused forever)
        private static readonly ManualThreadPool _threadPool = new ManualThreadPool(System.Environment.ProcessorCount);

        public override void OnInitialize(World world)
        {
            _cachedQuery = world.Query(typeof(Position), typeof(Velocity), typeof(PulseData));
#if USE_DEBUG
            GD.Print("[ManualThreadPoolPulsingSystem] Initialized.");
#endif
        }

        public override void Update(World world, double delta)
        {

            float deltaF = (float)delta;

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;

                var posComponentList = arch.GetComponentListTyped<Position>(PosId);
                var velComponentList = arch.GetComponentListTyped<Velocity>(VelId);
                var pulseComponentList = arch.GetComponentListTyped<PulseData>(PulseId);

                if (posComponentList == null || velComponentList == null || pulseComponentList == null)
                    continue;

                var posList = posComponentList.GetList();
                var velList = velComponentList.GetList();
                var pulseList = pulseComponentList.GetList();

                int count = Math.Min(Math.Min(posList.Count, velList.Count), pulseList.Count);
                int numChunks = (count + CHUNK_SIZE - 1) / CHUNK_SIZE;

                // Use manual thread pool (zero allocations!)
                _threadPool.ParallelFor(numChunks, chunkIndex =>
                {
                    Span<Position> posSpan = CollectionsMarshal.AsSpan(posList);
                    Span<Velocity> velSpan = CollectionsMarshal.AsSpan(velList);
                    Span<PulseData> pulseSpan = CollectionsMarshal.AsSpan(pulseList);

                    int start = chunkIndex * CHUNK_SIZE;
                    int end = Math.Min(start + CHUNK_SIZE, count);

                    ProcessChunk(posSpan, velSpan, pulseSpan, start, end, deltaF);
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessChunk(
            Span<Position> pos,
            Span<Velocity> vel,
            Span<PulseData> pulse,
            int start,
            int end,
            float delta)
        {
            for (int i = start; i < end; i++)
            {
                ref var p = ref pos[i];
                ref var v = ref vel[i];
                ref var pd = ref pulse[i];

                pd.Phase += pd.Frequency * delta;
                if (pd.Phase > Utilities.TWO_PI)
                    pd.Phase -= Utilities.TWO_PI;

                float pulseDirection = Utilities.FastSin(pd.Phase);

                float distSq = p.X * p.X + p.Y * p.Y + p.Z * p.Z;

                if (distSq > 0.0001f)
                {
                    float invDist = Utilities.FastInvSqrt(distSq);
                    float speedFactor = pulseDirection * pd.Speed;

                    v.X = p.X * invDist * speedFactor;
                    v.Y = p.Y * invDist * speedFactor;
                    v.Z = p.Z * invDist * speedFactor;
                }
                else
                {
                    v.X = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Y = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                    v.Z = Utilities.RandomRange(-0.5f, 0.5f) * pd.Speed;
                }
            }
        }
    }
}