# CHUNK SYSTEM AUDIT

**Date**: 2025-11-13
**Purpose**: Analyze current state vs. required architecture for sliding render window

---

## CURRENT STATE ANALYSIS

### ✅ **What EXISTS and WORKS:**

#### 1. **Generic ChunkManager** (`/UltraSim/ECS/Chunk/ChunkManager.cs`)
- **Status**: ✅ Correct - Already generic and reusable
- **Features**:
  - Coordinate conversion (WorldToChunk, ChunkToWorldBounds)
  - Register/Unregister chunk entities
  - Spatial queries (GetChunksInRadius, GetChunksInBounds)
  - Frame tracking (`TouchChunk`, `CollectStaleChunks`)
  - Statistics tracking
- **No changes needed** - This is engine-independent ✅

#### 2. **ChunkEntityTracker** (`/UltraSim/ECS/Chunk/ChunkEntityTracker.cs`)
- **Status**: ✅ Exists - Tracks which entities are in which chunks
- **Features**:
  - `Add(chunkEntity, entity)` - Add entity to chunk's list
  - `Remove(chunkEntity, entity)` - Remove entity from chunk's list
  - `GetEntities(chunkEntity)` - Enumerate all entities in chunk
  - `GetEntityCount(chunkEntity)` - Count entities in chunk
- **Used by**: Server ChunkSystem
- **Performance**: Bitset-based sparse storage (64 entities per block)

#### 3. **ChunkOwner Component** (`/UltraSim/ECS/Components/Chunk/ChunkOwner.cs`)
- **Status**: ✅ Exists - Entities track which chunk they're in
- **Fields**:
  - `Entity ChunkEntity` - Reference to owning chunk entity
  - `ChunkLocation CachedLocation` - Fast lookup without reading ChunkLocation component
- **Purpose**: Entity → Chunk mapping (reverse of ChunkEntityTracker)

#### 4. **ChunkState Component** (`/UltraSim/ECS/Components/Chunk/ChunkState.cs`)
- **Status**: ⚠️ Partial - Has lifecycle tracking, MISSING dirty flag
- **Current Fields**:
  - `ChunkLifecycleState Lifecycle` - Enum (Unregistered, Pending, Registered, Active, etc.)
  - `int EntityCount` - Number of entities in chunk
  - `ulong LastAccessFrame` - For LRU eviction
- **MISSING**: `bool IsDirty` or separate `DirtyChunkTag` component

#### 5. **Server ChunkSystem** (`/Server/ECS/Systems/ChunkSystem.cs`)
- **Status**: ⚠️ Needs refactoring - Works but doesn't communicate with RenderChunkManager
- **Current Responsibilities**:
  - Creates spatial chunk entities when needed
  - Assigns entities to chunks based on Position
  - Handles chunk pooling/eviction (IdleFrames, MaxChunkCount)
  - Uses ChunkEntityTracker to maintain entity lists
  - Subscribes to `EntityBatchCreated` and `EntityBatchProcessed`
- **MISSING**:
  - Marks chunks dirty when entities move in/out
  - API for client to query "which entities are in this chunk?"

#### 6. **RenderChunk Component** (`/Client/ECS/Components/RenderChunk.cs`)
- **Status**: ❌ INCOMPLETE - Missing server chunk reference
- **Current Fields**:
  - `ChunkLocation Location` - **PROBLEM**: Unclear if this is render position or server position
  - `ChunkBounds Bounds` - World-space bounds
  - `bool Visible` - Frustum culling result
- **MISSING**:
  - `ChunkLocation ServerChunkLocation` - Which server chunk does this render
  - OR: `Entity ServerChunkEntity` - Direct reference to server chunk entity

#### 7. **RenderChunkManager** (`/Client/ECS/Systems/RenderChunkManager.cs`)
- **Status**: ❌ INCORRECT LOGIC - Creates/destroys chunks instead of sliding window
- **Current Behavior** (lines 185-340):
  - Calculates window locations around player
  - Queries existing render chunks
  - **WRONG**: Pools chunks outside window
  - **WRONG**: Creates new chunks for missing locations
  - **WRONG**: Treats chunks as absolute positions, not relative offsets
- **Required Behavior**:
  - Maintain fixed pool of render chunk entities (one per relative offset)
  - Slide window by reassigning server chunk references
  - Pool/unpol only at edges (entering/exiting render distance)
  - Update `ServerChunkLocation` when window slides

---

## ❌ **What is MISSING:**

### 1. **Dirty Chunk Tracking**
**Status**: ❌ Does not exist

**Need**: Server chunks must track when entity lists change

**Options**:
```csharp
// Option A: Add to ChunkState
public struct ChunkState
{
    public ChunkLifecycleState Lifecycle;
    public int EntityCount;
    public ulong LastAccessFrame;
    public bool IsDirty; // NEW
}

// Option B: Separate tag component (cleaner for queries)
public struct DirtyChunkTag { }
```

**When to mark dirty**:
- Entity moves into chunk (ChunkEntityTracker.Add)
- Entity moves out of chunk (ChunkEntityTracker.Remove)
- Entity spawned in chunk
- Entity destroyed in chunk

**When to clear dirty**:
- Client rebuilds visual cache for that chunk

---

### 2. **Render Chunk → Server Chunk Mapping**
**Status**: ❌ Does not exist

**Need**: Each render chunk must know which server chunk it's rendering

**Add to RenderChunk component**:
```csharp
public struct RenderChunk
{
    public ChunkLocation Location;           // Relative render window position
    public ChunkLocation ServerChunkLocation; // NEW: Absolute server chunk
    public ChunkBounds Bounds;
    public bool Visible;
}
```

