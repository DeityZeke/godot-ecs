
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using UltraSim.ECS;
using UltraSim.ECS.Systems;
using UltraSim.IO;

namespace UltraSim
{
    /// <summary>
    /// Global event hub for UltraSim simulation lifecycle.
    /// Mirrors RunUO's EventSink pattern with pre/post events.
    /// </summary>
    public static class EventSink
    {
        // --- Static Lifecycle (global configuration / reflection) ---
        public static event Action? Configured;
        public static event Action? Initialized;

        // --- World Lifecycle ---
        public static event Action<World>? WorldStarted;
        public static event Action<World>? WorldInitialized;
        public static event Action<World>? WorldSystemsUpdated;
        public static event Action? WorldSave;
        public static event Action<IIOProfile>? WorldSaveRequested;
        public static event Action? WorldSaved;
        public static event Action? WorldLoad;
        public static event Action? WorldLoaded;
        public static event Action? WorldShutdown;

        // --- Entity Lifecycle ---
        /// <summary>
        /// Fired after a batch of entities has been created.
        /// Subscribers can perform initial setup/assignment on newly created entities.
        /// </summary>
        public static event EntityBatchCreatedHandler? EntityBatchCreated;

        /// <summary>
        /// Fired BEFORE a batch of entities is destroyed.
        /// Entities still exist, components are accessible.
        /// Use this to read component data before destruction.
        /// </summary>
        public static event EntityBatchDestroyRequestHandler? EntityBatchDestroyRequest;

        /// <summary>
        /// Fired AFTER a batch of entities has been destroyed.
        /// Entities are gone, components removed.
        /// Use this for cleanup that doesn't need component data.
        /// </summary>
        public static event EntityBatchDestroyedHandler? EntityBatchDestroyed;

        // --- System Lifecycle ---
        public static event Action<Type>? SystemRegistered;
        public static event Action<Type>? SystemUnregistered;
        public static event Action<BaseSystem>? SystemEnabled;
        public static event Action<BaseSystem>? SystemDisabled;

        // --- Dependency Resolution ---
        public static event Action<BaseSystem, BaseSystem>? DependencyResolved;

        #region Invoke Helpers

        public static void InvokeConfigured() => Configured?.Invoke();
        public static void InvokeInitialized() => Initialized?.Invoke();

        public static void InvokeWorldStarted(World w) => WorldStarted?.Invoke(w);
        public static void InvokeWorldInitialized(World w) => WorldInitialized?.Invoke(w);
        public static void InvokeWorldSystemsUpdated(World w) => WorldSystemsUpdated?.Invoke(w);
        public static void InvokeWorldSave() => WorldSave?.Invoke();
        public static void InvokeWorldSaveRequested(IIOProfile profile) => WorldSaveRequested?.Invoke(profile);
        public static void InvokeWorldSaved() => WorldSaved?.Invoke();
        public static void InvokeWorldLoad() => WorldLoad?.Invoke();
        public static void InvokeWorldLoaded() => WorldLoaded?.Invoke();
        public static void InvokeWorldShutdown() => WorldShutdown?.Invoke();

        public static void InvokeEntityBatchCreated(EntityBatchCreatedEventArgs args) => EntityBatchCreated?.Invoke(args);
        public static void InvokeEntityBatchDestroyRequest(EntityBatchDestroyRequestEventArgs args) => EntityBatchDestroyRequest?.Invoke(args);
        public static void InvokeEntityBatchDestroyed(EntityBatchDestroyedEventArgs args) => EntityBatchDestroyed?.Invoke(args);

        public static void InvokeSystemRegistered(Type t) => SystemRegistered?.Invoke(t);
        public static void InvokeSystemUnregistered(Type t) => SystemUnregistered?.Invoke(t);
        public static void InvokeSystemEnabled(BaseSystem s) => SystemEnabled?.Invoke(s);
        public static void InvokeSystemDisabled(BaseSystem s) => SystemDisabled?.Invoke(s);
        public static void InvokeDependencyResolved(BaseSystem sys, BaseSystem dep) => DependencyResolved?.Invoke(sys, dep);

        #endregion
    }

    #region Event Args

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

        public EntityBatchCreatedEventArgs(List<Entity> entities)
        {
            _entitiesList = entities ?? throw new ArgumentNullException(nameof(entities));
            _entitiesArray = null;
            _startIndex = 0;
            _count = entities.Count;
        }

