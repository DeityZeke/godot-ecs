# Chunk Entity Pooling Fix

## The Problem: "Tried to remove component from invalid entity" During Initialization

### Error Observed
```
[Error] ECS: [World] Tried to remove component from invalid entity 1260
```

**Context**: Error occurred BEFORE spawning any dynamic entities, during chunk system initialization.

## Root Cause Analysis

### The Chunk Entity Lifecycle Bug

The issue stems from **chunk entity pooling** combined with **deferred component removal**:

1. **Frame N**: Chunk entity (e.g., 1260) created with `UnregisteredChunkTag`
2. **Frame N**: `RegisterNewChunks()` queues removal:
   ```csharp
   _buffer.RemoveComponent(entity.Index, UnregisteredChunkTagTypeId);
   ```
   - ⚠️ Only stores `entity.Index` (1260), not full Entity with version
3. **Frame N or N+1**: Chunk gets evicted and **pooled**:
   ```csharp
   _chunkPool.Enqueue(chunkEntity); // Still has UnregisteredChunkTag!
   ```
4. **Frame N+1**: Chunk entity **reused** from pool with same index (1260):
   ```csharp
   var pooled = _chunkPool.Dequeue(); // Index 1260, possibly new version
   _chunkManager.RegisterChunk(pooled, location); // No UnregisteredChunkTag added
   ```
5. **Frame N+1 or N+2**: CommandBuffer finally applies deferred removal:
   ```csharp
   world.RemoveComponentFromEntityInternal(1260, UnregisteredChunkTagTypeId);
   ```
   - ❌ Entity 1260 is now a different entity (reused with new version or state)
   - ❌ Tries to remove UnregisteredChunkTag that doesn't exist or is from wrong entity
   - ❌ Error: "Tried to remove component from invalid entity 1260"

### Why This Happens

**CommandBuffer Limitation**:
- `RemoveComponent(uint entityIndex, int componentTypeId)` stores ONLY the index
- No version information stored
- When entity is destroyed/pooled/reused, the index may refer to a different entity

**Chunk Pooling Issue**:
- Pooled chunks retained `UnregisteredChunkTag` component
- Reused chunks didn't get `UnregisteredChunkTag` (they're immediately registered)
- Deferred removal operation became stale, referring to wrong entity

## The Solution

### Immediate Component Removal Before Pooling

**File**: `Server/ECS/Systems/ChunkSystem.cs:853-858`

```csharp
// Before pooling:
_chunkManager.UnregisterChunk(chunkEntity);

// SAFETY: Remove UnregisteredChunkTag if present before pooling
// This prevents stale CommandBuffer operations on pooled/reused entities
if (archetype.HasComponent(UnregisteredChunkTagTypeId))
{
    world.RemoveComponent<UnregisteredChunkTag>(chunkEntity);
}

_chunkPool.Enqueue(chunkEntity);
```

### How It Works

1. **Before pooling**: Immediately remove `UnregisteredChunkTag` (if present)
   - Uses `world.RemoveComponent<T>()` which is immediate, not deferred
   - Ensures component is gone BEFORE entity goes into pool

2. **Pooled entities**: No longer have `UnregisteredChunkTag`
   - Clean state for reuse
   - No stale deferred operations

3. **Reused entities**: Immediately registered, don't need `UnregisteredChunkTag`
   - Line 823: `_chunkManager.RegisterChunk(pooled, location);`
   - Already in correct state

4. **Deferred CommandBuffer**: When applied, removal becomes no-op
   - Component already removed → `RemoveComponentFromEntityInternal` returns early (line 216-217)
   - No error, silent success

### Why This Fix Works

- **Prevents stale references**: Component removed before entity can be reused
- **No performance impact**: Immediate removal is fast (no archetype change needed if already gone)
- **Handles race conditions**: Even if CommandBuffer fires later, it's a harmless no-op
- **Backward compatible**: Doesn't change chunk creation or registration flow

## Testing Recommendations

### Test 1: Initialization Without Errors
1. Start fresh session
2. Don't spawn any entities
3. Verify no "invalid entity" errors in console
4. **Expected**: Clean initialization, no errors

### Test 2: Chunk Pooling Stress Test
1. Enable chunk pooling: `SystemSettings.EnableChunkPooling = true`
2. Spawn entities across many chunks (force chunk creation)
3. Move entities to trigger chunk eviction/pooling
4. Spawn more entities to force chunk reuse from pool
5. **Expected**: No "invalid entity" errors during pooling/reuse

### Test 3: Dynamic Entity Spawning
1. Spawn 20k dynamic entities in batches
2. Monitor console for errors
3. Verify visuals render consistently
4. **Expected**: No entity validity errors, consistent rendering

## Related Fixes

This fix complements previous thread safety fixes:
1. **ChunkVisualPool thread safety** (IndexOutOfRangeException fix)
2. **ApplyPrototypeMesh thread safety** (material assignment fix)
3. **DynamicEntityRenderSystem entity validity checks**

All three fixes combined should eliminate:
- ✅ Entity validity errors during initialization
- ✅ IndexOutOfRangeException in ChunkVisualPool
- ✅ Material assignment errors from parallel threads
- ✅ Stale entity binding issues in rendering

## Summary

**Problem**: Deferred component removal from pooled chunk entities caused invalid entity errors.

**Root Cause**: CommandBuffer stores only entity index, not version. Pooled entities were reused with stale deferred operations.

**Solution**: Immediately remove UnregisteredChunkTag before pooling, making deferred operations harmless no-ops.

**Impact**: Eliminates "Tried to remove component from invalid entity" errors during chunk system operations.
