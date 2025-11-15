
namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Test component for stress testing.
    /// Represents temperature in degrees Celsius.
    /// </summary>
    public struct Temperature
    {
        public float Value;

        public Temperature(float value)
        {
            Value = value;
        }

        public override string ToString() => $"{Value:F1}°C";
    }
}
