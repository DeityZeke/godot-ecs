# CHUNK SYSTEM REFACTOR PLAN

**Date**: 2025-11-13
**Goal**: Implement sliding render window with server chunk correspondence

---

## PHASE 1: Add Missing Components (1 hour)

### Step 1.1: Add Dirty Chunk Tracking
**File**: `/UltraSim/ECS/Components/Chunk/DirtyChunkTag.cs` (NEW)

```csharp
#nullable enable

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Tag component indicating a server spatial chunk has been modified.
    /// Set when entities move in/out of the chunk.
    /// Cleared when client rebuilds visual cache for this chunk.
    /// </summary>
    public struct DirtyChunkTag { }
}
```

**Why tag instead of bool in ChunkState?**
- Query-based dirty chunk enumeration: `world.QueryArchetypes(typeof(DirtyChunkTag))`
- Archetype-based filtering (ECS pattern)
- No need to iterate all chunks to find dirty ones

---

### Step 1.2: Add Server Chunk Reference to RenderChunk
**File**: `/Client/ECS/Components/RenderChunk.cs`

**BEFORE**:
```csharp
public struct RenderChunk
{
    public ChunkLocation Location;  // AMBIGUOUS: Render or server?
    public ChunkBounds Bounds;
    public bool Visible;
}
```

**AFTER**:
```csharp
public struct RenderChunk
{
    /// <summary>
    /// Relative position in render window (e.g., +3, 0, -1 from player).
    /// This is the RENDER SPACE position, not world space.
    /// </summary>
    public ChunkLocation RenderOffset;

    /// <summary>
    /// Absolute server spatial chunk location this render chunk corresponds to.
    /// This is the WORLD SPACE chunk location.
    /// Example: Player at chunk (10,0,5), RenderOffset (+2,0,0) → ServerChunkLocation (12,0,5)
    /// </summary>
    public ChunkLocation ServerChunkLocation;

    /// <summary>World-space bounds for frustum culling.</summary>
    public ChunkBounds Bounds;

    /// <summary>Whether the chunk is currently visible (frustum culling result).</summary>
    public bool Visible;
}
```

**Migration**: All existing code using `RenderChunk.Location` must be updated to specify `RenderOffset` or `ServerChunkLocation`

---

### Step 1.3: Add Optional CurrentChunk Component (OPTIONAL OPTIMIZATION)
**File**: `/UltraSim/ECS/Components/Chunk/CurrentChunk.cs` (NEW)

```csharp
#nullable enable

using UltraSim.ECS.Chunk;

namespace UltraSim.ECS.Components
{
    /// <summary>
    /// Optional optimization: Tracks entity's current chunk for fast boundary checks.
    /// Allows entities to self-detect when they cross chunk boundaries after movement.
    /// </summary>
    public struct CurrentChunk
    {
        /// <summary>Current chunk entity this entity belongs to.</summary>
        public Entity ChunkEntity;

        /// <summary>Current chunk location.</summary>
        public ChunkLocation Location;

        /// <summary>
        /// Cached chunk bounds for fast containment checks.
        /// Entity can check if new position is within bounds without querying world.
        /// </summary>
        public ChunkBounds Bounds;
    }
}
```

**Usage**: After movement, entity checks `!CurrentChunk.Bounds.Contains(newPosition)` to queue chunk transfer

---

## PHASE 2: Server ChunkSystem Updates (2 hours)

### Step 2.1: Mark Chunks Dirty on Entity Changes
**File**: `/Server/ECS/Systems/ChunkSystem.cs`

**Locations to add dirty marking**:

1. **After entity assigned to chunk** (assign flow):
```csharp
private void AssignEntityToChunk(Entity entity, ChunkLocation location)
{
    // ... existing assignment logic ...
    _chunkEntityTracker.Add(chunkEntity, entity);

    // NEW: Mark chunk as dirty
    _buffer.AddComponent(chunkEntity, ComponentManager.GetTypeId<DirtyChunkTag>(), new DirtyChunkTag());
}
```

2. **After entity removed from chunk** (reassignment/destroy flow):
```csharp
private void RemoveEntityFromChunk(Entity entity, Entity oldChunkEntity)
{
    // ... existing removal logic ...
    _chunkEntityTracker.Remove(oldChunkEntity, entity);

    // NEW: Mark old chunk as dirty
    _buffer.AddComponent(oldChunkEntity, ComponentManager.GetTypeId<DirtyChunkTag>(), new DirtyChunkTag());
}
```

