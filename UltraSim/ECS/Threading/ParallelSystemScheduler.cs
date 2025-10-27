#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        public static void RunBatches(
            List<List<BaseSystem>> batches,
            World world,
            double delta,
            Action<BaseSystem>? onSystemStarted,
            Action<BaseSystem>? onSystemCompleted)
        {
            var batchSpan = CollectionsMarshal.AsSpan(batches);

            foreach (ref var batch in batchSpan)
            {
                var sysSpan = CollectionsMarshal.AsSpan(batch);
                var tasks = new List<Task>(); // Use List instead of array for enabled systems only

                for (int i = 0; i < sysSpan.Length; i++)
                {

                    // Skip disabled systems during execution
                    // Check enabled status directly (no copy)
                    if (!sysSpan[i].IsEnabled) continue;

                    // Copy reference for closure (required for Task lambda)
                    var sysCopy = sysSpan[i];  // eating speed?

                    var task = Task.Run(() =>
                    {
                        try
                        {
                            onSystemStarted?.Invoke(sysCopy);
                            // Use UpdateWithTiming instead of Update for automatic statistics tracking
                            sysCopy.UpdateWithTiming(world, delta);
                        }
                        finally
                        {
                            onSystemCompleted?.Invoke(sysCopy);
                        }
                    });

                    tasks.Add(task);
                }

                // Wait for all enabled systems in this batch to complete
                if (tasks.Count > 0)
                    Task.WaitAll(tasks.ToArray());
            }
        }
    }
}