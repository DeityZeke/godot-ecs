#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Client.ECS.Collections
{
    /// <summary>
    /// Growable bitset optimized for dense entity indices. Provides O(1) set/clear/test
    /// and exposes utilities to enumerate set bits without allocations.
    /// </summary>
    internal sealed class DynamicBitSet
    {
        private ulong[] _words = Array.Empty<ulong>();
        private int _count;

        public int Count => _count;

        public bool Set(int index)
        {
            EnsureCapacity(index);

            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if ((_words[word] & mask) != 0)
                return false;

            _words[word] |= mask;
            _count++;
            return true;
        }

        public bool Clear(int index)
        {
            int word = index >> 6;
            if (word >= _words.Length)
                return false;

            ulong mask = 1UL << (index & 63);
            if ((_words[word] & mask) == 0)
                return false;

            _words[word] &= ~mask;
            _count--;
            return true;
        }

        public bool Contains(int index)
        {
            int word = index >> 6;
            if (word >= _words.Length)
                return false;

            ulong mask = 1UL << (index & 63);
            return (_words[word] & mask) != 0;
        }

        public void ClearAll()
        {
            Array.Clear(_words, 0, _words.Length);
            _count = 0;
        }

        public void SwapWith(DynamicBitSet other)
        {
            (other._words, _words) = (_words, other._words);
            (other._count, _count) = (_count, other._count);
        }

        public void CopySetBitsTo(List<int> destination)
        {
            destination.Clear();

            for (int wordIndex = 0; wordIndex < _words.Length; wordIndex++)
            {
                ulong word = _words[wordIndex];
                while (word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(word);
                    destination.Add((wordIndex << 6) + bit);
                    word &= word - 1;
                }
            }
        }

        private void EnsureCapacity(int index)
        {
            int requiredWords = (index >> 6) + 1;
            if (_words.Length >= requiredWords)
                return;

            int newSize = _words.Length == 0 ? 4 : _words.Length;
            while (newSize < requiredWords)
            {
                newSize *= 2;
            }

            Array.Resize(ref _words, newSize);
        }
    }
}
