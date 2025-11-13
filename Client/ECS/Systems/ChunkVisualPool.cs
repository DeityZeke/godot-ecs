#nullable enable

using System;
using System.Collections.Generic;
using UltraSim.ECS.Components;

namespace Client.ECS.Systems
{
    /// <summary>
    /// Maintains a reusable set of visuals for a single chunk so MeshInstances can be
    /// rented and returned without hitting the GC or SceneTree every time an entity
    /// crosses zone boundaries.
    /// </summary>
    /// <typeparam name="TVisual">Concrete visual node type (e.g. MeshInstance3D).</typeparam>
    internal sealed class ChunkVisualPool<TVisual> where TVisual : class
    {
        private readonly Stack<TVisual> _available;
        private readonly HashSet<TVisual> _leased = new();
        private readonly Func<ChunkLocation, TVisual?> _factory;
        private readonly Action<TVisual>? _onAcquire;
        private readonly Action<TVisual>? _onRelease;
        private readonly object _lock = new(); // Thread safety for parallel access

        public ChunkLocation Location { get; private set; }

        public int ActiveCount
        {
            get
            {
                lock (_lock)
                    return _leased.Count;
            }
        }

        public int AvailableCount
        {
            get
            {
                lock (_lock)
                    return _available.Count;
            }
        }

        public ChunkVisualPool(
            ChunkLocation location,
            Func<ChunkLocation, TVisual?> factory,
            Action<TVisual>? onAcquire = null,
            Action<TVisual>? onRelease = null,
            int initialCapacity = 0)
        {
            Location = location;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onAcquire = onAcquire;
            _onRelease = onRelease;
            _available = new Stack<TVisual>(Math.Max(0, initialCapacity));
        }

        public void UpdateLocation(ChunkLocation location)
        {
            Location = location;
        }

        public TVisual? Acquire()
        {
            lock (_lock)
            {
                TVisual? visual = _available.Count > 0 ? _available.Pop() : _factory(Location);
                if (visual == null)
                    return null;

                _leased.Add(visual);
                _onAcquire?.Invoke(visual);
                return visual;
            }
        }

        public bool Release(TVisual visual)
        {
            lock (_lock)
            {
                if (!_leased.Remove(visual))
                    return false;

                _onRelease?.Invoke(visual);
                _available.Push(visual);
                return true;
            }
        }
    }
}
