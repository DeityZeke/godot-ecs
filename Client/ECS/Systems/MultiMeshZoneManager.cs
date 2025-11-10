#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using UltraSim.Server.ECS.Systems;
using UltraSim;
using Client.ECS;
using Client.ECS.Components;
using Client.ECS.Rendering;

namespace Client.ECS.Systems
{
    /// <summary>
    /// Manages MultiMesh batching for chunks in the Near rendering zone.
    /// Creates one MultiMeshInstance3D per chunk for efficient batched rendering.
    /// Entities in Near zone are visual-only (no individual interactivity).
    /// </summary>
    public sealed class MultiMeshZoneManager : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting EntityRadius { get; private set; }
            public IntSetting RadialSegments { get; private set; }
            public IntSetting Rings { get; private set; }
            public IntSetting InitialChunkCapacity { get; private set; }
            public IntSetting ChunkReleaseDelayFrames { get; private set; }
            public IntSetting MaxChunkUploadsPerFrame { get; private set; }
            public BoolSetting CastShadows { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }

            public Settings()
            {
                EntityRadius = RegisterFloat("Entity Radius", 0.5f,
                    min: 0.1f, max: 5.0f, step: 0.1f,
                    tooltip: "Radius of sphere mesh for each entity");

                RadialSegments = RegisterInt("Radial Segments", 6,
                    min: 3, max: 32, step: 1,
                    tooltip: "Sphere mesh radial segments (lower = better performance)");

                Rings = RegisterInt("Rings", 3,
                    min: 2, max: 32, step: 1,
                    tooltip: "Sphere mesh rings (lower = better performance)");

                InitialChunkCapacity = RegisterInt("Initial Chunk Capacity", 256,
                    min: 64, max: 100000, step: 64,
                    tooltip: "Initial MultiMesh capacity per chunk (auto-grows if needed)");

                ChunkReleaseDelayFrames = RegisterInt("Chunk Release Delay (frames)", 10,
                    min: 0, max: 300, step: 1,
                    tooltip: "How many frames to keep inactive chunk MultiMeshes before pooling them");

                MaxChunkUploadsPerFrame = RegisterInt("Chunk Uploads Per Frame", 256,
                    min: 0, max: 5000, step: 32,
                    tooltip: "How many dirty chunks can upload transforms per frame (0 = all)");

                CastShadows = RegisterBool("Cast Shadows", false,
                    tooltip: "Enable shadow casting for near zone entities");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log MultiMesh creation/updates");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "MultiMesh Zone Manager";
        public override int SystemId => typeof(MultiMeshZoneManager).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone), typeof(StaticRenderTag) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _multiMeshContainer;
        private SphereMesh? _sphereMesh;
        private BoxMesh? _cubeMesh;
        private StandardMaterial3D? _nearZoneMaterial;
        private StandardMaterial3D? _staticNearMaterial;
        private const float FrustumPadding = 32f;
        private const int FrustumHideGraceFrames = 5;
        private ChunkManager? _chunkManager;
        private ChunkSystem? _chunkSystem;

        // Track chunk -> MultiMeshInstance3D + data
        private class ChunkMultiMesh
        {
            public MultiMeshInstance3D Instance = null!;
            public MultiMesh MultiMesh = null!;
            public Transform3D[] Transforms = Array.Empty<Transform3D>(); // Pre-allocated transform buffer
            public int Capacity = 0;
            public int Count = 0;
            public ChunkLocation Location;
            public RenderPrototypeKind Prototype;
            public bool IsDirty;
            public int LastUploadedCount;
            public bool IsVisible;
            public int HiddenFrames;
        }

