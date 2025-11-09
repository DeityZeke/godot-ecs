# UltraSim Hybrid Rendering & Chunk System

## Overview

The hybrid renderer is a world-streaming architecture that enables:

- Massive chunk-based worlds (tens of km)
- ECS-driven entities that stream in/out
- Hybrid rendering with MeshInstances near the camera and MultiMesh batches at distance
- Memory-efficient chunk ownership and optional client caching

It combines ideas from Minecraft (chunk streaming), RunUO (static world serialization), Valheim (terrain physics), Godot 4 (MultiMesh), and modern ECS SoA design.

## Core Components

| Component / System                 | Role                                                                 |
|-----------------------------------|----------------------------------------------------------------------|
| `ChunkManager`                    | Spatial index of all chunks; exposes world <-> chunk conversion      |
| `ChunkSystem`                     | Assigns entities to chunks, creates chunk entities on-demand         |
| `HybridRenderSystem`              | Computes render zones (core / near / far) per chunk/entity           |
| `MeshInstanceBubbleManager`       | Manages individual MeshInstance nodes inside the core bubble         |
| `MultiMeshZoneManager`            | Maintains MultiMesh batches for near-zone chunks                     |
| `RenderZone` / `RenderChunk`      | ECS components that annotate entities / chunks with rendering state  |

## Chunk Coordinate Mapping

```
chunkX = floor(worldX / ChunkSizeXZ)
chunkZ = floor(worldZ / ChunkSizeXZ)
chunkY = floor(worldY / ChunkSizeY)
```

Each chunk owns all entities whose pivot falls inside its bounds. Empty chunks are never created.

## Hybrid Rendering Zones

| Zone        | Distance                             | Rendering Method             | Notes                                 |
|-------------|--------------------------------------|------------------------------|---------------------------------------|
| Core Bubble | e.g. 3×3×3 chunks around camera      | Individual `MeshInstance3D`  | Full interaction and collisions       |
| Near Zone   | Core bubble radius + N chunks        | Per-chunk `MultiMesh`        | Visual-only, aggressive batching      |
| Far Zone    | Optional (beyond near zone)          | Impostor/Billboard           | Lowest cost for extreme distances     |

- The core bubble shifts with the camera (hysteresis prevents thrashing).
- Conversions occur asynchronously and are throttled to avoid frame spikes.
- Both zone assignment and frustum culling are done per chunk.

## Runtime Flow

1. `ChunkSystem` registers chunks, assigns `ChunkOwner` to every entity.
2. `HybridRenderSystem` samples the camera chunk and assigns `RenderZone` to every entity (Core / Near / Far / None).
3. `MeshInstanceBubbleManager` reads `RenderZone` and spawns MeshInstances for Core entities (adding/removing as needed).
4. `MultiMeshZoneManager` batches all Near-zone entities per chunk into MultiMesh instances.

The renderer therefore adjusts seamlessly as the player moves: chunks entering the bubble switch to detailed MeshInstances, while chunks leaving it downgrade to MultiMesh.

## Chunk Streaming & Caching (Future)

The design anticipates:

- `WorldManifest` with chunk hashes.
- Client chunk cache at `_chunks/x_y_z.dat`.
- Hash comparison to reuse cached chunks.
- `ChunkSerializer` with LZ4 compression.
- Deterministic seeds for procedural worlds.

These hooks can be layered on top of the existing systems without refactoring the ECS core.

## Scalability Guidelines

- `ChunkManager` only allocates columns/bands that exist, keeping memory proportional to populated space.
- `MultiMeshZoneManager` reduces distant draw calls to O(chunks), instead of O(entities).
- `MeshInstanceBubbleManager` limits per-entity MeshInstances to a small bubble.
- Systems operate on SoA spans (`CollectionsMarshal.AsSpan`) and use manual thread pools for hot loops.

World sizes of millions of entities / thousands of chunks are supported when chunking + hybrid rendering are enabled.

## Key Principles

- **Sparse ownership:** empty chunks consume zero memory.
- **Deterministic chunk IDs:** prevents duplication and simplifies save/load.
- **Hybrid detail:** spend GPU budget where it matters; batch everything else.
- **Parallel-ready:** chunk assignment, movement systems, and render prep all run across cores.
- **Streaming-aware:** renderer only needs chunk metadata, so networking/storage can evolve independently.

## Data Flow Summary

```
Camera -> HybridRenderSystem -> RenderZone per entity
      -> MeshInstanceBubbleManager -> MeshInstances for core chunks
      -> MultiMeshZoneManager      -> MultiMesh for near chunks
ChunkSystem -> ChunkManager        -> chunk lookups + owner assignment
```

This document mirrors the implementation in `ChunkSystem`, `ChunkManager`, `HybridRenderSystem`, `MeshInstanceBubbleManager`, and `MultiMeshZoneManager`. Update it whenever zone logic or chunk ownership rules change.

For a broader treatment of chunk lifecycles, world streaming, tile data, and render-distance planning, see `Docs/WorldStreamingAndRendering.md`.