3. **After chunk reassignment** (movement flow):
```csharp
private void ReassignEntityChunk(Entity entity, Entity oldChunk, Entity newChunk)
{
    _chunkEntityTracker.Remove(oldChunk, entity);
    _chunkEntityTracker.Add(newChunk, entity);

    // NEW: Mark both chunks as dirty
    _buffer.AddComponent(oldChunk, ComponentManager.GetTypeId<DirtyChunkTag>(), new DirtyChunkTag());
    _buffer.AddComponent(newChunk, ComponentManager.GetTypeId<DirtyChunkTag>(), new DirtyChunkTag());
}
```

---

### Step 2.2: Add Public Query API
**File**: `/Server/ECS/Systems/ChunkSystem.cs`

Add public methods for client to query:

```csharp
/// <summary>
/// Get all entities currently in a server spatial chunk.
/// Used by client RenderChunkManager to query which entities to render.
/// </summary>
public IEnumerable<Entity> GetEntitiesInChunk(Entity chunkEntity)
{
    return _chunkEntityTracker.GetEntities(chunkEntity);
}

/// <summary>
/// Check if a server spatial chunk has been modified since last visual cache rebuild.
/// </summary>
public bool IsChunkDirty(World world, Entity chunkEntity)
{
    return world.HasComponent<DirtyChunkTag>(chunkEntity);
}

/// <summary>
/// Clear dirty flag after client rebuilds visual cache.
/// Called by RenderChunkManager after processing chunk.
/// </summary>
public void ClearChunkDirty(Entity chunkEntity)
{
    _buffer.RemoveComponent(chunkEntity, ComponentManager.GetTypeId<DirtyChunkTag>());
}

/// <summary>
/// Get all dirty chunks (for client to enumerate and rebuild).
/// </summary>
public List<Entity> GetDirtyChunks(World world)
{
    var result = new List<Entity>();
    var archetypes = world.QueryArchetypes(typeof(DirtyChunkTag));

    foreach (var archetype in archetypes)
    {
        var entities = archetype.GetEntityArray();
        result.AddRange(entities);
    }

    return result;
}
```

---

### Step 2.3: Add Sanity Check for Movement Reassignment (OPTIONAL)
**File**: `/Server/ECS/Systems/ChunkSystem.cs`

In `OnEntityBatchProcessed` or wherever chunk reassignment happens:

```csharp
private void ProcessMovementBatch(EntityBatchProcessedEventArgs args)
{
    var entities = args.GetSpan();

    for (int i = 0; i < entities.Length; i++)
    {
        var entity = entities[i];

        // Get entity's current chunk assignment
        if (!_world.TryGetComponent<ChunkOwner>(entity, out var owner))
            continue;

        // Get entity's current position
        if (!_world.TryGetComponent<Position>(entity, out var position))
            continue;

        // SANITY CHECK: Is entity still inside current chunk?
        var currentChunkBounds = _chunkManager.ChunkToWorldBounds(owner.CachedLocation);
        if (currentChunkBounds.Contains(position.X, position.Y, position.Z))
        {
            // Entity moved back into bounds, no reassignment needed
            continue;
        }

        // Entity is outside current chunk, reassign
        var newChunkLocation = _chunkManager.WorldToChunk(position.X, position.Y, position.Z);
        if (newChunkLocation == owner.CachedLocation)
            continue; // Still same chunk (rounding edge case)

        ReassignEntityToChunk(entity, newChunkLocation);
    }
}
```

---

## PHASE 3: Client RenderChunkManager Rewrite (4 hours)

### Step 3.1: Change Data Model
**Current**: Dictionary<ChunkLocation, Entity> (absolute positions)
**New**: Fixed array/pool of render chunk entities (relative offsets)

```csharp
public sealed class RenderChunkManager : BaseSystem
{
    // OLD: Dynamic dictionary
    // private readonly Dictionary<ChunkLocation, Entity> _renderChunks = new();

    // NEW: Fixed pool of render chunk entities (one per relative offset in max window)
    private readonly Dictionary<(int dx, int dy, int dz), Entity> _renderChunkPool = new();

    private ChunkLocation _lastPlayerChunkLocation = new(int.MaxValue, int.MaxValue, int.MaxValue);
}
```

---

