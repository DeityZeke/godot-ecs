# Terrain Integration with Hybrid Renderer

## Architecture Overview

The terrain system integrates seamlessly with the existing Hybrid Render System by treating **terrain chunks as entities**.

## Component-Based Design

Each terrain chunk is an entity with these components:

```csharp
// Server-side (authoritative data)
TerrainChunkComponent - Holds reference to TerrainChunkData (tile grid)
ChunkBounds           - Spatial AABB for culling
StaticRenderTag       - Marks as static (MultiMesh rendering)
ChunkLocation         - Grid coordinates

// Optional
Position              - World-space position (if needed for queries)
```

## How It Works

### 1. Terrain Chunk Creation (Server)

`TerrainEntitySystem` creates entities for terrain chunks:

```csharp
buffer.CreateEntity(e => e
    .Add(new TerrainChunkComponent
    {
        ChunkData = terrainData,  // 32×32 tile grid
        Location = chunkLoc,
        IsDirty = true
    })
    .Add(CalculateChunkBounds(chunkLoc))
    .Add(new StaticRenderTag())  // Static → MultiMesh
);
```

### 2. Mesh Generation (Client)

The **Hybrid Render System** detects terrain entities and generates meshes:

```
HybridRenderSystem (or extended version):
  1. Query entities with TerrainChunkComponent
  2. For each dirty terrain chunk:
     - Generate mesh using GreedyMesher.GenerateMesh(chunkData)
     - Cache mesh in TerrainMeshCache
     - Add to MultiMesh batch (static) or MeshInstance (dynamic)
  3. Mark IsDirty = false
```

### 3. Rendering Pipeline

**Static terrain** (majority of terrain):
- Has `StaticRenderTag`
- Goes through `MultiMeshZoneManager`
- Batched into MultiMesh instances
- Extremely efficient (1000s of chunks per draw call)

**Dynamic terrain** (rare - actively deforming):
- Remove `StaticRenderTag`
- Goes through `MeshInstanceBubbleManager`
- Individual MeshInstance3D nodes
- Supports real-time modification

## Integration Points

### Existing Systems (No Changes Needed)

- `HybridRenderSystem` - Central coordinator, already handles chunk windows
- `MultiMeshZoneManager` - Batch rendering, works with any static entity
- `MeshInstanceBubbleManager` - Individual rendering, works with any entity
- `ChunkVisualPool` - Debug overlays, already uses ChunkBounds

### New Terrain Systems

- `TerrainEntitySystem` (Server) - Creates terrain chunk entities, handles save/load
- `TerrainModificationSystem` (Server) - Modifies tile data, marks IsDirty=true

### Mesh Generation Utilities

- `GreedyMesher` - Converts TerrainChunkData → optimized mesh
- `TerrainMeshCache` - LRU cache for generated meshes
- `TerrainMesh` - Mesh data container

## Modification Workflow

When terrain is modified:

```csharp
// 1. Modify tile data
modificationSystem.ApplyHeightBrush(position, radius, delta);

// 2. Modification system updates component
terrainChunk.ChunkData.SetTile(x, z, newTile);
terrainChunk.IsDirty = true;  // Flag for mesh rebuild

// 3. Hybrid renderer detects dirty flag next frame
// 4. Generates new mesh via GreedyMesher
// 5. Updates MultiMesh batch or MeshInstance
```

## Performance Benefits

### Memory Efficiency
- Terrain data: 6 KB per chunk (1024 tiles × 6 bytes)
- Mesh cache: ~20-50 KB per chunk (after greedy meshing)
- Total: ~26-56 KB per chunk vs ~400 KB naïve approach

### Rendering Efficiency
- Static terrain: MultiMesh batching (1 draw call per zone)
- Greedy meshing: 80-95% fewer vertices
- Frustum culling: Automatic via ChunkBounds
- LOD: Can be added per-chunk based on distance

### Scalability
- ECS chunk system already handles 1M+ entities
- Terrain chunks are just more entities
- Same spatial partitioning
- Same parallel system execution

## Example: Complete Integration

```csharp
// Server initialization
public void InitializeTerrain(World world)
{
    // Create terrain entity system
    var terrainSystem = new TerrainEntitySystem();
    terrainSystem.SetGenerator(new NoiseTerrainGenerator(seed: 42));
    world.EnqueueSystemCreate(terrainSystem);
    world.EnqueueSystemEnable<TerrainEntitySystem>();

    // Create modification system
    var modSystem = new TerrainModificationSystem();
    modSystem.SetTerrainManager(terrainSystem);
    world.EnqueueSystemCreate(modSystem);
    world.EnqueueSystemEnable<TerrainModificationSystem>();

    // Request terrain around spawn
    terrainSystem.RequestChunksAroundPosition(new Position(0, 0, 0));
}

// Client rendering (no changes needed!)
// Hybrid renderer automatically detects terrain entities
// and generates meshes when IsDirty = true
```

## Migration Path

For the existing hybrid renderer to support terrain:

### Option A: Extend Existing Systems
Add terrain mesh generation to `MultiMeshZoneManager.Update()`:

```csharp
// In MultiMeshZoneManager
if (entity has TerrainChunkComponent && terrainChunk.IsDirty)
{
    var mesh = GreedyMesher.GenerateMesh(terrainChunk.ChunkData);
    AddToMultiMesh(entity, mesh);
    terrainChunk.IsDirty = false;
}
```

### Option B: New Terrain Mesh Provider
Create a separate system that runs before rendering:

```csharp
public class TerrainMeshProviderSystem : BaseSystem
{
    public override void Update(World world, double delta)
    {
        // Query terrain chunks
        // Generate meshes for dirty chunks
        // Store mesh reference in component or cache
    }
}
```

Then hybrid renderer just reads the mesh.

## Summary

✅ **No parallel rendering pipeline** - Terrain uses existing hybrid renderer
✅ **Component-based** - Terrain chunks are entities with components
✅ **Spatial integration** - Uses ChunkBounds for culling
✅ **Performance** - Static terrain batched via MultiMesh
✅ **Modification** - IsDirty flag triggers mesh rebuild
✅ **Scalable** - Leverages ECS architecture

The terrain system is **data** (components), not a separate **system** (renderer).
