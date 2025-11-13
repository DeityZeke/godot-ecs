#nullable enable

using System;

using Godot;

using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Chunk;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// System for spawning entities on-demand via button clicks in the control panel.
    /// This avoids "frame 0 stutter" by allowing entities to be created when needed.
    /// </summary>
    public sealed class EntitySpawnerSystem : BaseSystem
    {
        #region Settings

        public enum SpawnVisualMode
        {
            DynamicOnly,
            StaticOnly,
            Random
        }

        public sealed class Settings : SettingsManager
        {
            public FloatSetting SpawnRadius { get; private set; }
            public FloatSetting MinSpeed { get; private set; }
            public FloatSetting MaxSpeed { get; private set; }
            public FloatSetting PulseFrequencyMin { get; private set; }
            public FloatSetting PulseFrequencyMax { get; private set; }
            public EnumSetting<SpawnVisualMode> SpawnVisuals { get; private set; }

            public ButtonSetting Spawn100 { get; private set; }
            public ButtonSetting Spawn1000 { get; private set; }
            public ButtonSetting Spawn10000 { get; private set; }
            public ButtonSetting Spawn100000 { get; private set; }
            public ButtonSetting Spawn1000000 { get; private set; }
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

                SpawnVisuals = RegisterEnum("Spawn Visual Mode", SpawnVisualMode.DynamicOnly,
                    tooltip: "Dynamic = mesh instances (sphere), Static = MultiMesh cubes, Random = mix per entity");

                RegisterString("", ""); // Spacer

                Spawn100 = RegisterButton("Spawn 100 Entities",
                    tooltip: "Spawn 100 entities with current settings");

                Spawn1000 = RegisterButton("Spawn 1,000 Entities",
                    tooltip: "Spawn 1,000 entities with current settings");

                Spawn10000 = RegisterButton("Spawn 10,000 Entities",
                    tooltip: "Spawn 10,000 entities with current settings");

                Spawn100000 = RegisterButton("Spawn 100,000 Entities",
                    tooltip: "Spawn 100,000 entities with current settings");

                Spawn1000000 = RegisterButton("Spawn 1,000,000 Entities",
                    tooltip: "Spawn 1,000,000 entities with current settings");

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
        public override Type[] WriteSet { get; } = new[] { typeof(Position), typeof(ChunkOwner) };
        public override TickRate Rate => TickRate.Manual; // Only runs when triggered

        private World? _world;

        public override void OnInitialize(World world)
        {
            _world = world;

            // Subscribe to button clicks
            SystemSettings.Spawn100.Clicked += () => SpawnEntities(100);
            SystemSettings.Spawn1000.Clicked += () => SpawnEntities(1000);
            SystemSettings.Spawn10000.Clicked += () => SpawnEntities(10000);
            SystemSettings.Spawn100000.Clicked += () => SpawnEntities(100000);
            SystemSettings.Spawn1000000.Clicked += () => SpawnEntities(1000000);
            SystemSettings.ClearAll.Clicked += ClearAllEntities;

            Logging.Log($"[{Name}] Initialized with spawn radius {SystemSettings.SpawnRadius.Value}");
            Logging.Log($"[{Name}] Note: Entities will be assigned to chunks by ChunkSystem");
        }

        public override void Update(World world, double delta)
        {
            // This system doesn't run on regular ticks - it's manual/event-driven
        }

        private void SpawnEntities(int count)
        {
            if (_world == null)
            {
                Logging.Log($"[{Name}] Cannot spawn - world is null!", LogSeverity.Error);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            float radius = SystemSettings.SpawnRadius.Value;
            float minSpeed = SystemSettings.MinSpeed.Value;
            float maxSpeed = SystemSettings.MaxSpeed.Value;
            float minFreq = SystemSettings.PulseFrequencyMin.Value;
            float maxFreq = SystemSettings.PulseFrequencyMax.Value;

            Logging.Log($"[{Name}] Spawning {count} entities...");

            for (int i = 0; i < count; i++)
            {
                // Random position in sphere
                Vector3 randomPos = Utilities.RandomPointInSphere(radius);

                // Random speed and pulse frequency
                float speed = Utilities.RandomRange(minSpeed, maxSpeed);
                float frequency = Utilities.RandomRange(minFreq, maxFreq);
                float phaseOffset = Utilities.RandomRange(0f, Utilities.TWO_PI);

                // Create entity with ALL components at once (NO archetype thrashing!)
                // Using EntityBuilder + queue ensures proper event firing and deferred processing
                // ChunkSystem will automatically assign entities to chunks based on Position
                var builder = _world.CreateEntityBuilder();

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

                bool spawnStatic = ShouldSpawnStatic(SystemSettings.SpawnVisuals.Value);
                if (spawnStatic)
                {
                    builder.Add(new StaticRenderTag { });
                    builder.Add(new RenderPrototype(RenderPrototypeKind.Cube));
                }
                else
                {
                    builder.Add(new RenderPrototype(RenderPrototypeKind.Sphere));
                }

                // Enqueue for deferred creation (will be processed in next World.Tick)
                _world.EnqueueCreateEntity(builder);
            }

            sw.Stop();
            double enqueueTime = sw.Elapsed.TotalMilliseconds;

            Logging.Log($"[{Name}] Enqueued {count} entities in {enqueueTime:F3}ms");
            Logging.Log($"[{Name}] Entities will be created on next World.Tick, then assigned to chunks by ChunkSystem");
        }

        private void ClearAllEntities()
        {
            if (_world == null)
            {
                Logging.Log($"[{Name}] Cannot clear - world is null!", LogSeverity.Error);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int destroyed = 0;

            // Only destroy entities spawned for rendering (RenderTag is added during Spawn)
            var archetypes = _world.QueryArchetypes(typeof(RenderTag));
            foreach (var archetype in archetypes)
            {
                var entities = archetype.GetEntityArray();
                foreach (var entity in entities)
                {
                    _world.EnqueueDestroyEntity(entity);
                    destroyed++;
                }
            }

            sw.Stop();
            Logging.Log($"[{Name}] Cleared {destroyed} spawned entities in {sw.Elapsed.TotalMilliseconds:F3}ms");
        }

        private static bool ShouldSpawnStatic(SpawnVisualMode mode)
        {
            return mode switch
            {
                SpawnVisualMode.StaticOnly => true,
                SpawnVisualMode.DynamicOnly => false,
                SpawnVisualMode.Random => Utilities.RandomFloat() < 0.5f,
                _ => false
            };
        }
    }
}
