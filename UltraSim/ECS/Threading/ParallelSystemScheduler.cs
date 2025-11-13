#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using UltraSim.ECS.Systems;

namespace UltraSim.ECS.Threading
{
    /// <summary>
    /// Executes system batches in parallel using Task-based parallelism.
    /// Systems within a batch run concurrently; batches run sequentially.
    /// Now includes automatic performance statistics tracking.
    /// </summary>
    public static class ParallelSystemScheduler
    {
        [ThreadStatic]
        private static List<Task>? _taskList;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RunBatches(
            List<List<BaseSystem>> batches,
            World world,
            double delta,
            Action<BaseSystem>? onSystemStarted = null,
            Action<BaseSystem>? onSystemCompleted = null)
        {
            _taskList ??= new List<Task>(64);
            _taskList.Clear();

            var batchSpan = CollectionsMarshal.AsSpan(batches);

            foreach (ref var batch in batchSpan)
            {
                var sysSpan = CollectionsMarshal.AsSpan(batch);

                for (int i = 0; i < sysSpan.Length; i++)
                {
                    var sys = sysSpan[i];
                    if (!sys.IsEnabled) continue;

                    _taskList.Add(Task.Run(() =>
                    {
                        try
                        {
                            onSystemStarted?.Invoke(sys);
                            sys.UpdateWithTiming(world, delta);
                        }
                        finally
                        {
                            onSystemCompleted?.Invoke(sys);
                        }
                    }));
                }

                // CRITICAL: Wait for all tasks in parallel, not sequentially
                // Bug fix: Sequential Wait() blocked main thread unnecessarily
                // Example: Tasks complete in [5ms, 2ms, 8ms, 3ms]
                //   Sequential: 5ms + 8ms = 13ms (waits one by one)
                //   Parallel: max(5ms, 2ms, 8ms, 3ms) = 8ms (38% faster!)
                if (_taskList.Count > 0)
                {
                    Task.WaitAll(_taskList.ToArray());
                }

                _taskList.Clear();
            }
        }
    }
}