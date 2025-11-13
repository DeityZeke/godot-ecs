#nullable enable

using UltraSim.ECS.Components;

namespace Client.ECS.Components
{
    /// <summary>
    /// Base render chunk component shared by all rendering zones.
    ///
    /// ARCHITECTURE NOTE: Render chunks form a sliding window around the player.
    /// - Player is always at render position (0,0,0) in camera-relative coordinates
    /// - Location = relative offset from player (e.g., +3, 0, -1)
    /// - ServerChunkLocation = absolute world chunk this render chunk displays
    /// - As player moves, render chunks slide and update ServerChunkLocation references
    ///
    /// Zone assignment is determined by which tag component is present:
    /// - NearZoneTag: Full interactivity (MeshInstance3D + MultiMesh)
    /// - MidZoneTag: Visual only (MultiMesh batching)
    /// - FarZoneTag: Ultra low-res (billboards/impostors)
    ///
    /// This ECS pattern enables:
    /// - Automatic archetype-based filtering (no manual enum checks)
    /// - Parallel zone system execution (different archetypes = no conflicts)
    /// - Cache-friendly iteration (archetype groups chunks by zone)
    /// </summary>
    public struct RenderChunk
    {
        /// <summary>
        /// Relative position in render window (camera-relative coordinates).
        /// Example: (+3, 0, -1) means 3 chunks east, 0 up, 1 chunk south of player.
        /// This is RENDER SPACE, not world space.
        /// </summary>
        public ChunkLocation Location;

        /// <summary>
        /// Absolute server spatial chunk location this render chunk corresponds to.
        /// This is WORLD SPACE chunk location.
        /// Example: Player at chunk (10,0,5), Location (+2,0,0) → ServerChunkLocation (12,0,5)
        /// When player moves, this updates to point to new server chunk.
        /// </summary>
        public ChunkLocation ServerChunkLocation;

        /// <summary>World-space bounds for frustum culling.</summary>
        public ChunkBounds Bounds;

        /// <summary>Whether the chunk is currently visible (frustum culling result).</summary>
        public bool Visible;

        public RenderChunk(ChunkLocation location, ChunkLocation serverChunkLocation, ChunkBounds bounds, bool visible)
        {
            Location = location;
            ServerChunkLocation = serverChunkLocation;
            Bounds = bounds;
            Visible = visible;
        }

        public override string ToString()
            => $"RenderChunk[Render:{Location}, Server:{ServerChunkLocation}, Visible:{Visible}]";
    }
}
