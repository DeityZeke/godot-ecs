namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Base AI component for entities with artificial intelligence.
    /// Can be extended with more specific AI data.
    /// </summary>
    public struct AIComponent
    {
        public bool IsActive;
        public float TimeSinceLastDecision;
        public AIState CurrentState;
    }

    public enum AIState
    {
        Idle,
        Moving,
        Attacking,
        Fleeing
    }
}
