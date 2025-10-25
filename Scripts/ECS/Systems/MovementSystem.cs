#nullable enable

using System;
using Godot;
using UltraSim.Scripts.ECS.Systems.Settings;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Updates entity positions based on their velocity.
    /// SIMPLE VERSION - Just speed multiplier and optional features.
    /// </summary>
    public sealed class MovementSystem : BaseSystem
    {
        #region Settings

        public class Settings : BaseSettings
        {
            public FloatSetting GlobalSpeedMultiplier { get; private set; }
            public BoolSetting FreezeMovement { get; private set; }

            public Settings()
            {
                GlobalSpeedMultiplier = RegisterFloat("Global Speed Multiplier", 1.0f, 0.0f, 10.0f, 0.1f,
                    tooltip: "Multiplier applied to all movement (0 = frozen, 1 = normal, 2 = double speed)");

                FreezeMovement = RegisterBool("Freeze Movement", false,
                    tooltip: "Completely freeze all movement (useful for debugging)");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override BaseSettings? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "MovementSystem";
        public override int SystemId => typeof(MovementSystem).GetHashCode();
        public override Type[] ReadSet { get; } = new[] { typeof(Position), typeof(Velocity) };
        public override Type[] WriteSet { get; } = new[] { typeof(Position) };

        private static readonly int PosId = ComponentTypeRegistry.GetId<Position>();
        private static readonly int VelId = ComponentTypeRegistry.GetId<Velocity>();

        private int _frameCount = 0;

        public override void OnInitialize(World world)
        {
            base.OnInitialize(world);
            
            _cachedQuery = world.Query(typeof(Position), typeof(Velocity));
            
            // Load settings
            LoadSettings();
            
#if USE_DEBUG
            GD.Print("[MovementSystem] Initialized with cached query.");
            GD.Print($"[MovementSystem] Settings - Speed: {SystemSettings.GlobalSpeedMultiplier.Value}, " +
                     $"Frozen: {SystemSettings.FreezeMovement.Value}");
#endif
        }

        public override void Update(World world, double delta)
        {
            _frameCount++;

            // Early exit if movement is frozen
            if (SystemSettings.FreezeMovement.Value)
                return;

            // Read speed multiplier
            float speedMultiplier = SystemSettings.GlobalSpeedMultiplier.Value;

            // If speed is zero or negative, don't process
            if (speedMultiplier <= 0.0f)
                return;

            int archetypeCount = 0;
            int totalUpdated = 0;

            foreach (var arch in _cachedQuery!)
            {
                if (arch.Count == 0) continue;

                archetypeCount++;

                Span<Position> posSpan = arch.GetComponentSpan<Position>(PosId);
                Span<Velocity> velSpan = arch.GetComponentSpan<Velocity>(VelId);

                int count = Math.Min(posSpan.Length, velSpan.Length);

                // Calculate adjusted delta once
                float adjustedDelta = (float)delta * speedMultiplier;

                // Update all positions
                for (int i = 0; i < count; i++)
                {
                    posSpan[i].X += velSpan[i].X * adjustedDelta;
                    posSpan[i].Y += velSpan[i].Y * adjustedDelta;
                    posSpan[i].Z += velSpan[i].Z * adjustedDelta;
                    totalUpdated++;
                }
            }

#if USE_DEBUG
            if (_frameCount % 300 == 0) // Every ~5 seconds at 60fps
            {
                Statistics.SetCustomMetric("Entities Updated", totalUpdated);
                Statistics.SetCustomMetric("Archetypes Processed", archetypeCount);
            }
#endif
        }
    }
}