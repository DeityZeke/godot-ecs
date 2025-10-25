#nullable enable

using System;
using Godot;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Updates entity positions based on their velocity.
    /// </summary>
    public sealed class MovementSystem : BaseSystem
    {
        public override string Name => "MovementSystem";
        public override int SystemId => typeof(MovementSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(Velocity) };
        public override Type[] WriteSet { get; } = new[] { typeof(Position) };

        private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();

        private int _frameCount = 0;

        public override void OnInitialize(World world)
        {
            _cachedQuery = world.Query(typeof(Position), typeof(Velocity));
#if USE_DEBUG
            GD.Print("[MovementSystem] Initialized with cached query.");
#endif
        }

        public override void Update(World world, double delta)
        {
            _frameCount++;


            int archetypeCount = 0;
            int totalUpdated = 0;

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;

                archetypeCount++;

                Span<Position> posSpan = arch.GetComponentSpan<Position>(PosId);
                Span<Velocity> velSpan = arch.GetComponentSpan<Velocity>(VelId);

                int count = Math.Min(posSpan.Length, velSpan.Length);

                // Update all positions
                for (int i = 0; i < count; i++)
                {
                    posSpan[i].X += velSpan[i].X * (float)delta;
                    posSpan[i].Y += velSpan[i].Y * (float)delta;
                    posSpan[i].Z += velSpan[i].Z * (float)delta;
                    totalUpdated++;
                }
            }
        }
    }
}