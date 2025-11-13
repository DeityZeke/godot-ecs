#nullable enable

using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using Godot;

using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.SIMD;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Threading;
using UltraSim.Server;
using UltraSim.Server.ECS.Systems;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// OPTIMIZED MovementSystem with parallel chunk processing.
    /// Processes 1M entities in ~2-3ms instead of 8-12ms.
    /// </summary>
    public sealed class OptimizedMovementSystem : BaseSystem
    {

        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting GlobalSpeedMultiplier { get; private set; }
            public BoolSetting FreezeMovement { get; private set; }

            public Settings()
            {
                GlobalSpeedMultiplier = RegisterFloat("Global Speed Multiplier", 1.0f, 0.0f, 10.0f, 0.1f,
                    tooltip: "Multiplier applied to all movement (0 = frozen, 1 = normal, 2 = double speed)");

                FreezeMovement = RegisterBool("Freeze Movement", false,
                    tooltip: "Completely freeze all movement (useful for debugging)");
            }
        }

        // INTERNAL CLASS SETTINGS, NOT SETTINGSMANAGER GENERIC
        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;
        //public override ISetting? GetSettings() => SystemSettings;

        #endregion


        public override string Name => "OptimizedMovementSystem";
        public override int SystemId => typeof(OptimizedMovementSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(Velocity), typeof(StaticRenderTag) };
        public override Type[] WriteSet { get; } = new[] { typeof(Position) };
        private const int CHUNK_SIZE = 65536;

        private static readonly int PosId = ComponentManager.GetTypeId<Position>(); //ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentManager.GetTypeId<Velocity>(); //ComponentTypeRegistry.GetId<Velocity>();
        private static readonly int StaticRenderId = ComponentManager.GetTypeId<StaticRenderTag>();
        private static readonly int ChunkOwnerId = ComponentManager.GetTypeId<ChunkOwner>();

        private ChunkManager? _chunkManager;
        private static readonly ManualThreadPool _threadPool = new ManualThreadPool(System.Environment.ProcessorCount);

        public override void OnInitialize(World world)
        {
            _cachedQuery = world.QueryArchetypes(typeof(Position), typeof(Velocity));
            var chunkSystem = world.Systems.GetSystem<ChunkSystem>() as ChunkSystem;
            _chunkManager = chunkSystem?.GetChunkManager();

#if USE_DEBUG
            GD.Print("[ManualThreadPoolMovementSystem] Initialized.");
                        GD.Print($"[MovementSystem] Settings - Speed: {SystemSettings.GlobalSpeedMultiplier.Value}, " +
                     $"Frozen: {SystemSettings.FreezeMovement.Value}");
#endif
        }

        public override void Update(World world, double delta)
        {
            if (SystemSettings.FreezeMovement.Value)
                return;

            float deltaF = (float)delta;

            // Read speed multiplier
            float speedMultiplier = SystemSettings.GlobalSpeedMultiplier.Value;

            // If speed is zero or negative, don't process
            if (speedMultiplier <= 0.0f)
                return;

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;
                if (arch.HasComponent(StaticRenderId))
                    continue;

                var posComponentList = arch.GetComponentListTyped<Position>(PosId);
                var velComponentList = arch.GetComponentListTyped<Velocity>(VelId);

                if (posComponentList == null || velComponentList == null)
                    continue;

                var posList = posComponentList.GetList();
                var velList = velComponentList.GetList();
                var entities = arch.GetEntityArray();

                int count = Math.Min(posList.Count, velList.Count);
                int numChunks = (count + CHUNK_SIZE - 1) / CHUNK_SIZE;

                float adjustedDelta = (float)delta * speedMultiplier;

                // Use manual thread pool (zero allocations!)
                _threadPool.ParallelFor(numChunks, chunkIndex =>
                {
                    Span<Position> posSpan = CollectionsMarshal.AsSpan(posList);
                    Span<Velocity> velSpan = CollectionsMarshal.AsSpan(velList);

                    int start = chunkIndex * CHUNK_SIZE;
                    int end = Math.Min(start + CHUNK_SIZE, count);

                    var posSlice = posSpan.Slice(start, end - start);
                    var velSlice = velSpan.Slice(start, end - start);
                    SimdOperations.ApplyVelocity(posSlice, velSlice, adjustedDelta);
                });

                // OPTIMIZATION: Entities check their own boundaries and self-enqueue if crossed
                // This is MUCH faster than ChunkSystem doing archetype queries on all moved entities
                if (_chunkManager != null && arch.HasComponent(ChunkOwnerId))
                {
                    var posSpan = CollectionsMarshal.AsSpan(posList);
                    var owners = arch.GetComponentSpan<ChunkOwner>(ChunkOwnerId);

                    for (int i = 0; i < count; i++)
                    {
                        ref readonly var owner = ref owners[i];

                        // Only check entities that have been assigned to chunks
                        if (!owner.IsAssigned)
                            continue;

                        ref readonly var pos = ref posSpan[i];

                        // Check if entity crossed chunk boundary
                        var newChunkLoc = _chunkManager.WorldToChunk(pos.X, pos.Y, pos.Z);
                        if (!newChunkLoc.Equals(owner.Location))
                        {
                            // Entity crossed chunk boundary - enqueue for reassignment
                            ChunkAssignmentQueue.Enqueue(entities[i], newChunkLoc);
                        }
                    }
                }
            }
        }
    }
}
