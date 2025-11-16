#nullable enable

using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using Godot;

using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.SIMD;
using UltraSim.ECS.Threading;
using UltraSim.Server;
using UltraSim.Server.ECS.Systems;
using Server.ECS.Chunk;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// OPTIMIZED MovementSystem with parallel chunk processing.
    /// Processes 1M entities in ~2-3ms instead of 8-12ms.
    /// </summary>
    [RequireSystem("UltraSim.Server.ECS.Systems.SimplifiedChunkSystem")]
    public sealed class OptimizedMovementSystem : BaseSystem
    {

        #region Settings

        public sealed class Settings : SystemSettings
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

        // INTERNAL CLASS SETTINGS, NOT SystemSettings GENERIC
        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;
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

        private SimplifiedChunkManager? _chunkManager;
        private static readonly ManualThreadPool _threadPool = new ManualThreadPool(System.Environment.ProcessorCount);
        private Entity[] _chunkCrossingBuffer = new Entity[1024]; // Reusable buffer for entities that crossed chunks

        public override void OnInitialize(World world)
        {
            var chunkSystem = world.Systems.GetSystem<SimplifiedChunkSystem>() as SimplifiedChunkSystem;
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

            // Query archetypes dynamically each frame to avoid stale cached queries
            using var archetypes = world.QueryArchetypes(typeof(Position), typeof(Velocity));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                // Skip static entities for movement (they don't move)
                // But we still need to check chunk crossings for ALL entities
                bool isStatic = arch.HasComponent(StaticRenderId);

                if (isStatic)
                    continue; // Static entities don't move, skip velocity application

                var posComponentList = arch.GetComponentListTyped<Position>(PosId);
                var velComponentList = arch.GetComponentListTyped<Velocity>(VelId);

                if (posComponentList == null || velComponentList == null)
                    continue;

                var posList = posComponentList.GetList();
                var velList = velComponentList.GetList();
                var entities = arch.GetEntitySpan();

                int count = Math.Min(posList.Count, velList.Count);
                int numChunks = (count + CHUNK_SIZE - 1) / CHUNK_SIZE;

                //Logging.Log($"[OptimizedMovementSystem] Processing archetype: arch.Count={arch.Count}, posList.Count={posList.Count}, velList.Count={velList.Count}, calculated count={count}");

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

                // Check for entities that crossed chunk boundaries and fire event
                // Only entities that actually moved between chunks are sent to SimplifiedChunkSystem
                if (_chunkManager != null && arch.HasComponent(ChunkOwnerId))
                {
                    var posSpan = CollectionsMarshal.AsSpan(posList);
                    var owners = arch.GetComponentSpan<ChunkOwner>(ChunkOwnerId);

                    // Ensure buffer is large enough
                    if (_chunkCrossingBuffer.Length < count)
                        _chunkCrossingBuffer = new Entity[count];

                    int crossingCount = 0;

                    for (int i = 0; i < count; i++)
                    {
                        if (!world.IsEntityValid(entities[i]))
                            continue;

                        ref readonly var owner = ref owners[i];
                        ref readonly var pos = ref posSpan[i];

                        // Calculate new chunk location based on current position
                        var newChunkLoc = _chunkManager.WorldToChunk(pos.X, pos.Y, pos.Z);

                        // Only enqueue entities that crossed chunk boundaries
                        if (!newChunkLoc.Equals(owner.Location))
                        {
                            _chunkCrossingBuffer[crossingCount++] = entities[i];
                        }
                    }

                    // Fire event only if entities actually crossed chunk boundaries
                    if (crossingCount > 0)
                    {
                        Server.EventSink.InvokeEnqueueChunkUpdate(
                            new Server.ChunkUpdateEventArgs(_chunkCrossingBuffer, 0, crossingCount));
                    }
                }
            }
        }
    }
}
