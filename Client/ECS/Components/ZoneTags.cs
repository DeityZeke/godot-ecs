#nullable enable

namespace Client.ECS.Components
{
    /// <summary>
    /// Tag component indicating a chunk is in the Near rendering zone.
    /// Near zone: Immediate player bubble (full interactivity) - MeshInstance3D + MultiMesh.
    ///
    /// ECS Pattern: Using tag components instead of enums allows different archetypes per zone.
    /// This enables:
    /// - Automatic filtering (query by tag, not manual enum check)
    /// - Parallel execution (zone systems query different archetypes = no conflicts)
    /// - Cache-friendly iteration (archetype groups chunks by zone)
    /// </summary>
    public struct NearZoneTag { }

    /// <summary>
    /// Tag component indicating a chunk is in the Mid rendering zone.
    /// Mid zone: Next ring (visual only) - MultiMesh batching only.
    /// </summary>
    public struct MidZoneTag { }

    /// <summary>
    /// Tag component indicating a chunk is in the Far rendering zone.
    /// Far zone: Outer ring (ultra low-res) - Impostors/billboards.
    /// </summary>
    public struct FarZoneTag { }
}
