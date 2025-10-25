
#nullable enable

using Godot;
using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Ultra-optimized MultiMesh rendering with amortized updates.
    /// Updates entities in batches across multiple frames to maintain 60+ FPS.
    /// Can efficiently handle 500K+ entities by spreading the update cost.
    /// 
    /// PERFORMANCE OPTIMIZATIONS:
    /// - Cached IsInsideTree() check (only done once)
    /// - Reuses Transform3D structs (no Vector3 allocations)
    /// - Direct field assignment instead of new Vector3()
    /// - Cached query to avoid repeated Query() calls
    /// </summary>
    public sealed class MultiMeshRenderSystem : BaseSystem
    {
        public override string Name => "MultiMeshRenderSystem";
        public override int SystemId => typeof(MultiMeshRenderSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private MultiMeshInstance3D? _multiMeshInstance;
        private MultiMesh? _multiMesh;
        private Transform3D[] _transforms = Array.Empty<Transform3D>();
        private bool _initialized;
        private bool _isInTree = false;  // ✅ Cache IsInsideTree result
        private int _capacity;
        private int _frameCount;

        // Amortization settings
        public int UpdatesPerFrame = 10000; // How many instances to update per frame
        private int _updateOffset = 0; // Current batch offset

        public override void OnInitialize(World world)
        {
            GD.Print("[MultiMeshRenderSystem] Initializing.");

            // ✅ Cache query on initialization
            _cachedQuery = world.Query(typeof(Position), typeof(RenderTag), typeof(Visible));

            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.CurrentScene;

            if (root == null)
            {
                GD.PushWarning("[MultiMeshRenderSystem] No scene root found.");
                return;
            }

            // Create MultiMesh
            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false
            };

            // Optimized sphere mesh (reduced poly count)
            var sphereMesh = new SphereMesh
            {
                RadialSegments = 6,
                Rings = 3,
                Radius = 0.5f,
                Height = 1.0f
            };
            _multiMesh.Mesh = sphereMesh;

            _multiMeshInstance = new MultiMeshInstance3D
            {
                Name = "ECS_MultiMesh",
                Multimesh = _multiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled // Disable GI for performance
            };

            root.CallDeferred("add_child", _multiMeshInstance);

            GD.Print("[MultiMeshRenderSystem] Initialized.");
        }

        public override void Update(World world, double delta)
        {
            if (_multiMesh == null || _multiMeshInstance == null)
                return;

            // Cache the IsInsideTree check result after first successful check
            if (!_isInTree)
            {
                _isInTree = _multiMeshInstance.IsInsideTree();
                if (!_isInTree)
                    return;
            }

            _frameCount++;

            // Lazy initialization - only runs once
            if (!_initialized)
            {
                int entityCount = 0;

                // Use cached query
                foreach (var arch in _cachedQuery!)
                    entityCount += arch.Count;

                if (entityCount == 0)
                    return;

                _capacity = entityCount;
                _multiMesh.InstanceCount = _capacity;
                _transforms = new Transform3D[_capacity];

                // Initialize all transforms to identity
                for (int i = 0; i < _capacity; i++)
                    _transforms[i] = Transform3D.Identity;

                _initialized = true;
                GD.Print($"[MultiMeshRenderSystem] Initialized MultiMesh with {_capacity} instances.");
                GD.Print($"[MultiMeshRenderSystem] Amortization: {UpdatesPerFrame} updates/frame = {_capacity / UpdatesPerFrame} frames per full update");
                return;
            }

            // Amortized update: Only update a batch of entities per frame
            UpdateBatch(world);
        }

        private void UpdateBatch(World world)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            int startIdx = _updateOffset;
            int endIdx = Math.Min(startIdx + UpdatesPerFrame, _capacity);
            int currentIdx = 0;
            int updatedCount = 0;

            // Use cached query
            foreach (var arch in _cachedQuery!)
            {
                if (!arch.TryGetComponentSpan<Position>(out var posSpan))
                    continue;

                int count = posSpan.Length;

                for (int i = 0; i < count; i++)
                {
                    // Check if this entity is in our update batch
                    if (currentIdx >= startIdx && currentIdx < endIdx)
                    {
                        ref readonly var pos = ref posSpan[i];

                        // Reuse existing transform, only update origin
                        // This avoids "new Vector3()" allocation (10,000/frame → 0/frame)
                        ref var transform = ref _transforms[currentIdx];
                        transform.Origin.X = pos.X;
                        transform.Origin.Y = pos.Y;
                        transform.Origin.Z = pos.Z;

                        _multiMesh!.SetInstanceTransform(currentIdx, transform);
                        updatedCount++;
                    }

                    currentIdx++;
                    if (currentIdx >= endIdx)
                        break;
                }

                if (currentIdx >= endIdx)
                    break;
            }

            // Move to next batch (wraps around to 0 when complete)
            _updateOffset = endIdx;
            if (_updateOffset >= _capacity)
                _updateOffset = 0;

            sw.Stop();

            // Log first 10 frames for debugging
            if (_frameCount <= 10)
            {
                GD.Print($"[MultiMeshRenderSystem] Frame {_frameCount}: Updated batch {startIdx}-{endIdx} ({updatedCount} instances) in {sw.Elapsed.TotalMilliseconds:F3}ms");
            }
        }

        public override void OnShutdown(World world)
        {
            _multiMeshInstance?.QueueFree();
        }
    }
}