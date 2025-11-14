# Archetype Zombie Entity Investigation

## Problem Statement

After destroying 100k entities, movement systems still process 13,866 "zombie" entities that remain in archetype component lists.

## Observed Behavior

```
[Entity Spawner] Cleared 100000 spawned entities in 3.766ms
[SimplifiedChunkSystem] BEFORE destroy: 100000 entities tracked
[SimplifiedChunkSystem] AFTER fast path: 24325 remaining (removed 100000)
[SimplifiedChunkSystem] Removed 124325 entities total

// AFTER DESTROY:
[OptimizedMovementSystem] Processing archetype: arch.Count=13866, posList.Count=13866
[SimplifiedChunkSystem] Received chunk update event with 7889 entities (0 added, 7889 dead filtered)
```

**Key observations:**
1. More entities tracked in chunks (124k) than spawned (100k)
2. After destroy, archetype still has 13,866 entities
3. All 7,889 chunk update events filtered as "dead" entities

## Entity Lifecycle Trace

### 1. Entity Creation Flow

**Path:** `World.CreateEntity()` → `EntityManager.Create()` → `Archetype.AddEntity()`

```csharp
// EntityManager.Create()
public Entity Create()
{
    uint idx = AllocateIndex();
    var entity = CreateEntityHandle(idx);

    var arch = _archetypes.GetArchetype(ComponentSignature.Empty);
    int slot = arch.AddEntity(entity);  // ← Archetype gets entity

    UpdateLookup(entity.Index, arch, slot);
    _liveEntityCount++;
    return entity;
}

// Archetype.AddEntity()
public int AddEntity(Entity e)
{
    int newIndex = _entities.Count;  // ← Count before add
    _entities.Add(e);                 // ← Increments _entities.Count

    // Expand component lists
    for (int i = 0; i < _componentCount; i++)
        _lists[i].AddDefault();

    return newIndex;
}
```

**State after creation:**
- `archetype.Count` = `_entities.Count` ✅
- `component.List.Count` = `_entities.Count` ✅
- Entity lookup points to archetype + slot ✅

### 2. Entity Destruction Flow

**Path:** `World.DestroyEntity()` → `EntityManager.EnqueueDestroy()` → `ProcessDestructionQueue()` → `Destroy()`

```csharp
// ProcessEntityDestructionQueue()
private void ProcessEntityDestructionQueue()
{
    // PHASE 1: Collect entities
    while (_destroyQueue.TryDequeue(out var idx))
    {
        var entity = CreateEntityHandle(idx);
        _destroyedEntitiesCache.Add(entity);
    }

    // PHASE 2: Fire PRE-DESTROY event (entities still alive)
    EventSink.InvokeEntityBatchDestroyRequest(_destroyedEntitiesCache);

    // PHASE 3: Actually destroy
    foreach (var entity in _destroyedEntitiesCache)
    {
        Destroy(entity);  // ← Should update archetype
    }

    // PHASE 4: Fire POST-DESTROY event
    EventSink.InvokeEntityBatchDestroyed(_destroyedEntitiesCache);
}

// EntityManager.Destroy()
public void Destroy(Entity entity)
{
    if (!TryGetLookup(entity.Index, out var loc))
        return;  // ← EARLY RETURN if no lookup!

    var arch = _archetypes.GetArchetype(loc.archetypeIdx);

    arch.RemoveAtSwap(loc.slot);  // ← Should decrement Count
    _freeHandles.Push(entity.Packed + VersionIncrement);
    _entityVersions[(int)entity.Index]++;
    _packedVersions[(int)entity.Index] += VersionIncrement;
    ClearLookup(entity.Index);
    _liveEntityCount--;
}

// Archetype.RemoveAtSwap()
public void RemoveAtSwap(int slot)
{
    int last = _entities.Count - 1;

    if (slot < 0 || slot > last)
        return;  // ← EARLY RETURN if invalid slot!

    if (slot != last)
    {
        // Swap entity and components
        var movedEntity = _entities[last];
        _entities[slot] = movedEntity;

        for (int i = 0; i < _componentCount; i++)
            _lists[i].SwapLastIntoSlot(slot, last);

        World.Current?.UpdateEntityLookup(movedEntity.Index, this, slot);
    }

    // Remove last element
    for (int i = 0; i < _componentCount; i++)
        _lists[i].RemoveLast();

    _entities.RemoveAt(last);  // ← Decrements _entities.Count
}
```