        private readonly struct ChunkMeshKey : IEquatable<ChunkMeshKey>
        {
            public ChunkMeshKey(ChunkLocation location, RenderPrototypeKind prototype)
            {
                Location = location;
                Prototype = prototype;
            }

            public ChunkLocation Location { get; }
            public RenderPrototypeKind Prototype { get; }

            public bool Equals(ChunkMeshKey other) => Location.Equals(other.Location) && Prototype == other.Prototype;
            public override bool Equals(object? obj) => obj is ChunkMeshKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Location, (int)Prototype);
        }

        private readonly Dictionary<ChunkMeshKey, ChunkMultiMesh> _chunkMultiMeshes = new();
        private readonly Queue<ChunkMultiMesh> _pooledChunkMeshes = new();
        private readonly List<ChunkMeshKey> _chunkUploadList = new();
        private readonly List<ChunkMeshKey> _chunkKeyScratch = new();
        private readonly List<ChunkRenderWindow.Slot> _nearSlotScratch = new();
        private readonly List<ChunkRenderWindow.Slot> _nearExitedScratch = new();
        private float[] _multimeshBuffer = Array.Empty<float>();
        private const int FloatsPerInstance = 12;
        private readonly HybridRenderSharedState _sharedState = HybridRenderSharedState.Instance;
        private ulong _nearWindowVersionProcessed = 0;
        private int _multimeshCreatesThisFrame = 0;
        private int _multimeshPoolsThisFrame = 0;
        private int _createdSinceReport = 0;
        private int _pooledSinceReport = 0;

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
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

            // ChunkManager will be set by WorldECS after ChunkSystem initialization
            _chunkManager = null;

            // Create container node for all MultiMesh instances
            _multiMeshContainer = new Node3D
            {
                Name = "ECS_NearZone"
            };
            _rootNode.CallDeferred("add_child", _multiMeshContainer);

            // Create shared sphere mesh (same as AdaptiveMultiMeshRenderSystem)
            _sphereMesh = new SphereMesh
            {
                RadialSegments = SystemSettings.RadialSegments.Value,
                Rings = SystemSettings.Rings.Value,
                Radius = SystemSettings.EntityRadius.Value,
                Height = SystemSettings.EntityRadius.Value * 2.0f
            };
            _nearZoneMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.55f, 0.15f),
                Metallic = 0.0f,
                Roughness = 1.0f
            };
            _sphereMesh.SurfaceSetMaterial(0, _nearZoneMaterial);

            _cubeMesh = new BoxMesh
            {
                Size = Vector3.One * SystemSettings.EntityRadius.Value * 2.0f
            };
            _staticNearMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.5f, 0.95f, 0.6f),
                Metallic = 0.0f,
                Roughness = 0.9f
            };
            _cubeMesh.SurfaceSetMaterial(0, _staticNearMaterial);

            Logging.Log($"[{Name}] Initialized - Mesh: {SystemSettings.RadialSegments.Value}x{SystemSettings.Rings.Value} sphere, Initial capacity: {SystemSettings.InitialChunkCapacity.Value}/chunk");
        }

        /// <summary>
        /// Set the ChunkManager and ChunkSystem references for chunk-based iteration.
        /// </summary>
        public void SetChunkManager(ChunkManager chunkManager, ChunkSystem chunkSystem)
        {
            _chunkManager = chunkManager;
            _chunkSystem = chunkSystem;
            ResetChunkWindowState();
            Logging.Log($"[{Name}] ChunkManager + ChunkSystem references set");
        }

        private void ResetChunkWindowState()
        {
            _nearWindowVersionProcessed = 0;
            PoolAllChunkMultiMeshes();
        }

        private void PoolAllChunkMultiMeshes()
        {
            if (_chunkMultiMeshes.Count == 0)
                return;

            _chunkKeyScratch.Clear();
            foreach (var key in _chunkMultiMeshes.Keys)
            {
                _chunkKeyScratch.Add(key);
            }

            foreach (var key in _chunkKeyScratch)
            {
                PoolChunkMultiMesh(key);
            }

            _chunkKeyScratch.Clear();
        }

        private void PoolChunkMultiMeshes(ChunkLocation location)
        {
            _chunkKeyScratch.Clear();
            foreach (var key in _chunkMultiMeshes.Keys)
            {
                if (key.Location.Equals(location))
                {
                    _chunkKeyScratch.Add(key);
                }
            }

            foreach (var key in _chunkKeyScratch)
            {
                PoolChunkMultiMesh(key);
            }

            _chunkKeyScratch.Clear();
        }

        private static void CopySlotList(IReadOnlyList<ChunkRenderWindow.Slot> source, List<ChunkRenderWindow.Slot> destination)
        {
            // Snapshot count to prevent race condition with concurrent window updates
            int count = source.Count;
            if (count == 0)
                return;

            if (source is List<ChunkRenderWindow.Slot> list)
            {
                // Use try-catch to handle concurrent modification during iteration
                try
                {
                    var span = CollectionsMarshal.AsSpan(list);
                    // Re-check bounds in case list was modified
                    int safeLength = Math.Min(span.Length, count);
                    for (int i = 0; i < safeLength; i++)
                    {
                        destination.Add(span[i]);
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // List was modified during iteration, fall back to safer index-based copy
                    for (int i = 0; i < Math.Min(source.Count, count); i++)
                    {
                        destination.Add(source[i]);
                    }
                }
            }
            else
            {
                // Use snapshot count and bounds checking for non-List implementations
                for (int i = 0; i < count && i < source.Count; i++)
                {
                    destination.Add(source[i]);
                }
            }
        }

        private static void SetChunkVisibility(ChunkMultiMesh chunkData, bool visible)
        {
            if (visible)
            {
                chunkData.HiddenFrames = 0;
                if (!chunkData.IsVisible)
                {
                    chunkData.IsVisible = true;
                    chunkData.Instance.CallDeferred(Node3D.MethodName.SetVisible, true);
                    chunkData.IsDirty = true;
                }
                return;
            }

            if (chunkData.HiddenFrames < FrustumHideGraceFrames)
            {
                chunkData.HiddenFrames++;
                return;
            }

            if (!chunkData.IsVisible)
                return;

            chunkData.IsVisible = false;
            chunkData.Instance.CallDeferred(Node3D.MethodName.SetVisible, false);
            chunkData.MultiMesh.VisibleInstanceCount = 0;
        }

        private static int _visibilityCheckCount = 0;

        private bool IsChunkVisible(ChunkLocation chunkLocation)
        {
            if (!_sharedState.FrustumCullingEnabled || _chunkManager == null)
            {
                if (_visibilityCheckCount < 3)
                {
                    Logging.Log($"[{Name}] IsChunkVisible early return: cullingEnabled={_sharedState.FrustumCullingEnabled}, chunkManager={(_chunkManager != null ? "OK" : "NULL")}");
                    _visibilityCheckCount++;
                }
                return true;
            }

            var planes = _sharedState.FrustumPlanes;
            if (planes == null || planes.Count == 0)
            {
                if (_visibilityCheckCount < 3)
                {
                    Logging.Log($"[{Name}] IsChunkVisible early return: planes={(planes == null ? "NULL" : $"empty ({planes.Count})")}");
                    _visibilityCheckCount++;
                }
                return true;
            }

            var bounds = _chunkManager.ChunkToWorldBounds(chunkLocation);
            bool visible = FrustumUtility.IsVisible(bounds, planes, FrustumPadding);

            if (_visibilityCheckCount < 10)
            {
                Logging.Log($"[{Name}] IsChunkVisible for chunk {chunkLocation}: visible={visible}, planes={planes.Count}, padding={FrustumPadding}");
                _visibilityCheckCount++;
            }

            return visible;
        }

        public override void Update(World world, double delta)
        {
            if (_multiMeshContainer == null || _sphereMesh == null || _chunkSystem == null)
                return;

            _frameCounter++;
            _multimeshCreatesThisFrame = 0;
            _multimeshPoolsThisFrame = 0;

            SyncNearWindow();

            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
                if (chunkData == null)
                    continue;

                chunkData.Count = 0;
                if (chunkData.LastUploadedCount != 0)
                {
                    chunkData.IsDirty = true;
                }
            }

            // Snapshot slots to avoid concurrent modification if window recenters during iteration
            _nearSlotScratch.Clear();
            foreach (var slot in _sharedState.NearWindow.ActiveSlots)
            {
                _nearSlotScratch.Add(slot);
            }

            // NEW: Iterate only chunks in the near window, not all 100k entities!
            foreach (var slot in _nearSlotScratch)
            {
                var chunkEntity = _chunkManager?.GetChunk(slot.GlobalLocation);
                if (chunkEntity == null || chunkEntity == Entity.Invalid)
                    continue;

                bool isCoreChunk = _sharedState.CoreWindow.Contains(slot.GlobalLocation);
                bool chunkVisible = IsChunkVisible(slot.GlobalLocation);

                // Get all entities in this chunk from ChunkSystem
                var entitiesInChunk = _chunkSystem.GetEntitiesInChunk(chunkEntity.Value);

                foreach (var entity in entitiesInChunk)
                {
                    if (!world.TryGetEntityLocation(entity, out var archetype, out var entitySlot))
                        continue;

                    if (!archetype.HasComponent(PositionTypeId))
                        continue;

                    bool isStatic = archetype.HasComponent(StaticRenderTypeId);
                    if (isCoreChunk && !isStatic)
                        continue;

                    var prototype = ResolvePrototype(archetype, entitySlot);
                    var key = new ChunkMeshKey(slot.GlobalLocation, prototype);

                    ref var chunkDataRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_chunkMultiMeshes, key, out bool exists);
                    if (!exists || chunkDataRef == null)
                    {
                        chunkDataRef = AcquireChunkMultiMesh(slot.GlobalLocation, prototype);
                    }

                    var chunkData = chunkDataRef;
                    SetChunkVisibility(chunkData, chunkVisible);
                    if (!chunkVisible)
                        continue;

                    var positions = archetype.GetComponentSpan<Position>(PositionTypeId);
                    var pos = positions[entitySlot];

                    int nextIndex = chunkData.Count;
                    EnsureCapacity(chunkData, nextIndex + 1, slot.GlobalLocation);
                    chunkData.Transforms[nextIndex] = new Transform3D(Basis.Identity, new Vector3(pos.X, pos.Y, pos.Z));
                    chunkData.Count++;
                    chunkData.IsDirty = true;
                }
            }

            // Update all active chunk MultiMeshes
            UpdateChunkMultiMeshes();

            // Debug stats
            _createdSinceReport += _multimeshCreatesThisFrame;
            _pooledSinceReport += _multimeshPoolsThisFrame;

            if (SystemSettings.EnableDebugLogs.Value)
            {
                if (_frameCounter % 60 == 0)
                {
                    int totalEntities = 0;
                    foreach (var chunkData in _chunkMultiMeshes.Values)
                    {
                        if (chunkData == null)
                            continue;

                        totalEntities += chunkData.Count;
                    }
                    Logging.Log($"[{Name}] Active chunk meshes: {_chunkMultiMeshes.Count}, Total entities: {totalEntities}, Pool: {_pooledChunkMeshes.Count}");
                    if (_createdSinceReport > 0 || _pooledSinceReport > 0)
                    {
                        Logging.Log($"[{Name}] MultiMesh window diff: +{_createdSinceReport}, -{_pooledSinceReport}");
                        _createdSinceReport = 0;
                        _pooledSinceReport = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Rent a MultiMesh instance for the specified chunk + prototype.
        /// </summary>
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
            var newName = new StringName($"Chunk_{location.X}_{location.Z}_{location.Y}_{prototype}");
            chunkData.Instance.SetDeferred(Node.PropertyName.Name, newName);
            chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, false);
            chunkData.IsDirty = true;
            chunkData.LastUploadedCount = 0;
            chunkData.IsVisible = false;
        }

        /// <summary>
        /// Create a new MultiMeshInstance3D for pooling.
        /// </summary>
        private ChunkMultiMesh CreateChunkMultiMesh()
        {
            if (_multiMeshContainer == null || _sphereMesh == null)
                throw new InvalidOperationException("MultiMesh container or sphere mesh not initialized");

            int initialCapacity = SystemSettings.InitialChunkCapacity.Value;

            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false,
                Mesh = _sphereMesh,
                InstanceCount = 0 // Will be set during updates
            };

            var instance = new MultiMeshInstance3D
            {
                Name = "ChunkPoolInstance",
                Multimesh = multiMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false
            };
            if (_nearZoneMaterial != null)
            {
                instance.MaterialOverride = _nearZoneMaterial;
            }

            _multiMeshContainer.CallDeferred("add_child", instance);

            _multimeshCreatesThisFrame++;

            return new ChunkMultiMesh
            {
                Instance = instance,
                MultiMesh = multiMesh,
                Transforms = new Transform3D[initialCapacity],
                Capacity = initialCapacity
            };
        }
        private void EnsureCapacity(ChunkMultiMesh chunkData, int required, ChunkLocation location)
        {
            if (required <= chunkData.Capacity)
                return;

            int newCapacity = chunkData.Capacity == 0
                ? Math.Max(SystemSettings.InitialChunkCapacity.Value, required)
                : Math.Max(required, chunkData.Capacity * 2);

            var newBuffer = new Transform3D[newCapacity];
            if (chunkData.Capacity > 0)
            {
                Array.Copy(chunkData.Transforms, newBuffer, chunkData.Capacity);
            }
            chunkData.Transforms = newBuffer;
            chunkData.Capacity = newCapacity;

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Grown chunk {location} transform buffer to {newCapacity}");
            }
        }

        private Mesh? GetPrototypeMesh(RenderPrototypeKind prototype)
        {
            if (prototype == RenderPrototypeKind.Cube)
            {
                return _cubeMesh != null ? (Mesh)_cubeMesh : _sphereMesh;
            }

            return _sphereMesh;
        }

        private StandardMaterial3D? GetPrototypeMaterial(RenderPrototypeKind prototype)
        {
            return prototype switch
            {
                RenderPrototypeKind.Cube => _staticNearMaterial ?? _nearZoneMaterial,
                _ => _nearZoneMaterial
            };
        }

        private void PoolChunkMultiMesh(ChunkMeshKey key)
        {
            if (!_chunkMultiMeshes.Remove(key, out var chunkData))
                return;

            chunkData.MultiMesh.InstanceCount = 0;
            chunkData.MultiMesh.VisibleInstanceCount = 0;
            chunkData.Count = 0;
            chunkData.LastUploadedCount = 0;
            chunkData.IsDirty = false;
            chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, false);
            chunkData.IsVisible = false;
            chunkData.Location = default;
            chunkData.Prototype = RenderPrototypeKind.Sphere;

            _pooledChunkMeshes.Enqueue(chunkData);
            _multimeshPoolsThisFrame++;
        }

        /// <summary>
        /// Update MultiMesh transforms for all active chunks.
        /// </summary>
        private void UpdateChunkMultiMeshes()
        {
            if (_chunkMultiMeshes.Count == 0)
                return;

            _chunkUploadList.Clear();
            foreach (var kvp in _chunkMultiMeshes)
            {
                var chunkData = kvp.Value;
                if (chunkData.IsDirty || chunkData.LastUploadedCount != chunkData.Count)
                {
                    _chunkUploadList.Add(kvp.Key);
                }
            }

            if (_chunkUploadList.Count == 0)
                return;

            int dirtyCount = _chunkUploadList.Count;
            int budget = SystemSettings.MaxChunkUploadsPerFrame.Value;
            if (budget <= 0 || budget >= dirtyCount)
            {
                foreach (var chunkLoc in _chunkUploadList)
                {
                    UploadChunkMultiMesh(_chunkMultiMeshes[chunkLoc]);
                }
                _chunkUploadCursor = 0;
                return;
            }

            for (int i = 0; i < budget; i++)
            {
                int index = (_chunkUploadCursor + i) % dirtyCount;
                var chunkLoc = _chunkUploadList[index];
                if (_chunkMultiMeshes.TryGetValue(chunkLoc, out var chunkData))
                {
                    UploadChunkMultiMesh(chunkData);
                }
            }

            _chunkUploadCursor = (_chunkUploadCursor + budget) % dirtyCount;
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

        private void SyncNearWindow()
        {
            if (_sharedState.NearVersion == _nearWindowVersionProcessed)
                return;

            _nearWindowVersionProcessed = _sharedState.NearVersion;
            var window = _sharedState.NearWindow;

            _nearExitedScratch.Clear();
            CopySlotList(window.Exited, _nearExitedScratch);

            foreach (var slot in _nearExitedScratch)
            {
                PoolChunkMultiMeshes(slot.GlobalLocation);
            }
        }

        private void UploadChunkMultiMesh(ChunkMultiMesh chunkData)
        {
            int count = chunkData.Count;
            if (count == 0)
            {
                if (chunkData.LastUploadedCount != 0)
                {
                    chunkData.MultiMesh.InstanceCount = 0;
                    chunkData.MultiMesh.VisibleInstanceCount = 0;
                    chunkData.LastUploadedCount = 0;
                    if (chunkData.IsVisible)
                    {
                        chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, false);
                        chunkData.IsVisible = false;
                    }
                }

                chunkData.IsDirty = false;
                return;
            }

            var transformSpan = chunkData.Transforms.AsSpan(0, count);
            var bufferSpan = BuildTransformBuffer(transformSpan);

            int expectedFloats = count * FloatsPerInstance;
            System.Diagnostics.Debug.Assert(bufferSpan.Length == expectedFloats, $"Invalid MultiMesh buffer size. Expected {expectedFloats}, got {bufferSpan.Length}");

            chunkData.MultiMesh.InstanceCount = count;
            chunkData.MultiMesh.VisibleInstanceCount = count;
            RenderingServer.MultimeshSetBuffer(chunkData.MultiMesh.GetRid(), bufferSpan);

            if (!chunkData.IsVisible)
            {
                chunkData.Instance.SetDeferred(Node3D.PropertyName.Visible, true);
                chunkData.IsVisible = true;
            }

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
                {
                    newSize *= 2;
                }
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

        public override void OnShutdown(World world)
        {
            // Clean up all MultiMeshes
            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
                if (chunkData == null)
                    continue;

                if (chunkData.Instance.IsInsideTree())
                {
                    chunkData.Instance.QueueFree();
                }
            }
            _chunkMultiMeshes.Clear();

            // Remove container
            if (_multiMeshContainer != null && _multiMeshContainer.IsInsideTree())
            {
                _multiMeshContainer.QueueFree();
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete");
            }
        }
    }
}


