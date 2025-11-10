#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim.ECS.Components;

namespace Client.ECS.Rendering
{
    /// <summary>
    /// Maintains a fixed-size matrix of chunk slots centered on the camera chunk.
    /// Tracks which slots entered or exited the window whenever the center changes.
    /// </summary>
    internal sealed class ChunkRenderWindow
    {
        public readonly struct Slot
        {
            public Slot(ChunkLocation globalLocation, Vector3I relativeOffset, int index)
            {
                GlobalLocation = globalLocation;
                RelativeOffset = relativeOffset;
                Index = index;
            }

            public ChunkLocation GlobalLocation { get; }
            public Vector3I RelativeOffset { get; }
            public int Index { get; }
        }

        private readonly List<Vector3I> _relativeOffsets = new();
        private readonly Dictionary<ChunkLocation, Slot> _activeSlots = new();
        private readonly Dictionary<ChunkLocation, Slot> _rebuildBuffer = new();
        private readonly List<Slot> _entered = new();
        private readonly List<Slot> _exited = new();
        private readonly List<Slot> _moved = new();

        private bool _initialized;
        private ChunkLocation _currentCenter;
        private ChunkLocation _targetCenter;
        private double _shiftTimer;
        private int _radiusXZ;
        private int _radiusY;

        public IReadOnlyList<Slot> Entered => _entered;
        public IReadOnlyList<Slot> Exited => _exited;
        public IReadOnlyList<Slot> Moved => _moved;
        public ChunkLocation Center => _currentCenter;

        public IEnumerable<Slot> ActiveSlots => _activeSlots.Values;

        public bool Contains(ChunkLocation location) => _activeSlots.ContainsKey(location);

        public bool TryGetSlot(ChunkLocation location, out Slot slot) =>
            _activeSlots.TryGetValue(location, out slot);

        public void Configure(int radiusXZ, int radiusY)
        {
            radiusXZ = Math.Max(0, radiusXZ);
            radiusY = Math.Max(0, radiusY);

            if (_radiusXZ == radiusXZ && _radiusY == radiusY && _relativeOffsets.Count > 0)
                return;

            _radiusXZ = radiusXZ;
            _radiusY = radiusY;
            RebuildOffsets();
            Invalidate();
        }

        public void Reset()
        {
            _activeSlots.Clear();
            _rebuildBuffer.Clear();
            _entered.Clear();
            _exited.Clear();
            _moved.Clear();
            _initialized = false;
            _shiftTimer = 0;
        }

        private void Invalidate()
        {
            _initialized = false;
            _activeSlots.Clear();
        }

        private void RebuildOffsets()
        {
            _relativeOffsets.Clear();
            for (int dy = -_radiusY; dy <= _radiusY; dy++)
            {
                for (int dx = -_radiusXZ; dx <= _radiusXZ; dx++)
                {
                    for (int dz = -_radiusXZ; dz <= _radiusXZ; dz++)
                    {
                        _relativeOffsets.Add(new Vector3I(dx, dy, dz));
                    }
                }
            }
        }

        /// <summary>
        /// Updates the window center and produces diff lists when the center changes.
        /// Returns true if the slot set changed.
        /// </summary>
        public bool Update(ChunkLocation cameraChunk, double delta, float recenterDelaySeconds)
        {
            bool centerChanged = false;

            if (!_initialized)
            {
                _currentCenter = cameraChunk;
                _targetCenter = cameraChunk;
                _shiftTimer = 0;
                _initialized = true;
                centerChanged = true;
            }
            else
            {
                if (!_targetCenter.Equals(cameraChunk))
                {
                    _targetCenter = cameraChunk;
                    _shiftTimer = 0;
                }
                else
                {
                    _shiftTimer += delta;
                }

                if (!_currentCenter.Equals(_targetCenter))
                {
                    if (_shiftTimer >= recenterDelaySeconds)
                    {
                        _currentCenter = _targetCenter;
                        centerChanged = true;
                        _shiftTimer = 0;
                    }
                }
            }

            if (!centerChanged)
            {
                _entered.Clear();
                _exited.Clear();
                _moved.Clear();
                return false;
            }

            RebuildWindow();
            return _entered.Count > 0 || _exited.Count > 0 || _moved.Count > 0;
        }

        private void RebuildWindow()
        {
            _entered.Clear();
            _exited.Clear();
            _moved.Clear();
            _rebuildBuffer.Clear();

            for (int i = 0; i < _relativeOffsets.Count; i++)
            {
                var relative = _relativeOffsets[i];
                var global = new ChunkLocation(
                    _currentCenter.X + relative.X,
                    _currentCenter.Z + relative.Z,
                    _currentCenter.Y + relative.Y);

                var slot = new Slot(global, relative, i);
                _rebuildBuffer[global] = slot;

                if (_activeSlots.TryGetValue(global, out var previous))
                {
                    if (previous.Index != slot.Index || previous.RelativeOffset != slot.RelativeOffset)
                    {
                        _moved.Add(slot);
                    }
                }
                else
                {
                    _entered.Add(slot);
                }
            }

            foreach (var kvp in _activeSlots)
            {
                if (!_rebuildBuffer.ContainsKey(kvp.Key))
                {
                    _exited.Add(kvp.Value);
                }
            }

            _activeSlots.Clear();
            foreach (var kvp in _rebuildBuffer)
            {
                _activeSlots[kvp.Key] = kvp.Value;
            }

            _rebuildBuffer.Clear();
        }
    }
}
