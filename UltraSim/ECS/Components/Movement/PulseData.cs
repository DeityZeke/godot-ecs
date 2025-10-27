namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Component for entities that pulse in/out from origin.
    /// Used by pulsing movement systems.
    /// </summary>
    public struct PulseData
    {
        /// <summary>Movement speed</summary>
        public float Speed;

        /// <summary>How fast to oscillate (Hz)</summary>
        public float Frequency;

        /// <summary>Current phase in the sin wave (0 to 2PI)</summary>
        public float Phase;

        public PulseData(float speed, float frequency, float phase = 0f)
        {
            Speed = speed;
            Frequency = frequency;
            Phase = phase;
        }
    }
}