**Expected state after destruction:**
- `archetype.Count` should decrease ✅
- Component lists should shrink ✅
- Entity lookup cleared ✅

## Potential Bug Scenarios

### Scenario 1: Entity Lookup Already Cleared

If `TryGetLookup()` returns false in `Destroy()`, the method returns early and never calls `RemoveAtSwap()`.

**Possible causes:**
- Entity already destroyed (double-destroy)
- Lookup corrupted/cleared prematurely
- Concurrent modification

**Evidence:** Would show entities "missing" from archetype but still in component lists ❌

### Scenario 2: Invalid Slot in RemoveAtSwap

If `slot < 0 || slot > last` in `RemoveAtSwap()`, the method returns early without removing.

**Possible causes:**
- Slot became invalid between lookup and removal
- Concurrent archetype modifications
- Swap-and-pop race condition

**Evidence:** Component lists would have "gaps" or mismatched counts ❌

### Scenario 3: Movement System Query Cache Stale

Movement system caches archetype queries. If cache not invalidated when archetypes change:

```csharp
foreach (var arch in _cachedQuery!)  // ← Stale cache?
{
    if (arch.Count == 0) continue;    // ← Count is 0, but...

    var posList = arch.GetComponentListTyped<Position>().GetList();
    int count = Math.Min(posList.Count, velList.Count);  // ← Component lists still have data!
}
```

**Evidence:** `arch.Count=0` but `posList.Count=13866` ⚠️ **SMOKING GUN**

### Scenario 4: Component List Count Mismatch

If `ComponentList<T>.RemoveLast()` fails to decrement count or component lists don't synchronize:

```csharp
// After destroy:
_entities.Count = 0           // ← Cleared correctly
_lists[0].Count = 13866       // ← Position list not cleared!
_lists[1].Count = 13866       // ← Velocity list not cleared!
```

**Evidence:** Would show in archetype validation ⚠️

## Investigation Plan

### Step 1: Add Validation Logging

Add logging to detect count mismatches:

```csharp
// In Archetype.RemoveAtSwap()
public void RemoveAtSwap(int slot)
{
    int beforeCount = _entities.Count;

    // ... existing code ...

    int afterCount = _entities.Count;

    // Validate
    for (int i = 0; i < _componentCount; i++)
    {
        if (_lists[i].Count != afterCount)
        {
            Logging.LogError($"MISMATCH: Archetype entities={afterCount}, component {_typeIds[i]} has {_lists[i].Count}");
        }
    }
}
```

### Step 2: Log Movement System Queries

```csharp
foreach (var arch in _cachedQuery!)
{
    Logging.Log($"Query archetype: Count={arch.Count}, Components={GetComponentCounts(arch)}");

    if (arch.Count == 0)
    {
        Logging.Log($"  ⚠️ Empty archetype, but component lists may have data!");
        continue;
    }
}
```

### Step 3: Check ComponentList.RemoveLast()

Verify that `ComponentList<T>.RemoveLast()` correctly decrements internal count.

### Step 4: Validate Entity Lookups

Before destroy, verify entity lookup is valid:

```csharp
public void Destroy(Entity entity)
{
    if (!TryGetLookup(entity.Index, out var loc))
    {
        Logging.LogWarning($"Cannot destroy entity {entity}: lookup not found!");
        return;
    }

    Logging.Log($"Destroying entity {entity}: archetype={loc.archetypeIdx}, slot={loc.slot}");

    // ... rest of destroy ...
}
```

## Next Steps

1. ✅ Implement ConcurrentDictionary fix (filters dead entities early)
2. ⏳ Add diagnostic logging to archetype operations
3. ⏳ Trace component list count management
4. ⏳ Verify query cache invalidation
5. ⏳ Run test and collect detailed logs

## Expected Findings

Most likely cause: **Component lists not properly cleared during RemoveAtSwap()**

The logs show `posList.Count=13866` after `arch.Count` should be 0 or near-0. This suggests `ComponentList<T>.RemoveLast()` or the component list management has a bug.
