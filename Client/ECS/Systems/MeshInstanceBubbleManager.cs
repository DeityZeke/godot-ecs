#nullable enable

using System;
using System.Collections.Generic;
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
using Client.ECS.Rendering;
using System.Runtime.InteropServices;

namespace Client.ECS.Systems
{
    /// <summary>
    /// Manages individual MeshInstance3D nodes for chunks in the Core rendering zone.
    /// Creates one MeshInstance3D per entity in core chunks for full interactivity.
    /// Uses simple sphere mesh matching AdaptiveMultiMeshRenderSystem for consistency.
    /// </summary>
    public sealed class MeshInstanceBubbleManager : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting EntityRadius { get; private set; }
            public IntSetting RadialSegments { get; private set; }
            public IntSetting Rings { get; private set; }
            public IntSetting InitialChunkPoolCapacity { get; private set; }
            public BoolSetting CastShadows { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }
            public IntSetting MaxMeshInstances { get; private set; }

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

                InitialChunkPoolCapacity = RegisterInt("Initial Chunk Pool Capacity", 256,
                    min: 32, max: 4096, step: 32,
                    tooltip: "Initial reserve per chunk pool (stack doubles automatically as needed)");

                CastShadows = RegisterBool("Cast Shadows", false,
                    tooltip: "Enable shadow casting for core zone entities");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log node creation/destruction operations");

