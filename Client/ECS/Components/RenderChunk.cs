#nullable enable

using UltraSim.ECS.Components;

namespace Client.ECS.Components
{
    /// <summary>
    /// Render chunk component as described in UltraSim design doc.
    /// Tracks location, zone assignment, bounds, and visibility state.
    /// Updated by RenderChunkManager (zone tagging) and RenderVisibilitySystem (culling).
    /// </summary>
    public struct RenderChunk
    {
        /// <summary>Chunk location in world grid.</summary>
        public ChunkLocation Location;

        /// <summary>World-space bounds for frustum culling.</summary>
        public ChunkBounds Bounds;

        /// <summary>Assigned render zone (Near/Mid/Far/Culled) based on distance from camera.</summary>
        public ChunkZone Zone;

        /// <summary>Whether the chunk is currently visible (frustum culling result).</summary>
        public bool Visible;

        public RenderChunk(ChunkLocation location, ChunkBounds bounds, ChunkZone zone, bool visible)
        {
            Location = location;
            Bounds = bounds;
            Zone = zone;
            Visible = visible;
        }

        public override string ToString()
            => $"RenderChunk[{Location}, Zone:{Zone}, Visible:{Visible}]";
    }
}
