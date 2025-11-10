namespace UltraSim.ECS.Components
{
    public enum RenderPrototypeKind : byte
    {
        Sphere = 0,
        Cube = 1
    }

    /// <summary>
    /// Selects which mesh prototype to use for an entity's visual representation.
    /// </summary>
    public struct RenderPrototype
    {
        public RenderPrototypeKind Prototype;

        public RenderPrototype(RenderPrototypeKind prototype)
        {
            Prototype = prototype;
        }
    }
}
