# Thread Safety and Entity Validity Fixes

## Issues Identified and Fixed

### Issue 1: IndexOutOfRangeException in ChunkVisualPool ✅ FIXED
**File**: `Client/ECS/Systems/ChunkVisualPool.cs`
**Problem**: HashSet `_leased` was not thread-safe, causing concurrent modification exceptions when `Acquire()` was called from parallel threads in `DynamicEntityRenderSystem.ProcessChunksParallel()`.

**Stack Trace**:
```
System.IndexOutOfRangeException: Index was outside the bounds of the array.
at System.Collections.Generic.HashSet`1.AddIfNotPresent(T, System.Int32&)
at System.Collections.Generic.HashSet`1.Add(T)
ChunkVisualPool.cs:53 @ TVisual Client.ECS.Systems.ChunkVisualPool`1.Acquire()
```

**Root Cause**:
- `DynamicEntityRenderSystem.ProcessChunksParallel()` calls `ProcessChunk()` in parallel (line 270-273)
- Multiple threads call `GetOrCreateChunkPool().Acquire()` simultaneously
- HashSet internal array resizing during concurrent `Add()` operations caused IndexOutOfRangeException

**Fix Applied**:
- Added `private readonly object _lock = new();` to ChunkVisualPool
- Wrapped all HashSet/Stack access in `lock (_lock)` blocks
- Protected `Acquire()`, `Release()`, `ActiveCount`, and `AvailableCount`

**Impact**: Prevents crashes when spawning dynamic entities in parallel batches.

---

### Issue 2: Material/Mesh Assignment Thread Safety ✅ FIXED
**File**: `Client/ECS/Systems/DynamicEntityRenderSystem.cs:427-436`
**Problem**: `ApplyPrototypeMesh()` was modifying Godot scene graph nodes from parallel threads via direct assignment (`visual.Mesh = mesh`), but Godot's scene graph is NOT thread-safe.

**Root Cause**:
- `ApplyPrototypeMesh()` called from `UpdateOrAttachVisual()` (lines 332, 349)
- `UpdateOrAttachVisual()` called from `ProcessChunk()` (line 306)
- `ProcessChunk()` executed in parallel threads via `ProcessChunksParallel()` (line 270-273)
- Comment claimed "called from Update() which runs on main thread" but this was INCORRECT

**Fix Applied**:
```csharp
// Before (NOT thread-safe):
visual.Mesh = mesh;

// After (thread-safe):
if (visual.IsInsideTree())
    visual.SetDeferred(MeshInstance3D.PropertyName.Mesh, mesh);
```

**Impact**: Prevents "mesh_surface_set_material" errors and ensures thread-safe Godot node modification.

---

### Issue 3: Entity Validity Checks ✅ IMPROVED
**File**: `Client/ECS/Systems/DynamicEntityRenderSystem.cs:276-312`
**Problem**: DynamicEntityRenderSystem stores persistent per-entity bindings indexed by `entity.Index`, but when entities are destroyed/recycled, stale bindings remain, causing operations on invalid entities.

**Key Architectural Difference**:
- **StaticEntityRenderSystem**: Rebuilds entity list every frame by querying chunks → no stale references
- **DynamicEntityRenderSystem**: Stores persistent `_entityBindings` array across frames → accumulates stale references

**Fix Applied**:
Added entity validity check at start of `ProcessChunk()`:
```csharp
// SAFETY: Validate entity before processing to prevent stale binding issues
if (!world.IsEntityValid(entity))
    continue;