### Step 3.2: Initialize Fixed Render Chunk Pool
**When**: OnInitialize or first Update

```csharp
private void InitializeRenderChunkPool(World world)
{
    int maxRadius = SystemSettings.FarRenderDistance.Value;
    int yRadius = SystemSettings.NearBubbleSize.Value / 2;

    // Create render chunk entities for all possible relative offsets
    for (int dz = -maxRadius; dz <= maxRadius; dz++)
    {
        for (int dx = -maxRadius; dx <= maxRadius; dx++)
        {
            for (int dy = -yRadius; dy <= yRadius; dy++)
            {
                var offset = (dx, dy, dz);

                // Create render chunk entity
                var entity = world.CreateEntity();

                // Add RenderChunk component (initially unassigned)
                var renderChunk = new RenderChunk
                {
                    RenderOffset = new ChunkLocation(dx, dz, dy),
                    ServerChunkLocation = new ChunkLocation(0, 0, 0), // Will be set on first update
                    Bounds = default,
                    Visible = false
                };
                world.AddComponent(entity, renderChunk);

                // Add to pool (initially all pooled)
                world.AddComponent(entity, new RenderChunkPoolTag());

                _renderChunkPool[offset] = entity;
            }
        }
    }

    Logging.Log($"[{Name}] Initialized render chunk pool with {_renderChunkPool.Count} entities");
}
```

---

### Step 3.3: Sliding Window Algorithm (CORE LOGIC)
**When**: Player moves to new chunk

```csharp
private void UpdateRenderWindow(World world, ChunkLocation playerChunkLocation)
{
    if (_chunkManager == null)
        return;

    // Calculate movement delta
    var delta = new ChunkLocation(
        playerChunkLocation.X - _lastPlayerChunkLocation.X,
        playerChunkLocation.Z - _lastPlayerChunkLocation.Z,
        playerChunkLocation.Y - _lastPlayerChunkLocation.Y
    );

    // Skip if player hasn't moved chunks
    if (delta.X == 0 && delta.Y == 0 && delta.Z == 0)
        return;

    _lastPlayerChunkLocation = playerChunkLocation;

    // STEP 1: Slide all existing render chunks
    foreach (var kvp in _renderChunkPool)
    {
        var offset = kvp.Key;
        var renderChunkEntity = kvp.Value;

        var renderChunk = world.GetComponent<RenderChunk>(renderChunkEntity);

        // Calculate new server chunk location (window slid by -delta in relative space)
        // Example: Player moved (+1,0,0), so all chunks shift (-1,0,0) in relative space
        // But server chunk location shifts (+1,0,0) in absolute space
        var newServerChunkLocation = new ChunkLocation(
            renderChunk.ServerChunkLocation.X + delta.X,
            renderChunk.ServerChunkLocation.Z + delta.Z,
            renderChunk.ServerChunkLocation.Y + delta.Y
        );

        // Update render chunk to point to new server chunk
        renderChunk.ServerChunkLocation = newServerChunkLocation;
        renderChunk.Bounds = _chunkManager.ChunkToWorldBounds(newServerChunkLocation);
        world.SetComponent(renderChunkEntity, renderChunk);

        // Check if still in render distance
        float distXZ = MathF.Sqrt(offset.Item1 * offset.Item1 + offset.Item3 * offset.Item3);

        int nearRadius = SystemSettings.NearBubbleSize.Value / 2;
        int midRadius = SystemSettings.MidRenderDistance.Value;
        int farRadius = SystemSettings.FarRenderDistance.Value;

        if (distXZ > farRadius || Math.Abs(offset.Item2) > nearRadius)
        {
            // Outside render distance - pool this chunk
            RemoveAllZoneTags(_buffer, renderChunkEntity);
            _buffer.AddComponent(renderChunkEntity, ComponentManager.GetTypeId<RenderChunkPoolTag>(), new RenderChunkPoolTag());
        }
        else
        {
            // Still in render distance - update zone tags
            ZoneType zone;
            if (distXZ <= nearRadius)
                zone = ZoneType.Near;
            else if (distXZ <= midRadius)
                zone = ZoneType.Mid;
            else
                zone = ZoneType.Far;

            UpdateZoneTags(_buffer, renderChunkEntity, zone);

            // Remove pool tag if present
            if (world.HasComponent<RenderChunkPoolTag>(renderChunkEntity))
            {
                _buffer.RemoveComponent(renderChunkEntity, ComponentManager.GetTypeId<RenderChunkPoolTag>());
            }
        }
    }

    _buffer.Apply(world);
}
```

