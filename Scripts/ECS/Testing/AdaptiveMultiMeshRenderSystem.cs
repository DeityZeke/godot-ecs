#nullable enable

using Godot;
using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// FPS-ADAPTIVE MultiMesh rendering system.
    /// Prevents crashes by monitoring FPS and stopping visual scaling when performance limits are reached.
    /// Adds visuals in batches with delays to allow system stabilization.
    /// 
    /// CRITICAL FEATURE: This prevents the engine from crashing when spawning 500K+ entities.
    /// </summary>
    public sealed class AdaptiveMultiMeshRenderSystem : BaseSystem
    {
        public override string Name => "AdaptiveMultiMeshRenderSystem";
        public override int SystemId => typeof(AdaptiveMultiMeshRenderSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(RenderTag), typeof(Visible) };
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        // Performance monitoring
        [Export] public bool EnableFPSMonitoring = true;
        [Export] public float MinimumFPS = 30f;
        [Export] public int FPSHistorySize = 60; // 1 second at 60fps
        
        private float[] fpsHistory = new float[60]; // Pre-allocated array (no GC)
        private int fpsHistoryIndex = 0;
        private int fpsHistoryCount = 0;
        private float averageFPS;
        private bool performanceLimited;
        
        // Warmup period (don't monitor during startup)
        private const int WARMUP_FRAMES = 120;
        private int warmupFrameCounter = 0;
        private bool warmupComplete = false;
        
        // Batch settings
        [Export] public int BatchSize = 10000; // Add this many per batch
        [Export] public float BatchInterval = 0.020f; // Wait between batches (seconds)
        [Export] public int StartingCapacity = 10000;
        [Export] public int MaxCapacity = 1000000;
        
        private int currentVisualCapacity;
        private int targetVisualCapacity;
        private int activeVisuals;
        private bool addingVisualsInProgress;
        private float batchTimer;
        
        // Rendering
        private MultiMeshInstance3D? _multiMeshInstance;
        private MultiMesh? _multiMesh;
        private Transform3D[] _transforms = Array.Empty<Transform3D>(); // Will be pre-allocated in OnInitialize
        private bool _initialized;
        private bool _isInTree = false;
        private int _frameCount;
        
        // Amortized updates - tier-based system
        [Export] public int BaseUpdatesPerFrame = 10000; // Safe baseline
        [Export] public float TargetRefreshSeconds = 1.0f; // Aim to refresh all entities in this time
        [Export] public float MaxEntityPercentPerFrame = 0.05f; // Never update more than 5% per frame
        
        private int _currentUpdatesPerFrame = 10000;
        private int _updateOffset = 0;
        private int _framesAtCurrentTier = 0; // Stability counter
        private const int TIER_CHANGE_DELAY = 60; // Wait this many frames before increasing tier

        public override void OnInitialize(World world)
        {
            GD.Print("[AdaptiveMultiMeshRenderSystem] Initializing with FPS-adaptive scaling.");
            
            _cachedQuery = world.Query(typeof(Position), typeof(RenderTag), typeof(Visible));
            
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            // ZERO-ALLOCATION: Pre-allocate to MaxCapacity to avoid resize GC
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            _transforms = new Transform3D[MaxCapacity];
            fpsHistory = new float[FPSHistorySize];
            
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.CurrentScene;
            
            if (root == null)
            {
                GD.PushWarning("[AdaptiveMultiMeshRenderSystem] No scene root found.");
                return;
            }
            
            // Create MultiMesh
            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false
            };
            
            // Optimized sphere mesh
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
                Name = "ECS_AdaptiveMultiMesh",
                Multimesh = _multiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled
            };
            
            root.CallDeferred("add_child", _multiMeshInstance);
            
            // Initialize capacity targets
            currentVisualCapacity = 0;
            targetVisualCapacity = StartingCapacity;
            
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Initialized. Target: {StartingCapacity:N0} ƒÆ’Â¢ƒÂ¢â€šÂ¬Â ƒÂ¢â€šÂ¬â€žÂ¢ Max: {MaxCapacity:N0}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Batch size: {BatchSize:N0}, Interval: {BatchInterval:F3}s, Min FPS: {MinimumFPS}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Tier-based updates: Base {BaseUpdatesPerFrame:N0}/frame, Max {MaxEntityPercentPerFrame:P0} of entities, Target {TargetRefreshSeconds:F1}s refresh");
        }

        public override void Update(World world, double delta)
        {
            if (_multiMesh == null || _multiMeshInstance == null)
                return;
            
            // Cache IsInsideTree check
            if (!_isInTree)
            {
                _isInTree = _multiMeshInstance.IsInsideTree();
                if (!_isInTree)
                    return;
            }
            
            _frameCount++;
            
            // Update FPS monitoring
            if (EnableFPSMonitoring)
                UpdateFPSMonitoring(delta);
            
            // Handle batch addition
            if (currentVisualCapacity < targetVisualCapacity)
                HandleBatchAddition(delta);
            
            // Lazy initialization
            if (!_initialized && currentVisualCapacity > 0)
            {
                _initialized = true;
            }
            
            // Sync entities to visuals
            if (_initialized)
                SyncEntitiesToInstances(world);
        }
        
        private void UpdateFPSMonitoring(double delta)
        {
            // Warmup phase - don't monitor during startup
            if (!warmupComplete)
            {
                warmupFrameCounter++;
                if (warmupFrameCounter >= WARMUP_FRAMES)
                {
                    warmupComplete = true;
                    GD.Print($"[AdaptiveMultiMeshRenderSystem] ƒÆ’Â¢ƒâ€¦â‚¬Å“ƒÂ¢â€šÂ¬Â¦ Warmup complete, FPS monitoring active");
                }
                return;
            }
            
            float currentFPS = 1.0f / (float)delta;
            
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            // ZERO-ALLOCATION: Circular buffer instead of Queue
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            fpsHistory[fpsHistoryIndex] = currentFPS;
            fpsHistoryIndex = (fpsHistoryIndex + 1) % FPSHistorySize;
            if (fpsHistoryCount < FPSHistorySize)
                fpsHistoryCount++;
            
            // Calculate average
            averageFPS = 0;
            for (int i = 0; i < fpsHistoryCount; i++)
                averageFPS += fpsHistory[i];
            averageFPS /= fpsHistoryCount;
            
            // Check performance limits
            if (averageFPS < MinimumFPS && !performanceLimited)
            {
                performanceLimited = true;
                GD.Print($"[AdaptiveMultiMeshRenderSystem] ƒÆ’Â¢ƒâ€¦Â¡ƒâ€šÂ ƒÆ’Â¯ƒâ€šÂ¸ƒâ€šÂ Performance limit at {averageFPS:F1} FPS");
                GD.Print($"[AdaptiveMultiMeshRenderSystem] Stopped at {activeVisuals:N0} visuals");
                
                // Stop adding more visuals
                targetVisualCapacity = currentVisualCapacity;
            }
        }
        
        private void HandleBatchAddition(double delta)
        {
            if (performanceLimited)
                return;
            
            if (addingVisualsInProgress)
            {
                batchTimer -= (float)delta;
                if (batchTimer <= 0)
                {
                    AddVisualBatch();
                    batchTimer = BatchInterval;
                }
            }
            else if (currentVisualCapacity < targetVisualCapacity)
            {
                // Start batch process
                addingVisualsInProgress = true;
                batchTimer = BatchInterval;
                GD.Print($"[AdaptiveMultiMeshRenderSystem] Starting batch addition: " +
                        $"{currentVisualCapacity:N0} ƒÆ’Â¢ƒÂ¢â€šÂ¬Â ƒÂ¢â€šÂ¬â€žÂ¢ {targetVisualCapacity:N0}");
            }
        }
        
        private void AddVisualBatch()
        {
            if (currentVisualCapacity >= targetVisualCapacity || 
                currentVisualCapacity >= MaxCapacity)
            {
                addingVisualsInProgress = false;
                GD.Print($"[AdaptiveMultiMeshRenderSystem] ƒÆ’Â¢ƒâ€¦â‚¬Å“ƒÂ¢â€šÂ¬Â¦ Target reached: {currentVisualCapacity:N0}");
                return;
            }
            
            int newCapacity = Math.Min(
                currentVisualCapacity + BatchSize,
                Math.Min(targetVisualCapacity, MaxCapacity)
            );
            
            ResizeMultiMesh(newCapacity);
            currentVisualCapacity = newCapacity;
            
            float progress = (float)currentVisualCapacity / targetVisualCapacity * 100f;
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Batch added: {currentVisualCapacity:N0} ({progress:F1}%) FPS: {averageFPS:F1}");
        }
        
        private void ResizeMultiMesh(int newSize)
        {
            var oldSize = _multiMesh!.InstanceCount;
            _multiMesh.InstanceCount = newSize;
            
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            // ZERO-ALLOCATION: _transforms already pre-allocated to MaxCapacity
            // Just initialize new instances as hidden (no array resize!)
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            
            var hiddenTransform = new Transform3D(Basis.Identity, new Vector3(0, -10000, 0));
            
            // Update both _transforms array and MultiMesh instances
            for (int i = oldSize; i < newSize; i++)
            {
                _transforms[i] = hiddenTransform;
                _multiMesh.SetInstanceTransform(i, hiddenTransform);
            }
        }
        
        private void SyncEntitiesToInstances(World world)
        {
            int entityCount = 0;
            
            // Count total entities
            foreach (var arch in _cachedQuery!)
                entityCount += arch.Count;
            
            // Adjust target if entity count increased (but respect performance limit)
            if (entityCount > targetVisualCapacity && !performanceLimited)
            {
                targetVisualCapacity = Math.Min(entityCount, MaxCapacity);
            }
            
            // Cap sync to current capacity
            int maxInstances = Math.Min(entityCount, currentVisualCapacity);
            activeVisuals = maxInstances;
            
            // TIER-BASED UPDATES: Adjust based on FPS stability, not raw calculation
            // This prevents the death spiral while still scaling up when performance allows
            if (maxInstances > 0 && warmupComplete)
            {
                // Calculate ideal updates for target refresh time
                float idealFramesInCycle = 30f * TargetRefreshSeconds; // Assume 30fps baseline
                int idealUpdates = Math.Max(BaseUpdatesPerFrame, (int)(maxInstances / idealFramesInCycle));
                
                // Apply safety cap: never exceed X% of entities per frame
                int safetyMax = Math.Max(BaseUpdatesPerFrame, (int)(maxInstances * MaxEntityPercentPerFrame));
                int targetUpdates = Math.Min(idealUpdates, safetyMax);
                
                // Only INCREASE tier if FPS is good and stable
                if (targetUpdates > _currentUpdatesPerFrame)
                {
                    _framesAtCurrentTier++;
                    // Require sustained good FPS before increasing
                    if (_framesAtCurrentTier >= TIER_CHANGE_DELAY && averageFPS >= MinimumFPS + 5f)
                    {
                        int increase = Math.Min(10000, targetUpdates - _currentUpdatesPerFrame);
                        _currentUpdatesPerFrame += increase;
                        _framesAtCurrentTier = 0;
                        GD.Print($"[AdaptiveMultiMeshRenderSystem] â¬†ï¸ Increased to {_currentUpdatesPerFrame:N0} updates/frame (FPS: {averageFPS:F1})");
                    }
                }
                // DECREASE immediately if FPS drops (safety mechanism)
                else if (averageFPS < MinimumFPS + 2f && _currentUpdatesPerFrame > BaseUpdatesPerFrame)
                {
                    _currentUpdatesPerFrame = Math.Max(BaseUpdatesPerFrame, _currentUpdatesPerFrame / 2);
                    _framesAtCurrentTier = 0;
                    GD.Print($"[AdaptiveMultiMeshRenderSystem] â¬‡ï¸ Decreased to {_currentUpdatesPerFrame:N0} updates/frame (FPS: {averageFPS:F1})");
                }
                // Target reached, reset stability counter
                else if (_currentUpdatesPerFrame == targetUpdates)
                {
                    _framesAtCurrentTier = 0;
                }
            }
            
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂ
            // AMORTIZED UPDATE: Only update _currentUpdatesPerFrame instances per frame
            // This matches the standard MultiMeshRenderSystem performance
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂ
            
            int startIdx = _updateOffset;
            int endIdx = Math.Min(startIdx + _currentUpdatesPerFrame, maxInstances);
            int currentIdx = 0;
            int batchIdx = 0; // Track position in _batchTransforms
            
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            // ZERO-ALLOCATION: Update transforms in local array, then batch to GPU
            // ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢
            
            // Update only the current batch
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
                        ref var transform = ref _transforms[currentIdx];
                        
                        // Reuse existing transform, only update origin
                        transform.Origin.X = pos.X;
                        transform.Origin.Y = pos.Y;
                        transform.Origin.Z = pos.Z;
                        
                        // Individual call required (Godot has no batch method)
                        _multiMesh!.SetInstanceTransform(currentIdx, transform);
                        batchIdx++;
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
            if (_updateOffset >= maxInstances)
                _updateOffset = 0;
            
            // Log amortization info on first few frames
            if (_frameCount <= 10)
            {
                GD.Print($"[AdaptiveMultiMeshRenderSystem] Frame {_frameCount}: Batch updated {startIdx}-{endIdx} ({batchIdx} instances)");
            }
        }

        /* // Currently never used, but might be usefull in the future?
        /// <summary>
        /// Prints diagnostic information about the renderer state.
        /// </summary>
        public void PrintDiagnostics()
        {
            GD.Print($"\n[AdaptiveMultiMeshRenderSystem] ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂ DIAGNOSTICS ƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂƒÆ’Â¢ƒÂ¢â€šÂ¬Â¢ƒâ€šÂ");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Total Entities: {activeVisuals:N0}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Active Visuals: {activeVisuals:N0}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Visual Capacity: {currentVisualCapacity:N0}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Target Capacity: {targetVisualCapacity:N0}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Usage: {(currentVisualCapacity > 0 ? (float)activeVisuals / currentVisualCapacity * 100 : 0):F1}%");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Average FPS: {averageFPS:F1}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Performance Limited: {performanceLimited}");
            GD.Print($"[AdaptiveMultiMeshRenderSystem] Warmup Complete: {warmupComplete}");
        }
        */

        public override void OnShutdown(World world)
        {
            _multiMeshInstance?.QueueFree();
        }
    }
}