        public EntityBatchCreatedEventArgs(Entity[] entities, int startIndex, int count)
        {
            _entitiesList = null;
            _entitiesArray = entities ?? throw new ArgumentNullException(nameof(entities));
            _startIndex = startIndex;
            _count = count;
        }

        public int Count => _count;

        public IReadOnlyList<Entity> Entities =>
            _entitiesList ?? (IReadOnlyList<Entity>)_entitiesArray!;

        public ReadOnlySpan<Entity> GetSpan()
        {
            if (_entitiesList != null)
            {
                var span = CollectionsMarshal.AsSpan(_entitiesList);
                return span.Slice(_startIndex, _count);
            }
            else
            {
                return new ReadOnlySpan<Entity>(_entitiesArray, _startIndex, _count);
            }
        }

        public List<Entity>? GetListUnsafe() => _entitiesList;
    }

    /// <summary>
    /// Event args for entity batch destruction REQUEST events.
    /// Fired BEFORE entities are destroyed - components are still accessible.
    /// Allows systems to read component data and perform cleanup.
    /// </summary>
    public readonly struct EntityBatchDestroyRequestEventArgs
    {
        private readonly List<Entity>? _entitiesList;
        private readonly Entity[]? _entitiesArray;
        private readonly int _startIndex;
        private readonly int _count;

        public EntityBatchDestroyRequestEventArgs(List<Entity> entities)
        {
            _entitiesList = entities ?? throw new ArgumentNullException(nameof(entities));
            _entitiesArray = null;
            _startIndex = 0;
            _count = entities.Count;
        }

        public EntityBatchDestroyRequestEventArgs(Entity[] entities, int startIndex, int count)
        {
            _entitiesList = null;
            _entitiesArray = entities ?? throw new ArgumentNullException(nameof(entities));
            _startIndex = startIndex;
            _count = count;
        }

        public int Count => _count;

        public IReadOnlyList<Entity> Entities =>
            _entitiesList ?? (IReadOnlyList<Entity>)_entitiesArray!;

        public ReadOnlySpan<Entity> GetSpan()
        {
            if (_entitiesList != null)
            {
                var span = CollectionsMarshal.AsSpan(_entitiesList);
                return span.Slice(_startIndex, _count);
            }
            else
            {
                return new ReadOnlySpan<Entity>(_entitiesArray, _startIndex, _count);
            }
        }

        public List<Entity>? GetListUnsafe() => _entitiesList;
    }

    /// <summary>
    /// Event args for entity batch DESTROYED events.
    /// Fired AFTER entities are destroyed - components are removed.
    /// </summary>
    public readonly struct EntityBatchDestroyedEventArgs
    {
        private readonly List<Entity>? _entitiesList;
        private readonly Entity[]? _entitiesArray;
        private readonly int _startIndex;
        private readonly int _count;

        public EntityBatchDestroyedEventArgs(List<Entity> entities)
        {
            _entitiesList = entities ?? throw new ArgumentNullException(nameof(entities));
            _entitiesArray = null;
            _startIndex = 0;
            _count = entities.Count;
        }

        public EntityBatchDestroyedEventArgs(Entity[] entities, int startIndex, int count)
        {
            _entitiesList = null;
            _entitiesArray = entities ?? throw new ArgumentNullException(nameof(entities));
            _startIndex = startIndex;
            _count = count;
        }

        public int Count => _count;

        public IReadOnlyList<Entity> Entities =>
            _entitiesList ?? (IReadOnlyList<Entity>)_entitiesArray!;

        public ReadOnlySpan<Entity> GetSpan()
        {
            if (_entitiesList != null)
            {
                var span = CollectionsMarshal.AsSpan(_entitiesList);
                return span.Slice(_startIndex, _count);
            }
            else
            {
                return new ReadOnlySpan<Entity>(_entitiesArray, _startIndex, _count);
            }
        }

        public List<Entity>? GetListUnsafe() => _entitiesList;
    }

    #endregion

    #region Delegates

    public delegate void EntityBatchCreatedHandler(EntityBatchCreatedEventArgs args);
    public delegate void EntityBatchDestroyRequestHandler(EntityBatchDestroyRequestEventArgs args);
    public delegate void EntityBatchDestroyedHandler(EntityBatchDestroyedEventArgs args);

    #endregion
}
