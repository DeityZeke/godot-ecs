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
using Client.ECS;
using Client.ECS.Components;
using Client.ECS.Rendering;

namespace Client.ECS.Systems
{
    /// <summary>
    /// Hybrid rendering system with 3 zones based on camera distance:
    /// - Core (3x3x3 or 5x5x5): Individual MeshInstance3D per chunk (full interactivity)
    /// - Near (configurable): MultiMesh batching per chunk (visual only)
    /// - Far (optional): Billboard/impostor rendering (ultra low-res)
    ///
    /// Updates RenderZone component based on camera position and chunk location.
    ///
    /// DEPENDENCY: Requires ChunkSystem to be registered first for chunk management.
    /// </summary>
    public sealed class HybridRenderSystem : BaseSystem
    {
        #region Settings
        public sealed class Settings : SettingsManager
        {
            public IntSetting CoreBubbleSize { get; private set; }
            public IntSetting RenderDistanceChunks { get; private set; }
            public BoolSetting EnableFarZone { get; private set; }
            public IntSetting FarZoneDistance { get; private set; }
            public BoolSetting EnableFrustumCulling { get; private set; }
            public IntSetting UpdateFrequency { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }
            public FloatSetting ChunkWindowRecenterDelaySeconds { get; private set; }
            public Settings()
            {
                CoreBubbleSize = RegisterInt("Core Bubble Size", 3,
                    min: 1, max: 11, step: 2,
                    tooltip: "Size of MeshInstance3D bubble (3 = 3x3x3 = 27 chunks, 5 = 5x5x5 = 125 chunks)");
                RenderDistanceChunks = RegisterInt("Render Distance (chunks)", 16,
                    min: 4, max: 64, step: 1,
                    tooltip: "Total chunk radius for MultiMesh rendering (includes core bubble). Increase to see trees/props farther away.");
                EnableFarZone = RegisterBool("Enable Far Zone", false,
                    tooltip: "Enable billboard/impostor rendering for distant chunks");
                FarZoneDistance = RegisterInt("Far Zone Distance", 16,
                    min: 0, max: 128, step: 1,
                    tooltip: "Billboard render distance in chunks (only if Far Zone enabled)");
                EnableFrustumCulling = RegisterBool("Enable Frustum Culling", true,
                    tooltip: "Cull chunks outside camera frustum");
                UpdateFrequency = RegisterInt("Update Frequency", 10,
                    min: 1, max: 60, step: 1,
                    tooltip: "Update zone assignments every N frames");
                EnableDebugLogs = RegisterBool("Enable Debug Logs", true,
                    tooltip: "Log zone transitions and culling operations");
                ChunkWindowRecenterDelaySeconds = RegisterFloat("Chunk Window Recenter Delay (s)", 0.0f,
                    min: 0.0f, max: 1.0f, step: 0.05f,
                    tooltip: "Delay before chunk windows recenters around a new camera chunk.");
            }
        }
        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;
        #endregion
        public override string Name => "Hybrid Render System";
        public override int SystemId => typeof(HybridRenderSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;
        public override Type[] ReadSet { get; } = Array.Empty<Type>();
        public override Type[] WriteSet { get; } = Array.Empty<Type>();
        private ChunkManager? _chunkManager;
        private int _frameCounter = 0;
        private ChunkLocation _lastCameraChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private readonly HybridRenderSharedState _sharedState = HybridRenderSharedState.Instance;
        public override void OnInitialize(World world)
        {
            // ChunkManager will be accessed via static service locator pattern
            // This is set during WorldECS initialization
            _chunkManager = null; // Will be set by SetChunkManager() from WorldECS
            Logging.Log($"[{Name}] Initialized - Core bubble: {SystemSettings.CoreBubbleSize.Value}x{SystemSettings.CoreBubbleSize.Value}x{SystemSettings.CoreBubbleSize.Value}");
            Logging.Log($"[{Name}] Render distance: {SystemSettings.RenderDistanceChunks.Value} chunks, Far zone: {(SystemSettings.EnableFarZone.Value ? SystemSettings.FarZoneDistance.Value + " chunks" : "disabled")}");
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
            if (_frameCounter == 0 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] First Update call - initializing");
            }
            if (_chunkManager == null)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] ChunkManager is null, waiting for connection");
                }
                _frameCounter++;
                return;
            }
            _frameCounter++;

            // Use camera cache (updated on main thread by WorldECS)
            if (!CameraCache.IsValid)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] CameraCache not valid");
                }
                return;
            }

            Vector3 cameraPos = CameraCache.Position;
            ChunkLocation cameraChunk = _chunkManager.WorldToChunk(cameraPos.X, cameraPos.Y, cameraPos.Z);

            _sharedState.UpdateFrustum(CameraCache.FrustumPlanes, SystemSettings.EnableFrustumCulling.Value);

            // Configure and update the shared render windows
            // Render systems (MeshInstanceBubbleManager, MultiMeshZoneManager) read from these windows
            ConfigureSharedWindow();
            _sharedState.Update(cameraChunk, delta, SystemSettings.ChunkWindowRecenterDelaySeconds.Value);

            if (_frameCounter == 1 && SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Camera at chunk {cameraChunk}, position {cameraPos}");
            }

            bool cameraMovedChunk = cameraChunk != _lastCameraChunk;
            if (cameraMovedChunk)
            {
                if (SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Camera moved to chunk {cameraChunk}");
                }
                _lastCameraChunk = cameraChunk;
            }
        }
        private void ConfigureSharedWindow()
        {
            int coreRadius = Math.Max(0, SystemSettings.CoreBubbleSize.Value / 2);
            int renderRadius = Math.Max(coreRadius, SystemSettings.RenderDistanceChunks.Value);
            int coreRadiusY = coreRadius;
            int nearRadiusY = coreRadiusY; // Match core Y radius (original behavior)

            _sharedState.Configure(coreRadius, coreRadiusY, renderRadius, nearRadiusY);
        }
        public override void OnShutdown(World world)
        {
            _sharedState.Reset();
            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
        }
    }
}
