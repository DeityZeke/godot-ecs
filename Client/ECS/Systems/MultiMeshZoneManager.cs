#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim.ECS;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using UltraSim;
using Client.ECS.Components;

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

                InitialChunkCapacity = RegisterInt("Initial Chunk Capacity", 1000,
                    min: 100, max: 100000, step: 100,
                    tooltip: "Initial MultiMesh capacity per chunk (auto-grows if needed)");

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

        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _rootNode;
        private Node3D? _multiMeshContainer;
        private SphereMesh? _sphereMesh;
        private ChunkManager? _chunkManager;

        // Track chunk -> MultiMeshInstance3D + data
        private class ChunkMultiMesh
        {
            public MultiMeshInstance3D Instance = null!;
            public MultiMesh MultiMesh = null!;
            public Transform3D[] Transforms = Array.Empty<Transform3D>(); // Pre-allocated transform buffer
            public int Capacity = 0;
            public int Count = 0;
        }

        private readonly Dictionary<ChunkLocation, ChunkMultiMesh> _chunkMultiMeshes = new();
        private readonly HashSet<ChunkLocation> _activeChunksLastFrame = new();
        private readonly HashSet<ChunkLocation> _activeChunksThisFrame = new();

        private static readonly int PositionTypeId = ComponentManager.GetTypeId<Position>();
        private static readonly int ChunkOwnerTypeId = ComponentManager.GetTypeId<ChunkOwner>();
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

            Logging.Log($"[{Name}] Initialized - Mesh: {SystemSettings.RadialSegments.Value}x{SystemSettings.Rings.Value} sphere, Initial capacity: {SystemSettings.InitialChunkCapacity.Value}/chunk");
        }

        /// <summary>
        /// Set the ChunkManager reference (called by WorldECS after ChunkSystem initialization).
        /// </summary>
        public void SetChunkManager(ChunkManager chunkManager)
        {
            _chunkManager = chunkManager;
            Logging.Log($"[{Name}] ChunkManager reference set");
        }

        public override void Update(World world, double delta)
        {
            if (_multiMeshContainer == null || _sphereMesh == null)
                return;

            _frameCounter++;

            // Clear per-frame tracking
            _activeChunksThisFrame.Clear();
            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
                chunkData.Count = 0;
            }

            // Query entities with Position + RenderTag + Visible + ChunkOwner + RenderZone
            var archetypes = world.QueryArchetypes(typeof(Position), typeof(RenderTag), typeof(Visible), typeof(ChunkOwner), typeof(RenderZone));

            foreach (var arch in archetypes)
            {
                if (arch.Count == 0) continue;

                var positions = arch.GetComponentSpan<Position>(PositionTypeId);
                var chunkOwners = arch.GetComponentSpan<ChunkOwner>(ChunkOwnerTypeId);
                var renderZones = arch.GetComponentSpan<RenderZone>(RenderZoneTypeId);
                var entities = arch.GetEntityArray();

                for (int i = 0; i < entities.Length; i++)
                {
                    var zone = renderZones[i];

                    // Only process entities in Near zone
                    if (zone.Zone != RenderZoneType.Near)
                        continue;

                    var entity = entities[i];
                    var pos = positions[i];
                    var chunkOwner = chunkOwners[i];

                    if (!chunkOwner.IsAssigned)
                        continue;

                    var chunkLoc = chunkOwner.Location;
                    _activeChunksThisFrame.Add(chunkLoc);

                    if (!_chunkMultiMeshes.TryGetValue(chunkLoc, out var chunkData))
                    {
                        chunkData = CreateChunkMultiMesh(chunkLoc);
                        _chunkMultiMeshes[chunkLoc] = chunkData;
                    }

                    int nextIndex = chunkData.Count;
                    EnsureCapacity(chunkData, nextIndex + 1, chunkLoc);
                    chunkData.Transforms[nextIndex] = new Transform3D(Basis.Identity, new Vector3(pos.X, pos.Y, pos.Z));
                    chunkData.Count++;
                }
            }

            // Update all active chunk MultiMeshes
            UpdateChunkMultiMeshes();

            // Remove chunks that left the near zone
            RemoveInactiveChunks();

            // Debug stats
            if (SystemSettings.EnableDebugLogs.Value && _frameCounter % 60 == 0)
            {
                int totalEntities = 0;
                foreach (var chunkData in _chunkMultiMeshes.Values)
                {
                    totalEntities += chunkData.Count;
                }
                Logging.Log($"[{Name}] Active chunks: {_chunkMultiMeshes.Count}, Total entities: {totalEntities}");
            }
        }

        /// <summary>
        /// Create a new MultiMeshInstance3D for a chunk.
        /// </summary>
        private ChunkMultiMesh CreateChunkMultiMesh(ChunkLocation location)
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
                Name = $"Chunk_{location.X}_{location.Z}_{location.Y}",
                Multimesh = multiMesh,
                CastShadow = SystemSettings.CastShadows.Value
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled
            };

            _multiMeshContainer.CallDeferred("add_child", instance);

            var chunkData = new ChunkMultiMesh
            {
                Instance = instance,
                MultiMesh = multiMesh,
                Transforms = new Transform3D[initialCapacity],
                Capacity = initialCapacity
            };

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Created MultiMesh for chunk {location}, Capacity: {initialCapacity}");
            }

            return chunkData;
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

        /// <summary>
        /// Update MultiMesh transforms for all active chunks.
        /// </summary>
        private void UpdateChunkMultiMeshes()
        {
            foreach (var chunkEntry in _chunkMultiMeshes)
            {
                var chunkData = chunkEntry.Value;
                int count = chunkData.Count;

                if (count == 0)
                {
                    chunkData.MultiMesh.InstanceCount = 0;
                    continue;
                }

                chunkData.MultiMesh.InstanceCount = count;
                for (int i = 0; i < count; i++)
                {
                    chunkData.MultiMesh.SetInstanceTransform(i, chunkData.Transforms[i]);
                }
            }
        }

        /// <summary>
        /// Remove MultiMeshes for chunks that left the near zone.
        /// </summary>
        private void RemoveInactiveChunks()
        {
            if (_multiMeshContainer == null)
                return;

            var toRemove = new List<ChunkLocation>();
            foreach (var chunkLoc in _activeChunksLastFrame)
            {
                if (!_activeChunksThisFrame.Contains(chunkLoc))
                {
                    toRemove.Add(chunkLoc);
                }
            }

            foreach (var chunkLoc in toRemove)
            {
                if (_chunkMultiMeshes.TryGetValue(chunkLoc, out var chunkData))
                {
                    if (chunkData.Instance.IsInsideTree())
                    {
                        chunkData.Instance.QueueFree();
                    }
                    _chunkMultiMeshes.Remove(chunkLoc);

                    if (SystemSettings.EnableDebugLogs.Value)
                    {
                        Logging.Log($"[{Name}] Removed MultiMesh for chunk {chunkLoc}");
                    }
                }
            }

            // Update tracking for next frame
            _activeChunksLastFrame.Clear();
            foreach (var chunkLoc in _activeChunksThisFrame)
            {
                _activeChunksLastFrame.Add(chunkLoc);
            }
        }

        public override void OnShutdown(World world)
        {
            // Clean up all MultiMeshes
            foreach (var chunkData in _chunkMultiMeshes.Values)
            {
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