                MaxMeshInstances = RegisterInt("Max MeshInstances", 25000,
                    min: 1000, max: 200000, step: 1000,
                    tooltip: "Hard cap to prevent freezing when too many MeshInstances would be created");

            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "MeshInstance Bubble Manager";
        public override int SystemId => typeof(MeshInstanceBubbleManager).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone), typeof(StaticRenderTag), typeof(RenderPrototype) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _bubbleContainer;
        private SphereMesh? _sphereMesh;
        private BoxMesh? _cubeMesh;
        private StandardMaterial3D? _bubbleMaterial;
        private ChunkManager? _chunkManager;
        private ChunkSystem? _chunkSystem;

        private EntityVisualBinding[] _entityBindings = Array.Empty<EntityVisualBinding>();
        private readonly DynamicBitSet _bindingMask = new();
        private sealed class ChunkPoolEntry
        {
            public ChunkVisualPool<MeshInstance3D> Pool = null!;
            public Node3D Container = null!;
            public bool IsVisible;
            public int HiddenFrames;
        }

        private readonly Dictionary<ChunkLocation, ChunkPoolEntry> _chunkPools = new();
        private readonly Queue<ChunkPoolEntry> _pooledChunkEntries = new();
        private readonly List<ChunkLocation> _chunkRemovalQueue = new();
        private readonly List<ChunkLocation> _chunkRemovalScratch = new();
        private readonly DynamicBitSet _coreEntitiesCurrent = new();
        private readonly DynamicBitSet _coreEntitiesLast = new();
        private readonly List<int> _bitsetScratch = new();
        private readonly List<ChunkRenderWindow.Slot> _coreSlotScratch = new();
        private readonly List<ChunkRenderWindow.Slot> _coreEnteredScratch = new();
        private readonly List<ChunkRenderWindow.Slot> _coreExitedScratch = new();
        private readonly HybridRenderSharedState _sharedState = HybridRenderSharedState.Instance;
        private const float FrustumPadding = 48f;
        private const int FrustumHideGraceFrames = 5;
        private ulong _coreWindowVersionProcessed = 0;
        private int _attachmentsThisFrame = 0;
        private int _releasesThisFrame = 0;
        private int _attachedSinceReport = 0;
        private int _releasedSinceReport = 0;

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderZoneTypeId = ComponentManager.GetTypeId<RenderZone>();
        private static readonly int RenderPrototypeTypeId = ComponentManager.GetTypeId<RenderPrototype>();
        private static readonly int StaticRenderTypeId = ComponentManager.GetTypeId<StaticRenderTag>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();
        private static readonly Vector3 HiddenPosition = new(0, -10000, 0);

        private int _totalAllocatedMeshInstances = 0;
        private int _activeBindingCount = 0;
        private int _lastFrameCoreCount = 0;

        public int ActiveCoreEntities => _lastFrameCoreCount;

        private int _frameCounter = 0;
        private bool _loggedMaxWarning = false;

        public override void OnInitialize(World world)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            _rootNode = tree?.CurrentScene as Node3D;

            if (_rootNode == null)
            {
                Logging.Log($"[{Name}] ERROR: No scene root found!", LogSeverity.Error);
                return;
            }

            // Create container node for all bubble MeshInstances
            _bubbleContainer = new Node3D
            {
                Name = "ECS_CoreBubble"
            };
            _rootNode.CallDeferred("add_child", _bubbleContainer);

            // Create shared sphere mesh (same as AdaptiveMultiMeshRenderSystem)
            _sphereMesh = new SphereMesh
            {
                RadialSegments = SystemSettings.RadialSegments.Value,
                Rings = SystemSettings.Rings.Value,
                Radius = SystemSettings.EntityRadius.Value,
                Height = SystemSettings.EntityRadius.Value * 2.0f
            };
            _bubbleMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.35f, 0.65f, 1.0f),
                Metallic = 0.0f,
                Roughness = 0.7f
            };
            _sphereMesh.SurfaceSetMaterial(0, _bubbleMaterial);

            _cubeMesh = new BoxMesh
            {
                Size = Vector3.One * SystemSettings.EntityRadius.Value * 2.0f
            };
            _cubeMesh.SurfaceSetMaterial(0, _bubbleMaterial);

            Logging.Log($"[{Name}] Initialized - Mesh: {SystemSettings.RadialSegments.Value}x{SystemSettings.Rings.Value} sphere, Radius: {SystemSettings.EntityRadius.Value}, Chunk pool reserve: {SystemSettings.InitialChunkPoolCapacity.Value}");
        }

        /// <summary>
        /// Set ChunkManager and ChunkSystem references for chunk-based iteration.
        /// </summary>
        public void SetChunkManager(ChunkManager chunkManager, ChunkSystem chunkSystem)
        {
            _chunkManager = chunkManager;
            _chunkSystem = chunkSystem;
            ResetChunkWindowState();
        }

        private void ResetChunkWindowState()
        {
            _coreWindowVersionProcessed = 0;
            _chunkRemovalQueue.Clear();
            PoolAllChunkEntries();
        }

        public override void Update(World world, double delta)
        {
            if (_bubbleContainer == null || _sphereMesh == null || _chunkSystem == null)
                return;

            SyncCoreWindow();

            _frameCounter++;
            _coreEntitiesCurrent.ClearAll();
            _attachmentsThisFrame = 0;
            _releasesThisFrame = 0;

            // Snapshot slots to avoid concurrent modification if window recenters during iteration
            _coreSlotScratch.Clear();
            foreach (var slot in _sharedState.CoreWindow.ActiveSlots)
            {
                _coreSlotScratch.Add(slot);
            }

            // NEW: Iterate only chunks in the core window (27 chunks for 3x3x3, not 100k entities!)
            foreach (var slot in _coreSlotScratch)
            {
                var chunkEntity = _chunkManager?.GetChunk(slot.GlobalLocation);
                if (chunkEntity == null || chunkEntity == Entity.Invalid)
                    continue;

                // Always keep bubble chunk containers visible; MultiMesh handles culling
                _ = GetOrCreateChunkEntry(slot.GlobalLocation);

                // Get all entities in this chunk from ChunkSystem
                var entitiesInChunk = _chunkSystem.GetEntitiesInChunk(chunkEntity.Value);

                foreach (var entity in entitiesInChunk)
                {
                    // Get entity location and check for Position component
                    if (!world.TryGetEntityLocation(entity, out var archetype, out var entitySlot))
                        continue;

                    // Skip entities without required components
                    if (!archetype.HasComponent(PositionTypeId))
                        continue;

                    // Static visuals stay batched inside MultiMesh even in the bubble
                    if (archetype.HasComponent(StaticRenderTypeId))
                        continue;

                    var positions = archetype.GetComponentSpan<Position>(PositionTypeId);
                    var position = positions[entitySlot];

                    var prototype = ResolvePrototype(archetype, entitySlot);

                    uint entityIndex = entity.Index;
                    _coreEntitiesCurrent.Set((int)entityIndex);

                    UpdateOrAttachVisual(entityIndex, slot.GlobalLocation, position, prototype);
                }
            }

            RemoveOutsideCoreZone();
            ProcessPendingChunkRemovals();

            _attachedSinceReport += _attachmentsThisFrame;
            _releasedSinceReport += _releasesThisFrame;

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                Logging.Log($"[{Name}] Active MeshInstances: {_activeBindingCount}, Pools: {_chunkPools.Count}");
                if (_attachedSinceReport > 0 || _releasedSinceReport > 0)
                {
                    Logging.Log($"[{Name}] MeshInstance diff: +{_attachedSinceReport}, -{_releasedSinceReport}");
                    _attachedSinceReport = 0;
                    _releasedSinceReport = 0;
                }
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

        private void UpdateOrAttachVisual(uint entityIndex, ChunkLocation chunkLocation, Position position, RenderPrototypeKind prototype)
        {
            if (_bubbleContainer == null)
                return;

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
                    return;
                }

                ReleaseEntityVisual(entityIndex);
                hasBinding = false;
            }

            var pool = GetOrCreateChunkPool(chunkLocation);
            var visual = pool.Acquire();

            if (visual == null)
            {
                if (!_loggedMaxWarning)
                {
                    Logging.Log($"[{Name}] MeshInstance limit of {SystemSettings.MaxMeshInstances.Value:N0} reached. Additional core entities will render via MultiMesh.", LogSeverity.Warning);
                    _loggedMaxWarning = true;
                }
                return;
            }

            ApplyPrototypeMesh(visual, prototype);
            _entityBindings[entityIndex] = new EntityVisualBinding(chunkLocation, visual, prototype);
            if (_bindingMask.Set((int)entityIndex))
            {
                _activeBindingCount++;
                _attachmentsThisFrame++;
            }

            UpdateVisualPosition(visual, position);
        }

        private ChunkVisualPool<MeshInstance3D> GetOrCreateChunkPool(ChunkLocation chunkLocation)
        {
            if (_chunkPools.TryGetValue(chunkLocation, out var existing))
                return existing.Pool;

            var entry = GetOrCreateChunkEntry(chunkLocation);
            return entry.Pool;
        }

        private ChunkPoolEntry GetOrCreateChunkEntry(ChunkLocation chunkLocation)
        {
            if (_chunkPools.TryGetValue(chunkLocation, out var existing))
                return existing;

            var entry = AcquireChunkPoolEntry(chunkLocation);
            _chunkPools[chunkLocation] = entry;
            return entry;
        }

        private ChunkPoolEntry AcquireChunkPoolEntry(ChunkLocation chunkLocation)
        {
            ChunkPoolEntry entry;
            if (_pooledChunkEntries.Count > 0)
            {
                entry = _pooledChunkEntries.Dequeue();
            }
            else
            {
                entry = CreateChunkPoolEntry();
            }

            PrepareChunkPoolEntry(entry, chunkLocation);
            return entry;
        }

        private ChunkPoolEntry CreateChunkPoolEntry()
        {
            if (_bubbleContainer == null)
                throw new InvalidOperationException("Bubble container not initialized");

            var chunkContainer = new Node3D
            {
                Name = "CoreChunkPool",
                Visible = false
            };
            _bubbleContainer.CallDeferred("add_child", chunkContainer);

            MeshInstance3D? Factory(ChunkLocation _) => TryCreateMeshInstance(chunkContainer);
            void OnAcquire(MeshInstance3D visual) => ActivateVisual(visual);
            void OnRelease(MeshInstance3D visual) => DeactivateVisual(visual);

            var pool = new ChunkVisualPool<MeshInstance3D>(
                default,
                Factory,
                OnAcquire,
                OnRelease,
                SystemSettings.InitialChunkPoolCapacity.Value);

            return new ChunkPoolEntry
            {
                Pool = pool,
                Container = chunkContainer
            };
        }

        private void PrepareChunkPoolEntry(ChunkPoolEntry entry, ChunkLocation chunkLocation)
        {
            var newName = new StringName($"CoreChunk_{chunkLocation.X}_{chunkLocation.Z}_{chunkLocation.Y}");
            entry.Container.SetDeferred(Node.PropertyName.Name, newName);
            entry.Container.SetDeferred(Node3D.PropertyName.Visible, true);
            entry.IsVisible = true;
            entry.HiddenFrames = 0;
            entry.Pool.UpdateLocation(chunkLocation);
            if (entry.Pool.ActiveCount != 0)
            {
                Logging.Log($"[{Name}] WARNING: Reusing chunk pool with {entry.Pool.ActiveCount} active visuals. Forcing release.", LogSeverity.Warning);
            }
        }

        private void PoolChunkEntry(ChunkLocation chunkLocation)
        {
            if (!_chunkPools.TryGetValue(chunkLocation, out var entry))
                return;

            if (entry.Pool.ActiveCount != 0)
            {
                ReleaseAllVisualsForChunk(chunkLocation);
            }

            if (!_chunkPools.Remove(chunkLocation))
                return;

            entry.Container.SetDeferred(Node3D.PropertyName.Visible, false);
            entry.IsVisible = false;
            entry.HiddenFrames = 0;
            _pooledChunkEntries.Enqueue(entry);
        }

        private void EnsureBindingCapacity(uint entityIndex)
        {
            if (entityIndex < _entityBindings.Length)
                return;

            int newSize = _entityBindings.Length == 0 ? 1024 : _entityBindings.Length;
            while (newSize <= entityIndex)
            {
                newSize *= 2;
            }

            Array.Resize(ref _entityBindings, newSize);
        }

        private MeshInstance3D? TryCreateMeshInstance(Node3D parent)
        {
            if (_sphereMesh == null)
                return null;

            if (_totalAllocatedMeshInstances >= SystemSettings.MaxMeshInstances.Value)
                return null;

            var meshInstance = new MeshInstance3D
            {
                Name = $"CoreEntity_{_totalAllocatedMeshInstances}",
                Mesh = _sphereMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false
            };
            if (_bubbleMaterial != null)
            {
                meshInstance.MaterialOverride = _bubbleMaterial;
            }

            parent.CallDeferred("add_child", meshInstance);
            meshInstance.Position = HiddenPosition;

            _totalAllocatedMeshInstances++;
            if (_totalAllocatedMeshInstances < SystemSettings.MaxMeshInstances.Value)
            {
                _loggedMaxWarning = false;
            }

            return meshInstance;
        }

        private static void ActivateVisual(MeshInstance3D visual)
        {
            visual.SetDeferred(Node3D.PropertyName.Visible, true);
        }

        private static void DeactivateVisual(MeshInstance3D visual)
        {
            if (!visual.IsInsideTree())
                return;

            visual.SetDeferred(Node3D.PropertyName.Visible, false);
            visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, HiddenPosition);
        }

        private void ApplyPrototypeMesh(MeshInstance3D visual, RenderPrototypeKind prototype)
        {
            var mesh = GetPrototypeMesh(prototype);
            if (mesh != null && visual.Mesh != mesh)
            {
                visual.Mesh = mesh;
            }
        }

        private PrimitiveMesh? GetPrototypeMesh(RenderPrototypeKind prototype)
        {
            if (prototype == RenderPrototypeKind.Cube)
            {
                return _cubeMesh != null ? _cubeMesh : _sphereMesh;
            }

            return _sphereMesh;
        }

        private static void UpdateVisualPosition(MeshInstance3D visual, Position position)
        {
            if (!visual.IsInsideTree())
                return;

            visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, new Vector3(position.X, position.Y, position.Z));
        }

        private void ReleaseEntityVisual(uint entityIndex)
        {
            if (entityIndex >= _entityBindings.Length)
                return;

            if (!_bindingMask.Clear((int)entityIndex))
                return;

            var binding = _entityBindings[entityIndex];
            if (_chunkPools.TryGetValue(binding.Chunk, out var entry))
            {
                entry.Pool.Release(binding.Visual);
            }
            else
            {
                DeactivateVisual(binding.Visual);
            }

            _activeBindingCount = Math.Max(0, _activeBindingCount - 1);
            _releasesThisFrame++;
            _entityBindings[entityIndex] = default;
        }

        private void ReleaseAllVisualsForChunk(ChunkLocation chunkLocation)
        {
            for (int i = 0; i < _entityBindings.Length; i++)
            {
                if (!_bindingMask.Contains(i))
                    continue;

                if (_entityBindings[i].Chunk == chunkLocation)
                {
                    ReleaseEntityVisual((uint)i);
                }
            }
        }

        private void SyncCoreWindow()
        {
            if (_sharedState.CoreVersion == _coreWindowVersionProcessed)
                return;

            _coreWindowVersionProcessed = _sharedState.CoreVersion;
            var window = _sharedState.CoreWindow;

            _coreExitedScratch.Clear();
            _coreEnteredScratch.Clear();

            CopySlotList(window.Exited, _coreExitedScratch);
            CopySlotList(window.Entered, _coreEnteredScratch);

            foreach (var slot in _coreExitedScratch)
            {
                QueueChunkForRemoval(slot.GlobalLocation);
            }

            foreach (var slot in _coreEnteredScratch)
            {
                EnsureChunkPoolExists(slot.GlobalLocation);
            }
        }

        private void EnsureChunkPoolExists(ChunkLocation chunkLocation)
        {
            if (_chunkPools.ContainsKey(chunkLocation))
                return;

            var entry = AcquireChunkPoolEntry(chunkLocation);
            _chunkPools[chunkLocation] = entry;
        }

        private void QueueChunkForRemoval(ChunkLocation chunkLocation)
        {
            if (_chunkRemovalQueue.Contains(chunkLocation))
                return;
            _chunkRemovalQueue.Add(chunkLocation);
        }

        private void ProcessPendingChunkRemovals()
        {
            if (_chunkRemovalQueue.Count == 0)
                return;

            for (int i = _chunkRemovalQueue.Count - 1; i >= 0; i--)
            {
                var chunk = _chunkRemovalQueue[i];
                if (_sharedState.CoreWindow.Contains(chunk))
                {
                    _chunkRemovalQueue.RemoveAt(i);
                    continue;
                }

                if (!_chunkPools.TryGetValue(chunk, out var entry))
                {
                    _chunkRemovalQueue.RemoveAt(i);
                    continue;
                }

                if (entry.Pool.ActiveCount == 0)
                {
                    PoolChunkEntry(chunk);
                    _chunkRemovalQueue.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Return MeshInstance3D nodes for entities that left the core zone.
        /// </summary>
        private void RemoveOutsideCoreZone()
        {
            _coreEntitiesLast.CopySetBitsTo(_bitsetScratch);

            foreach (var entityIndex in _bitsetScratch)
            {
                if (!_coreEntitiesCurrent.Contains(entityIndex))
                {
                    ReleaseEntityVisual((uint)entityIndex);

                    if (SystemSettings.EnableDebugLogs.Value)
                    {
                        Logging.Log($"[{Name}] Returned MeshInstance for entity {entityIndex} to pool");
                    }
                }
            }

            _coreEntitiesLast.SwapWith(_coreEntitiesCurrent);
            _coreEntitiesCurrent.ClearAll();
            _lastFrameCoreCount = _coreEntitiesLast.Count;

            if (_activeBindingCount < SystemSettings.MaxMeshInstances.Value)
            {
                _loggedMaxWarning = false;
            }
        }

        public override void OnShutdown(World world)
        {
            _bindingMask.ClearAll();
            _entityBindings = Array.Empty<EntityVisualBinding>();
            _activeBindingCount = 0;
            foreach (var entry in _chunkPools.Values)
            {
                if (entry.Container.IsInsideTree())
                {
                    entry.Container.QueueFree();
                }
            }
            PoolAllChunkEntries();
            _chunkPools.Clear();

            while (_pooledChunkEntries.Count > 0)
            {
                var pooled = _pooledChunkEntries.Dequeue();
                if (pooled.Container.IsInsideTree())
                {
                    pooled.Container.QueueFree();
                }
            }

            _coreEntitiesLast.ClearAll();
            _coreEntitiesCurrent.ClearAll();
            _bitsetScratch.Clear();
            _lastFrameCoreCount = 0;

            if (_bubbleContainer != null && _bubbleContainer.IsInsideTree())
            {
                _bubbleContainer.QueueFree();
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete - recycled {_totalAllocatedMeshInstances} MeshInstances");
            }
        }

        private void PoolAllChunkEntries()
        {
            _chunkRemovalScratch.Clear();
            foreach (var chunk in _chunkPools.Keys)
            {
                _chunkRemovalScratch.Add(chunk);
            }

            foreach (var chunk in _chunkRemovalScratch)
            {
                PoolChunkEntry(chunk);
            }

            _chunkRemovalScratch.Clear();
        }

        private bool ShouldChunkBeVisible(ChunkLocation chunkLocation)
        {
            if (!_sharedState.FrustumCullingEnabled || _chunkManager == null)
                return true;

            var planes = _sharedState.FrustumPlanes;
            if (planes == null || planes.Count == 0)
                return true;

            var bounds = _chunkManager.ChunkToWorldBounds(chunkLocation);
            return FrustumUtility.IsVisible(bounds, planes, FrustumPadding);
        }

        private static void SetChunkEntryVisibility(ChunkPoolEntry entry, bool visible)
        {
            if (visible)
            {
                entry.HiddenFrames = 0;
                if (!entry.IsVisible)
                {
                    entry.IsVisible = true;
                    entry.Container.CallDeferred(Node3D.MethodName.SetVisible, true);
                }
                return;
            }

            if (entry.HiddenFrames < FrustumHideGraceFrames)
            {
                entry.HiddenFrames++;
                return;
            }

            if (!entry.IsVisible)
                return;

            entry.IsVisible = false;
            entry.Container.CallDeferred(Node3D.MethodName.SetVisible, false);
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
