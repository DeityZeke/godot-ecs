#nullable enable

using System;

using Godot;

using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// System for spawning entities on-demand via button clicks in the control panel.
    /// This avoids "frame 0 stutter" by allowing entities to be created when needed.
    /// </summary>
    public sealed class EntitySpawnerSystem : BaseSystem
    {
        #region Settings

        public sealed class Settings : SettingsManager
        {
            public FloatSetting SpawnRadius { get; private set; }
            public FloatSetting MinSpeed { get; private set; }
            public FloatSetting MaxSpeed { get; private set; }
            public FloatSetting PulseFrequencyMin { get; private set; }
            public FloatSetting PulseFrequencyMax { get; private set; }

            public ButtonSetting Spawn100 { get; private set; }
            public ButtonSetting Spawn1000 { get; private set; }
            public ButtonSetting Spawn10000 { get; private set; }
            public ButtonSetting Spawn100000 { get; private set; }
            public ButtonSetting ClearAll { get; private set; }

            public Settings()
            {
                SpawnRadius = RegisterFloat("Spawn Radius", 50f, 1f, 500f, 1f,
                    tooltip: "Radius of the sphere in which entities spawn");

                MinSpeed = RegisterFloat("Min Speed", 2f, 0f, 50f, 0.5f,
                    tooltip: "Minimum movement speed for spawned entities");

                MaxSpeed = RegisterFloat("Max Speed", 8f, 0f, 50f, 0.5f,
                    tooltip: "Maximum movement speed for spawned entities");

                PulseFrequencyMin = RegisterFloat("Min Pulse Frequency", 0.5f, 0.1f, 10f, 0.1f,
                    tooltip: "Minimum pulse frequency (Hz) for spawned entities");

                PulseFrequencyMax = RegisterFloat("Max Pulse Frequency", 2f, 0.1f, 10f, 0.1f,
                    tooltip: "Maximum pulse frequency (Hz) for spawned entities");

                RegisterString("", ""); // Spacer

                Spawn100 = RegisterButton("Spawn 100 Entities",
                    tooltip: "Spawn 100 entities with current settings");

                Spawn1000 = RegisterButton("Spawn 1,000 Entities",
                    tooltip: "Spawn 1,000 entities with current settings");

                Spawn10000 = RegisterButton("Spawn 10,000 Entities",
                    tooltip: "Spawn 10,000 entities with current settings");

                Spawn100000 = RegisterButton("Spawn 100,000 Entities",
                    tooltip: "Spawn 100,000 entities with current settings");

                ClearAll = RegisterButton("Clear All Entities",
                    tooltip: "Destroy all entities in the world");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SettingsManager? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Entity Spawner";
        public override int SystemId => typeof(EntitySpawnerSystem).GetHashCode();
        public override Type[] ReadSet { get; } = Array.Empty<Type>();
        public override Type[] WriteSet { get; } = Array.Empty<Type>();
        public override TickRate Rate => TickRate.Manual; // Only runs when triggered

        private World? _world;

        public override void OnInitialize(World world)
        {
            _world = world;
            LoadSettings();

            // Subscribe to button clicks
            SystemSettings.Spawn100.Clicked += () => SpawnEntities(100);
            SystemSettings.Spawn1000.Clicked += () => SpawnEntities(1000);
            SystemSettings.Spawn10000.Clicked += () => SpawnEntities(10000);
            SystemSettings.Spawn100000.Clicked += () => SpawnEntities(100000);
            SystemSettings.ClearAll.Clicked += ClearAllEntities;

            Logging.Logger.Log($"[{Name}] Initialized with spawn radius {SystemSettings.SpawnRadius.Value}");
        }

        public override void Update(World world, double delta)
        {
            // This system doesn't run on regular ticks - it's manual/event-driven
        }

        private void SpawnEntities(int count)
        {
            if (_world == null)
            {
                Logging.Logger.Log($"[{Name}] Cannot spawn - world is null!", Logging.LogSeverity.Error);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var buffer = new CommandBuffer();

            float radius = SystemSettings.SpawnRadius.Value;
            float minSpeed = SystemSettings.MinSpeed.Value;
            float maxSpeed = SystemSettings.MaxSpeed.Value;
            float minFreq = SystemSettings.PulseFrequencyMin.Value;
            float maxFreq = SystemSettings.PulseFrequencyMax.Value;

            Logging.Logger.Log($"[{Name}] Spawning {count} entities...");

            for (int i = 0; i < count; i++)
            {
                // Random position in sphere
                Vector3 randomPos = Utilities.RandomPointInSphere(radius);

                // Random speed and pulse frequency
                float speed = Utilities.RandomRange(minSpeed, maxSpeed);
                float frequency = Utilities.RandomRange(minFreq, maxFreq);
                float phaseOffset = Utilities.RandomRange(0f, Utilities.TWO_PI);

                // Create entity with ALL components at once (NO archetype thrashing!)
                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position
                    {
                        X = randomPos.X,
                        Y = randomPos.Y,
                        Z = randomPos.Z
                    });

                    builder.Add(new Velocity
                    {
                        X = 0f,
                        Y = 0f,
                        Z = 0f
                    });

                    builder.Add(new PulseData
                    {
                        Speed = speed,
                        Frequency = frequency,
                        Phase = phaseOffset
                    });

                    builder.Add(new RenderTag { });
                    builder.Add(new Visible { });
                });
            }

            sw.Stop();
            double queueTime = sw.Elapsed.TotalMilliseconds;

            // Apply the buffer
            sw.Restart();
            buffer.Apply(_world);
            sw.Stop();
            double applyTime = sw.Elapsed.TotalMilliseconds;

            Logging.Logger.Log($"[{Name}] Spawned {count} entities - Queue: {queueTime:F3}ms, Apply: {applyTime:F3}ms, Total: {queueTime + applyTime:F3}ms");
        }

        private void ClearAllEntities()
        {
            if (_world == null)
            {
                Logging.Logger.Log($"[{Name}] Cannot clear - world is null!", Logging.LogSeverity.Error);
                return;
            }

            int entityCount = _world.EntityCount;
            Logging.Logger.Log($"[{Name}] Clearing {entityCount} entities...");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Destroy all entities by querying all archetypes
            var archetypes = _world.GetArchetypes();
            foreach (var archetype in archetypes)
            {
                var entities = archetype.GetEntityArray();
                foreach (var entity in entities)
                {
                    _world.EnqueueDestroyEntity(entity);
                }
            }

            sw.Stop();
            Logging.Logger.Log($"[{Name}] Cleared {entityCount} entities in {sw.Elapsed.TotalMilliseconds:F3}ms");
        }
    }
}
