#nullable enable

namespace Client.ECS.Components
{
    /// <summary>
    /// Tracks rendering metadata for a chunk multi-mesh or mesh instance.
    /// </summary>
    public struct RenderChunk
    {
        /// <summary>NodePath to the scene graph node rendering this chunk.</summary>
        public string NodePath;

        /// <summary>Total number of entities rendered within this chunk.</summary>
        public int RenderedEntityCount;

        /// <summary>Whether the chunk is currently visible.</summary>
        public bool IsVisible;

        public RenderChunk(string nodePath, int entityCount, bool isVisible)
        {
            NodePath = nodePath;
            RenderedEntityCount = entityCount;
            IsVisible = isVisible;
        }

        public override string ToString()
            => !string.IsNullOrEmpty(NodePath)
                ? $"RenderChunk[{NodePath}, Entities:{RenderedEntityCount}, Visible:{IsVisible}]"
                : "RenderChunk[No Node]";
    }
}
