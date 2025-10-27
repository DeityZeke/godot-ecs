
namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Test component for stress testing.
    /// Represents health points.
    /// </summary>
    public struct Health
    {
        public int Value;

        public Health(int value)
        {
            Value = value;
        }

        public override string ToString() => $"{Value} HP";
    }
}