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
using Client.ECS.Components;

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

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _bubbleContainer;
        private SphereMesh? _sphereMesh;

        private readonly Dictionary<uint, EntityVisualBinding> _entityBindings = new();
        private readonly Dictionary<ChunkLocation, ChunkVisualPool<MeshInstance3D>> _chunkPools = new();
        private readonly List<uint> _releaseBuffer = new();
        private readonly HashSet<uint> _coreEntitiesLastFrame = new();
        private readonly HashSet<uint> _coreEntitiesThisFrame = new();

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderZoneTypeId = ComponentManager.GetTypeId<RenderZone>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();
        private static readonly Vector3 HiddenPosition = new(0, -10000, 0);

        private int _totalAllocatedMeshInstances = 0;

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

            Logging.Log($"[{Name}] Initialized - Mesh: {SystemSettings.RadialSegments.Value}x{SystemSettings.Rings.Value} sphere, Radius: {SystemSettings.EntityRadius.Value}");
        }

        public override void Update(World world, double delta)
        {
            if (_bubbleContainer == null || _sphereMesh == null)
                return;

            _frameCounter++;
            _coreEntitiesThisFrame.Clear();

            var archetypes = world.QueryArchetypes(typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0)
                    continue;

                var positions = arch.GetComponentSpan<Position>(PositionTypeId);
                var renderZones = arch.GetComponentSpan<RenderZone>(RenderZoneTypeId);
                var chunkOwners = arch.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                var entities = arch.GetEntityArray();

                for (int i = 0; i < entities.Length; i++)
                {
                    if (renderZones[i].Zone != RenderZoneType.Core)
                        continue;

                    var owner = chunkOwners[i];
                    if (!owner.IsAssigned)
                        continue;

                    var entity = entities[i];
                    uint entityIndex = entity.Index;
                    _coreEntitiesThisFrame.Add(entityIndex);

                    UpdateOrAttachVisual(entityIndex, owner.Location, positions[i]);
                }
            }

            RemoveOutsideCoreZone();

            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                Logging.Log($"[{Name}] Active MeshInstances: {_entityBindings.Count}, Pools: {_chunkPools.Count}");
            }
        }

        private void UpdateOrAttachVisual(uint entityIndex, ChunkLocation chunkLocation, Position position)
        {
            if (_bubbleContainer == null)
                return;

            if (_entityBindings.TryGetValue(entityIndex, out var binding))
            {
                if (binding.Chunk == chunkLocation)
                {
                    UpdateVisualPosition(binding.Visual, position);
                    return;
                }

                ReleaseEntityVisual(entityIndex);
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

            _entityBindings[entityIndex] = new EntityVisualBinding(chunkLocation, visual);
            UpdateVisualPosition(visual, position);
        }

        private ChunkVisualPool<MeshInstance3D> GetOrCreateChunkPool(ChunkLocation chunkLocation)
        {
            if (_bubbleContainer == null)
                throw new InvalidOperationException("Bubble container not initialized");

            if (_chunkPools.TryGetValue(chunkLocation, out var existing))
                return existing;

            var chunkContainer = new Node3D
            {
                Name = $"CoreChunk_{chunkLocation.X}_{chunkLocation.Z}_{chunkLocation.Y}"
            };
            _bubbleContainer.CallDeferred("add_child", chunkContainer);

            MeshInstance3D? Factory(ChunkLocation _) => TryCreateMeshInstance(chunkContainer);
            void OnAcquire(MeshInstance3D visual) => ActivateVisual(visual);
            void OnRelease(MeshInstance3D visual) => DeactivateVisual(visual);

            var pool = new ChunkVisualPool<MeshInstance3D>(chunkLocation, Factory, OnAcquire, OnRelease);
            _chunkPools[chunkLocation] = pool;
            return pool;
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

            parent.CallDeferred("add_child", meshInstance);
            meshInstance.GlobalPosition = HiddenPosition;

            _totalAllocatedMeshInstances++;
            if (_totalAllocatedMeshInstances < SystemSettings.MaxMeshInstances.Value)
            {
                _loggedMaxWarning = false;
            }

            return meshInstance;
        }

        private static void ActivateVisual(MeshInstance3D visual)
        {
            if (!visual.Visible)
            {
                visual.Visible = true;
            }
        }

        private static void DeactivateVisual(MeshInstance3D visual)
        {
            if (!visual.IsInsideTree())
                return;

            visual.Visible = false;
            visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, HiddenPosition);
        }

        private static void UpdateVisualPosition(MeshInstance3D visual, Position position)
        {
            if (!visual.IsInsideTree())
                return;

            visual.CallDeferred(Node3D.MethodName.SetGlobalPosition, new Vector3(position.X, position.Y, position.Z));
        }

        private void ReleaseEntityVisual(uint entityIndex)
        {
            if (!_entityBindings.TryGetValue(entityIndex, out var binding))
                return;

            if (_chunkPools.TryGetValue(binding.Chunk, out var pool))
            {
                pool.Release(binding.Visual);
            }

            _entityBindings.Remove(entityIndex);
        }

        /// <summary>
        /// Return MeshInstances for entities that left the core zone.
        /// </summary>
        private void RemoveOutsideCoreZone()
        {
            _releaseBuffer.Clear();

            foreach (var entityIndex in _coreEntitiesLastFrame)
            {
                if (!_coreEntitiesThisFrame.Contains(entityIndex))
                {
                    _releaseBuffer.Add(entityIndex);
                }
            }

            foreach (var entityIndex in _releaseBuffer)
            {
                ReleaseEntityVisual(entityIndex);

                if (SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Returned MeshInstance for entity {entityIndex} to pool");
                }
            }

            _coreEntitiesLastFrame.Clear();
            foreach (var entityIndex in _coreEntitiesThisFrame)
            {
                _coreEntitiesLastFrame.Add(entityIndex);
            }

            if (_entityBindings.Count < SystemSettings.MaxMeshInstances.Value)
            {
                _loggedMaxWarning = false;
            }
        }

        public override void OnShutdown(World world)
        {
            _entityBindings.Clear();
            _chunkPools.Clear();
            _coreEntitiesLastFrame.Clear();
            _coreEntitiesThisFrame.Clear();
            _releaseBuffer.Clear();

            if (_bubbleContainer != null && _bubbleContainer.IsInsideTree())
            {
                _bubbleContainer.QueueFree();
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete - recycled {_totalAllocatedMeshInstances} MeshInstances");
            }
        }

        private readonly struct EntityVisualBinding
        {
            public ChunkLocation Chunk { get; }
            public MeshInstance3D Visual { get; }

            public EntityVisualBinding(ChunkLocation chunk, MeshInstance3D visual)
            {
                Chunk = chunk;
                Visual = visual;
            }
        }
    }
}


