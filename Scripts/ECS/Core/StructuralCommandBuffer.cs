#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Batches structural changes (entity creation/destruction) for efficient processing.
    /// Creates entities with all components in one operation - NO archetype thrashing!
    /// </summary>
    public sealed class StructuralCommandBuffer
    {
        private struct EntityCreationCommand
        {
            public List<(int typeId, object value)> Components;
        }

        private readonly List<EntityCreationCommand> _creates = new();
        private readonly List<Entity> _destroys = new();

        /// <summary>
        /// Queues an entity for creation with all specified components.
        /// Entity will be created in the optimal archetype in one operation.
        /// </summary>
        public void CreateEntity(Action<EntityBuilder> setup)
        {
            var builder = new EntityBuilder();
            setup(builder);

            _creates.Add(new EntityCreationCommand
            {
                Components = new List<(int typeId, object value)>(builder.GetComponents())
            });
        }

        /// <summary>
        /// Queues an entity for destruction.
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            _destroys.Add(entity);
        }

        /// <summary>
        /// Applies all queued structural changes to the world.
        /// This is WHERE THE MAGIC HAPPENS - creates entities with ALL components at once!
        /// </summary>
        public void Apply(World world)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Process all entity creations
            int created = 0;
            //foreach (var cmd in _creates)
            foreach (ref var cmd in CollectionsMarshal.AsSpan(_creates))
            {
                if (cmd.Components.Count == 0)
                {
                    // Empty entity
                    world.CreateEntity();
                    created++;
                    continue;
                }

                // Build signature from all components
                var signature = new ComponentSignature();
                //foreach (var (typeId, _) in cmd.Components)
                //foreach (var (typeId, _) in CollectionsMarshal.AsSpan(cmd.Components))
                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                    signature = signature.Add(component.typeId);

                // Create entity directly in target archetype (ONE archetype move!)
                var entity = world.CreateEntityWithSignature(signature);

                // Set all component values
                if (!world.TryGetEntityLocation(entity, out var archetype, out var slot))
                {
                    GD.PrintErr($"[StructuralCommandBuffer] Failed to get location for entity {entity}");
                    continue;
                }

                //foreach (var (typeId, value) in cmd.Components)
                //foreach (var (typeId, value) in CollectionsMarshal.AsSpan(cmd.Components))
                //{
                //}
                foreach (ref readonly var component in CollectionsMarshal.AsSpan(cmd.Components))
                    archetype.SetComponentValueBoxed(component.typeId, slot, component.value);


                created++;
            }

            // Process all entity destructions
            int destroyed = 0;
            foreach (var entity in _destroys)
            {
                world.DestroyEntity(entity);
                destroyed++;
            }

            sw.Stop();

            if (created > 0 || destroyed > 0)
            {
                GD.Print($"[StructuralCommandBuffer] Applied: {created} created, {destroyed} destroyed in {sw.Elapsed.TotalMilliseconds:F3}ms");
            }

            Clear();
        }

        /// <summary>
        /// Clears all queued commands without applying them.
        /// </summary>
        public void Clear()
        {
            _creates.Clear();
            _destroys.Clear();
        }

        /// <summary>
        /// Returns true if there are any pending commands.
        /// </summary>
        public bool HasCommands => _creates.Count > 0 || _destroys.Count > 0;

        /// <summary>
        /// Number of queued entity creations.
        /// </summary>
        public int CreateCount => _creates.Count;

        /// <summary>
        /// Number of queued entity destructions.
        /// </summary>
        public int DestroyCount => _destroys.Count;
    }
}