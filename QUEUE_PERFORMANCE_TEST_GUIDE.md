# Entity Queue Performance Testing Guide

**Created**: 2025-11-12
**Purpose**: Measure queue vs CommandBuffer performance to determine architectural direction

---

## Tests Created

### 1. EntityQueuePerformanceTest.cs
**Measures**: Individual phase timing in queue-based entity creation

**What it tests**:
- Enqueue time (adding to ConcurrentQueue)
- ProcessQueues time (draining queue)
- CreateEntity time (actual entity creation)
- Event firing time (EntityBatchCreated)
- Total end-to-end time

**Pattern**: Random batch sizes (1-10,000 entities) at random intervals over 120 frames

**Output**: Detailed breakdown of each phase, per-entity costs, batch size analysis

---

### 2. QueueVsCommandBufferComparison.cs
**Measures**: Direct head-to-head comparison

**What it tests**:
- Queue path: Enqueue → ProcessQueues → Create
- CommandBuffer path: Buffer → Apply → Create
- Identical workloads (10, 100, 1k, 10k, 100k entities)

**Output**: Side-by-side timing, slowdown factor, overhead analysis

---

## How to Run Tests

### Option 1: From Godot Editor (Recommended)

1. **Open Godot project**
2. **Create test scene**:
   - Scene → New Scene
   - Add Node (root)
   - Attach script: `Client/ECS/StressTests/EntityQueuePerformanceTest.cs`
   - Or: `Client/ECS/StressTests/QueueVsCommandBufferComparison.cs`
3. **Run scene** (F6)
4. **Check Output panel** for results

### Option 2: Programmatic (From Main.cs or WorldECS)

```csharp
// In Client/Main.cs or Server/Main.cs
public override void _Ready()
{
    // ... existing setup ...

    // Add performance test
    var queueTest = new EntityQueuePerformanceTest();
    AddChild(queueTest);

    // Or comparison test
    var comparisonTest = new QueueVsCommandBufferComparison();
    AddChild(comparisonTest);
}
```

### Option 3: Temporary Scene

Create `Scenes/Tests/QueuePerformanceTest.tscn`:
```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://Client/ECS/StressTests/EntityQueuePerformanceTest.cs" id="1"]

[node name="QueuePerformanceTest" type="Node"]
script = ExtResource("1")
```

Run with: `godot --path . Scenes/Tests/QueuePerformanceTest.tscn`

---

## Interpreting Results

### EntityQueuePerformanceTest Output

```
Phase Timing (Average per batch):
  Enqueue:    125.50 μs (10.2%)  ← Time to add to queue
  Process:    850.30 μs (69.1%)  ← Time to drain queue
  Create:     254.20 μs (20.7%)  ← Actual entity creation
  TOTAL:     1230.00 μs

Per-Entity Cost:
  Enqueue:    125 ns  ← Cost per entity to enqueue
  Process:    850 ns  ← Cost per entity to process
  Create:     254 ns  ← Cost per entity to create
  TOTAL:     1229 ns (813,821 entities/sec)
```

**Good**: Per-entity total < 500 ns (2M entities/sec)
**Acceptable**: Per-entity total < 2000 ns (500k entities/sec)
**Poor**: Per-entity total > 10000 ns (100k entities/sec)

---

### QueueVsCommandBufferComparison Output

```
Entity Count | Queue Time | Buffer Time | Slowdown
-------------|------------|-------------|----------
          10 |     0.05ms |     0.03ms |    1.67x
         100 |     0.25ms |     0.15ms |    1.67x
       1,000 |     1.80ms |     1.20ms |    1.50x
      10,000 |    15.50ms |    11.20ms |    1.38x
     100,000 |   155.00ms |   112.00ms |    1.38x
```

**Interpretation**:
- **Slowdown < 1.5x**: Queue overhead is acceptable
- **Slowdown 1.5-3x**: Queue adds moderate overhead
- **Slowdown > 3x**: Queue is a bottleneck (needs optimization)

**Overhead calculation**:
```
Queue overhead = (Queue Time - Buffer Time) / Buffer Time * 100%
Example: (15.5ms - 11.2ms) / 11.2ms = 38.4% overhead
```

---

## What to Look For

### 1. Queue Overhead Sources

**High Enqueue Time** (> 30% of total):
- Problem: ConcurrentQueue.Enqueue is slow
- Cause: Locking contention, allocation
- Fix: Use lock-free data structure or pre-allocate

**High Process Time** (> 70% of total):
- Problem: ProcessQueues is slow
- Cause: Too many allocations, inefficient dequeue
- Fix: Batch processing, reduce allocations

**High Create Time** (> 40% of total):
- Problem: Entity creation itself is slow
- Not queue-related, but worth investigating

---

### 2. Batch Size Scaling

**Expected**: Linear scaling (2x entities = 2x time)

**Bad Signs**:
- Small batches fast, large batches disproportionately slow (O(n²) behavior)
- Choppy performance (GC spikes)
- Frame time spikes (> 16.67ms for any batch)

**Good Scaling Example**:
```
Small (1-99):     0.05 ms  (50 μs / entity)
Medium (100-999): 0.50 ms  (50 μs / entity) ✓ Linear!
Large (1000+):    5.00 ms  (50 μs / entity) ✓ Linear!
```

