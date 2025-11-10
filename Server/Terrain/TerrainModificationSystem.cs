#nullable enable

using System;
using System.Collections.Generic;
using UltraSim;
using UltraSim.ECS.Components;
using UltraSim.ECS.Systems;
using UltraSim.Terrain;
using UltraSim.ECS;

namespace Server.Terrain
{
    /// <summary>
    /// Server-side system for validating and applying terrain modifications.
    /// Queues tile edits and batch-applies them to prevent race conditions.
    /// </summary>
    public sealed class TerrainModificationSystem : BaseSystem
    {
        private TerrainManagerSystem? _terrainManager;
        private readonly Queue<TileModification> _modificationQueue = new();
        private readonly List<ChunkLocation> _dirtyChunks = new();
        private int _modificationsProcessedPerFrame = 100;

        public override int SystemId => GetHashCode();
        public override string Name => "Terrain Modification";
        public override Type[] ReadSet => Array.Empty<Type>();
        public override Type[] WriteSet => Array.Empty<Type>();
        public override TickRate Rate => TickRate.EveryFrame;

        /// <summary>
        /// Sets the terrain manager reference.
        /// </summary>
        public void SetTerrainManager(TerrainManagerSystem manager)
        {
            _terrainManager = manager;
        }

        /// <summary>
        /// Queues a tile modification to be applied on next update.
        /// </summary>
        public void QueueModification(Position worldPos, TerrainTile newTile)
        {
            _modificationQueue.Enqueue(new TileModification
            {
                WorldPosition = worldPos,
                NewTile = newTile,
                ModificationType = ModificationType.SetTile
            });
        }

        /// <summary>
        /// Queues a height adjustment at a world position.
        /// </summary>
        public void QueueHeightAdjustment(Position worldPos, sbyte heightDelta)
        {
            _modificationQueue.Enqueue(new TileModification
            {
                WorldPosition = worldPos,
                HeightDelta = heightDelta,
                ModificationType = ModificationType.AdjustHeight
            });
        }

        /// <summary>
        /// Queues a material change at a world position.
        /// </summary>
        public void QueueMaterialChange(Position worldPos, byte materialId)
        {
            _modificationQueue.Enqueue(new TileModification
            {
                WorldPosition = worldPos,
                MaterialId = materialId,
                ModificationType = ModificationType.ChangeMaterial
            });
        }

        /// <summary>
        /// Applies a brush-based height modification (circular area).
        /// </summary>
        public void ApplyHeightBrush(Position centerPos, float radiusMeters, sbyte heightDelta, BrushFalloff falloff = BrushFalloff.Smooth)
        {
            float radiusTiles = radiusMeters / TerrainConstants.TileSize;
            int radiusInt = (int)Math.Ceiling(radiusTiles);

            int centerTileX = (int)Math.Floor(centerPos.X / TerrainConstants.TileSize);
            int centerTileZ = (int)Math.Floor(centerPos.Z / TerrainConstants.TileSize);

            for (int dz = -radiusInt; dz <= radiusInt; dz++)
            {
                for (int dx = -radiusInt; dx <= radiusInt; dx++)
                {
                    float distance = MathF.Sqrt(dx * dx + dz * dz);
                    if (distance > radiusTiles)
                    {
                        continue;
                    }

                    float strength = CalculateFalloff(distance / radiusTiles, falloff);
                    sbyte adjustedDelta = (sbyte)(heightDelta * strength);

                    if (adjustedDelta == 0)
                    {
                        continue;
                    }

                    float tileWorldX = (centerTileX + dx) * TerrainConstants.TileSize;
                    float tileWorldZ = (centerTileZ + dz) * TerrainConstants.TileSize;

                    QueueHeightAdjustment(new Position(tileWorldX, centerPos.Y, tileWorldZ), adjustedDelta);
                }
            }
        }

        public override void Update(World world, double delta)
        {
            if (_terrainManager == null)
            {
                if (_modificationQueue.Count > 0)
                {
                    Logging.Log($"[{Name}] TerrainManagerSystem not set, discarding {_modificationQueue.Count} modifications", LogSeverity.Warning, Name);
                    _modificationQueue.Clear();
                }
                return;
            }

            _dirtyChunks.Clear();

            int processed = 0;
            while (_modificationQueue.Count > 0 && processed < _modificationsProcessedPerFrame)
            {
                var mod = _modificationQueue.Dequeue();
                ApplyModification(mod);
                processed++;
            }

            // Notify dirty chunks (future: trigger mesh rebuild on client)
            if (_dirtyChunks.Count > 0)
            {
                // TODO: Broadcast dirty chunks to clients for mesh rebuild
                // For now, just log
                Logging.Log($"[{Name}] Modified {_dirtyChunks.Count} chunks", source: Name);
            }
        }

