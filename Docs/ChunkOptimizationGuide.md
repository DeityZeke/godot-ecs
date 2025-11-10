# Chunk System Optimization Guide

This guide documents the staged improvements we are applying to `Server/ECS/Systems/ChunkSystem` and related components. Each milestone should be implemented, benchmarked, and committed separately so regressions are obvious.

## Baseline
- `ChunkSystem.AssignEntitiesToChunks` scans **every** entity every `AssignmentFrequency` frames.
- Uses dictionary lookups for chunk entities (`_chunkEntityCache`, `_pendingChunkCreations`).
- All work runs on the main thread.
- Benchmarked via `Tests/Benchmarks/ChunkBench` (`RunChunkRegistrationBenchmark`, `RunEntityAssignmentBenchmark`).

## Optimization Stages

1. **Dirty-driven assignments**
   - Introduce a `ChunkAssignmentQueue` fed by movement systems (or a helper like `ChunkAssignment.MarkDirty(entityIndex, newChunkLoc)`).
   - `ChunkSystem` processes only dirty entities each frame; fallback to full scan only if queue disabled.
   - Benchmark: Compare assignment ops/sec before vs. after with the queue enabled.

2. **Parallel reassignment** ✅
   - Dirty queue now drains into a staging buffer, then workers process batches via `Parallel.ForEach` with thread-local chunk caches (deduplicated chunk entity lookups).
   - New settings: `Parallel Dirty Queue`, `Parallel Threshold`, `Parallel Batch Size`.
   - Entities whose chunks do not yet exist fall back to the sequential path, preserving creation semantics.
   - Benchmark (`ChunkBench` entity assignment) before: 0.70 M ops/sec; after Stage 2: 0.70 M ops/sec (synthetic test does not exercise the dirty queue yet, so numbers are unchanged—real-world profiling pending).
3. **Flat chunk index** ✅
   - Replaced the `Dictionary<ChunkLocation, Entity>` hot path with a custom open-addressing `ChunkLookupTable` (no allocations during assignment) and trimmed per-column metadata to lightweight stats only.
   - `ChunkManager` spatial queries now iterate the flat table directly, avoiding nested dictionary churn; column stats are recomputed lazily only when vertical bounds are needed for logging.
   - Benchmark (`ChunkBench` registration): was 17.6 ms to register 16,384 chunks, now 26.7 ms (hash table initialization overhead); assignment throughput remains ~0.69 M ops/sec but avoids `Dictionary` boxing on the main thread.

4. **Chunk preallocation & pooling** ✅
   - `ChunkSystem` can now preallocate an origin-centered chunk grid (configurable radius/height/batch) so large worlds spin up without runtime spikes. Preallocation consumes pooled chunk entities first, falling back to deferred creation.
   - Added optional chunk pooling: stale chunks (idle frame budget or over the configured max) are evicted via LRU, `ChunkState` is flipped to `Inactive`, and the entity is recycled for the next allocation; `RegisterNewChunks` skips inactive entries, so pooled chunks stay invisible until reused.
   - `ChunkBench` (registration) improved slightly due to reuse path: 16,384 chunk registrations now 25.36 ms vs. 26.72 ms (post Stage 3) thanks to the zero-allocation pool when running repeated batches. Real-world measurement will focus on frame-spike reduction when teleporting bubble radii.

5. **ChunkOwner SoA**
   - Store chunk owner data in dedicated arrays (`ChunkEntity[]`, `ChunkLocation[]`) rather than interleaved structs.
   - Update movement code to read/write the SoA layout; reduces memory traffic during reassignment.
   - Benchmark: rerun assignment test to capture the gains from cache-friendly layout.

6. **Fused movement + reassignment**
   - Movement systems call `ChunkAssignment.Notify(entityIndex, newPosition)` inline; function checks chunk boundary and pushes to queue immediately.
   - `ChunkSystem` only drains the queue; eliminates redundant chunk recalculations.
   - Benchmark: rerun entity movement scenario to verify minimal overhead.

7. **Benchmark harness updates**
   - Expand `ChunkBench` to cover:
     - Dirty vs full scan (`--dirty` flag).
     - Parallel vs single-thread.
     - Chunk prealloc vs on-demand.
     - Chunk destruction/pooling scenarios.
   - Record results (CSV/JSON) for future regressions.

## Process Checklist (per stage)
1. Update implementation + settings.
2. Run `dotnet run --project Tests/Benchmarks/ChunkBench` (plus any other relevant tests).
3. Capture before/after metrics in commit description or separate log.
4. Keep guide updated if process changes.

Following this structured approach will let us quantify each improvement and keep the chunk pipeline scalable as entity counts grow.
