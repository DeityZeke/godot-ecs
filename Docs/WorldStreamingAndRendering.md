# UltraSim World Streaming & Rendering Plan

This document consolidates the current design decisions, open ideas, and long‑term goals for the UltraSim chunk system, hybrid renderer, and world-streaming pipeline. It blends the strengths of Ultima Online’s static data, RollerCoaster Tycoon’s modular terrain, Minecraft’s chunk streaming, and Valheim’s traversal/combat scale while targeting **>100 k visual entities** in view.

---

## 1. Vision & Target Capabilities

- **Entity budget:** Double (or exceed) Valheim’s ~50 k entity counts while keeping >60 FPS on modern GPUs.
- **Hybrid rendering:** Dedicate MeshInstances to the immediate interaction bubble, downgrade everything else to MultiMesh/impostor rendering.
- **MMO-scale streaming:** Allow both sparse, on-demand streaming *and* full-world residency (server-side or opt-in on clients).
- **Authorable yet systemic terrain:** Support modular tile/terrain editing (UO + RCT style) with per-corner heights, slopes, and object layers.
- **Physics optionality:** Default to lightweight collision (Areas, capsules, logic bodies) and reserve full rigid bodies for rare hero entities.

---

## 2. Spatial Hierarchy

| Layer                | Purpose                                                                   | Notes                                                                   |
|----------------------|---------------------------------------------------------------------------|-------------------------------------------------------------------------|
| **World grid**       | 64 m × 64 m × 32 m chunks (XZ × Y)                                        | Chunks = ECS entities; empty space is never instantiated                |
| **Tile resolution**  | 0.5 m sub-tiles (per chunk: 128 × 128 = 16 384 tiles)                     | Gives human-scale fidelity (tight paths, goat trails, stairs, etc.)     |
| **Vertical bands**   | Sparse `VerticalChunkBand` per X/Z column                                 | Most columns only allocate bands where content exists (ground, cave, sky) |
| **Tile struct**      | Corner heights (H00…H11), slope/material flags, traversal metadata        | Covers 0°, 22.5°, 45°, 67.5°, 90° slope classes with movement modifiers  |
| **Object ownership** | Entities belong to the chunk containing their origin/pivot                | Overlaps are fine; split mega‑assets into stacked pieces per chunk      |

### Why 0.5 m Tiles?

- 0.5 m ≈ 20 in → natural fit for humanoid width (17–18 in), narrow trails, ladders.
- Enables smooth slope ramps without voxel explosion.
- Chunk memory: 16 384 tiles × 8 B ≈ 128 KB before compression (trivial even at 200 k chunks).

---

## 3. ECS Integration

**Core components**

- `ChunkLocation` (int3 indices), `ChunkBounds`, `ChunkState` (Loaded / Streaming / Frozen), `ChunkHash`.
- `TileMapComponent` (compressed tile array), `ChunkStaticObjects`, `ChunkRenderComponent`.
- `ChunkOwner` on entities (assigned once; updated in place to avoid archetype churn).

**Primary systems**

| System                        | Responsibility                                                                                   |
|------------------------------|--------------------------------------------------------------------------------------------------|
| `ChunkSystem`                | Registers chunks, assigns `ChunkOwner`, manages lifecycle queues, throttles updates               |
| `ChunkManager`               | Spatial index (X/Z columns, vertical bands, chunk stats)                                         |
| `ChunkStreamingSystem` (planned) | Loads/unloads chunk payloads, coordinates network/disk IO, maintains manifest state           |
| `HybridRenderSystem`         | Samples camera chunk, assigns `RenderZone` (Core/Near/Far/Culled)                                |
| `MeshInstanceBubbleManager`  | Maintains MeshInstances for Core bubble (full interaction, collisions, animation)                |
| `MultiMeshZoneManager`       | Batches Near-zone entities per chunk (shader/baked animation, no per-entity node cost)           |
| `Impostor/FarZoneManager` (planned) | Handles far LOD (billboards, cards, point clouds) to represent millions of distant actors |

---

## 4. Chunk Lifecycle & Streaming

1. **Manifest sync:** Server sends `WorldManifest` (chunk coordinates → hashes, format version, chunk size). Client compares to local cache.
2. **Verification:** Cached chunks with matching hashes load instantly; mismatches trigger re-download.
3. **Chunk payload:** Serialised blob (e.g., LZ4/Zstd) with tile data, static objects, metadata. Stored as `_chunks/x_y_z.dat` alongside a local manifest.
4. **Activation:** When a chunk enters the activation radius, `ChunkStreamingSystem` ensures payload is present, spawns/updates ECS components, and registers with `ChunkManager`.
5. **Deactivation:** Chunks leaving the radius mark `ChunkState = Frozen` and return pooled resources, but can remain in memory if desired.
6. **Server residency:** Servers may keep all chunk entities resident (195 k chunks ≈ a few hundred MB) while using `ChunkTickRate` (Frozen/Rare/Normal/Frequent) to throttle simulation.

This mirrors RunUO’s static/dynamic split, Minecraft’s chunk streaming, and Git-like hash verification.

---

## 5. Hybrid Rendering Zones

