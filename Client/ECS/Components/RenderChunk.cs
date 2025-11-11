#nullable enable

using UltraSim.ECS.Components;

namespace Client.ECS.Components
{
    /// <summary>
    /// Base render chunk component shared by all rendering zones.
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
        /// <summary>Chunk location in world grid.</summary>
        public ChunkLocation Location;

        /// <summary>World-space bounds for frustum culling.</summary>
        public ChunkBounds Bounds;

        /// <summary>Whether the chunk is currently visible (frustum culling result).</summary>
        public bool Visible;

        public RenderChunk(ChunkLocation location, ChunkBounds bounds, bool visible)
        {
            Location = location;
            Bounds = bounds;
            Visible = visible;
        }

        public override string ToString()
            => $"RenderChunk[{Location}, Visible:{Visible}]";
    }
}
