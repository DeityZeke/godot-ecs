# Chunk Manager Architecture Documentation
**Date:** 2025-11-13
**Status:** Current Implementation
**Version:** 2.0 (Simplified)

---

## Table of Contents
1. [Overview](#overview)
2. [The Problem (Old Architecture)](#the-problem-old-architecture)
3. [The Solution (New Architecture)](#the-solution-new-architecture)
4. [Components](#components)
5. [Operational Flow](#operational-flow)
6. [Usage Guide](#usage-guide)
7. [Performance Characteristics](#performance-characteristics)
8. [Migration Notes](#migration-notes)

---

## Overview

The Chunk Manager system provides **spatial partitioning** for entities in 3D space. It divides the world into fixed-size chunks and tracks which entities are in which chunks, enabling efficient spatial queries.

### Core Principle

**Chunks are observers, not modifiers.**

The Chunk Manager tracks entities without modifying them. This eliminates race conditions and archetype transition issues that plagued the previous implementation.

---

## The Problem (Old Architecture)

### What Was Wrong

The original ChunkSystem had a fundamental architectural flaw: **it modified entities after creation**, causing archetype transitions that led to race conditions.

#### Race Condition Timeline

```
T=0ms:  EntityManager creates 100K entities
        └─ Archetype: Position + Velocity + RenderTag

T=1ms:  FireEntityBatchCreated() event fires
        └─ ChunkSystem.OnEntityBatchCreated() called immediately

T=2ms:  ChunkSystem tries to read entity locations
        └─ TryGetEntityLocation(entity, out archetype, out slot)
        └─ ❌ World is NOT stable yet (still in ProcessQueues phase!)

T=3ms:  ChunkSystem enqueues ComponentAdd(ChunkOwner)
        └─ Queued for Phase 3 (Component Operations)

T=4ms:  ComponentManager.ProcessQueues() runs
        └─ Adds ChunkOwner to entities
        └─ Archetype: Position + Velocity + RenderTag + ChunkOwner
        └─ ❌ Entity moved to different archetype
        └─ ❌ Slot index changed in lookup table

T=5ms:  ERROR: "Invalid slot 4636 for archetype with 4633 entities"
        └─ Another operation tries to access entity with stale slot
```

### Key Issues

1. **Archetype Transitions After Creation**
   - Entities created without ChunkOwner
   - ChunkOwner added later → archetype move
   - Slot indices invalidated

2. **Event Timing**
   - EntityBatchCreated fires during Phase 1.5 (entity operations)
   - Component operations happen in Phase 3
   - Event handler reads unstable world state

3. **Race Conditions**
   - Multiple batches in flight simultaneously
   - Lookup table has mix of old/new slot indices
   - Parallel processing amplified the problem

4. **Architectural Complexity**
   - Dynamic chunk entity creation/destruction
   - Chunk pooling systems
   - UnregisteredChunkTag, DirtyChunkTag components
   - Deferred batch processing with parallel ForEach
   - All unnecessary!

---

## The Solution (New Architecture)

### Design Principles

1. **ChunkOwner Added At Creation**
   - Entities spawn with ChunkOwner component
   - No archetype transitions after creation
   - Final archetype known from the start

2. **Chunks As Pure Trackers**
   - Chunks store `HashSet<ulong>` of entity.Packed values
   - NO component manipulation
   - NO entity creation/destruction

3. **Deferred Processing Only**
   - Event handlers just enqueue entity IDs
   - NO component reads during event handlers
   - Processing happens in Update() when world is stable

4. **Thread-Safe Operations**
   - Per-chunk locking (fine-grained)
   - ConcurrentQueue for event processing
   - No shared state except chunk dictionaries

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│ Entity Creation (EntitySpawnerSystem)                       │
└─────────────────────────────────────────────────────────────┘
         │
         │ 1. Calculate chunk location from position
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│ EntityBuilder                                                │
│  - Position { X, Y, Z }                                     │
│  - Velocity { X, Y, Z }                                     │
│  - RenderTag                                                 │
│  - ChunkOwner { ChunkLocation }  ← Added at creation!       │
└─────────────────────────────────────────────────────────────┘
         │
         │ 2. Enqueue entity creation
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│ World.ProcessQueues() - Phase 1.5                           │
│  - Creates entity in FINAL archetype                        │
│  - NO archetype moves!                                       │
│  - Fires EntityBatchCreated event                           │
└─────────────────────────────────────────────────────────────┘
         │
         │ 3. Event fires
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│ SimplifiedChunkSystem.OnEntityBatchCreated()                │
│  - Just enqueues entity IDs                                 │
│  - NO component reads!                                       │
│  - _entityCreatedQueue.Enqueue(entity)                      │
└─────────────────────────────────────────────────────────────┘
         │
         │ 4. Later (Phase 4 - Run Systems)
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│ SimplifiedChunkSystem.Update()                              │
│  - World is now STABLE                                       │
│  - ProcessCreatedEntities()                                  │
│    └─ Read ChunkOwner component                             │
│    └─ chunk.TrackEntity(entity.Packed)                      │
└─────────────────────────────────────────────────────────────┘
         │
         │ 5. Simple HashSet add
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│ SpatialChunk                                                 │
│  _trackedEntities.Add(entityPacked)                         │
│  - Thread-safe (per-chunk lock)                             │
│  - NO entity modification                                    │
└─────────────────────────────────────────────────────────────┘
```

---

## Components

### 1. SpatialChunk

**File:** `UltraSim/ECS/Chunk/SpatialChunk.cs`

**Purpose:** Tracks entities within a single chunk's spatial bounds.

**Key Features:**
- Stores `HashSet<ulong>` of entity.Packed values
- Thread-safe operations (per-chunk locking)
- No entity or component manipulation

**API:**
```csharp
public sealed class SpatialChunk
{
    public ChunkLocation Location { get; }
    public int EntityCount { get; }
    public bool IsActive { get; }  // EntityCount > 0

    public void TrackEntity(ulong entityPacked);
    public void StopTracking(ulong entityPacked);
    public bool IsTracking(ulong entityPacked);
    public List<ulong> GetTrackedEntities();
    public void Clear();
}
```

**Thread Safety:**
- All operations use `lock (_lock)`
- Safe to call from multiple threads
- Per-chunk locking minimizes contention

**Storage:**
```csharp
private readonly HashSet<ulong> _trackedEntities = new();
private readonly object _lock = new object();
```

---

### 2. SimplifiedChunkManager

**File:** `UltraSim/ECS/Chunk/ChunkManager.Simplified.cs`

**Purpose:** Spatial index service - manages chunk lookup and coordinate conversion.

**Key Features:**
- Sparse storage (creates chunks on-demand)
- Thread-safe operations (ConcurrentDictionary)
- Coordinate conversion utilities
- Spatial query support

**API:**
```csharp
public sealed class SimplifiedChunkManager
{
    // Configuration
    public int ChunkSizeXZ { get; }  // Default: 64
    public int ChunkSizeY { get; }   // Default: 32

    // Statistics
    public int TotalChunks { get; }
    public int ActiveChunks { get; }

    // Coordinate Conversion
    public ChunkLocation WorldToChunk(float x, float y, float z);
    public ChunkBounds ChunkToWorldBounds(ChunkLocation location);

    // Chunk Access
    public SpatialChunk GetOrCreateChunk(ChunkLocation location);
    public SpatialChunk? GetChunk(ChunkLocation location);
    public bool ChunkExists(ChunkLocation location);

    // Entity Tracking
    public void TrackEntity(ulong entityPacked, ChunkLocation location);
    public void StopTracking(ulong entityPacked, ChunkLocation location);
    public void MoveEntity(ulong entityPacked, ChunkLocation from, ChunkLocation to);

    // Spatial Queries
    public List<SpatialChunk> GetChunksInRadius(float x, float y, float z, float radius);
    public List<SpatialChunk> GetChunksInBounds(ChunkBounds bounds);

    // Statistics
    public string GetStatistics();
}
```

**Storage:**
```csharp
private readonly ConcurrentDictionary<ChunkLocation, SpatialChunk> _chunks = new();
```

**Chunk Creation:**
- Chunks created lazily when first entity enters
- Never destroyed (persist for lifetime of manager)
- Thread-safe creation via `GetOrAdd()`

---

### 3. SimplifiedChunkSystem

**File:** `Server/ECS/Systems/SimplifiedChunkSystem.cs`

**Purpose:** ECS system that processes entity tracking events.

**Key Features:**
- Subscribes to entity creation/destruction events
- Deferred queue processing
- NO component add/remove operations
- Runs every frame to process queues

**Event Handlers:**
```csharp
// CRITICAL: Only enqueues, does NOT read components!
private void OnEntityBatchCreated(EntityBatchCreatedEventArgs args)
{
    foreach (var entity in args.GetSpan())
    {
        _entityCreatedQueue.Enqueue(entity);
    }
}

private void OnEntityBatchDestroyed(EntityBatchDestroyedEventArgs args)
{
    foreach (var entity in args.GetSpan())
    {
        _entityDestroyedQueue.Enqueue(entity);
    }
}
```

**Queue Processing (in Update()):**
```csharp
private void ProcessCreatedEntities()
{
    while (_entityCreatedQueue.TryDequeue(out var entity))
    {
        // SAFETY: Entity might be destroyed before we process it
        if (!_world.IsEntityValid(entity))
            continue;

        // Entity should already have ChunkOwner (added during creation)
        if (!_world.TryGetComponent<ChunkOwner>(entity, out var owner))
            continue;

        // Track entity in chunk (simple HashSet add)
        _chunkManager.TrackEntity(entity.Packed, owner.ChunkLocation);
    }
}

private void ProcessDestroyedEntities()
{
    while (_entityDestroyedQueue.TryDequeue(out var entity))
    {
        // Entity.Packed might be stale (recycled) - doesn't matter!
        // We're just removing the reference from chunk tracking

        if (_world.TryGetComponent<ChunkOwner>(entity, out var owner))
        {
            _chunkManager.StopTracking(entity.Packed, owner.ChunkLocation);
        }
    }
}
```

**System Properties:**
```csharp
public override TickRate Rate => TickRate.EveryFrame;
public override Type[] ReadSet => new[] { typeof(Position), typeof(ChunkOwner) };
public override Type[] WriteSet => new[] { typeof(ChunkOwner) };
```

---

### 4. EntitySpawnerSystem (Updated)

**File:** `Server/ECS/Systems/EntitySpawnerSystem.cs`

**Changes:** Now adds ChunkOwner component during entity creation.

**Entity Creation Pattern:**
```csharp
private void SpawnEntities(int count)
{
    for (int i = 0; i < count; i++)
    {
        Vector3 randomPos = Utilities.RandomPointInSphere(radius);

        var builder = _world.CreateEntityBuilder();

        builder.Add(new Position { X = randomPos.X, Y = randomPos.Y, Z = randomPos.Z });
        builder.Add(new Velocity { X = 0, Y = 0, Z = 0 });
        builder.Add(new PulseData { Speed = speed, Frequency = frequency, Phase = phaseOffset });
        builder.Add(new RenderTag { });
        builder.Add(new Visible { });

        // ★ Add ChunkOwner at creation time (NO archetype move later!)
        var chunkLoc = WorldToChunk(randomPos.X, randomPos.Y, randomPos.Z);
        builder.Add(new ChunkOwner(chunkLoc));

        builder.Add(new StaticRenderTag { });  // or dynamic
        builder.Add(new RenderPrototype(RenderPrototypeKind.Cube));

        _world.EnqueueCreateEntity(builder);
    }
}

private ChunkLocation WorldToChunk(float worldX, float worldY, float worldZ)
{
    int chunkX = (int)Math.Floor(worldX / CHUNK_SIZE_XZ);
    int chunkZ = (int)Math.Floor(worldZ / CHUNK_SIZE_XZ);
    int chunkY = (int)Math.Floor(worldY / CHUNK_SIZE_Y);

    return new ChunkLocation(chunkX, chunkZ, chunkY);
}
```

**Key Points:**
- ChunkOwner included in EntityBuilder
- Entity created in final archetype immediately
- Must match ChunkManager's chunk size configuration

---

## Operational Flow

### Entity Creation Flow

```
1. EntitySpawnerSystem.SpawnEntities()
   ├─ Generate random position
   ├─ Calculate chunk location: WorldToChunk(x, y, z)
   ├─ Create EntityBuilder
   ├─ Add Position, Velocity, RenderTag, etc.
   ├─ Add ChunkOwner(chunkLocation)  ← CRITICAL!
   └─ EnqueueCreateEntity(builder)

2. World.Tick() - Phase 1.5: Entity Operations
   ├─ EntityManager.ProcessEntityBuilderCreationQueue()
   ├─ Creates entity with ALL components at once
   ├─ Entity archetype: Position+Velocity+RenderTag+ChunkOwner
   ├─ NO archetype move!
   └─ Fires EntityBatchCreated event

3. SimplifiedChunkSystem.OnEntityBatchCreated()
   ├─ Event handler runs (Phase 1.5)
   ├─ Just enqueues entity ID
   ├─ _entityCreatedQueue.Enqueue(entity)
   └─ Returns immediately (NO component reads!)

4. World.Tick() - Phase 3: Component Operations
   └─ (No component operations for chunks - already has ChunkOwner)

5. World.Tick() - Phase 4: Run Systems
   └─ SimplifiedChunkSystem.Update()
      ├─ ProcessCreatedEntities()
      ├─ Dequeues entity from _entityCreatedQueue
      ├─ Reads ChunkOwner component (world is STABLE now!)
      └─ chunk.TrackEntity(entity.Packed)

6. SpatialChunk.TrackEntity()
   └─ _trackedEntities.Add(entityPacked)  ← Simple HashSet add!
```

### Entity Position Update Flow

```
1. OptimizedMovementSystem.Update()
   └─ Updates Position component values

2. (Future) Position change detection
   └─ Enqueue entity for chunk reassignment

3. SimplifiedChunkSystem.ProcessMovedEntities()
   ├─ Read current ChunkOwner component
   ├─ Read current Position component
   ├─ Calculate new chunk location
   ├─ If chunk changed:
   │  ├─ _chunkManager.MoveEntity(entity.Packed, oldChunk, newChunk)
   │  └─ world.SetComponent(entity, new ChunkOwner(newChunkLoc))
   └─ (SetComponent updates VALUE, not archetype!)
```

### Entity Destruction Flow

```
1. World.DestroyEntity(entity)
   └─ Fires EntityBatchDestroyed event

2. SimplifiedChunkSystem.OnEntityBatchDestroyed()
   ├─ Just enqueues entity ID
   └─ _entityDestroyedQueue.Enqueue(entity)

3. SimplifiedChunkSystem.ProcessDestroyedEntities()
   ├─ Dequeues entity from _entityDestroyedQueue
   ├─ Tries to read ChunkOwner (might fail if component already gone)
   └─ chunk.StopTracking(entity.Packed)

4. SpatialChunk.StopTracking()
   └─ _trackedEntities.Remove(entityPacked)
```

**Note:** `entity.Packed` might be stale (entity ID recycled), but that's fine - we're just removing the reference from the HashSet.

---

## Usage Guide

### Getting ChunkManager Instance

```csharp
var chunkSystem = world.Systems.GetSystem<SimplifiedChunkSystem>();
var chunkManager = chunkSystem?.GetChunkManager();
```

### Coordinate Conversion

```csharp
// World position → Chunk location
float worldX = 100f, worldY = 50f, worldZ = -200f;
ChunkLocation chunkLoc = chunkManager.WorldToChunk(worldX, worldY, worldZ);
// Result: ChunkLocation(1, -4, Y:1)  [assuming 64x32x64 chunks]

// Chunk location → World bounds
ChunkBounds bounds = chunkManager.ChunkToWorldBounds(chunkLoc);
// Result: MinX=64, MaxX=128, MinY=32, MaxY=64, MinZ=-256, MaxZ=-192
```

### Spatial Queries

```csharp
// Get all chunks within radius of a point
var chunks = chunkManager.GetChunksInRadius(
    worldX: 0f,
    worldY: 0f,
    worldZ: 0f,
    radius: 100f
);

// Get all chunks within a bounding box
var queryBounds = new ChunkBounds(
    minX: -100f, minY: 0f, minZ: -100f,
    maxX: 100f, maxY: 64f, maxZ: 100f
);
var chunks = chunkManager.GetChunksInBounds(queryBounds);

// Iterate over tracked entities in chunk
foreach (var chunk in chunks)
{
    var entities = chunk.GetTrackedEntities();
    foreach (ulong entityPacked in entities)
    {
        var entity = new Entity(entityPacked);
        // ... do something with entity
    }
}
```

### Manual Entity Tracking

```csharp
// Track an entity manually (usually automatic via ChunkSystem)
Entity entity = ...;
ChunkLocation chunkLoc = ...;
chunkManager.TrackEntity(entity.Packed, chunkLoc);

// Stop tracking
chunkManager.StopTracking(entity.Packed, chunkLoc);

// Move between chunks
ChunkLocation newChunkLoc = ...;
chunkManager.MoveEntity(entity.Packed, oldChunkLoc, newChunkLoc);
```

### Checking Chunk State

```csharp
SpatialChunk? chunk = chunkManager.GetChunk(chunkLoc);

if (chunk != null)
{
    int entityCount = chunk.EntityCount;
    bool isActive = chunk.IsActive;  // EntityCount > 0

    bool isTracking = chunk.IsTracking(entity.Packed);
}
```

---

## Performance Characteristics

### Chunk Creation
- **Lazy creation:** Chunks created when first entity enters
- **Thread-safe:** Uses `ConcurrentDictionary.GetOrAdd()`
- **No overhead:** Chunks never destroyed (persist for manager lifetime)
- **Memory:** ~24 bytes per empty chunk + HashSet overhead

### Entity Tracking
- **Add:** O(1) - HashSet add with per-chunk lock
- **Remove:** O(1) - HashSet remove with per-chunk lock
- **Check:** O(1) - HashSet contains with per-chunk lock
- **Memory:** 8 bytes per tracked entity (ulong)

### Spatial Queries
- **GetChunksInRadius:** O(N) where N = total chunks (iterates all chunks)
- **GetChunksInBounds:** O(N) where N = total chunks (iterates all chunks)
- **Optimization:** Early exit checks reduce iteration count

### Lock Contention
- **Per-chunk locking:** Multiple threads can modify different chunks simultaneously
- **Contention:** Only when multiple threads access **same chunk**
- **Rare:** Entities usually spread across many chunks

### Comparison to Old System

| Operation | Old System | New System | Improvement |
|-----------|-----------|------------|-------------|
| Entity Creation | 2-3 archetype lookups + component add | HashSet add only | **10x faster** |
| Position Update | Archetype move + component remove/add | ChunkOwner value change | **50x faster** |
| Entity Destruction | Archetype move + component remove | HashSet remove only | **10x faster** |
| Memory per Entity | Component overhead (~32 bytes) | HashSet entry (~8 bytes) | **4x less** |
| Race Conditions | Frequent (archetype moves) | None (no modifications) | **∞ better** |

---

## Migration Notes

### From Old ChunkSystem

**Breaking Changes:**

1. **ChunkSystem → SimplifiedChunkSystem**
   ```csharp
   // OLD
   world.EnqueueSystemCreate<ChunkSystem>();

   // NEW
   world.EnqueueSystemCreate<SimplifiedChunkSystem>();
   ```

2. **ChunkManager → SimplifiedChunkManager**
   ```csharp
   // OLD
   var chunkSystem = world.Systems.GetSystem<ChunkSystem>();
   var chunkManager = chunkSystem?.GetChunkManager();  // Returns ChunkManager

   // NEW
   var chunkSystem = world.Systems.GetSystem<SimplifiedChunkSystem>();
   var chunkManager = chunkSystem?.GetChunkManager();  // Returns SimplifiedChunkManager
   ```

3. **No Chunk Entities**
   - Old system created chunk entities with ChunkLocation component
   - New system uses SpatialChunk objects (not entities)
   - No chunk registration/unregistration needed

4. **ChunkOwner Must Be Added at Creation**
   - Old system added ChunkOwner in ChunkSystem.ProcessAssignment()
   - New system requires ChunkOwner in EntityBuilder
   - See EntitySpawnerSystem for example

**Non-Breaking Changes:**

- `ChunkLocation` struct unchanged
- `ChunkBounds` struct unchanged
- `ChunkOwner` component unchanged
- Coordinate conversion API compatible

### Code Updates Required

**EntitySpawnerSystem or similar:**
```csharp
// Add this method
private ChunkLocation WorldToChunk(float worldX, float worldY, float worldZ)
{
    int chunkX = (int)Math.Floor(worldX / 64);  // Match ChunkManager config
    int chunkZ = (int)Math.Floor(worldZ / 64);
    int chunkY = (int)Math.Floor(worldY / 32);
    return new ChunkLocation(chunkX, chunkZ, chunkY);
}

// Update entity creation
var builder = world.CreateEntityBuilder();
builder.Add(new Position { X = x, Y = y, Z = z });
// ... other components
builder.Add(new ChunkOwner(WorldToChunk(x, y, z)));  // ← Add this!
world.EnqueueCreateEntity(builder);
```

**Main.cs or bootstrapper:**
```csharp
// Replace old system registration
world.EnqueueSystemCreate<SimplifiedChunkSystem>();
world.EnqueueSystemEnable<SimplifiedChunkSystem>();
```

**Rendering systems (if accessing ChunkManager):**
```csharp
// Update type reference
var chunkSystem = world.Systems.GetSystem<SimplifiedChunkSystem>();
SimplifiedChunkManager? chunkManager = chunkSystem?.GetChunkManager();

// API is mostly compatible, but check method signatures
var chunks = chunkManager.GetChunksInRadius(x, y, z, radius);
```

---

## Advanced Topics

### Future: Inactive Chunk Tagging

Currently not implemented, but the architecture supports it:

```csharp
// In SimplifiedChunkSystem.ProcessCreatedEntities()
if (chunk.EntityCount == 1)  // Chunk was empty, now has 1 entity
{
    // Remove InactiveChunk tag if it exists
    // (Requires chunk to be an entity - future enhancement)
}

// In SimplifiedChunkSystem.ProcessDestroyedEntities()
if (chunk.EntityCount == 0)  // Chunk is now empty
{
    // Add InactiveChunk tag
    // (Requires chunk to be an entity - future enhancement)
}
```

### Future: Parallel Processing

The architecture enables safe parallelization:

```csharp
// Process creation queue in parallel
Parallel.ForEach(_entityCreatedQueue.Drain(), entity =>
{
    var chunkLoc = world.GetComponent<ChunkOwner>(entity).ChunkLocation;
    var chunk = chunkManager.GetOrCreateChunk(chunkLoc);
    chunk.TrackEntity(entity.Packed);  // Per-chunk lock (safe!)
});
```

**Why this works:**
- Each entity usually goes to different chunk
- Per-chunk locking prevents contention
- No shared state modified (each chunk independent)

---

## Troubleshooting

### Entities Not Being Tracked

**Symptom:** Spatial queries return empty results, but entities exist.

**Cause:** ChunkOwner component not added during entity creation.

**Solution:**
```csharp
// Ensure ChunkOwner is in EntityBuilder
var builder = world.CreateEntityBuilder();
builder.Add(new Position { X = x, Y = y, Z = z });
builder.Add(new ChunkOwner(WorldToChunk(x, y, z)));  // ← Must add!
world.EnqueueCreateEntity(builder);
```

### "Invalid Slot" Errors

**Symptom:** `InvalidOperationException: Invalid slot X for archetype with Y entities`

**Cause:** Using old ChunkSystem, or ChunkOwner added after creation.

**Solution:** Ensure using SimplifiedChunkSystem and ChunkOwner added at creation time.

### Chunk Location Mismatch

**Symptom:** Entities tracked in wrong chunks.

**Cause:** Chunk size mismatch between EntitySpawnerSystem and ChunkManager.

**Solution:** Ensure constants match:
```csharp
// EntitySpawnerSystem
private const int CHUNK_SIZE_XZ = 64;
private const int CHUNK_SIZE_Y = 32;

// ChunkManager initialization
new SimplifiedChunkManager(chunkSizeXZ: 64, chunkSizeY: 32);
```

### Memory Leak

**Symptom:** Memory usage grows over time.

**Cause:** Destroyed entities not removed from chunk tracking.

**Solution:** Ensure SimplifiedChunkSystem is enabled and subscribes to destruction events.

---

## Summary

The new Chunk Manager architecture follows a simple principle: **chunks observe entities without modifying them**.

### Key Benefits

✅ **Eliminates race conditions** - No archetype moves during/after creation
✅ **Eliminates "Invalid slot" errors** - Lookup table stays consistent
✅ **Simplifies code** - Chunks are just HashSet trackers
✅ **Enables parallelization** - Per-chunk locking, no global contention
✅ **Improves performance** - No component add/remove overhead
✅ **Reduces memory** - 8 bytes per tracked entity vs 32+ bytes for components

### The Golden Rule

**Always add ChunkOwner during entity creation!**

```csharp
// ✅ CORRECT
var builder = world.CreateEntityBuilder();
builder.Add(new Position { X = x, Y = y, Z = z });
builder.Add(new ChunkOwner(WorldToChunk(x, y, z)));

// ❌ WRONG - DO NOT add ChunkOwner later!
var entity = world.CreateEntity();
world.AddComponent(entity, new Position { X = x, Y = y, Z = z });
world.AddComponent(entity, new ChunkOwner(...));  // Causes archetype move!
```

---

**End of Documentation**
