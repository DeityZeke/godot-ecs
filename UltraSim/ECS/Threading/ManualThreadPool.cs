
#nullable enable

using System;
using System.Threading;

namespace UltraSim.ECS.Threading
{

    /// <summary>
    /// Manual thread pool implementation - zero-allocation parallel execution.
    /// Creates threads once and reuses them forever.
    /// </summary>
    internal sealed class ManualThreadPool
    {
        private readonly Thread[] _threads;
        private readonly AutoResetEvent[] _workEvents;
        private readonly AutoResetEvent _completeEvent;
        private Action<int>? _workAction;
        private int _workCount;
        private int _workIndex;
        private int _completedCount;
        private volatile bool _shutdown;

        public ManualThreadPool(int threadCount)
        {
            _threads = new Thread[threadCount];
            _workEvents = new AutoResetEvent[threadCount];
            _completeEvent = new AutoResetEvent(false);

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                _workEvents[i] = new AutoResetEvent(false);
                _threads[i] = new Thread(() => WorkerThread(threadIndex))
                {
                    IsBackground = true,
                    Name = $"ECS_Worker_{threadIndex}"
                };
                _threads[i].Start();
            }
        }

        public void ParallelFor(int count, Action<int> action)
        {
            if (count == 0) return;
            if (count == 1)
            {
                // Single item - just execute directly (no threading overhead)
                action(0);
                return;
            }

            // Setup work
            _workAction = action;
            _workCount = count;
            _workIndex = 0;
            _completedCount = 0;

            // Wake up all threads
            foreach (var evt in _workEvents.AsSpan())
                evt.Set();

            // Wait for completion
            _completeEvent.WaitOne();
        }

        private void WorkerThread(int threadIndex)
        {
            while (!_shutdown)
            {
                // Wait for work
                _workEvents[threadIndex].WaitOne();

                if (_shutdown) break;

                // Process work items
                while (true)
                {
                    int index = Interlocked.Increment(ref _workIndex) - 1;
                    if (index >= _workCount) break;

                    _workAction?.Invoke(index);
                }

                // Signal completion if we're the last thread
                if (Interlocked.Increment(ref _completedCount) == _threads.Length)
                {
                    _completeEvent.Set();
                }
            }
        }

        public void Shutdown()
        {
            _shutdown = true;
            foreach (var evt in _workEvents.AsSpan())
                evt.Set();
        }
    }
}