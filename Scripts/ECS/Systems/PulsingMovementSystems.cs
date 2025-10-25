
#nullable enable

using System;
using Godot;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Updates velocity to create pulsing in/out movement from origin.
    /// Uses sin wave to oscillate between moving inward and outward.
    /// </summary>
    public sealed class PulsingMovementSystem : BaseSystem
    {
        public override string Name => "PulsingMovementSystem";
        public override int SystemId => typeof(PulsingMovementSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(PulseData) };
        public override Type[] WriteSet { get; } = new[] { typeof(Velocity), typeof(PulseData) };

        private const float TWO_PI = 6.28318530718f; // Cached constant (MathF.PI * 2)
        private readonly Random _random = new Random(); // Reuse Random instance

            private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
    private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();
    private static readonly int PulseId = ComponentTypeRegistry.GetId<PulseData>();

        public override void OnInitialize(World world)
        {
            _cachedQuery = world.Query(typeof(Position), typeof(Velocity), typeof(PulseData));
#if USE_DEBUG
            GD.Print("[PulsingMovementSystem] Initialized with cached query.");
#endif
        }

        public override void Update(World world, double delta)
        {
            //int posId = ComponentTypeRegistry.GetId<Position>();
            //int velId = ComponentTypeRegistry.GetId<Velocity>();
            //int pulseId = ComponentTypeRegistry.GetId<PulseData>();

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;

                Span<Position> posSpan = arch.GetComponentSpan<Position>(PosId);
                Span<Velocity> velSpan = arch.GetComponentSpan<Velocity>(VelId);
                Span<PulseData> pulseSpan = arch.GetComponentSpan<PulseData>(PulseId);

                int count = Math.Min(Math.Min(posSpan.Length, velSpan.Length), pulseSpan.Length);

                for (int i = 0; i < count; i++)
                {
                    ref var pos = ref posSpan[i];
                    ref var vel = ref velSpan[i];
                    ref var pulse = ref pulseSpan[i];

                    // Update phase
                    pulse.Phase += pulse.Frequency * (float)delta;
                    if (pulse.Phase > TWO_PI) // ✅ Use cached constant
                        pulse.Phase -= TWO_PI;

                    // Calculate direction to/from origin
                    float distToOrigin = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z);

                    if (distToOrigin > 0.01f) // Avoid division by zero
                    {
                        // Normalized direction from origin to position
                        float dirX = pos.X / distToOrigin;
                        float dirY = pos.Y / distToOrigin;
                        float dirZ = pos.Z / distToOrigin;

                        // Sin wave oscillates between -1 (move in) and 1 (move out)
                        float pulseDirection = MathF.Sin(pulse.Phase);

                        // Set velocity based on pulse direction
                        vel.X = dirX * pulseDirection * pulse.Speed;
                        vel.Y = dirY * pulseDirection * pulse.Speed;
                        vel.Z = dirZ * pulseDirection * pulse.Speed;
                    }
                    else
                    {
                        // At origin, give a small random push outward
                        // ✅ Use cached Random instance (not new Random()!)
                        vel.X = (float)(_random.NextDouble() - 0.5) * pulse.Speed;
                        vel.Y = (float)(_random.NextDouble() - 0.5) * pulse.Speed;
                        vel.Z = (float)(_random.NextDouble() - 0.5) * pulse.Speed;
                    }
                }
            }
        }
    }
}