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

namespace Client.ECS.Systems
{
    /// <summary>
    /// StaticEntityRenderSystem - SINGLE RESPONSIBILITY: Render ALL STATIC entities with MultiMesh batching.
    ///
    /// Rendering strategy split:
    /// - DynamicEntityRenderSystem: DYNAMIC entities → Individual MeshInstance3D
    /// - StaticEntityRenderSystem: STATIC entities → MultiMesh batches (this system)
    /// - BillboardEntityRenderSystem: BILLBOARD entities → Impostors/sprites (far distance)
    ///
    /// This system renders ALL static entities in BOTH Near and Mid zones using MultiMesh batching:
    /// - Near zone statics (NearZoneTag + StaticRenderTag): MultiMesh batches (efficient batching for non-interactive entities)
    /// - Mid zone statics (MidZoneTag): MultiMesh batches (visual only, far from player)
    ///
    /// Process:
    /// 1. Queries chunks with NearZoneTag OR MidZoneTag (archetype-based filtering)
    /// 2. Gets entities in each chunk from ChunkSystem
    /// 3. Filters to ONLY static entities (has StaticRenderTag)
    /// 4. Builds MultiMesh batches per chunk per prototype (no individual MeshInstances)
    /// 5. Respects RenderChunk.Visible flag (set by RenderVisibilitySystem)
    /// 6. Parallelizes per-chunk processing
    ///
    /// ECS PATTERN: Queries by NearZoneTag + MidZoneTag, filters by StaticRenderTag.
    /// - Queries both Near and Mid zone archetypes
    /// - Dynamic entities handled separately by DynamicEntityRenderSystem
    /// - Can run in parallel with DynamicEntityRenderSystem (different entity sets)
    ///
    /// DOES NOT:
    /// - Render dynamic entities (DynamicEntityRenderSystem handles those with MeshInstance3D)
    /// - Assign zones (that's RenderChunkManager's job)
    /// - Do frustum culling (that's RenderVisibilitySystem's job)
    /// - Build individual MeshInstances (all statics use MultiMesh batching)
    /// - Operate on Far zone (that's BillboardEntityRenderSystem's job)
    ///
    /// DEPENDENCIES: Requires RenderChunkManager to tag chunks before building visuals.
    /// </summary>
    [RequireSystem("Client.ECS.Systems.RenderChunkManager")]
    public sealed class StaticEntityRenderSystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting EntityRadius { get; private set; }
            public IntSetting RadialSegments { get; private set; }
            public IntSetting Rings { get; private set; }
            public IntSetting InitialChunkCapacity { get; private set; }
            public IntSetting MaxChunkUploadsPerFrame { get; private set; }
            public BoolSetting CastShadows { get; private set; }
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

                InitialChunkCapacity = RegisterInt("Initial Chunk Capacity", 256,
                    min: 64, max: 100000, step: 64,
                    tooltip: "Initial MultiMesh capacity per chunk");

                MaxChunkUploadsPerFrame = RegisterInt("Chunk Uploads Per Frame", 256,
                    min: 0, max: 5000, step: 32,
                    tooltip: "Max dirty chunks to upload per frame (0 = all)");

                CastShadows = RegisterBool("Cast Shadows", false,
                    tooltip: "Enable shadow casting for Mid zone entities");

                ParallelThreshold = RegisterInt("Parallel Threshold", 16,
                    min: 1, max: 256, step: 1,
                    tooltip: "Minimum Mid chunks before parallel processing kicks in");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log MultiMesh creation/updates");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Static Entity Render System";
        public override int SystemId => typeof(StaticEntityRenderSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: Position, Near/Mid zone tags, render metadata, static tag
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(NearZoneTag), typeof(MidZoneTag), typeof(RenderChunk), typeof(ChunkOwner), typeof(StaticRenderTag), typeof(RenderPrototype) };
        // Write: None (we modify Godot scene graph)
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _midZoneContainer;
        private SphereMesh? _sphereMesh;
        private BoxMesh? _cubeMesh;
        private StandardMaterial3D? _midMaterial;
        private StandardMaterial3D? _staticMidMaterial;
        private ChunkManager? _chunkManager;
        private ChunkSystem? _chunkSystem;

