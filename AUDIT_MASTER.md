# ECS Architecture Deep Audit

**Date Started**: 2025-11-12
**Purpose**: Comprehensive trace of all ECS systems to understand architecture, find dead code, identify optimization opportunities, and plan potential rebuild.

**Audit Status**: 🔴 IN PROGRESS

---

## Audit Scope

### ✅ Completed
- [ ] Entity System (Creation/Destruction)
- [ ] Component System (Add/Remove/Storage)
- [ [ Archetype System (Creation/Transitions/Caching)
- [ ] Spatial Chunk System (Creation/Pooling/Assignment)
- [ ] System Manager (Execution/Batching/Parallelism)
- [ ] CommandBuffer (Queuing/Application/Threading)
- [ ] World Tick Pipeline (Phase ordering/Timing)
- [ ] Query System (Archetype queries/Filtering)
- [ ] Serialization (Save/Load/IOProfile)
- [ ] Event System (Firing/Subscription)

### 🔍 Findings Summary (Updated as audit progresses)

**Dead Code Discovered:**
- EntityManager.EnqueueCreate() - Never used
- EntityManager.ProcessQueues() - Processes empty queue
- World.EnqueueCreateEntity() - Never called
- TBD - More to discover

**Architecture Issues:**
- Component removals deferred to next frame, entities can be pooled/reused same frame
- No parallelism in entity creation despite CommandBuffer infrastructure
- CommandBuffer stores only entity.Index, not version (causes stale reference bugs)
- TBD - More to discover

**Performance Opportunities:**
- Parallel entity creation (potential 3-7x speedup)
- Bulk entity index allocation
- Parallel CommandBuffer application
- TBD - More to discover

---

## Detailed Audits

Each section below contains:
1. **API Surface** - All public methods/properties
2. **Actual Usage** - Where it's actually called from
3. **Data Flow** - How data moves through the system
4. **Threading Model** - What's thread-safe, what's not
5. **Performance Characteristics** - Hot paths, bottlenecks
6. **Issues Found** - Dead code, bugs, optimization opportunities

---

## [AUDIT 1] Entity System

**Status**: 🔴 IN PROGRESS
**Files**:
- `UltraSim/ECS/Entities/Entity.cs`
- `UltraSim/ECS/Entities/EntityManager.cs`

See: [AUDIT_01_ENTITY_SYSTEM.md](AUDIT_01_ENTITY_SYSTEM.md)

---

## [AUDIT 2] Component System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/Components/ComponentManager.cs`
- `UltraSim/ECS/Components/ComponentSignature.cs`

See: [AUDIT_02_COMPONENT_SYSTEM.md](AUDIT_02_COMPONENT_SYSTEM.md)

---

## [AUDIT 3] Archetype System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/Archetype.cs`
- `UltraSim/ECS/ArchetypeManager.cs`

See: [AUDIT_03_ARCHETYPE_SYSTEM.md](AUDIT_03_ARCHETYPE_SYSTEM.md)

---

## [AUDIT 4] Spatial Chunk System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/Chunk/ChunkManager.cs`
- `Server/ECS/Systems/ChunkSystem.cs`

See: [AUDIT_04_CHUNK_SYSTEM.md](AUDIT_04_CHUNK_SYSTEM.md)

---

## [AUDIT 5] System Manager

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/Systems/SystemManager.cs`
- `UltraSim/ECS/Systems/BaseSystem.cs`

See: [AUDIT_05_SYSTEM_MANAGER.md](AUDIT_05_SYSTEM_MANAGER.md)

---

## [AUDIT 6] CommandBuffer

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/CommandBuffer.cs`
- `UltraSim/ECS/EntityBuilder.cs`

See: [AUDIT_06_COMMAND_BUFFER.md](AUDIT_06_COMMAND_BUFFER.md)

---

## [AUDIT 7] World Tick Pipeline

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/World/World.cs`
- `UltraSim/ECS/World/World.Tick.cs` (if exists)

See: [AUDIT_07_WORLD_PIPELINE.md](AUDIT_07_WORLD_PIPELINE.md)

---

## [AUDIT 8] Query System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/World/World.cs` (query methods)
- `UltraSim/ECS/ArchetypeManager.cs` (query impl)

See: [AUDIT_08_QUERY_SYSTEM.md](AUDIT_08_QUERY_SYSTEM.md)

---

## [AUDIT 9] Serialization System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/IO/*.cs`
- `UltraSim/ECS/World/World.Save.cs`

See: [AUDIT_09_SERIALIZATION.md](AUDIT_09_SERIALIZATION.md)

---

## [AUDIT 10] Event System

**Status**: ⚪ NOT STARTED
**Files**:
- `UltraSim/ECS/Events/WorldEvents.cs`
- Event usage across systems

See: [AUDIT_10_EVENT_SYSTEM.md](AUDIT_10_EVENT_SYSTEM.md)

---

## Rebuild Plan (Generated After Audit)

See: [REBUILD_PLAN.md](REBUILD_PLAN.md) (will be created after audit completes)

This will contain:
- Architecture design for ECS v2
- Step-by-step implementation plan
- Benchmarking strategy
- Migration plan
- Estimated timeline

---

## Decision Matrix (Rebuild vs Modify)

Will be filled out after audit completes:

| Factor | Weight | Rebuild Score | Modify Score | Notes |
|--------|--------|---------------|--------------|-------|
| Dead Code % | High | TBD | TBD | If >30%, rebuild favored |
| Arch Issues | High | TBD | TBD | Fundamental vs surface |
| Perf Gains | Medium | TBD | TBD | How much is on table? |
| Time Investment | High | TBD | TBD | 2-3mo vs 1-2mo |
| Risk | High | TBD | TBD | Breaking changes |
| Learning Value | Low | TBD | TBD | Nice-to-have |

**Final Recommendation**: TBD (after audit)

---

## Progress Tracking

- **Audit Started**: 2025-11-12
- **Current Phase**: Entity System Audit
- **Estimated Completion**: TBD (depends on findings)
- **Time Invested**: 0 hours

---

## Notes

Add observations, "aha!" moments, and questions as we go:

- 2025-11-12: EntityManager.EnqueueCreate() never used - suggests more dead code exists
- 2025-11-12: CommandBuffer stores only entity.Index - version mismatch bugs possible
- 2025-11-12: No parallelism in entity creation despite infrastructure existing
