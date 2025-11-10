#nullable enable

using System;

namespace UltraSim.Terrain
{
    /// <summary>
    /// Authoring unit for terrain editing or procedural generation.
    /// Holds up to four corner heights (0.5m increments), material id, and tile flags.
    /// </summary>
    public struct TerrainTile
    {
        /// <summary>Heights are stored as signed bytes and interpreted as (value * 0.5m).</summary>
        public sbyte HeightNW;
        public sbyte HeightNE;
        public sbyte HeightSW;
        public sbyte HeightSE;

        /// <summary>Material/atlas region ID (0-255).</summary>
        public byte MaterialId;

        public TerrainTileFlags Flags;

        public static readonly TerrainTile Empty = new()
        {
            HeightNW = 0,
            HeightNE = 0,
            HeightSW = 0,
            HeightSE = 0,
            MaterialId = 0,
            Flags = TerrainTileFlags.None
        };

        public bool IsWalkable => (Flags & TerrainTileFlags.Blocked) == 0;

        public void SetUniformHeight(sbyte height)
        {
            HeightNW = height;
            HeightNE = height;
            HeightSW = height;
            HeightSE = height;
        }

        public void ClampHeights(sbyte min, sbyte max)
        {
            HeightNW = Clamp(HeightNW, min, max);
            HeightNE = Clamp(HeightNE, min, max);
            HeightSW = Clamp(HeightSW, min, max);
            HeightSE = Clamp(HeightSE, min, max);
        }

        private static sbyte Clamp(sbyte value, sbyte min, sbyte max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    [Flags]
    public enum TerrainTileFlags : byte
    {
        None = 0,
        Blocked = 1 << 0,
        Cliff = 1 << 1,
        Water = 1 << 2,
        DecorationAnchor = 1 << 3
    }
}
