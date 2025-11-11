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

    /// <summary>
    /// Tag component indicating a render chunk entity is pooled (inactive, ready for reuse).
    /// Pooled chunks have no zone tag and are invisible to zone render systems.
    ///
    /// ECS Pattern: Using a tag instead of Queue<Entity> allows:
    /// - Query-based pool access (no manual sync)
    /// - Archetype-based filtering (pooled vs active)
    /// - Clear semantic meaning in ECS
    /// - Scalable pattern (BulletPoolTag, ParticlePoolTag, etc.)
    /// </summary>
    public struct RenderChunkPoolTag { }
}
