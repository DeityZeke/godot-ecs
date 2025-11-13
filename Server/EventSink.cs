#nullable enable

using System;

using UltraSim.ECS;

namespace UltraSim.Server
{
    /// <summary>
    /// Event args for entity batch processing events.
    /// Provides access to a batch of entities that were processed.
    /// </summary>
    public readonly struct EntityBatchProcessedEventArgs
    {
        public readonly Entity[] Entities;
        public readonly int StartIndex;
        public readonly int Count;

        public EntityBatchProcessedEventArgs(Entity[] entities, int startIndex, int count)
        {
            Entities = entities;
            StartIndex = startIndex;
            Count = count;
        }

        /// <summary>
        /// Get a ReadOnlySpan view of the batch entities.
        /// </summary>
        public ReadOnlySpan<Entity> GetSpan() => new ReadOnlySpan<Entity>(Entities, StartIndex, Count);
    }

    /// <summary>
    /// Delegate for entity batch processed events.
    /// </summary>
    public delegate void EntityBatchProcessedHandler(EntityBatchProcessedEventArgs args);

    /// <summary>
    /// Server-side event hub for gameplay events.
    /// Events specific to server-side game logic (movement, AI, combat, etc).
    /// </summary>
    public static class EventSink
    {
        // --- Entity Processing Events ---
        /// <summary>
        /// Fired after a batch of entities has been processed by a system.
        /// Currently fired by OptimizedMovementSystem after updating entity positions.
        /// Used by ChunkSystem to detect entities that moved between chunks.
        /// </summary>
        public static event EntityBatchProcessedHandler? EntityBatchProcessed;

        #region Invoke Helpers

        public static void InvokeEntityBatchProcessed(EntityBatchProcessedEventArgs args)
            => EntityBatchProcessed?.Invoke(args);

        #endregion
    }
}
