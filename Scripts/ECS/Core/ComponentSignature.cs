#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;

namespace UltraSim.ECS
{
    /// <summary>
    /// Immutable component signature - identifies an archetype's component combination.
    /// </summary>
    public sealed class ComponentSignature
    {
        private readonly HashSet<int> _ids = new();

        public IEnumerable<int> GetIds() => _ids;

        public int Count => _ids.Count;

        public ComponentSignature() { }

        private ComponentSignature(HashSet<int> ids)
        {
            _ids = new HashSet<int>(ids);
        }

        /// <summary>
        /// Returns a new signature with the component ID added.
        /// </summary>
        public ComponentSignature Add(int id)
        {
            var clone = new HashSet<int>(_ids) { id };
            return new ComponentSignature(clone);
        }

        /// <summary>
        /// Returns a new signature with the component ID removed.
        /// </summary>
        public ComponentSignature Remove(int id)
        {
            var clone = new HashSet<int>(_ids);
            clone.Remove(id);
            return new ComponentSignature(clone);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id) => _ids.Contains(id);

        public bool Equals(ComponentSignature other) => _ids.SetEquals(other._ids);

        public ComponentSignature Clone() => new(_ids);

        public override bool Equals(object? obj) => obj is ComponentSignature sig && Equals(sig);

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var id in _ids.OrderBy(x => x))
                hash = hash * 31 + id;
            return hash;
        }

        public override string ToString() => $"Signature[{string.Join(",", _ids.OrderBy(x => x))}]";
    }
}