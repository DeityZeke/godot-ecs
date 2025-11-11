#nullable enable

using System;

namespace UltraSim.ECS.Events
{
    /// <summary>
    /// Event args for entity batch creation events.
    /// Provides access to entities that were just created.
    /// </summary>
    public readonly struct EntityBatchCreatedEventArgs
    {
        public readonly Entity[] Entities;
        public readonly int StartIndex;
        public readonly int Count;

        public EntityBatchCreatedEventArgs(Entity[] entities, int startIndex, int count)
        {
            Entities = entities;
            StartIndex = startIndex;
            Count = count;
        }

        /// <summary>
        /// Get a ReadOnlySpan view of the created entities.
        /// </summary>
        public ReadOnlySpan<Entity> GetSpan() => new ReadOnlySpan<Entity>(Entities, StartIndex, Count);
    }

    /// <summary>
    /// Delegate for entity batch created events.
    /// </summary>
    public delegate void EntityBatchCreatedHandler(EntityBatchCreatedEventArgs args);
}