| Zone        | Coverage (example)                     | Rendering mode                                    | Features                                                                 |
|-------------|----------------------------------------|---------------------------------------------------|--------------------------------------------------------------------------|
| Core Bubble | 3×3×3 chunks around camera (hysteresis) | Real `MeshInstance3D` per entity/chunk             | Full animations, collisions, dynamic lights, player interaction          |
| Near Zone   | Core radius + 8–16 chunks               | Per-chunk `MultiMeshInstance3D`                    | Shader/baked animation via custom data, no per-entity node cost          |
| Mid Zone    | Optional ring beyond near zone          | Simplified MultiMesh, baked lighting, no shadows   | Reduced vertex density (25–50 %)                                        |
| Far Zone    | >32–64 chunks                           | Impostors/billboards/height cards/point sprites    | Single draw per chunk or per cluster; atmospheric fog blends transitions |

**Conversion rules**

- Chunks entering the core bubble schedule MeshInstance builds over multiple frames.
- Chunks leaving downgrade to MultiMesh first, then impostor if distance allows.
- HybridRenderSystem handles zone assignment; managers act on `RenderZone` changes.

**Animation**

- Core: conventional Skeleton3D animation.
- Near: shader-driven (custom data) or baked animation textures (GPU sampling).
- Far: flipbooks or time-sliced impostors.

---

## 6. Culling & Render Distance

### Frustum/Occlusion

- Chunk-level frustum culling: test axis-aligned `ChunkBounds` before touching contents.
- Optional occlusion: hierarchical depth maps per chunk ring or GPU occlusion queries.
- Entities inherit chunk visibility; special cases (tall towers) can optionally register multiple bounds.

### Render distance budget

| Distance (chunks) | Radius (m, chunk=64 m) | Approx chunks before culling | Typical visible after culling | Notes                                                                 |
|-------------------|------------------------|------------------------------|-------------------------------|-----------------------------------------------------------------------|
| 8                 | 512 m                  | 17² ≈ 289                    | 50–100                        | Low-end default                                                       |
| 16                | 1 024 m                | 33² ≈ 1 089                  | 150–300                       | Mid-range default                                                     |
| 32                | 2 048 m                | 65² ≈ 4 225                  | 400–600                       | High-end                                                              |
| 64                | 4 096 m                | 129² ≈ 16 641                | 700–900                       | Scenic / photo mode                                                   |
| 128               | 8 192 m                | 257² ≈ 66 049                | 1 000–1 500                   | Extreme; MultiMesh + impostor only                                    |

Per chunk draw cost stays manageable because MultiMesh collapses hundreds of entities into a single draw call, and mesh density drops with distance (e.g., 100 % → 50 % → 10 %).

---

## 7. Physics, Collision & Interaction

- Default to **logic bodies**: Areas or simple capsules provide collision callbacks without heavy rigid-body costs.
- Use ECS `AABB` / `CollisionComponent` data for broad-phase queries; only the player/hero entities require Godot CharacterBody/RigidBody nodes.
- Falling/grounding can be faked via raycasts against chunk height data; slopes drive traversal speed and slide behaviors.
- Combat traces operate on chunk tile metadata + logic volumes; MultiMesh visuals mirror transforms but never influence physics.

---

## 8. GPU Offload & Specialized Systems

- **Chunk debug overlay:** Visualizes chunk bounds/planes to diagnose reassignment thrash.
- **Grass/foliage:** Plan to drive via procedural MultiMesh or compute shader, seeded per chunk to keep it “insanely cheap.”
- **Shader animation:** Use MultiMesh `custom_data` channels for phase/time offsets, palette selection, impostor frame index.
- **Compute skinning (future):** For high-volume animated crowds, stage bone matrices in textures and sample per vertex.

---

## 9. Data Integrity & Caching

1. **Manifest:** `chunks.json` with chunk hashes, compression type, format version.
2. **Local cache:** `_chunks/x_y_z.dat` (binary blob) + world manifest; hashed to detect tampering.
3. **Networking:** Client requests only missing/outdated chunks; server responds with compressed chunk payload + hash.
4. **Verification:** On receipt, client hashes payload, stores to disk, and promotes chunk to `Loaded`.
5. **Offline mode:** If all chunks validated locally, client can run in offline/test mode using cached data.

This mimics UO statics, but with git-style verification and per-chunk granularity.

---

## 10. Roadmap & Next Steps

1. **Finalize chunk lifecycle:** Add streaming/activation system, chunk tick throttling, and pooled resources.
2. **Document tile data format:** Corner heights, slope flags, materials, collision metadata.
3. **Implement render hysteresis:** Buffered chunk swaps so MeshInstance ↔ MultiMesh conversions never pop.
4. **Add impostor/far zone manager:** Billboards or cards fed from chunk summaries; integrate atmospheric fog.
5. **Manifest + cache:** Define serialization, hash algorithm, versioning, and toolchain for chunk patching/deltas.
6. **Grass/foliage prototype:** Use chunk seeds + compute/texture-driven instancing to populate surfaces.
7. **Expose tuning knobs:** Chunk size settings, render distance, slope traversal multipliers, overlay toggles.

Keeping this document updated ensures the `ChunkSystem`, `ChunkManager`, hybrid renderer, streaming pipeline, and design targets stay aligned as features land.