**Bad Scaling Example**:
```
Small (1-99):     0.05 ms  (50 μs / entity)
Medium (100-999): 1.50 ms  (150 μs / entity) ✗ 3x slower!
Large (1000+):    25.0 ms  (250 μs / entity) ✗ 5x slower!
```

---

### 3. Frame Budget Analysis

**60 FPS = 16.67ms per frame**

The test reports:
```
Frame Budget Analysis (60 FPS = 16.67ms):
  Max entities per frame: 13,571 (staying under 16.67ms)
  WARNING: 5 batches exceeded 16.67ms!
```

**Interpretation**:
- Can safely create ~13k entities per frame
- Some large batches cause frame drops
- Consider spreading creation over multiple frames

---

## Common Issues & Fixes

### Issue 1: Queue 5x+ Slower Than CommandBuffer

**Symptoms**:
```
100,000 entities:
  Queue:  500 ms
  Buffer: 100 ms
  Slowdown: 5x
```

**Likely Causes**:
1. ProcessQueues allocates too much (List resizing)
2. ConcurrentQueue has contention
3. Event firing is expensive

**Fix**:
```csharp
// Pre-allocate capacity
private List<Entity> _createdEntities = new(10000);

public void ProcessQueues()
{
    _createdEntities.Clear();  // Reuse list

    while (_createQueue.TryDequeue(out var builder))
    {
        // Batch allocate capacity if needed
        if (_createdEntities.Capacity < _createdEntities.Count + 1000)
            _createdEntities.Capacity = _createdEntities.Count + 1000;

        _createdEntities.Add(Create());
    }
}
```

---

### Issue 2: Small Batches Fast, Large Batches Choppy

**Symptoms**:
```
10 entities:   0.05ms (smooth)
100 entities:  0.50ms (smooth)
1000 entities: 8.00ms (choppy!) ← Should be ~5ms
```

**Likely Cause**: GC spikes from allocations

**Fix**: Reduce allocations in hot path
```csharp
// BAD: Allocates every call
var entities = new List<Entity>();
foreach (var builder in queue)
    entities.Add(Create());

// GOOD: Reuse list
_cachedList.Clear();
foreach (var builder in queue)
    _cachedList.Add(Create());
```

---

### Issue 3: ProcessQueues Takes Most Time

**Symptoms**:
```
Enqueue:  50 μs (5%)
Process:  800 μs (80%)  ← Bottleneck!
Create:   150 μs (15%)
```

**Possible Causes**:
1. Inefficient TryDequeue loop
2. Too many allocations during processing
3. Event firing inside loop

**Fix**: Batch dequeue, defer events
```csharp
// Dequeue all at once
var batch = new List<Action<Entity>>(100);
while (_createQueue.TryDequeue(out var builder) && batch.Count < 100)
    batch.Add(builder);

// Create all
foreach (var builder in batch)
    _createdEntities.Add(Create());

// Fire event ONCE at end
if (_createdEntities.Count > 0)
    FireBatchEvent(_createdEntities);
```

---

## Decision Matrix

After running tests, use this matrix to decide:

| Queue Slowdown | Overhead | Verdict | Recommendation |
|----------------|----------|---------|----------------|
| < 1.2x | < 20% | ✅ Excellent | Use queues everywhere |
| 1.2-1.5x | 20-50% | ✅ Good | Use queues for ordering |
| 1.5-3x | 50-200% | ⚠️ Moderate | Optimize queue or hybrid |
| > 3x | > 200% | ❌ Poor | Stick with CommandBuffer |

**Hybrid Approach** (if queue has moderate overhead):
- Use **CommandBuffer** for bulk creation (1000+ entities)
- Use **Queue** for simple operations (< 100 entities)
- Best of both worlds: performance + ordering

---

## Next Steps

1. **Run both tests**
2. **Review output** (check console logs)
3. **Identify bottleneck** (Enqueue? Process? Create?)
4. **Make decision**:
   - If queue is competitive → Route CommandBuffer through queues
   - If queue is slow → Optimize ProcessQueues or keep CommandBuffer
5. **Create implementation task** based on results

---

## Expected Results (Hypothesis)

Based on code review, I predict:

**EntityQueuePerformanceTest**:
- Enqueue: ~50-200 ns/entity (ConcurrentQueue is fast)
- Process: ~300-1000 ns/entity (dequeue + create)
- Create: ~200-500 ns/entity (actual entity creation)
- Total: ~500-1700 ns/entity

**QueueVsCommandBufferComparison**:
- Small batches: 1.5-2x slowdown (queue overhead visible)
- Large batches: 1.2-1.5x slowdown (amortized overhead)
- Overall: Queue should be acceptable (< 2x slowdown)

**If results are worse**: ProcessQueues likely has inefficient implementation (allocations, etc.)

---

## Files Modified/Created

- ✅ `Client/ECS/StressTests/EntityQueuePerformanceTest.cs` (new)
- ✅ `Client/ECS/StressTests/QueueVsCommandBufferComparison.cs` (new)
- ✅ `QUEUE_PERFORMANCE_TEST_GUIDE.md` (this file)

**Ready to test!** Run tests and report back results.
