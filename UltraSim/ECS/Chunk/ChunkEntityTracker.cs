#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.ECS;

namespace UltraSim.ECS.Chunk
{
public sealed class ChunkEntityTracker
    {
        private const int BlockCapacity = 64;
        private const ulong BlockFullMask = ulong.MaxValue;

        internal struct ChunkBlock
        {
            public ulong Occupancy;
            public Entity[] Entries;
        }

        private struct ChunkData
        {
            public ChunkBlock[]? Blocks;
            public int BlockCount;
            public int Count;
        }

        private struct EntitySlot
        {
            public Entity Chunk;
            public int BlockIndex;
            public int BitIndex;

            public EntitySlot(Entity chunk, int blockIndex, int bitIndex)
            {
                Chunk = chunk;
                BlockIndex = blockIndex;
                BitIndex = bitIndex;
            }
        }

        private readonly Dictionary<Entity, ChunkData> _chunks = new();
        private readonly Dictionary<Entity, EntitySlot> _entitySlots = new();

        public ChunkEntityEnumerable GetEntities(Entity chunkEntity)
        {
            if (_chunks.TryGetValue(chunkEntity, out var data) && data.Count > 0 && data.Blocks != null)
            {
                return new ChunkEntityEnumerable(data.Blocks, data.BlockCount, data.Count);
            }

            return ChunkEntityEnumerable.Empty;
        }

        public int GetEntityCount(Entity chunkEntity)
        {
            return _chunks.TryGetValue(chunkEntity, out var data) ? data.Count : 0;
        }

        public void Add(Entity chunkEntity, Entity entity)
        {
            ref var data = ref CollectionsMarshal.GetValueRefOrAddDefault(_chunks, chunkEntity, out _);
            EnsureBlockStorage(ref data);

            int blockIndex = FindBlockWithSpace(ref data);
            if (blockIndex < 0)
            {
                blockIndex = data.BlockCount;
                EnsureBlockCapacity(ref data, blockIndex + 1);
                data.BlockCount++;
            }

            ref var block = ref data.Blocks![blockIndex];
            if (block.Entries == null)
            {
                block.Entries = new Entity[BlockCapacity];
            }

            int bitIndex = FindFreeBit(block.Occupancy);
            block.Occupancy |= 1UL << bitIndex;
            block.Entries[bitIndex] = entity;

            data.Count++;
            _entitySlots[entity] = new EntitySlot(chunkEntity, blockIndex, bitIndex);
        }

        public void Remove(Entity chunkEntity, Entity entity)
        {
            if (!_entitySlots.TryGetValue(entity, out var slot) || slot.Chunk != chunkEntity)
                return;

            if (!_chunks.TryGetValue(chunkEntity, out var data) || data.Blocks == null || slot.BlockIndex >= data.BlockCount)
                return;

            ref var block = ref data.Blocks[slot.BlockIndex];
            block.Occupancy &= ~(1UL << slot.BitIndex);
            block.Entries[slot.BitIndex] = Entity.Invalid;
            data.Count--;
            _entitySlots.Remove(entity);

            if (block.Occupancy == 0 && slot.BlockIndex == data.BlockCount - 1)
            {
                TrimTrailingBlocks(ref data);
            }

            _chunks[chunkEntity] = data;
        }

        public void Move(Entity entity, Entity oldChunk, Entity newChunk)
        {
            if (oldChunk == newChunk)
                return;

            Remove(oldChunk, entity);
            Add(newChunk, entity);
        }

        public void Clear(Entity chunkEntity)
        {
            if (!_chunks.TryGetValue(chunkEntity, out var data) || data.Blocks == null || data.Count == 0)
            {
                _chunks.Remove(chunkEntity);
                return;
            }

            for (int b = 0; b < data.BlockCount; b++)
            {
                var block = data.Blocks[b];
                if (block.Occupancy == 0 || block.Entries == null)
                    continue;

                ulong mask = block.Occupancy;
                while (mask != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(mask);
                    var entity = block.Entries[bit];
                    if (entity != Entity.Invalid)
                        _entitySlots.Remove(entity);
                    block.Entries[bit] = Entity.Invalid;
                    mask &= mask - 1;
                }

                data.Blocks[b].Occupancy = 0;
            }

            data.Count = 0;
            data.BlockCount = 0;
            _chunks.Remove(chunkEntity);
        }

        private static void EnsureBlockStorage(ref ChunkData data)
        {
            if (data.Blocks == null)
            {
                data.Blocks = new ChunkBlock[1];
                data.BlockCount = 1;
            }
            else if (data.BlockCount == 0)
            {
                data.BlockCount = 1;
            }
        }

        private static void EnsureBlockCapacity(ref ChunkData data, int requiredBlocks)
        {
            if (data.Blocks == null)
            {
                data.Blocks = new ChunkBlock[Math.Max(1, requiredBlocks)];
                return;
            }

            if (data.Blocks.Length >= requiredBlocks)
                return;

            int newSize = Math.Max(data.Blocks.Length * 2, requiredBlocks);
            Array.Resize(ref data.Blocks, newSize);
        }

        private static int FindBlockWithSpace(ref ChunkData data)
        {
            if (data.Blocks == null)
                return -1;

            for (int i = 0; i < data.BlockCount; i++)
            {
                if (data.Blocks[i].Occupancy != BlockFullMask)
                    return i;
            }
            return -1;
        }

        private static int FindFreeBit(ulong occupancy)
        {
            ulong inverted = ~occupancy;
            int bit = BitOperations.TrailingZeroCount(inverted);
            return bit;
        }

        private static void TrimTrailingBlocks(ref ChunkData data)
        {
            if (data.Blocks == null)
                return;

            int newCount = data.BlockCount;
            while (newCount > 0 && data.Blocks[newCount - 1].Occupancy == 0)
            {
                newCount--;
            }
            data.BlockCount = newCount;
        }

        public readonly struct ChunkEntityEnumerable
        {
            private readonly ChunkBlock[]? _blocks;
            private readonly int _blockCount;
            private readonly int _entityCount;

            public static readonly ChunkEntityEnumerable Empty = new(null, 0, 0);

            internal ChunkEntityEnumerable(ChunkBlock[]? blocks, int blockCount, int entityCount)
            {
                _blocks = blocks;
                _blockCount = blockCount;
                _entityCount = entityCount;
            }

            public Enumerator GetEnumerator() => new Enumerator(_blocks, _blockCount, _entityCount);

            public struct Enumerator
            {
                private readonly ChunkBlock[]? _blocks;
                private readonly int _blockCount;
                private readonly int _entityCount;
                private int _blockIndex;
                private int _activeBlock;
                private ulong _mask;
                private Entity _current;

                internal Enumerator(ChunkBlock[]? blocks, int blockCount, int entityCount)
                {
                    _blocks = blocks;
                    _blockCount = blockCount;
                    _entityCount = entityCount;
                    _blockIndex = 0;
                    _activeBlock = -1;
                    _mask = 0;
                    _current = Entity.Invalid;
                }

                public Entity Current => _current;

                public bool MoveNext()
                {
                    if (_blocks == null || _entityCount == 0)
                        return false;

                    while (true)
                    {
                        if (_mask != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(_mask);
                            _mask &= _mask - 1;
                            ref readonly var block = ref _blocks[_activeBlock];
                            _current = block.Entries[bit];
                            return true;
                        }

                        if (_blockIndex >= _blockCount)
                            return false;

                        _mask = _blocks[_blockIndex].Occupancy;
                        _activeBlock = _blockIndex;
                        _blockIndex++;
                        if (_mask == 0)
                            continue;
                    }
                }
            }
        }
    }
}
