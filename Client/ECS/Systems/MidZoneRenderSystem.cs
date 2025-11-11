#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
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
    /// MidZoneRenderSystem - SINGLE RESPONSIBILITY: Build MultiMesh visuals for Mid zone chunks.
    ///
    /// Design doc quote: "MidChunkSystem handles only MultiMeshes."
    ///
    /// This system:
    /// 1. Queries chunks with MidZoneTag component (archetype-based filtering)
    /// 2. Gets entities in each chunk from ChunkSystem
    /// 3. Builds MultiMesh batches for ALL entities (no individual MeshInstances)
    /// 4. Respects RenderChunk.Visible flag (set by RenderVisibilitySystem)
    /// 5. Parallelizes per-chunk processing
    ///
    /// ECS PATTERN: Queries by MidZoneTag, not enum check.
    /// - Only iterates chunks in Mid zone archetype (automatic filtering)
    /// - No read conflicts with Near/FarZoneRenderSystems (different tags)
    /// - Can run in parallel with other zone systems
    ///
    /// DOES NOT:
    /// - Assign zones (that's RenderChunkManager's job)
    /// - Do frustum culling (that's RenderVisibilitySystem's job)
    /// - Build individual MeshInstances (Mid zone is visual-only batched)
    /// - Handle Near or Far zones (that's other zone systems' job)
    ///
    /// DEPENDENCIES: Requires RenderChunkManager to tag chunks before building visuals.
    /// </summary>
    [RequireSystem("Client.ECS.Systems.RenderChunkManager")]
    public sealed class MidZoneRenderSystem : BaseSystem
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

        public override string Name => "Mid Zone Render System";
        public override int SystemId => typeof(MidZoneRenderSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: Position, Mid zone tag, render metadata
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(MidZoneTag), typeof(RenderChunk), typeof(ChunkOwner), typeof(RenderPrototype) };
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
        private readonly Queue<ChunkMultiMesh> _pooledChunkMeshes = new();
        private readonly List<ChunkMeshKey> _chunkUploadList = new();
        private readonly List<ChunkMeshKey> _chunksToRemove = new();
        private float[] _multimeshBuffer = Array.Empty<float>();
        private const int FloatsPerInstance = 12;

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();
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

            _midZoneContainer = new Node3D { Name = "ECS_MidZone" };
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
            _sphereMesh.Material = _midMaterial;

            _cubeMesh = new BoxMesh { Size = Vector3.One * SystemSettings.EntityRadius.Value * 2.0f };
            _staticMidMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.5f, 0.95f, 0.6f),
                Metallic = 0.0f,
                Roughness = 0.9f
            };
            _cubeMesh.Material = _staticMidMaterial;

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

            // Reset all chunk counts
            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
                chunkData.Count = 0;
                if (chunkData.LastUploadedCount != 0)
                    chunkData.IsDirty = true;
            }

            // Query all chunks with Mid zone assignment
            var midChunks = GetMidChunks(world);

            if (midChunks.Count == 0)
            {
                MarkAllChunksForRemoval();
                RemoveInactiveChunks();
                return;
            }

            // Parallelize per-chunk processing if we have enough chunks
            if (midChunks.Count >= SystemSettings.ParallelThreshold.Value)
            {
                ProcessChunksParallel(world, midChunks);
            }
            else
            {
                ProcessChunksSequential(world, midChunks);
            }

            // Upload dirty chunks to GPU
            UpdateChunkMultiMeshes();

            // Remove chunks that are no longer in Mid zone
            RemoveInactiveChunks();

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                int totalEntities = 0;
                foreach (var chunkData in _chunkMultiMeshes.Values)
                    totalEntities += chunkData.Count;

                Logging.Log($"[{Name}] Active chunks: {_chunkMultiMeshes.Count}, Total entities: {totalEntities}, Pool: {_pooledChunkMeshes.Count}");
            }
        }

        private List<(ChunkLocation Location, bool Visible)> GetMidChunks(World world)
        {
            var midChunks = new List<(ChunkLocation, bool)>();

            // Query by MidZoneTag - automatic archetype filtering!
            // Only gets chunks in Mid zone (no manual enum check needed)
            var archetypes = world.QueryArchetypes(typeof(MidZoneTag));

            foreach (var archetype in archetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var renderChunks = archetype.GetComponentSpan<RenderChunk>(RenderChunkTypeId);

                for (int i = 0; i < renderChunks.Length; i++)
                {
                    ref var renderChunk = ref renderChunks[i];
                    midChunks.Add((renderChunk.Location, renderChunk.Visible));
                }
            }

            return midChunks;
        }

        private void ProcessChunksSequential(World world, List<(ChunkLocation Location, bool Visible)> midChunks)
        {
            // Use AsSpan for read-only iteration (better performance)
            var chunksSpan = CollectionsMarshal.AsSpan(midChunks);
            for (int i = 0; i < chunksSpan.Length; i++)
            {
                ProcessChunk(world, chunksSpan[i].Location, chunksSpan[i].Visible);
            }
        }

        private void ProcessChunksParallel(World world, List<(ChunkLocation Location, bool Visible)> midChunks)
        {
            // Parallel.ForEach over chunks - each chunk is independent
            Parallel.ForEach(midChunks, chunkInfo =>
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
                if (!world.TryGetEntityLocation(entity, out var archetype, out var entitySlot))
                    continue;

                if (!archetype.HasComponent(PositionTypeId))
                    continue;

                var positions = archetype.GetComponentSpan<Position>(PositionTypeId);
                var pos = positions[entitySlot];
                var prototype = ResolvePrototype(archetype, entitySlot);

                var key = new ChunkMeshKey(chunkLocation, prototype);

                // GetOrAdd is thread-safe for ConcurrentDictionary
                var chunkData = _chunkMultiMeshes.GetOrAdd(key, k => AcquireChunkMultiMesh(chunkLocation, prototype));

                // Always build transforms, but track visibility for GPU upload
                chunkData.Visible = visible;

                int nextIndex = chunkData.Count;
                EnsureCapacity(chunkData, nextIndex + 1);
                chunkData.Transforms[nextIndex] = new Transform3D(Basis.Identity, new Vector3(pos.X, pos.Y, pos.Z));
                chunkData.Count++;
                chunkData.IsDirty = true;
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
            ChunkMultiMesh chunkData;
            if (_pooledChunkMeshes.Count > 0)
            {
                chunkData = _pooledChunkMeshes.Dequeue();
            }
            else
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

            // Find chunks that have 0 entities (not updated this frame)
            foreach (var kvp in _chunkMultiMeshes)
            {
                if (kvp.Value.Count == 0 && kvp.Value.LastUploadedCount == 0)
                    _chunksToRemove.Add(kvp.Key);
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
