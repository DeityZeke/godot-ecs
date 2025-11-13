#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UltraSim.ECS.Events
{
    /// <summary>
    /// Event args for entity batch creation events.
    /// Provides access to entities that were just created.
    /// Uses List&lt;Entity&gt; internally for zero-allocation event firing.
    /// </summary>
    public readonly struct EntityBatchCreatedEventArgs
    {
        private readonly List<Entity>? _entitiesList;
        private readonly Entity[]? _entitiesArray;
        private readonly int _startIndex;
        private readonly int _count;

        /// <summary>
        /// Creates event args from a List (zero-allocation path).
        /// </summary>
        public EntityBatchCreatedEventArgs(List<Entity> entities)
        {
            _entitiesList = entities ?? throw new ArgumentNullException(nameof(entities));
            _entitiesArray = null;
            _startIndex = 0;
            _count = entities.Count;
        }

        /// <summary>
        /// Creates event args from an array (backward compatibility).
        /// </summary>
        public EntityBatchCreatedEventArgs(Entity[] entities, int startIndex, int count)
        {
            _entitiesList = null;
            _entitiesArray = entities ?? throw new ArgumentNullException(nameof(entities));
            _startIndex = startIndex;
            _count = count;
        }

        /// <summary>
        /// Number of entities in this batch.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the entities as IReadOnlyList for iteration.
        /// For zero-allocation iteration, use GetSpan() instead.
        /// </summary>
        public IReadOnlyList<Entity> Entities =>
            _entitiesList ?? (IReadOnlyList<Entity>)_entitiesArray!;

        /// <summary>
        /// Get a ReadOnlySpan view of the created entities.
        /// ZERO ALLOCATION - Uses CollectionsMarshal.AsSpan when source is List.
        /// </summary>
        public ReadOnlySpan<Entity> GetSpan()
        {
            if (_entitiesList != null)
            {
                // Zero-allocation path: Direct access to List's internal buffer
                var span = CollectionsMarshal.AsSpan(_entitiesList);
                return span.Slice(_startIndex, _count);
            }
            else
            {
                // Array path (backward compatibility)
                return new ReadOnlySpan<Entity>(_entitiesArray, _startIndex, _count);
            }
        }

        /// <summary>
        /// Get direct access to the underlying List (if available).
        /// Returns null if event was created from an array.
        /// </summary>
        public List<Entity>? GetListUnsafe() => _entitiesList;
    }

    /// <summary>
    /// Delegate for entity batch created events.
    /// </summary>
    public delegate void EntityBatchCreatedHandler(EntityBatchCreatedEventArgs args);
}
