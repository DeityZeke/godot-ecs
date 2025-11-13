
#nullable enable

using System;

using UltraSim.ECS;
using UltraSim.ECS.Systems;
using UltraSim.ECS.Events;

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
        public static event Action<World>? WorldEntitiesSpawned;
        public static event Action<World>? WorldSystemsUpdated;
        public static event Action? WorldSave;
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
        /// Fired after a batch of entities has been destroyed.
        /// Subscribers can perform cleanup/UN-assignment for destroyed entities.
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
        public static void InvokeWorldEntitiesSpawned(World w) => WorldEntitiesSpawned?.Invoke(w);
        public static void InvokeWorldSystemsUpdated(World w) => WorldSystemsUpdated?.Invoke(w);
        public static void InvokeWorldSave() => WorldSave?.Invoke();
        public static void InvokeWorldSaved() => WorldSaved?.Invoke();
        public static void InvokeWorldLoad() => WorldLoad?.Invoke();
        public static void InvokeWorldLoaded() => WorldLoaded?.Invoke();
        public static void InvokeWorldShutdown() => WorldShutdown?.Invoke();

        public static void InvokeEntityBatchCreated(EntityBatchCreatedEventArgs args) => EntityBatchCreated?.Invoke(args);
        public static void InvokeEntityBatchDestroyed(EntityBatchDestroyedEventArgs args) => EntityBatchDestroyed?.Invoke(args);

        public static void InvokeSystemRegistered(Type t) => SystemRegistered?.Invoke(t);
        public static void InvokeSystemUnregistered(Type t) => SystemUnregistered?.Invoke(t);
        public static void InvokeSystemEnabled(BaseSystem s) => SystemEnabled?.Invoke(s);
        public static void InvokeSystemDisabled(BaseSystem s) => SystemDisabled?.Invoke(s);
        public static void InvokeDependencyResolved(BaseSystem sys, BaseSystem dep) => DependencyResolved?.Invoke(sys, dep);

        #endregion
    }
}