```

**Additional Protection**: ChunkSystem already has 5 validity checks added in previous fixes:
- RegisterNewChunks
- ProcessCreationBatchImmediate
- ProcessMovementBatchSmart
- TryProcessAssignmentFast
- ProcessAssignment

**Status**: This prevents crashes but doesn't address the root cause of why entities become invalid during spawning.

---

## Remaining Investigation Required

### Entity Validity Mystery ⚠️ NEEDS INVESTIGATION
**User Report**: "I'm getting a ton of these: [Error about invalid entity 1707]"
**User Clarification**: "all i'm doing is spawning dynamic entities. what is 'destroying' them before they're even spawned?"

**What We Know**:
1. User is ONLY spawning entities (no manual destruction)
2. Entities become invalid (version mismatch) hundreds of times per second
3. Error message: "Tried to remove component from invalid entity X"
4. Visuals only spawn sometimes (inconsistent rendering)

**Entity Versioning System**:
- Entity = (Index, Version) packed into ulong
- When entity destroyed: Version increments
- When index recycled: New entity gets incremented version
- `IsAlive()` checks: `entity.Version == _entityVersions[entity.Index]`
- If versions don't match → entity is INVALID

**Possible Causes**:
1. **Entity index recycling bug**: EntityManager recycling indices prematurely
2. **CommandBuffer race condition**: Multiple threads applying CommandBuffers simultaneously
3. **Hidden entity destruction**: Some system destroying entities without user's knowledge
4. **Event timing issue**: EntityBatchCreated fires but entities not fully initialized

**Evidence From Code**:
- EntityManager.ProcessQueues() (lines 232-250): Creates entities, applies builder, THEN fires event → looks correct
- Only component removal in ChunkSystem: Line 578 removes UnregisteredChunkTag from CHUNK entities (not regular entities)
- No obvious entity destruction in dynamic entity spawning path

**Next Steps for Debugging**:
1. Add logging in EntityManager.Destroy() to track ALL entity destructions
2. Add logging in EntityManager.Create() to track entity creation with stack traces
3. Monitor entity version increments in _entityVersions array
4. Check if CommandBuffer.Apply() is being called from multiple threads
5. Verify no systems are destroying entities automatically

---

## Rendering Pipeline Comparison

### Static Entity Rendering (StaticEntityRenderSystem)
- Uses MultiMesh batching
- No per-entity state stored
- Rebuilds entity list every frame from chunk queries
- Material: Set on MultiMeshInstance3D via MaterialOverride
- Mesh: Set on MultiMesh.Mesh property
- No cross-frame entity references → immune to stale entity issues

### Dynamic Entity Rendering (DynamicEntityRenderSystem)
- Uses individual MeshInstance3D per entity
- Stores persistent `_entityBindings[entityIndex]` array
- Expects entities to remain valid across frames
- Material: Set on MeshInstance3D via MaterialOverride
- Mesh: Set on MeshInstance3D.Mesh property (NOW using SetDeferred for thread safety)
- Persistent bindings → vulnerable to stale entity references

**Key Insight**: Dynamic rendering's persistent binding architecture makes it more susceptible to entity validity issues than static rendering's stateless architecture.

---

## Testing Recommendations

### Test 1: Verify Thread Safety Fixes
1. Spawn 20k dynamic entities in multiple batches
2. Check for IndexOutOfRangeException in ChunkVisualPool → should be GONE
3. Check for mesh_surface_set_material errors → should be GONE

### Test 2: Monitor Entity Validity Errors
1. Enable debug logging in ChunkSystem and DynamicEntityRenderSystem
2. Spawn dynamic entities and monitor console
3. Count "invalid entity" errors per second
4. Check if errors correlate with specific frame patterns

### Test 3: Rendering Consistency
1. Spawn 5k dynamic entities
2. Verify all entities render (no missing visuals)
3. Spawn additional batches (5k more)
4. Verify consistency across multiple spawn operations

### Test 4: Static vs Dynamic Comparison
1. Spawn 10k static entities → check for errors
2. Spawn 10k dynamic entities → check for errors
3. Compare error rates and rendering success
4. If static works but dynamic doesn't → confirms persistent binding issue

---

## Summary

**Fixed Issues**:
1. ✅ Thread safety in ChunkVisualPool (IndexOutOfRangeException)
2. ✅ Thread safety in ApplyPrototypeMesh (material assignment)
3. ✅ Entity validity checks in DynamicEntityRenderSystem

**Remaining Issues**:
1. ⚠️ Root cause of entity validity errors during spawning (investigation needed)
2. ⚠️ Inconsistent visual rendering for dynamic entities
3. ⚠️ Why entities become invalid when only spawning (no manual destruction)

**Recommended Next Step**: Test with the thread safety fixes applied. If entity validity errors persist, add comprehensive logging to EntityManager to track entity lifecycle and identify what's destroying entities.