**Alternative** (if need entity reference):
```csharp
public struct RenderChunk
{
    public ChunkLocation Location;           // Relative render window position
    public Entity ServerChunkEntity;         // NEW: Direct reference to server chunk
    public ChunkBounds Bounds;
    public bool Visible;
}
```

---

### 3. **Entity Movement → Chunk Transfer Queue**
**Status**: ❌ Does not exist

**Need**: After movement, entities should queue chunk transfers if they crossed boundaries

**Current Flow** (EntityBatchProcessed event):
1. OptimizedMovementSystem updates Position components
2. Fires `EntityBatchProcessed` event
3. ChunkSystem receives event
4. ChunkSystem checks if entities crossed chunk boundaries
5. ChunkSystem reassigns entities to new chunks

**Required Addition**:
- After reassignment, mark old chunk dirty
- After reassignment, mark new chunk dirty

**Optimization** (user's suggestion):
```csharp
// On entity (optional component for fast checks)
public struct CurrentChunk
{
    public Entity ChunkEntity;        // Current chunk entity
    public ChunkLocation Location;    // Current chunk location
    public ChunkBounds Bounds;        // Cached bounds for fast checks
}

// After movement in OptimizedMovementSystem:
if (!currentChunk.Bounds.Contains(newPosition))
{
    // Queue chunk transfer
    _chunkTransferQueue.Enqueue(entity);
}
```

**Sanity Check in ChunkSystem**:
```csharp
// Before reassignment, double-check position
if (entity has CurrentChunk)
{
    var currentChunkBounds = world.GetComponent<ChunkBounds>(currentChunk.ChunkEntity);
    if (currentChunkBounds.Contains(entity.Position))
    {
        // Entity moved back into bounds, skip reassignment
        continue;
    }
}
```

---

### 4. **Client Query API for Server Chunks**
**Status**: ❌ Does not exist

**Need**: RenderChunkManager must query server chunks for entity lists

**Required API**:
```csharp
// In ChunkSystem or ChunkManager
public IEnumerable<Entity> GetEntitiesInChunk(Entity chunkEntity)
{
    return _chunkEntityTracker.GetEntities(chunkEntity);
}

public bool IsChunkDirty(Entity chunkEntity)
{
    return world.HasComponent<DirtyChunkTag>(chunkEntity);
}

public void ClearChunkDirty(Entity chunkEntity)
{
    world.RemoveComponent<DirtyChunkTag>(chunkEntity);
}
```

---

### 5. **Render Window Sliding Algorithm**
**Status**: ❌ Completely wrong implementation

**Current**: Creates/destroys chunks at absolute positions
**Required**: Reassigns fixed pool to new server chunks

**Pseudocode for CORRECT sliding**:
```csharp
// Player moves from chunk (5,0,0) to chunk (6,0,0)
// Window slides by (-1,0,0) in relative space

foreach (var renderChunkEntity in allRenderChunks)
{
    var renderChunk = world.GetComponent<RenderChunk>(renderChunkEntity);

    // Slide relative position
    var newRelativePos = renderChunk.Location - playerMovementDelta;

    // Check if still in render distance
    if (IsOutsideRenderDistance(newRelativePos))
    {
        // Pool this chunk
        RemoveZoneTags(renderChunkEntity);
        AddComponent<RenderChunkPoolTag>(renderChunkEntity);
        continue;
    }

    // Update render chunk to point to new server chunk
    var newServerChunkLocation = playerChunkLocation + newRelativePos;
    renderChunk.Location = newRelativePos;
    renderChunk.ServerChunkLocation = newServerChunkLocation;
    world.SetComponent(renderChunkEntity, renderChunk);

    // Update zone tags based on new distance
    UpdateZoneTags(renderChunkEntity, Distance(newRelativePos));
}

// Fill gaps at edges (chunks entering render distance)
foreach (var newRelativeOffset in GetNewRenderOffsets())
{
    // Grab pooled chunk or create new
    var renderChunkEntity = GetPooledChunk() ?? CreateNewRenderChunk();

    // Assign to server chunk
    var serverChunkLocation = playerChunkLocation + newRelativeOffset;
    var renderChunk = new RenderChunk
    {
        Location = newRelativeOffset,
        ServerChunkLocation = serverChunkLocation,
        Bounds = chunkManager.ChunkToWorldBounds(serverChunkLocation),
        Visible = false
    };

    world.SetComponent(renderChunkEntity, renderChunk);
    UpdateZoneTags(renderChunkEntity, Distance(newRelativeOffset));
}
```

---

## SUMMARY: FILES NEEDING CHANGES

### ✅ **No Changes Needed:**
1. `/UltraSim/ECS/Chunk/ChunkManager.cs` - Already generic ✅
2. `/UltraSim/ECS/Chunk/ChunkEntityTracker.cs` - Already tracks entity lists ✅
3. `/UltraSim/ECS/Components/Chunk/ChunkOwner.cs` - Already tracks entity→chunk ✅

### ⚠️ **Minor Additions:**
4. `/UltraSim/ECS/Components/Chunk/ChunkState.cs` - Add `bool IsDirty` field
   - OR: Create new `/UltraSim/ECS/Components/Chunk/DirtyChunkTag.cs`
5. `/Client/ECS/Components/RenderChunk.cs` - Add `ChunkLocation ServerChunkLocation`
6. `/UltraSim/ECS/Components/Chunk/CurrentChunk.cs` - NEW (optional optimization)

### 🔧 **Major Refactoring:**
7. `/Server/ECS/Systems/ChunkSystem.cs` - Mark chunks dirty on entity add/remove
8. `/Client/ECS/Systems/RenderChunkManager.cs` - Rewrite sliding window logic

---

## NEXT STEP: REFACTOR PLAN

See `CHUNK_SYSTEM_REFACTOR_PLAN.md` for detailed implementation steps.
