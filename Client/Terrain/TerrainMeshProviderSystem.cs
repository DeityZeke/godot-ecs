#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.ECS.Systems;
using Server.ECS.Components.Terrain;

namespace Client.Terrain
{
    /// <summary>
    /// Client-side system that generates meshes for terrain chunk entities.
    /// Runs BEFORE HybridRenderSystem to populate mesh data.
    /// Uses RequireSystem to ensure proper ordering and registration.
    /// </summary>
    [RequireSystem("Client.ECS.Systems.HybridRenderSystem")]
    public sealed class TerrainMeshProviderSystem : BaseSystem
    {
        private readonly TerrainMeshCache _meshCache = new();
        private int _meshesBuiltPerFrame = 2;

        public override int SystemId => GetHashCode();
        public override string Name => "Terrain Mesh Provider";
        public override Type[] ReadSet => new[] { typeof(TerrainChunkComponent) };
        public override Type[] WriteSet => new[] { typeof(TerrainChunkComponent) }; // Modifies IsDirty flag
        public override TickRate Rate => TickRate.EveryFrame;

        public override void Update(World world, double delta)
        {
            // Query terrain entities directly
            var archetype = world.GetArchetype<TerrainChunkComponent>();
            if (archetype == null)
                return;

            var terrainComponents = archetype.GetComponentSpan<TerrainChunkComponent>();
            int meshesBuilt = 0;

            // Process dirty terrain chunks (need mesh generation)
            for (int i = 0; i < terrainComponents.Length && meshesBuilt < _meshesBuiltPerFrame; i++)
            {
                ref var terrain = ref terrainComponents[i];

                if (!terrain.IsDirty || terrain.ChunkData == null)
                    continue;

                // Generate or retrieve cached mesh
                var mesh = _meshCache.GetOrCreateMesh(terrain.Location, () =>
                {
                    return GreedyMesher.GenerateMesh(terrain.ChunkData);
                });

                if (mesh == null)
                    continue;

                // Mark as clean - mesh is now available in cache
                terrain.IsDirty = false;
                terrain.MeshVersion = terrain.ChunkData.Version;

                meshesBuilt++;
            }
        }

        /// <summary>
        /// Gets a cached mesh for a terrain chunk location.
        /// Called by the hybrid renderer when building visual instances.
        /// </summary>
        public ArrayMesh? GetMeshForChunk(ChunkLocation location)
        {
            // Check if mesh exists in cache
            // The cache will return null if not generated yet
            return _meshCache.GetOrCreateMesh(location, () => null);
        }

        /// <summary>
        /// Invalidates a cached mesh (call when terrain is modified).
        /// </summary>
        public void InvalidateMesh(ChunkLocation location)
        {
            _meshCache.Invalidate(location);
        }

        /// <summary>
        /// Sets the maximum number of meshes to build per frame.
        /// </summary>
        public void SetMeshBuildRate(int meshesPerFrame)
        {
            _meshesBuiltPerFrame = Math.Max(1, meshesPerFrame);
        }

        /// <summary>
        /// Configures the mesh cache limits.
        /// </summary>
        public void ConfigureCache(int maxMeshes, int maxMemoryMB)
        {
            _meshCache.SetMaxCachedMeshes(maxMeshes);
            _meshCache.SetMaxMemoryMB(maxMemoryMB);
        }

        public void Cleanup(World world)
        {
            _meshCache.Clear();
            _cachedQuery = null;
        }

        /// <summary>
        /// Gets debug statistics about mesh generation.
        /// </summary>
        public string GetDebugInfo()
        {
            var stats = _meshCache.GetStats();
            return $"Cache: {stats.CachedMeshCount}/{stats.MaxCachedMeshes} meshes, {stats.MemoryUsedMB:F1}/{stats.MaxMemoryMB:F0} MB";
        }
    }
}
