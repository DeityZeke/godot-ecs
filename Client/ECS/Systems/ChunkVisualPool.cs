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
        private readonly Stack<TVisual> _available = new();
        private readonly HashSet<TVisual> _leased = new();
        private readonly Func<ChunkLocation, TVisual?> _factory;
        private readonly Action<TVisual>? _onAcquire;
        private readonly Action<TVisual>? _onRelease;

        public ChunkLocation Location { get; }

        public int ActiveCount => _leased.Count;
        public int AvailableCount => _available.Count;

        public ChunkVisualPool(
            ChunkLocation location,
            Func<ChunkLocation, TVisual?> factory,
            Action<TVisual>? onAcquire = null,
            Action<TVisual>? onRelease = null)
        {
            Location = location;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onAcquire = onAcquire;
            _onRelease = onRelease;
        }

        public TVisual? Acquire()
        {
            TVisual? visual = _available.Count > 0 ? _available.Pop() : _factory(Location);
            if (visual == null)
                return null;

            _leased.Add(visual);
            _onAcquire?.Invoke(visual);
            return visual;
        }

        public bool Release(TVisual visual)
        {
            if (!_leased.Remove(visual))
                return false;

            _onRelease?.Invoke(visual);
            _available.Push(visual);
            return true;
        }
    }
}
