#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// Interface for type-erased component storage operations.
    /// </summary>
    public interface IComponentList
    {
        void AddDefault();
        void AddBoxed(object? value);
        void SwapLastIntoSlot(int slot, int last);
        object? GetValueBoxed(int slot);
        void SetValueBoxed(int slot, object value);
        void CopyValueTo(int sourceIndex, IComponentList targetList, int targetIndex);
        void RemoveLast();
        int Count { get; }
    }

    /// <summary>
    /// Strongly-typed component container using List<T> with Span access.
    /// </summary>
    public sealed class ComponentList<T> : IComponentList
    {
        private readonly List<T> _list;

        /// <summary>
        /// Exposes the underlying list for zero-allocation parallel processing.
        /// Safe because the List<T> reference itself never changes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<T> GetList() => _list;

        public ComponentList(int capacity = 0)
        {
            _list = capacity > 0 ? new List<T>(capacity) : new List<T>(65535);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDefault() => _list.Add(default!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddBoxed(object? boxed)
        {
            if (boxed is T t)
                _list.Add(t);
            else
                _list.Add(default!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddAtSlot(int slot, T value)
        {
            if (slot >= _list.Count)
                _list.Add(value);
            else
                _list[slot] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetValue(int slot) => _list[slot];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object? GetValueBoxed(int slot) => _list[slot];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValueBoxed(int slot, object value) => _list[slot] = (T)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyValueTo(int sourceIndex, IComponentList targetList, int targetIndex)
        {
            var value = _list[sourceIndex];
            if (targetList is ComponentList<T> typedTarget)
            {
                typedTarget.AddAtSlot(targetIndex, value);
            }
            else
            {
                targetList.SetValueBoxed(targetIndex, value!);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SwapLastIntoSlot(int slot, int last)
        {
            if (slot != last)
                _list[slot] = _list[last];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveLast() => _list.RemoveAt(_list.Count - 1);

        public int Count => _list.Count;

        /// <summary>
        /// High-performance span accessor for iteration.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan() => CollectionsMarshal.AsSpan(_list);
    }
}
