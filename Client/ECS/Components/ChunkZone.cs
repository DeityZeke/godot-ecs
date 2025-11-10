#nullable enable

namespace Client.ECS.Components
{
    /// <summary>
    /// Render zone assignment for a chunk based on distance from camera.
    /// Matches UltraSim design doc terminology:
    /// - Near: Immediate player bubble (full interactivity) - MeshInstances + MultiMesh
    /// - Mid: Next ring (visual only) - MultiMesh batching only
    /// - Far: Outer ring (ultra low-res) - Impostors/billboards
    /// - Culled: Outside render distance
    /// </summary>
    public enum ChunkZone : byte
    {
        /// <summary>Outside render distance - not visible.</summary>
        Culled = 0,

        /// <summary>Immediate player bubble (e.g., 3x3x3 chunks). Full interactivity with individual MeshInstance3D + MultiMesh for statics.</summary>
        Near = 1,

        /// <summary>Next ring (e.g., +2 chunk radius). MultiMesh batching only for visual representation.</summary>
        Mid = 2,

        /// <summary>Outer ring (optional). Billboard/impostor rendering for ultra-low detail.</summary>
        Far = 3
    }
}