        private void ApplyModification(TileModification mod)
        {
            // Convert world position to tile coordinates
            var tileCoords = WorldToTileCoords(mod.WorldPosition);
            if (!tileCoords.HasValue)
            {
                return;
            }

            var (chunkLoc, localX, localZ) = tileCoords.Value;

            // Get or load chunk
            var chunk = _terrainManager!.GetOrLoadChunk(chunkLoc);
            var tile = chunk.GetTile(localX, localZ);

            // Apply modification based on type
            switch (mod.ModificationType)
            {
                case ModificationType.SetTile:
                    tile = mod.NewTile;
                    break;

                case ModificationType.AdjustHeight:
                    tile.HeightNW = ClampHeight(tile.HeightNW + mod.HeightDelta);
                    tile.HeightNE = ClampHeight(tile.HeightNE + mod.HeightDelta);
                    tile.HeightSW = ClampHeight(tile.HeightSW + mod.HeightDelta);
                    tile.HeightSE = ClampHeight(tile.HeightSE + mod.HeightDelta);
                    break;

                case ModificationType.ChangeMaterial:
                    tile.MaterialId = mod.MaterialId;
                    break;
            }

            // Validate and apply
            if (ValidateTile(tile, localX, localZ))
            {
                chunk.SetTile(localX, localZ, tile);

                if (!_dirtyChunks.Contains(chunkLoc))
                {
                    _dirtyChunks.Add(chunkLoc);
                }
            }
        }

        private (ChunkLocation, int, int)? WorldToTileCoords(Position worldPos)
        {
            // Convert world position to tile coordinates
            int tileX = (int)Math.Floor(worldPos.X / TerrainConstants.TileSize);
            int tileZ = (int)Math.Floor(worldPos.Z / TerrainConstants.TileSize);

            // Determine chunk
            int chunkX = (int)Math.Floor((float)tileX / TerrainConstants.ChunkTilesPerSide);
            int chunkZ = (int)Math.Floor((float)tileZ / TerrainConstants.ChunkTilesPerSide);
            int chunkY = (int)Math.Floor(worldPos.Y / TerrainConstants.ChunkWorldSize);

            // Local tile coordinates within chunk
            int localX = tileX - (chunkX * TerrainConstants.ChunkTilesPerSide);
            int localZ = tileZ - (chunkZ * TerrainConstants.ChunkTilesPerSide);

            // Bounds check
            if (localX < 0 || localX >= TerrainConstants.ChunkTilesPerSide ||
                localZ < 0 || localZ >= TerrainConstants.ChunkTilesPerSide)
            {
                return null;
            }

            return (new ChunkLocation(chunkX, chunkZ, chunkY), localX, localZ);
        }

        private bool ValidateTile(TerrainTile tile, int localX, int localZ)
        {
            // Basic validation (can be extended for game-specific rules)

            // Check height bounds
            if (tile.HeightNW < sbyte.MinValue || tile.HeightNW > sbyte.MaxValue)
            {
                return false;
            }

            // Check material ID
            if (tile.MaterialId > TerrainConstants.MaxMaterialId)
            {
                return false;
            }

            // Future: Add slope validation, blocked tile checks, etc.
            return true;
        }

        private sbyte ClampHeight(int height)
        {
            return (sbyte)Math.Clamp(height, sbyte.MinValue, sbyte.MaxValue);
        }

        private float CalculateFalloff(float normalizedDistance, BrushFalloff falloff)
        {
            return falloff switch
            {
                BrushFalloff.Linear => 1.0f - normalizedDistance,
                BrushFalloff.Smooth => (MathF.Cos(normalizedDistance * MathF.PI) + 1.0f) * 0.5f,
                BrushFalloff.Sharp => MathF.Pow(1.0f - normalizedDistance, 2.0f),
                _ => 1.0f
            };
        }

        public void Cleanup(World world)
        {
            _modificationQueue.Clear();
            _dirtyChunks.Clear();
        }

        private struct TileModification
        {
            public Position WorldPosition;
            public TerrainTile NewTile;
            public sbyte HeightDelta;
            public byte MaterialId;
            public ModificationType ModificationType;
        }

        private enum ModificationType
        {
            SetTile,
            AdjustHeight,
            ChangeMaterial
        }

        public enum BrushFalloff
        {
            Linear,
            Smooth,
            Sharp
        }
    }
}
