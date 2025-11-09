#nullable enable

using Client.ECS.Systems;
using UltraSim.ECS.Components;
using Xunit;

namespace GodotECS.Tests
{
    public sealed class ChunkVisualPoolTests
    {
        private sealed class TestVisual
        {
            public bool Active { get; set; }
        }

        [Fact]
        public void AcquireCreatesNewVisualWhenPoolEmpty()
        {
            var chunk = new ChunkLocation(1, 2, 3);
            int factoryCalls = 0;

            var pool = new ChunkVisualPool<TestVisual>(
                chunk,
                _ =>
                {
                    factoryCalls++;
                    return new TestVisual();
                },
                v => v.Active = true,
                v => v.Active = false);

            var visual = pool.Acquire();

            Assert.NotNull(visual);
            Assert.True(visual!.Active);
            Assert.Equal(1, factoryCalls);
            Assert.Equal(1, pool.ActiveCount);
            Assert.Equal(0, pool.AvailableCount);
        }

        [Fact]
        public void ReleaseReturnsVisualToPool()
        {
            var chunk = new ChunkLocation(0, 0, 0);

            var pool = new ChunkVisualPool<TestVisual>(
                chunk,
                _ => new TestVisual(),
                v => v.Active = true,
                v => v.Active = false);

            var visual = pool.Acquire()!;
            Assert.True(visual.Active);

            bool released = pool.Release(visual);
            Assert.True(released);
            Assert.False(visual.Active);
            Assert.Equal(0, pool.ActiveCount);
            Assert.Equal(1, pool.AvailableCount);

            var recycled = pool.Acquire();
            Assert.Same(visual, recycled);
            Assert.Equal(1, pool.ActiveCount);
        }

        [Fact]
        public void AcquireReturnsNullWhenFactoryDeniesAllocation()
        {
            var chunk = new ChunkLocation(5, 5, 0);
            bool allowCreate = true;

            var pool = new ChunkVisualPool<TestVisual>(
                chunk,
                _ =>
                {
                    if (!allowCreate)
                        return null;
                    allowCreate = false;
                    return new TestVisual();
                });

            var first = pool.Acquire();
            Assert.NotNull(first);

            var second = pool.Acquire();
            Assert.Null(second);
            Assert.Equal(1, pool.ActiveCount);
        }
    }
}
