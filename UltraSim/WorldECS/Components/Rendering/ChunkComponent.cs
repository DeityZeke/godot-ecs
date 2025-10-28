namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Identifies which spatial chunk an entity belongs to.
    /// Used for spatial partitioning and culling optimization.
    /// </summary>
    public struct ChunkComponent
    {
        public int LOD;
        public bool IsActive;
    }
}
