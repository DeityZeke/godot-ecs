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

        // Track entity -> MeshInstance3D mapping
        private readonly Dictionary<uint, MeshInstance3D> _entityNodes = new();

        // Track which entities were in core zone last frame (for cleanup)
        private readonly HashSet<uint> _coreEntitiesLastFrame = new();
        private readonly HashSet<uint> _coreEntitiesThisFrame = new();

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int RenderZoneTypeId = ComponentManager.GetTypeId<RenderZone>();

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

            // Swap frame tracking sets
            _coreEntitiesThisFrame.Clear();

            // Query entities with Position + RenderTag + Visible + RenderZone
            var archetypes = world.QueryArchetypes(typeof(Position), typeof(RenderTag), typeof(Visible), typeof(RenderZone));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                var positions = arch.GetComponentSpan<Position>(PositionTypeId);
                var renderZones = arch.GetComponentSpan<RenderZone>(RenderZoneTypeId);
                var entities = arch.GetEntityArray();

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var zone = renderZones[i];

                    // Only process entities in Core zone
                    if (zone.Zone != RenderZoneType.Core)
                        continue;

                    var pos = positions[i];
                    uint entityIndex = entity.Index;
                    _coreEntitiesThisFrame.Add(entityIndex);

                    // Create or update MeshInstance3D
                    if (!_entityNodes.TryGetValue(entityIndex, out var meshInstance))
                    {
                        // Create new MeshInstance3D for this entity
                        meshInstance = CreateMeshInstance(entityIndex);
                        _entityNodes[entityIndex] = meshInstance;

                        if (SystemSettings.EnableDebugLogs.Value)
                        {
                            Logging.Log($"[{Name}] Created MeshInstance for entity {entity.Index} at {pos}");
                        }
                    }

                    // Update position
                    if (meshInstance.IsInsideTree())
                    {
                        meshInstance.GlobalPosition = new Vector3(pos.X, pos.Y, pos.Z);
                    }
                }
            }

            // Remove entities that left the core zone
            RemoveOutsideCoreZone();

            // Debug stats every 60 frames
            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                Logging.Log($"[{Name}] Active MeshInstances: {_entityNodes.Count}");
            }
        }

        /// <summary>
        /// Create a new MeshInstance3D for an entity.
        /// </summary>
        private MeshInstance3D CreateMeshInstance(uint entityIndex)
        {
            if (_bubbleContainer == null || _sphereMesh == null)
                throw new InvalidOperationException("Bubble container or sphere mesh not initialized");

            var meshInstance = new MeshInstance3D
            {
                Name = $"Entity_{entityIndex}",
                Mesh = _sphereMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled
            };

            _bubbleContainer.CallDeferred("add_child", meshInstance);
            return meshInstance;
        }

        /// <summary>
        /// Remove MeshInstance3D nodes for entities that left the core zone.
        /// </summary>
        private void RemoveOutsideCoreZone()
        {
            if (_bubbleContainer == null)
                return;

            // Find entities that were in core last frame but not this frame
            var toRemove = new List<uint>();
            foreach (var entityIndex in _coreEntitiesLastFrame)
            {
                if (!_coreEntitiesThisFrame.Contains(entityIndex))
                {
                    toRemove.Add(entityIndex);
                }
            }

            // Remove MeshInstances for entities outside core zone
            foreach (var entityIndex in toRemove)
            {
                if (_entityNodes.TryGetValue(entityIndex, out var meshInstance))
                {
                    if (meshInstance.IsInsideTree())
                    {
                        meshInstance.QueueFree();
                    }
                    _entityNodes.Remove(entityIndex);

                    if (SystemSettings.EnableDebugLogs.Value)
                    {
                        Logging.Log($"[{Name}] Removed MeshInstance for entity {entityIndex}");
                    }
                }
            }

            // Update tracking set for next frame
            _coreEntitiesLastFrame.Clear();
            foreach (var entityIndex in _coreEntitiesThisFrame)
            {
                _coreEntitiesLastFrame.Add(entityIndex);
            }
        }

        public override void OnShutdown(World world)
        {
            // Clean up all MeshInstances
            foreach (var meshInstance in _entityNodes.Values)
            {
                if (meshInstance.IsInsideTree())
                {
                    meshInstance.QueueFree();
                }
            }
            _entityNodes.Clear();

            // Remove bubble container
            if (_bubbleContainer != null && _bubbleContainer.IsInsideTree())
            {
                _bubbleContainer.QueueFree();
            }

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutdown complete - cleaned up {_entityNodes.Count} MeshInstances");
            }
        }
    }
}


