namespace UltraSim.ECS
{
    /// <summary>
    /// Example component definitions. All components must be structs.
    /// </summary>
    
    public struct Position 
    { 
        public float X, Y, Z;
        
        public Position(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    public struct Velocity 
    { 
        public float X, Y, Z;
        
        public Velocity(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    public struct RenderTag { }
    public struct Visible { }

    /// <summary>
    /// Component for entities that pulse in/out from origin
    /// </summary>
    public struct PulseData
    {
        public float Speed;      // Movement speed
        public float Frequency;  // How fast to oscillate
        public float Phase;      // Current phase in the sin wave (0 to 2PI)
    }
}
