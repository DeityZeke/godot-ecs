#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using UltraSim.Server.ECS.Systems;
using Client.ECS;
using Client.ECS.Components;
using Client.ECS.Collections;

namespace Client.ECS.Systems
{
    /// <summary>
    /// DynamicEntityRenderSystem - SINGLE RESPONSIBILITY: Render DYNAMIC entities with MeshInstance3D.
    ///
    /// Rendering strategy split:
    /// - DynamicEntityRenderSystem: DYNAMIC entities → Individual MeshInstance3D (this system)
    /// - StaticEntityRenderSystem: STATIC entities → MultiMesh batches
    /// - BillboardEntityRenderSystem: BILLBOARD entities → Impostors/sprites (far distance)
    ///
    /// This system renders ONLY dynamic entities in the Near zone using individual MeshInstance3D:
    /// - Near zone dynamics (NearZoneTag + !StaticRenderTag): MeshInstance3D (full interactivity)
    /// - Ignores static entities (handled by StaticEntityRenderSystem with MultiMesh)
    ///
    /// Process:
    /// 1. Queries chunks with NearZoneTag component (archetype-based filtering)
    /// 2. Gets entities in each chunk from ChunkSystem
    /// 3. Filters to ONLY dynamic entities (does NOT have StaticRenderTag)
    /// 4. Builds MeshInstance3D for each dynamic entity (full interactivity)
    /// 5. Respects RenderChunk.Visible flag (set by RenderVisibilitySystem)
    /// 6. Parallelizes per-chunk processing
    ///
    /// ECS PATTERN: Queries by NearZoneTag, filters by !StaticRenderTag.
    /// - Only iterates chunks in Near zone archetype (automatic filtering)
    /// - Static entities handled separately by StaticEntityRenderSystem
    /// - Can run in parallel with StaticEntityRenderSystem (different entity sets)
    ///
    /// DOES NOT:
    /// - Render static entities (StaticEntityRenderSystem handles those with MultiMesh)
    /// - Assign zones (that's RenderChunkManager's job)
    /// - Do frustum culling (that's RenderVisibilitySystem's job)
    /// - Operate on Mid or Far zones (Near zone only for dynamics)
    ///
    /// DEPENDENCIES: Requires RenderChunkManager to tag chunks before building visuals.
    /// </summary>
    [RequireSystem("Client.ECS.Systems.RenderChunkManager")]
    public sealed class DynamicEntityRenderSystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting EntityRadius { get; private set; }
            public IntSetting RadialSegments { get; private set; }
            public IntSetting Rings { get; private set; }
            public IntSetting InitialChunkPoolCapacity { get; private set; }
            public BoolSetting CastShadows { get; private set; }
            public IntSetting MaxMeshInstances { get; private set; }
            public IntSetting ParallelThreshold { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                EntityRadius = RegisterFloat("Entity Radius", 0.5f,
                    min: 0.1f, max: 5.0f, step: 0.1f,
                    tooltip: "Radius of sphere mesh for each entity");

                RadialSegments = RegisterInt("Radial Segments", 6,
                    min: 3, max: 32, step: 1,
                    tooltip: "Sphere mesh radial segments");

                Rings = RegisterInt("Rings", 3,
                    min: 2, max: 32, step: 1,
                    tooltip: "Sphere mesh rings");

                InitialChunkPoolCapacity = RegisterInt("Initial Chunk Pool Capacity", 256,
                    min: 32, max: 4096, step: 32,
                    tooltip: "Initial reserve per chunk pool");

                CastShadows = RegisterBool("Cast Shadows", false,
                    tooltip: "Enable shadow casting for Near zone entities");

                MaxMeshInstances = RegisterInt("Max MeshInstances", 25000,
                    min: 1000, max: 200000, step: 1000,
                    tooltip: "Hard cap to prevent freezing");

                ParallelThreshold = RegisterInt("Parallel Threshold", 8,
                    min: 1, max: 128, step: 1,
                    tooltip: "Minimum Near chunks before parallel processing kicks in");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log visual creation/destruction");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Dynamic Entity Render System";
        public override int SystemId => typeof(DynamicEntityRenderSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: Position, Near zone tag, render metadata
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(NearZoneTag), typeof(RenderChunk), typeof(ChunkOwner), typeof(StaticRenderTag), typeof(RenderPrototype) };
        // Write: None (we modify Godot scene graph, not ECS components)
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _nearZoneContainer;
        private SphereMesh? _sphereMesh;
        private BoxMesh? _cubeMesh;
        private StandardMaterial3D? _nearMaterial;
        private ChunkManager? _chunkManager;
        private ChunkSystem? _chunkSystem;

        private EntityVisualBinding[] _entityBindings = Array.Empty<EntityVisualBinding>();
        private readonly DynamicBitSet _bindingMask = new();
        private readonly object _bindingsLock = new(); // Thread-safety for array resize

        private sealed class ChunkPoolEntry
        {
            public ChunkVisualPool<MeshInstance3D> Pool = null!;
            public Node3D Container = null!;
        }

        private readonly ConcurrentDictionary<ChunkLocation, ChunkPoolEntry> _chunkPools = new();
        private readonly ConcurrentQueue<ChunkPoolEntry> _pooledChunkEntries = new();
        private readonly DynamicBitSet _activeEntitiesThisFrame = new();
        private readonly DynamicBitSet _activeEntitiesLastFrame = new();
        private readonly List<int> _entitiesToRelease = new();

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();
        private static readonly int StaticRenderTypeId = ComponentManager.GetTypeId<StaticRenderTag>();
        private static readonly int RenderPrototypeTypeId = ComponentManager.GetTypeId<RenderPrototype>();
        private static readonly Vector3 HiddenPosition = new(0, -10000, 0);

        private int _totalAllocatedMeshInstances = 0;
        private int _activeBindingCount = 0;
        private int _frameCounter = 0;

        public override void OnInitialize(World world)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            _rootNode = tree?.CurrentScene as Node3D;

            if (_rootNode == null)
            {
                Logging.Log($"[{Name}] ERROR: No scene root found!", LogSeverity.Error);
                return;
            }

            _nearZoneContainer = new Node3D { Name = "ECS_DynamicEntities" };
            _rootNode.CallDeferred("add_child", _nearZoneContainer);

            _sphereMesh = new SphereMesh
            {
                RadialSegments = SystemSettings.RadialSegments.Value,
                Rings = SystemSettings.Rings.Value,
                Radius = SystemSettings.EntityRadius.Value,
                Height = SystemSettings.EntityRadius.Value * 2.0f
            };

            _nearMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.35f, 0.65f, 1.0f),
                Metallic = 0.0f,
                Roughness = 0.7f
            };
            // NOTE: Do NOT set .Material on mesh resources - procedural meshes haven't generated surfaces yet
            // Material is applied via MaterialOverride on MeshInstance3D nodes (see CreateMeshInstance)

            _cubeMesh = new BoxMesh { Size = Vector3.One * SystemSettings.EntityRadius.Value * 2.0f };
            // NOTE: Do NOT set .Material on mesh resources - procedural meshes haven't generated surfaces yet
            // Material is applied via MaterialOverride on MeshInstance3D nodes (see CreateMeshInstance)

            Logging.Log($"[{Name}] Initialized - Max instances: {SystemSettings.MaxMeshInstances.Value}");
        }

        public void SetChunkManager(ChunkManager chunkManager, ChunkSystem chunkSystem)
        {
            _chunkManager = chunkManager;
            _chunkSystem = chunkSystem;
            Logging.Log($"[{Name}] ChunkManager + ChunkSystem references set");
        }

        public override void Update(World world, double delta)
        {
            if (_nearZoneContainer == null || _sphereMesh == null || _chunkSystem == null || _chunkManager == null)
                return;

            _frameCounter++;
            _activeEntitiesThisFrame.ClearAll();

            // Query all chunks with Near zone assignment
            var nearChunks = GetNearChunks(world);

            if (nearChunks.Count == 0)
            {
                ReleaseAllVisuals();
                return;
            }

            // Parallelize per-chunk processing if we have enough chunks
            if (nearChunks.Count >= SystemSettings.ParallelThreshold.Value)
            {
                ProcessChunksParallel(world, nearChunks);
            }
            else
            {
                ProcessChunksSequential(world, nearChunks);
            }

            // PHASE 3: Clear dirty flags after processing chunks
            foreach (var chunkInfo in nearChunks)
            {
                var chunkEntity = _chunkManager!.GetChunk(chunkInfo.Location);
                if (chunkEntity != Entity.Invalid)
                {
                    _chunkSystem!.ClearChunkDirty(chunkEntity);
                }
            }

            // Release entities that are no longer in Near zone
            ReleaseInactiveVisuals();

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                int totalNearChunks = _chunkPools.Count;
                int processedThisFrame = nearChunks.Count;
                float skipPercentage = totalNearChunks > 0 ? (1.0f - (float)processedThisFrame / totalNearChunks) * 100.0f : 0;

                Logging.Log($"[{Name}] Active: {_activeBindingCount}/{_totalAllocatedMeshInstances} instances, Pools: {totalNearChunks} | Processed: {processedThisFrame} ({skipPercentage:F1}% skipped via dirty tracking)");
            }
        }

        // Track previous visibility per chunk for change detection
        private readonly Dictionary<ChunkLocation, bool> _previousVisibility = new();

        private List<(ChunkLocation Location, bool Visible)> GetNearChunks(World world)
        {
            var nearChunks = new List<(ChunkLocation, bool)>();

            // Query by NearZoneTag - automatic archetype filtering!
            // Only gets chunks in Near zone (no manual enum check needed)
            var archetypes = world.QueryArchetypes(typeof(NearZoneTag));

            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var renderChunks = archetype.GetComponentSpan<RenderChunk>(RenderChunkTypeId);

                for (int i = 0; i < renderChunks.Length; i++)
                {
                    ref var renderChunk = ref renderChunks[i];
                    var serverChunkLoc = renderChunk.ServerChunkLocation;
                    var visible = renderChunk.Visible;

                    // PHASE 3 OPTIMIZATION: Only process chunks if:
                    // 1. Server chunk is dirty (entity list changed), OR
                    // 2. Visibility changed (frustum culling update)
                    bool isDirty = _chunkSystem!.IsChunkDirty(world, _chunkManager!.GetChunk(serverChunkLoc));
                    bool visibilityChanged = !_previousVisibility.TryGetValue(serverChunkLoc, out var prevVis) || prevVis != visible;

                    if (isDirty || visibilityChanged)
                    {
                        // Use ServerChunkLocation (absolute world position) to identify which server chunk entities to render
                        nearChunks.Add((serverChunkLoc, visible));
                        _previousVisibility[serverChunkLoc] = visible;
                    }
                }
            }

            return nearChunks;
        }

        private void ProcessChunksSequential(World world, List<(ChunkLocation Location, bool Visible)> nearChunks)
        {
            // Use AsSpan for read-only iteration (better performance)
            var chunksSpan = CollectionsMarshal.AsSpan(nearChunks);
            for (int i = 0; i < chunksSpan.Length; i++)
            {
                ProcessChunk(world, chunksSpan[i].Location, chunksSpan[i].Visible);
            }
        }

        private void ProcessChunksParallel(World world, List<(ChunkLocation Location, bool Visible)> nearChunks)
        {
            // Parallel.ForEach over chunks - each chunk is independent
            Parallel.ForEach(nearChunks, chunkInfo =>
            {
                ProcessChunk(world, chunkInfo.Location, chunkInfo.Visible);
            });
        }

        private void ProcessChunk(World world, ChunkLocation chunkLocation, bool visible)
        {
            var chunkEntity = _chunkManager!.GetChunk(chunkLocation);
            if (chunkEntity == Entity.Invalid)
                return;

            // Get entities in this chunk
            var entitiesInChunk = _chunkSystem!.GetEntitiesInChunk(chunkEntity);

            foreach (var entity in entitiesInChunk)
            {
                // SAFETY: Validate entity before processing to prevent stale binding issues
                if (!world.IsEntityValid(entity))
                    continue;

                if (!world.TryGetEntityLocation(entity, out var archetype, out var entitySlot))
                    continue;

                if (!archetype.HasComponent(PositionTypeId))
                    continue;

                // Static entities stay batched in MultiMesh (handled by MidZoneRenderSystem for statics)
                // Near zone: Only render DYNAMIC entities with MeshInstance3D
                if (archetype.HasComponent(StaticRenderTypeId))
                    continue;

                var positions = archetype.GetComponentSpan<Position>(PositionTypeId);
                var position = positions[entitySlot];
                var prototype = ResolvePrototype(archetype, entitySlot);

                uint entityIndex = entity.Index;
                _activeEntitiesThisFrame.Set((int)entityIndex);

                // Always build/update visuals, but set Godot node visibility based on frustum culling
                UpdateOrAttachVisual(entityIndex, chunkLocation, position, prototype, visible);
            }
        }

        private RenderPrototypeKind ResolvePrototype(Archetype archetype, int entitySlot)
        {
            if (archetype.HasComponent(RenderPrototypeTypeId))
            {
                var prototypes = archetype.GetComponentSpan<RenderPrototype>(RenderPrototypeTypeId);
                return prototypes[entitySlot].Prototype;
            }
            return RenderPrototypeKind.Sphere;
        }

        private void UpdateOrAttachVisual(uint entityIndex, ChunkLocation chunkLocation, Position position, RenderPrototypeKind prototype, bool chunkVisible)
        {
            EnsureBindingCapacity(entityIndex);

            bool hasBinding = _bindingMask.Contains((int)entityIndex);
            if (hasBinding)
            {
                var existing = _entityBindings[entityIndex];
                if (existing.Chunk == chunkLocation)
                {
                    if (existing.Prototype != prototype)
                    {
                        ApplyPrototypeMesh(existing.Visual, prototype);
                        _entityBindings[entityIndex] = new EntityVisualBinding(existing.Chunk, existing.Visual, prototype);
                    }
                    UpdateVisualPosition(existing.Visual, position);
                    UpdateVisualVisibility(existing.Visual, chunkVisible);
                    return;
                }

                ReleaseEntityVisual(entityIndex);
            }

            var pool = GetOrCreateChunkPool(chunkLocation);
            var visual = pool.Acquire();

            if (visual == null)
                return; // Hit max instances cap

            ApplyPrototypeMesh(visual, prototype);
            _entityBindings[entityIndex] = new EntityVisualBinding(chunkLocation, visual, prototype);

            if (_bindingMask.Set((int)entityIndex))
            {
                _activeBindingCount++;
            }

            UpdateVisualPosition(visual, position);
            UpdateVisualVisibility(visual, chunkVisible);
        }

        private ChunkVisualPool<MeshInstance3D> GetOrCreateChunkPool(ChunkLocation chunkLocation)
        {
            // Thread-safe: GetOrAdd ensures only one thread creates the entry
            var entry = _chunkPools.GetOrAdd(chunkLocation, CreateChunkPoolEntry);
            return entry.Pool;
        }

        private ChunkPoolEntry CreateChunkPoolEntry(ChunkLocation chunkLocation)
        {
            if (_nearZoneContainer == null)
                throw new InvalidOperationException("Near zone container not initialized");

            var chunkContainer = new Node3D
            {
                Name = $"NearChunk_{chunkLocation.X}_{chunkLocation.Z}_{chunkLocation.Y}",
                Visible = true
            };
            _nearZoneContainer.CallDeferred("add_child", chunkContainer);

            MeshInstance3D? Factory(ChunkLocation _) => TryCreateMeshInstance(chunkContainer);
            void OnAcquire(MeshInstance3D visual) => visual.SetDeferred(Node3D.PropertyName.Visible, true);
            void OnRelease(MeshInstance3D visual)
            {
                if (visual.IsInsideTree())
                {
                    visual.SetDeferred(Node3D.PropertyName.Visible, false);
                    visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, HiddenPosition);
                }
            }

            var pool = new ChunkVisualPool<MeshInstance3D>(
                chunkLocation,
                Factory,
                OnAcquire,
                OnRelease,
                SystemSettings.InitialChunkPoolCapacity.Value);

            return new ChunkPoolEntry { Pool = pool, Container = chunkContainer };
        }

        private MeshInstance3D? TryCreateMeshInstance(Node3D parent)
        {
            if (_sphereMesh == null || _totalAllocatedMeshInstances >= SystemSettings.MaxMeshInstances.Value)
                return null;

            var meshInstance = new MeshInstance3D
            {
                Name = $"NearEntity_{_totalAllocatedMeshInstances}",
                Mesh = _sphereMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false
            };

            if (_nearMaterial != null)
                meshInstance.MaterialOverride = _nearMaterial;

            parent.CallDeferred("add_child", meshInstance);
            meshInstance.Position = HiddenPosition;
            _totalAllocatedMeshInstances++;

            return meshInstance;
        }

        private void ApplyPrototypeMesh(MeshInstance3D visual, RenderPrototypeKind prototype)
        {
            Mesh? mesh = prototype == RenderPrototypeKind.Cube ? (_cubeMesh != null ? (Mesh)_cubeMesh : _sphereMesh) : _sphereMesh;
            if (mesh != null && visual.Mesh != mesh)
            {
                // THREAD-SAFE: Use SetDeferred since this is called from parallel threads in ProcessChunksParallel
                if (visual.IsInsideTree())
                    visual.SetDeferred(MeshInstance3D.PropertyName.Mesh, mesh);
            }
        }

        private static void UpdateVisualPosition(MeshInstance3D visual, Position position)
        {
            if (visual.IsInsideTree())
                visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, new Vector3(position.X, position.Y, position.Z));
        }

        private static void UpdateVisualVisibility(MeshInstance3D visual, bool visible)
        {
            if (visual.IsInsideTree())
                visual.SetDeferred(Node3D.PropertyName.Visible, visible);
        }

        private void ReleaseEntityVisual(uint entityIndex)
        {
            if (entityIndex >= _entityBindings.Length || !_bindingMask.Clear((int)entityIndex))
                return;

            var binding = _entityBindings[entityIndex];
            if (_chunkPools.TryGetValue(binding.Chunk, out var entry))
                entry.Pool.Release(binding.Visual);

            _activeBindingCount = Math.Max(0, _activeBindingCount - 1);
            _entityBindings[entityIndex] = default;
        }

        private void ReleaseInactiveVisuals()
        {
            _entitiesToRelease.Clear();
            _activeEntitiesLastFrame.CopySetBitsTo(_entitiesToRelease);

            foreach (var entityIndex in _entitiesToRelease)
            {
                if (!_activeEntitiesThisFrame.Contains(entityIndex))
                {
                    ReleaseEntityVisual((uint)entityIndex);
                }
            }

            _activeEntitiesLastFrame.SwapWith(_activeEntitiesThisFrame);
        }

        private void ReleaseAllVisuals()
        {
            for (int i = 0; i < _entityBindings.Length; i++)
            {
                if (_bindingMask.Contains(i))
                    ReleaseEntityVisual((uint)i);
            }
        }

        private void EnsureBindingCapacity(uint entityIndex)
        {
            // Fast path: no lock needed if capacity is sufficient
            if (entityIndex < _entityBindings.Length)
                return;

            // Slow path: lock to prevent race conditions during resize
            lock (_bindingsLock)
            {
                // Double-check after acquiring lock (another thread may have resized)
                if (entityIndex < _entityBindings.Length)
                    return;

                int newSize = _entityBindings.Length == 0 ? 1024 : _entityBindings.Length;
                while (newSize <= entityIndex)
                    newSize *= 2;

                Array.Resize(ref _entityBindings, newSize);
            }
        }

        public override void OnShutdown(World world)
        {
            ReleaseAllVisuals();

            foreach (var entry in _chunkPools.Values)
            {
                if (entry.Container.IsInsideTree())
                    entry.Container.QueueFree();
            }
            _chunkPools.Clear();

            if (_nearZoneContainer != null && _nearZoneContainer.IsInsideTree())
                _nearZoneContainer.QueueFree();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete - recycled {_totalAllocatedMeshInstances} MeshInstances");
            }
        }

        private readonly struct EntityVisualBinding
        {
            public ChunkLocation Chunk { get; }
            public MeshInstance3D Visual { get; }
            public RenderPrototypeKind Prototype { get; }

            public EntityVisualBinding(ChunkLocation chunk, MeshInstance3D visual, RenderPrototypeKind prototype)
            {
                Chunk = chunk;
                Visual = visual;
                Prototype = prototype;
            }
        }
    }
}
