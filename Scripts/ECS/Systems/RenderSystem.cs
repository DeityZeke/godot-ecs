
#nullable enable

using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// SIMPLE, WORKING RenderSystem - NO CULLING
    /// Renders all entities with Position, RenderTag, and Visible components.
    /// 
    /// Use this for debugging or when you want ALL entities visible.
    /// For large entity counts (100K+), use MultiMeshRenderSystem instead.
    /// </summary>
    public sealed class RenderSystem : BaseSystem
    {
        public override string Name => "RenderSystem";
        public override int SystemId => typeof(RenderSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private Node3D? _root;
        private PackedScene? _cubeScene;
        private readonly List<MeshInstance3D> _instances = new();
        private bool _initialized = false;
        private bool _allInstancesInTree = false;
        private int _frameCount = 0;
        private int _framesAfterInit = 0;

        public override void OnInitialize(World world)
        {
            GD.Print("[RenderSystem] Initializing SIMPLE version (no culling).");

            // Cache query
            _cachedQuery = world.Query(typeof(Position), typeof(RenderTag), typeof(Visible));

            var tree = Engine.GetMainLoop() as SceneTree;
            _root = tree?.CurrentScene as Node3D;

            if (_root == null)
            {
                GD.PushWarning("[RenderSystem] No scene root found.");
                return;
            }

            _cubeScene = GD.Load<PackedScene>("res://Scenes/cube_mesh.tscn");

            GD.Print("[RenderSystem] Initialized.");
        }

        public override void Update(World world, double delta)
        {
            if (_root == null || _cubeScene == null || _cachedQuery == null)
                return;

            _frameCount++;

            // Lazy initialization - create all visuals on first update
            if (!_initialized)
            {
                InitializeVisuals(world);
                if (!_initialized)
                    return;
                _framesAfterInit = 0;
                return;
            }

            _framesAfterInit++;

            // Check tree status once after spawn completes
            if (!_allInstancesInTree && _framesAfterInit == 2)
            {
                _allInstancesInTree = CheckAllInstancesInTree();
                GD.Print($"[RenderSystem] Tree check complete: {_allInstancesInTree}");
            }

            // Wait one frame after initialization for nodes to be added to tree
            if (_framesAfterInit < 2)
                return;

            // Update all positions (NO CULLING)
            UpdatePositions(world);
        }

        private void InitializeVisuals(World world)
        {
            int entityCount = 0;
            foreach (var arch in _cachedQuery!)
                entityCount += arch.Count;

            if (entityCount == 0)
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            _instances.Capacity = entityCount;

            int spawnedCount = 0;

            foreach (var arch in _cachedQuery!)
            {
                for (int i = 0; i < arch.Count; i++)
                {
                    var instance = _cubeScene!.Instantiate<MeshInstance3D>();
                    instance.Name = $"EntityCube_{spawnedCount}";
                    instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    instance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                    instance.Visible = true; // Always visible

                    _root!.CallDeferred("add_child", instance);
                    _instances.Add(instance);
                    spawnedCount++;
                }
            }

            sw.Stop();
            _initialized = true;

            GD.Print($"[RenderSystem] Initialized {spawnedCount} visuals in {sw.Elapsed.TotalMilliseconds:F3}ms");
        }

        private bool CheckAllInstancesInTree()
        {
            //foreach (var instance in _instances)
            foreach (ref var instance in CollectionsMarshal.AsSpan(_instances))
            {
                if (!instance.IsInsideTree())
                    return false;
            }
            return true;
        }

        private void UpdatePositions(World world)
        {
            int idx = 0;

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;

                if (!arch.TryGetComponentSpan<Position>(out var posSpan))
                    continue;

                int count = Math.Min(posSpan.Length, _instances.Count - idx);

                for (int i = 0; i < count; i++, idx++)
                {
                    if (idx >= _instances.Count) break;

                    // Skip tree check if we've confirmed all are in tree
                    if (!_allInstancesInTree && !_instances[idx].IsInsideTree())
                        continue;

                    ref readonly var pos = ref posSpan[i];

                    // Direct position update - all entities always visible
                    _instances[idx].GlobalPosition = new Vector3(pos.X, pos.Y, pos.Z);
                }
            }

            // Log first few frames
            if (_frameCount <= 10 || _frameCount % 120 == 0)
            {
                GD.Print($"[RenderSystem] Frame {_frameCount}: Updated {idx} entity positions (all visible)");
            }
        }

        public override void OnShutdown(World world)
        {
            //foreach (var instance in _instances)
            foreach (ref var instance in CollectionsMarshal.AsSpan(_instances))
                instance?.QueueFree();
            _instances.Clear();
        }
    }
}