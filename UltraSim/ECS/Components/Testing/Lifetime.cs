
namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Test component for stress testing.
    /// Entity will be destroyed after RemainingSeconds reaches zero.
    /// </summary>
    public struct Lifetime
    {
        public float RemainingSeconds;

        public Lifetime(float seconds)
        {
            RemainingSeconds = seconds;
        }

        public override string ToString() => $"{RemainingSeconds:F1}s";
    }
}