---

### Step 3.4: Query Dirty Server Chunks and Rebuild Visual Caches
**When**: Every frame (or based on settings)

```csharp
private void ProcessDirtyChunks(World world)
{
    if (_chunkManager == null || _chunkSystem == null)
        return;

    // Query all dirty server chunks
    var dirtyChunks = _chunkSystem.GetDirtyChunks(world);

    foreach (var dirtyChunkEntity in dirtyChunks)
    {
        // Find render chunk(s) that correspond to this server chunk
        // (Could be multiple if render window overlaps server chunks)

        if (!world.TryGetComponent<ChunkLocation>(dirtyChunkEntity, out var serverLocation))
            continue;

        // Find render chunk entity for this server location
        foreach (var kvp in _renderChunkPool)
        {
            var renderChunkEntity = kvp.Value;
            var renderChunk = world.GetComponent<RenderChunk>(renderChunkEntity);

            if (renderChunk.ServerChunkLocation == serverLocation)
            {
                // This render chunk needs to rebuild its visual cache
                RebuildVisualCache(world, renderChunkEntity, dirtyChunkEntity);
                break;
            }
        }

        // Clear dirty flag after processing
        _chunkSystem.ClearChunkDirty(dirtyChunkEntity);
    }
}

private void RebuildVisualCache(World world, Entity renderChunkEntity, Entity serverChunkEntity)
{
    // Query entities in server chunk
    var entitiesInChunk = _chunkSystem.GetEntitiesInChunk(serverChunkEntity);

    // TODO: Build visual meshes/billboards based on entities
    // This is where you'd:
    // 1. Query entity positions/models
    // 2. Generate MeshInstance/MultiMesh/Billboards
    // 3. Update render chunk's visual data component

    Logging.Log($"[{Name}] Rebuilt visual cache for render chunk {renderChunkEntity.Index} (server chunk {serverChunkEntity.Index})");
}
```

---

## PHASE 4: Integration & Testing (2 hours)

### Step 4.1: Wire up ChunkSystem reference in RenderChunkManager
**File**: `/Client/ECS/Systems/RenderChunkManager.cs`

```csharp
private ChunkSystem? _chunkSystem;

public override void OnInitialize(World world)
{
    // Get reference to server ChunkSystem
    _chunkSystem = world.Systems.GetSystem<ChunkSystem>() as ChunkSystem;

    if (_chunkSystem == null)
    {
        Logging.Log($"[{Name}] WARNING: ChunkSystem not found!", LogSeverity.Warning);
    }
}
```

---

### Step 4.2: Test Scenarios

**Test 1: Player stationary**
- Render chunks should maintain same server chunk references
- No visual cache rebuilds

**Test 2: Player moves +X**
- All render chunks slide in relative space
- Server chunk references update
- Chunks at +X edge enter render distance
- Chunks at -X edge exit to pool

**Test 3: Entity moves between chunks**
- Server chunk marked dirty
- Render chunk(s) rebuild visual cache
- Dirty flag cleared

**Test 4: Render distance change**
- New chunks unpooled at edges
- Old chunks pooled beyond distance

---

## ESTIMATED EFFORT

| Phase | Task | Time |
|-------|------|------|
| 1 | Add missing components | 1 hour |
| 2 | Update server ChunkSystem | 2 hours |
| 3 | Rewrite RenderChunkManager | 4 hours |
| 4 | Integration & testing | 2 hours |
| **TOTAL** | | **9 hours** |

---

## RISK ASSESSMENT

### High Risk:
- **RenderChunkManager rewrite**: Complete logic change, high chance of bugs

### Medium Risk:
- **ChunkSystem dirty marking**: Must catch all entity add/remove paths
- **CommandBuffer timing**: Dirty tags must be applied in correct pipeline phase

### Low Risk:
- **Component additions**: Simple data structures, low complexity

---

## ROLLBACK PLAN

If refactor fails:
1. Keep old RenderChunkManager.cs as `RenderChunkManager.OLD.cs`
2. Git branch for refactor: `feature/sliding-render-window`
3. Can revert to old absolute-position logic if needed

---

## NEXT STEPS

**Ready to begin?** Start with Phase 1 (add components) as it's low-risk and foundational.