        private sealed class ChunkMultiMesh
        {
            public MultiMeshInstance3D Instance = null!;
            public MultiMesh MultiMesh = null!;
            public Transform3D[] Transforms = Array.Empty<Transform3D>();
            public int Capacity = 0;
            public int Count = 0;
            public ChunkLocation Location;
            public RenderPrototypeKind Prototype;
            public bool IsDirty;
            public int LastUploadedCount;
            public bool Visible; // Frustum culling result from RenderVisibilitySystem
        }

        private readonly struct ChunkMeshKey : IEquatable<ChunkMeshKey>
        {
            public ChunkLocation Location { get; }
            public RenderPrototypeKind Prototype { get; }

            public ChunkMeshKey(ChunkLocation location, RenderPrototypeKind prototype)
            {
                Location = location;
                Prototype = prototype;
            }

            public bool Equals(ChunkMeshKey other) => Location.Equals(other.Location) && Prototype == other.Prototype;
            public override bool Equals(object? obj) => obj is ChunkMeshKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Location, (int)Prototype);
        }

        // Thread-safe dictionary for parallel chunk processing
        private readonly ConcurrentDictionary<ChunkMeshKey, ChunkMultiMesh> _chunkMultiMeshes = new();
        private readonly ConcurrentQueue<ChunkMultiMesh> _pooledChunkMeshes = new();
        private readonly List<ChunkMeshKey> _chunkUploadList = new();
        private readonly List<ChunkMeshKey> _chunksToRemove = new();
        private readonly HashSet<ChunkMeshKey> _activeChunksThisFrame = new(); // Track which chunks have entities this frame
        private float[] _multimeshBuffer = Array.Empty<float>();
        private const int FloatsPerInstance = 12;

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();
        private static readonly int StaticRenderTypeId = ComponentManager.GetTypeId<StaticRenderTag>();
        private static readonly int RenderPrototypeTypeId = ComponentManager.GetTypeId<RenderPrototype>();

        private int _frameCounter = 0;
        private int _chunkUploadCursor = 0;

        public override void OnInitialize(World world)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            _rootNode = tree?.CurrentScene as Node3D;

            if (_rootNode == null)
            {
                Logging.Log($"[{Name}] ERROR: No scene root found!", LogSeverity.Error);
                return;
            }

            _midZoneContainer = new Node3D { Name = "ECS_StaticEntities" };
            _rootNode.CallDeferred("add_child", _midZoneContainer);

            _sphereMesh = new SphereMesh
            {
                RadialSegments = SystemSettings.RadialSegments.Value,
                Rings = SystemSettings.Rings.Value,
                Radius = SystemSettings.EntityRadius.Value,
                Height = SystemSettings.EntityRadius.Value * 2.0f
            };

            _midMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.55f, 0.15f),
                Metallic = 0.0f,
                Roughness = 1.0f
            };
            // NOTE: Do NOT set .Material on mesh resources - procedural meshes haven't generated surfaces yet
            // Material is applied via MultiMesh.Mesh after surfaces are generated

            _cubeMesh = new BoxMesh { Size = Vector3.One * SystemSettings.EntityRadius.Value * 2.0f };
            _staticMidMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.5f, 0.95f, 0.6f),
                Metallic = 0.0f,
                Roughness = 0.9f
            };
            // NOTE: Do NOT set .Material on mesh resources - procedural meshes haven't generated surfaces yet
            // Material is applied via MultiMesh.Mesh after surfaces are generated

            Logging.Log($"[{Name}] Initialized - Initial capacity: {SystemSettings.InitialChunkCapacity.Value}/chunk");
        }

        public void SetChunkManager(ChunkManager chunkManager, ChunkSystem chunkSystem)
        {
            _chunkManager = chunkManager;
            _chunkSystem = chunkSystem;
            Logging.Log($"[{Name}] ChunkManager + ChunkSystem references set");
        }

        public override void Update(World world, double delta)
        {
            if (_midZoneContainer == null || _sphereMesh == null || _chunkSystem == null || _chunkManager == null)
                return;

            _frameCounter++;

            // OPTIMIZATION: Don't reset all chunk counts every frame.
            // Instead, track which chunks are active this frame and only rebuild changed chunks.
            // Static entities don't move, so if the count hasn't changed, we skip the rebuild.
            _activeChunksThisFrame.Clear();

            // Query all chunks with Near OR Mid zone assignment (for static entities)
            var staticChunks = GetStaticRenderChunks(world);

            if (staticChunks.Count == 0)
            {
                MarkAllChunksForRemoval();
                RemoveInactiveChunks();
                return;
            }

            // Parallelize per-chunk processing if we have enough chunks
            if (staticChunks.Count >= SystemSettings.ParallelThreshold.Value)
            {
                ProcessChunksParallel(world, staticChunks);
            }
            else
            {
                ProcessChunksSequential(world, staticChunks);
            }

            // Upload dirty chunks to GPU
            UpdateChunkMultiMeshes();

            // PHASE 3: Clear dirty flags after processing chunks
            foreach (var chunkInfo in staticChunks)
            {
                var chunkEntity = _chunkManager!.GetChunk(chunkInfo.Location);
                if (chunkEntity != Entity.Invalid)
                {
                    _chunkSystem!.ClearChunkDirty(chunkEntity);
                }
            }

            // Remove chunks that are no longer in Near/Mid zones
            RemoveInactiveChunks();

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                int totalEntities = 0;
                foreach (var chunkData in _chunkMultiMeshes.Values)
                    totalEntities += chunkData.Count;

                int totalRenderChunks = _chunkMultiMeshes.Count;
                int processedThisFrame = staticChunks.Count;
                float skipPercentage = totalRenderChunks > 0 ? (1.0f - (float)processedThisFrame / totalRenderChunks) * 100.0f : 0;

                Logging.Log($"[{Name}] Active: {totalRenderChunks} chunks, Entities: {totalEntities}, Pool: {_pooledChunkMeshes.Count} | Processed: {processedThisFrame} ({skipPercentage:F1}% skipped via dirty tracking)");
            }
        }

        // Track previous visibility per chunk for change detection
        private readonly Dictionary<ChunkLocation, bool> _previousVisibility = new();

        private List<(ChunkLocation Location, bool Visible)> GetStaticRenderChunks(World world)
        {
            var staticChunks = new List<(ChunkLocation, bool)>();

            // Query by NearZoneTag AND MidZoneTag - automatic archetype filtering!
            // Gets chunks in BOTH Near and Mid zones (for static entity rendering)
            var zoneTagTypes = new[] { typeof(NearZoneTag), typeof(MidZoneTag) };

            foreach (var zoneTagType in zoneTagTypes)
            {
                var archetypes = world.QueryArchetypes(zoneTagType);

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
                            staticChunks.Add((serverChunkLoc, visible));
                            _previousVisibility[serverChunkLoc] = visible;
                        }
                    }
                }
            }

            return staticChunks;
        }

        private void ProcessChunksSequential(World world, List<(ChunkLocation Location, bool Visible)> staticChunks)
        {
            // Use AsSpan for read-only iteration (better performance)
            var chunksSpan = CollectionsMarshal.AsSpan(staticChunks);
            for (int i = 0; i < chunksSpan.Length; i++)
            {
                ProcessChunk(world, chunksSpan[i].Location, chunksSpan[i].Visible);
            }
        }

        private void ProcessChunksParallel(World world, List<(ChunkLocation Location, bool Visible)> staticChunks)
        {
            Parallel.ForEach(staticChunks, chunkInfo =>
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

            // OPTIMIZATION STEP 1: Count static entities per prototype FIRST (before building transforms)
            // This lets us detect if the count changed and skip rebuilding if nothing changed.
            var prototypeEntityCounts = new Dictionary<RenderPrototypeKind, (List<(Position pos, int slot, Archetype archetype)> entities, int count)>();

            foreach (var entity in entitiesInChunk)
            {
                if (!world.TryGetEntityLocation(entity, out var archetype, out var entitySlot))
                    continue;

                if (!archetype.HasComponent(PositionTypeId))
                    continue;

                // Static entities only - dynamic entities handled by DynamicEntityRenderSystem
                // This system renders statics in BOTH Near and Mid zones with MultiMesh batching
                if (!archetype.HasComponent(StaticRenderTypeId))
                    continue;

                var positions = archetype.GetComponentSpan<Position>(PositionTypeId);
                var pos = positions[entitySlot];
                var prototype = ResolvePrototype(archetype, entitySlot);

                if (!prototypeEntityCounts.TryGetValue(prototype, out var entry))
                {
                    entry = (new List<(Position, int, Archetype)>(), 0);
                    prototypeEntityCounts[prototype] = entry;
                }

                entry.entities.Add((pos, entitySlot, archetype));
                entry.count++;
                prototypeEntityCounts[prototype] = entry;
            }

            // OPTIMIZATION STEP 2: For each prototype, check if count changed
            // Only rebuild transforms if count changed (entities added/removed)
            foreach (var kvp in prototypeEntityCounts)
            {
                var prototype = kvp.Key;
                var entities = kvp.Value.entities;
                var newCount = kvp.Value.count;

                var key = new ChunkMeshKey(chunkLocation, prototype);

                // Mark this chunk as active (processed this frame)
                // Note: HashSet is NOT thread-safe, but we'll handle this carefully
                lock (_activeChunksThisFrame)
                {
                    _activeChunksThisFrame.Add(key);
                }

                // GetOrAdd is thread-safe for ConcurrentDictionary
                var chunkData = _chunkMultiMeshes.GetOrAdd(key, k => AcquireChunkMultiMesh(chunkLocation, prototype));

                // Update visibility (may have changed due to frustum culling)
                chunkData.Visible = visible;

                // OPTIMIZATION: Only rebuild if count changed OR if this is a new chunk
                // For static entities, if count hasn't changed, transforms are still valid!
                if (chunkData.Count == newCount && chunkData.LastUploadedCount != 0)
                {
                    // Count unchanged and chunk was previously uploaded - skip rebuild!
                    // This is the key optimization for static entities.
                    continue;
                }

                // Count changed (entities added/removed) - rebuild transforms
                chunkData.Count = 0; // Reset for rebuild
                EnsureCapacity(chunkData, newCount);

                for (int i = 0; i < entities.Count; i++)
                {
                    var pos = entities[i].pos;
                    int nextIndex = chunkData.Count;
                    chunkData.Transforms[nextIndex] = new Transform3D(Basis.Identity, new Vector3(pos.X, pos.Y, pos.Z));
                    chunkData.Count++;
                }

                chunkData.IsDirty = true; // Mark for GPU upload
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

        private ChunkMultiMesh AcquireChunkMultiMesh(ChunkLocation location, RenderPrototypeKind prototype)
        {
            // Thread-safe: Try to reuse from pool, otherwise create new
            if (!_pooledChunkMeshes.TryDequeue(out var chunkData))
            {
                chunkData = CreateChunkMultiMesh();
            }

            PrepareChunkMultiMesh(chunkData, location, prototype);
            return chunkData;
        }

        private void PrepareChunkMultiMesh(ChunkMultiMesh chunkData, ChunkLocation location, RenderPrototypeKind prototype)
        {
            chunkData.Location = location;
            chunkData.Prototype = prototype;
            chunkData.Count = 0;
            chunkData.MultiMesh.InstanceCount = 0;
            chunkData.MultiMesh.VisibleInstanceCount = 0;
            chunkData.MultiMesh.Mesh = GetPrototypeMesh(prototype);
            chunkData.Instance.MaterialOverride = GetPrototypeMaterial(prototype);
            chunkData.Instance.SetDeferred(Node.PropertyName.Name, new StringName($"MidChunk_{location.X}_{location.Z}_{location.Y}_{prototype}"));
            chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, true);
            chunkData.IsDirty = true;
            chunkData.LastUploadedCount = 0;
        }

        private ChunkMultiMesh CreateChunkMultiMesh()
        {
            if (_midZoneContainer == null || _sphereMesh == null)
                throw new InvalidOperationException("Mid zone container not initialized");

            int initialCapacity = SystemSettings.InitialChunkCapacity.Value;

            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false,
                Mesh = _sphereMesh,
                InstanceCount = 0
            };

            var instance = new MultiMeshInstance3D
            {
                Name = "MidChunkPoolInstance",
                Multimesh = multiMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false
            };

            if (_midMaterial != null)
                instance.MaterialOverride = _midMaterial;

            _midZoneContainer.CallDeferred("add_child", instance);

            return new ChunkMultiMesh
            {
                Instance = instance,
                MultiMesh = multiMesh,
                Transforms = new Transform3D[initialCapacity],
                Capacity = initialCapacity
            };
        }

        private void EnsureCapacity(ChunkMultiMesh chunkData, int required)
        {
            if (required <= chunkData.Capacity)
                return;

            int newCapacity = chunkData.Capacity == 0
                ? Math.Max(SystemSettings.InitialChunkCapacity.Value, required)
                : Math.Max(required, chunkData.Capacity * 2);

            var newBuffer = new Transform3D[newCapacity];
            if (chunkData.Capacity > 0)
                Array.Copy(chunkData.Transforms, newBuffer, chunkData.Capacity);

            chunkData.Transforms = newBuffer;
            chunkData.Capacity = newCapacity;
        }

        private Mesh? GetPrototypeMesh(RenderPrototypeKind prototype)
        {
            return prototype == RenderPrototypeKind.Cube ? (_cubeMesh != null ? (Mesh)_cubeMesh : _sphereMesh) : _sphereMesh;
        }

        private StandardMaterial3D? GetPrototypeMaterial(RenderPrototypeKind prototype)
        {
            return prototype == RenderPrototypeKind.Cube ? (_staticMidMaterial ?? _midMaterial) : _midMaterial;
        }


        private void UpdateChunkMultiMeshes()
        {
            if (_chunkMultiMeshes.Count == 0)
                return;

            _chunkUploadList.Clear();
            foreach (var kvp in _chunkMultiMeshes)
            {
                if (kvp.Value.IsDirty || kvp.Value.LastUploadedCount != kvp.Value.Count)
                    _chunkUploadList.Add(kvp.Key);
            }

            if (_chunkUploadList.Count == 0)
                return;

            int dirtyCount = _chunkUploadList.Count;
            int budget = SystemSettings.MaxChunkUploadsPerFrame.Value;

            if (budget <= 0 || budget >= dirtyCount)
            {
                foreach (var key in _chunkUploadList)
                    UploadChunkMultiMesh(_chunkMultiMeshes[key]);
                _chunkUploadCursor = 0;
                return;
            }

            for (int i = 0; i < budget; i++)
            {
                int index = (_chunkUploadCursor + i) % dirtyCount;
                var key = _chunkUploadList[index];
                if (_chunkMultiMeshes.TryGetValue(key, out var chunkData))
                    UploadChunkMultiMesh(chunkData);
            }

            _chunkUploadCursor = (_chunkUploadCursor + budget) % dirtyCount;
        }

        private void UploadChunkMultiMesh(ChunkMultiMesh chunkData)
        {
            int count = chunkData.Count;

            // Set Godot node visibility based on RenderVisibilitySystem's frustum culling
            bool shouldBeVisible = chunkData.Visible && count > 0;

            if (count == 0)
            {
                if (chunkData.LastUploadedCount != 0)
                {
                    chunkData.MultiMesh.InstanceCount = 0;
                    chunkData.MultiMesh.VisibleInstanceCount = 0;
                    chunkData.LastUploadedCount = 0;
                    chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, false);
                }
                chunkData.IsDirty = false;
                return;
            }

            var transformSpan = chunkData.Transforms.AsSpan(0, count);
            var bufferSpan = BuildTransformBuffer(transformSpan);

            chunkData.MultiMesh.InstanceCount = count;
            chunkData.MultiMesh.VisibleInstanceCount = count;
            RenderingServer.MultimeshSetBuffer(chunkData.MultiMesh.GetRid(), bufferSpan);

            // Set visibility based on frustum culling result
            chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, shouldBeVisible);
            chunkData.LastUploadedCount = count;
            chunkData.IsDirty = false;
        }

        private ReadOnlySpan<float> BuildTransformBuffer(Span<Transform3D> transforms)
        {
            int requiredFloats = transforms.Length * FloatsPerInstance;

            if (_multimeshBuffer.Length < requiredFloats)
            {
                int newSize = _multimeshBuffer.Length == 0 ? 1024 : _multimeshBuffer.Length;
                while (newSize < requiredFloats)
                    newSize *= 2;
                Array.Resize(ref _multimeshBuffer, newSize);
            }

            int offset = 0;
            foreach (var transform in transforms)
            {
                var basis = transform.Basis;
                var origin = transform.Origin;
                var row0 = basis.Row0;
                var row1 = basis.Row1;
                var row2 = basis.Row2;

                _multimeshBuffer[offset++] = row0.X;
                _multimeshBuffer[offset++] = row0.Y;
                _multimeshBuffer[offset++] = row0.Z;
                _multimeshBuffer[offset++] = origin.X;

                _multimeshBuffer[offset++] = row1.X;
                _multimeshBuffer[offset++] = row1.Y;
                _multimeshBuffer[offset++] = row1.Z;
                _multimeshBuffer[offset++] = origin.Y;

                _multimeshBuffer[offset++] = row2.X;
                _multimeshBuffer[offset++] = row2.Y;
                _multimeshBuffer[offset++] = row2.Z;
                _multimeshBuffer[offset++] = origin.Z;
            }

            return _multimeshBuffer.AsSpan(0, requiredFloats);
        }

        private void MarkAllChunksForRemoval()
        {
            _chunksToRemove.Clear();
            foreach (var key in _chunkMultiMeshes.Keys)
                _chunksToRemove.Add(key);
        }

        private void RemoveInactiveChunks()
        {
            _chunksToRemove.Clear();

            // OPTIMIZATION: Remove chunks that weren't processed this frame
            // (i.e., chunks that no longer have entities or moved out of Near/Mid zones)
            foreach (var key in _chunkMultiMeshes.Keys)
            {
                if (!_activeChunksThisFrame.Contains(key))
                {
                    _chunksToRemove.Add(key);
                }
            }

            foreach (var key in _chunksToRemove)
            {
                if (_chunkMultiMeshes.Remove(key, out var chunkData))
                {
                    chunkData.MultiMesh.InstanceCount = 0;
                    chunkData.MultiMesh.VisibleInstanceCount = 0;
                    chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, false);
                    _pooledChunkMeshes.Enqueue(chunkData);
                }
            }
        }

        public override void OnShutdown(World world)
        {
            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
                if (chunkData.Instance.IsInsideTree())
                    chunkData.Instance.QueueFree();
            }
            _chunkMultiMeshes.Clear();

            if (_midZoneContainer != null && _midZoneContainer.IsInsideTree())
                _midZoneContainer.QueueFree();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete");
            }
        }
    }
}
