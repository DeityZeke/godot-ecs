namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Marks an entity as a static visual.
    /// Static visuals stay batched inside MultiMesh, even when inside the core bubble.
    /// </summary>
    public struct StaticRenderTag { }
}
