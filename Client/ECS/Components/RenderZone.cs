#nullable enable

namespace Client.ECS.Components
{
    public enum RenderZoneType : byte
    {
        None = 0,
        Core = 1,   // Full detail, interactive
        Near = 2,   // MultiMesh, visual only
        Far = 3     // Impostor/billboard
    }

    /// <summary>
    /// Render zone assignment for an entity/chunk.
    /// Updated by HybridRenderSystem based on camera position.
    /// </summary>
    public struct RenderZone
    {
        /// <summary>The assigned zone.</summary>
        public RenderZoneType Zone;

        /// <summary>Frame index when this zone was last updated.</summary>
        public ulong LastUpdateFrame;

        public RenderZone(RenderZoneType zone, ulong frame)
        {
            Zone = zone;
            LastUpdateFrame = frame;
        }

        public bool IsRendered => Zone != RenderZoneType.None;

        public override string ToString()
            => $"RenderZone[{Zone}, Frame:{LastUpdateFrame}]";
    }
}
