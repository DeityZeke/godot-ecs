# Claude Context Memory - UltraSim ECS Project
*Last Updated: October 26, 2025*
*Auto-update triggers: Major decisions, performance milestones, architecture changes, or user request*

---

## ðŸŽ¯ Current State (As of Last Session)

### Performance Achievements
- **Single-Core**: 800K entities @ 41 FPS
- **Multi-Core**: 1M entities @ 48 FPS  
- **Bottleneck**: GPU rendering (RTX 4070 Ti SUPER), not CPU
- **Frame Time**: ~4ms ECS logic + rendering

### Active Systems
- **Core ECS**: Archetype-based, Span<T> queries, zero-GC runtime
- **Tick Scheduling**: Variable tick rates (EveryFrame, 30Hz, 10Hz, 1Hz, 0.2Hz, Manual)
- **Parallel Execution**: ManualThreadPool (eliminated 99.8% GC allocations)
- **Rendering**: AdaptiveMultiMeshRenderSystem (FPS-adaptive scaling to 1M visuals)
- **Debug Tools**: ECSControlPanel with F-key hotkeys, advanced statistics, CSV exports

### Current Work
- Exploring memory/context persistence systems
- Planning culling strategies (frustum, distance, occlusion)
- Considering chunk-based spatial partitioning for massive scale

---

## ðŸ“œ Project Evolution Timeline

### Phase 0: Initial Concept (Pre-Project)
- **Old ECS v0.1**: Dictionary-based, OOP components, ~5K entities max
- **Performance**: 500K visuals @ 60 FPS (no systems), 300K @ 20 FPS (with systems)
- **Key Features**: FPS-adaptive visualization, comprehensive stress tests
- **Limitation**: Single-threaded, GC pressure, not scalable

### Phase 1: v2 Foundation (Weeks 1-2)
**Goal**: Build data-oriented archetype-based ECS from scratch

**Key Decisions**:
- âœ… Archetype-based over traditional ECS (cache-friendly, fast queries)
- âœ… Unmanaged components (zero GC)
- âœ… Span<T> for queries (zero-copy, fast iteration)
- âœ… Command buffers for deferred changes (thread-safe)
- âœ… C# in Godot 4.5 (preferred over Rust/Bevy for rapid iteration)

**Achievements**:
- 100K entities @ 0.5-0.6ms per frame (logic only)
- Zero-GC steady state
- ~8.5M entities/ms throughput

**Challenges**:
- Initial rendering bottleneck with individual MeshInstance3D nodes
- GC spikes from Parallel.For closures (20-40KB per frame)
- Command buffer overflow on large entity batches

### Phase 2: Parallelization & Optimization (Weeks 3-4)
**Goal**: Achieve 1M+ entities while maintaining 60 FPS

**Major Implementations**:
1. **ParallelSystemScheduler**: Auto-detects R/W conflicts, batches safe systems
2. **ManualThreadPool**: Persistent threads, zero lambda allocations
3. **ThreadSafeCommandBuffers**: Thread-local buffers, lock-free execution
4. **AdaptiveMultiMeshRenderSystem**: FPS-based dynamic entity addition

**Performance Gains**:
- Before parallelism: 332K entities @ 20 FPS
- After parallelism: 500K entities @ 20 FPS (95% performance recovery)
- GC: 3.6-4.2MB/frame â†’ 0-8KB/frame (99.8% reduction)

**Critical Bug Fixes**:
- `ref Entity` pass-by-value bug (components not persisting)
- Archetype transition failures (entities stuck in empty archetype)
- Structural command buffer race conditions
- Adaptive renderer death spiral (reducing performance when FPS dropped)

### Phase 3: Advanced Features & Polish (Weeks 5-6)
**Implementations**:
1. **Tick Rate Scheduling**: 73-75% reduction in update overhead
2. **ECSControlPanel**: Runtime GUI with F1-F12 hotkeys
3. **Settings Framework**: Per-system config, auto-save, ISetting types
4. **Stress Testing Suite**: Spawn, Churn, Archetype, System tests
5. **Debug Infrastructure**: CSV exports, timing breakdowns, GC monitoring

**Key Patterns Established**:
- Deferred queues for all structural changes
- Per-system settings with fluent registration API
- Event-driven visual synchronization
- Capacity pre-allocation (32 for systems, 65K for entities)

---

## ðŸ—ï¸ Architecture

### Core Components

**World.cs** (Central Hub)
- Entity lifecycle management
- System registration and execution
- Archetype management via ArchetypeManager
- Command buffer coordination

**Archetype.cs** (Data Storage)
- Structure-of-Arrays layout
- ComponentList<T> for each component type
- O(1) component access via type registry
- Chunk-based memory allocation

**SystemManager.cs** (Execution Coordinator)
- Tick-rate scheduling with O(1) bucket lookup
- Parallel batch execution via ManualThreadPool
- System enable/disable at runtime
- Performance monitoring (when USE_DEBUG enabled)

**StructuralCommandBuffer.cs** (Safe Mutations)
- Thread-local buffers (via ThreadStatic)
- Deferred entity create/destroy
- Deferred component add/remove  
- Applied in deterministic order post-frame

### Data Flow
```
1. _Process(delta)
   â†“
2. World.Update(delta)
   â†“
3. SystemManager.UpdateTicked()  [filters by tick rate]
   â†“
4. ParallelSystemScheduler.RunBatches()  [parallel execution]
   â†“
5. ThreadSafeCommandBuffers.FlushThreadLocalBuffers()
   â†“
6. World.ApplyStructuralChanges()  [mutations applied]
   â†“
7. AdaptiveMultiMeshRenderSystem.OnFrameComplete()  [visuals sync]
```

### Memory Model
- **Entities**: Lightweight structs (12 bytes: ID, archetype pointer, index)
- **Components**: Unmanaged value types (Position, Velocity, etc.)
- **Storage**: Contiguous Span<T> per archetype (cache-friendly)
- **Queries**: Cached archetype lists (zero allocation)
- **Recycling**: Entity IDs reused via free list

---

## ðŸŽ“ Critical Lessons Learned

### 1. **Parallel Task Allocations Are Deadly**
**Problem**: `Parallel.For` creates closures = 20-40KB per frame
**Solution**: ManualThreadPool with persistent threads
**Impact**: 99.8% GC reduction

### 2. **GPU is the Bottleneck, Not CPU**
**Evidence**: 800K entities single-core @ 41 FPS â‰ˆ 1M multi-core @ 48 FPS
**Insight**: AdaptiveMultiMeshRenderSystem caps visuals before CPU maxes out
**Implication**: Focus on culling strategies, not more CPU cores

### 3. **Adaptive Systems Prevent Death Spirals**
**Anti-Pattern**: Increase work when FPS drops (makes it worse)
**Solution**: Tier-based scaling with hysteresis
**Example**: Renderer only increases update rate after sustained good FPS

### 4. **Command Buffers Must Match Architecture**
**Original**: Fixed-size arrays (crashes on overflow)
**Current**: Dynamic List<T> with CollectionsMarshal.AsSpan() (zero-copy performance)
**Key**: Process every frame to keep lists small

### 5. **Tick Scheduling is Mandatory at Scale**
**Without**: Every system runs every frame (wasteful)
**With**: Systems run at required frequency only
**Savings**: 73-75% reduction in frame time

### 6. **Debug Tools Are Not Optional**
**ECSControlPanel**: Saved weeks of debugging time
**CSV Exports**: Revealed performance patterns not visible in profiler
**Hotkeys**: Enabled rapid iteration without code changes

---

## ðŸ”§ Current Architecture Patterns

### Pattern: Tick Rate System
```csharp
public override TickRate Rate => TickRate.Tick1s;  // Runs once per second
```
**Use Cases**:
- EveryFrame: Movement, rendering
- Tick33ms (30 Hz): AI decisions
- Tick100ms: Medium-frequency logic
- Tick1s: Economy, population, saves
- Manual: Debug commands, user triggers

### Pattern: Settings Framework
```csharp
public class MySystemSettings : BaseSettings
{
    public FloatSetting Speed { get; private set; }
    public BoolSetting Enabled { get; private set; }
    
    protected override void RegisterSettings()
    {
        Speed = Register(new FloatSetting("Speed", 5.0f, 0f, 100f));
        Enabled = Register(new BoolSetting("Enabled", true));
    }
}
```
**Benefits**: Type-safe, persistent, GUI auto-generation, no reflection

### Pattern: Command Buffer Usage
```csharp
// In system Update()
for (int i = 0; i < positions.Length; i++)
{
    structuralBuffer.CreateEntity();  // Queued
    valueBuffer.SetComponent(id, newPosition);  // Queued
}
// Applied automatically after all systems complete
```
**Safety**: No immediate mutations, thread-safe, deterministic order

### Pattern: Zero-Allocation Queries
```csharp
var archetypes = world.GetArchetypesWithComponents<Position, Velocity>();
foreach (var arch in archetypes)  // List reused, no allocation
{
    var pos = arch.GetComponentSpan<Position>();  // Direct memory access
    var vel = arch.GetComponentSpan<Velocity>();
    // Process...
}
```
**Performance**: Spans are stack-allocated, zero GC

---

## âš ï¸ Known Issues & Workarounds

### Issue 1: Unicode Characters Don't Render in Console
**Symptom**: Box-drawing, checkmarks show as garbled text
**Workaround**: Use ASCII alternatives (-, +, |, >, <)
**Status**: Documented, not critical

### Issue 2: First MultiMesh Update Spike (5+ seconds)
**Symptom**: Initial SetInstanceTransform call extremely slow
**Cause**: Godot allocating GPU buffers on first access
**Workaround**: Pre-initialize transforms to identity, or accept one-time cost
**Status**: Engine limitation, cannot fix

### Issue 3: Advanced Statistics Cause 10-25% FPS Drop
**Symptom**: Per-system timing adds overhead
**Solution**: Made opt-in via ECSControlPanel toggle with warning
**Status**: Fixed, documented

### Issue 4: Settings Only Save When Explicitly Applied
**Intended Behavior**: Settings â‰  game state
**Rationale**: Prevent accidental persistence of temporary GUI changes
**Status**: Working as designed

---

## ðŸŽ¯ Performance Targets & Limits

### Proven Capabilities
| Scenario | Entity Count | FPS | Notes |
|----------|-------------|-----|-------|
| Logic Only | 1M | 1600+ | No rendering, systems enabled |
| Single-Core Visual | 800K | 41 | All systems, adaptive rendering |
| Multi-Core Visual | 1M | 48 | All systems, adaptive rendering |
| Debug Build | 1M | 40-43 | With advanced statistics |
| Release Build | ? | ? | Not yet tested, expect 20-30% boost |

### Bottleneck Analysis
- **0-50K entities**: Godot scene tree overhead (individual nodes)
- **50K-1M entities**: GPU rendering (MultiMesh transforms)
- **1M+ entities**: Not yet tested, likely memory bandwidth

### Scaling Projections
**With Frustum Culling** (planned):
- Expected: 5-10Ã— visible entity reduction
- Impact: 500K-1M total, 50K-100K visible â†’ 200+ FPS

**With Chunk System** (planned):
- Expected: Process only nearby chunks
- Impact: 10M total, 100K active â†’ 60 FPS

---

## ðŸ“‚ File Organization

### Critical Files (Don't Break These!)
- `World.cs` - Core ECS hub
- `SystemManager.cs` - Main execution loop
- `SystemManager_TickScheduling.cs` - Tick rate logic
- `ManualThreadPool.cs` - Zero-GC parallelism
- `AdaptiveMultiMeshRenderSystem.cs` - FPS-adaptive rendering
- `ECSControlPanel.cs` - Runtime debug GUI

### Supporting Files
- `BaseSystem.cs` - System base class with Rate property
- `StructuralCommandBuffer.cs` - Entity create/destroy
- `ThreadLocalCommandBuffer.cs` - Thread-safe buffers
- `TickRate.cs` - Enum with 12 predefined rates
- `Components.cs` - Unmanaged component definitions
- `Utilities.cs` - Helper functions

### Documentation
- `ECS_Design_Spec_v2.md` - Complete architecture reference
- `IMPLEMENTATION_GUIDE.md` - Step-by-step feature implementation
- `SUCCESS_REPORT.md` - Major milestones and wins
- `ManualThreadPool_Performance_Analysis.md` - GC elimination proof
- `TickRate_Implementation_Spec.md` - Scheduling system design

---

## ðŸš€ Next Steps & Opportunities

### Immediate Wins (1-2 days each)
1. **Frustum Culling** - Only render visible entities (10Ã— speedup expected)
2. **Distance Culling** - Don't process entities beyond range (2Ã— speedup)
3. **Release Build Test** - Verify zero-GC without debug overhead

### Medium-Term Goals (1-2 weeks each)
1. **Chunk System** - Spatial partitioning for massive worlds
2. **Hybrid Rendering** - Near (individual nodes) + Far (MultiMesh)
3. **SIMD Optimization** - Vector math for movement systems
4. **Occlusion Culling** - Don't render behind geometry

### Long-Term Vision (1-2 months)
1. **10M Entity Support** - With aggressive culling and LOD
2. **Networked ECS** - Sync state over network
3. **Save/Load System** - Serialize entire world state
4. **Advanced AI** - Behavior trees, utility AI, needs-based

### Research Topics
- **Job System**: Replace ManualThreadPool with Godot's job system?
- **Burst Compiler**: C# SIMD without unsafe code?
- **GPU Compute**: Offload movement to compute shaders?
- **WebAssembly**: Can this run in browser at acceptable performance?

---

## ðŸ’¡ Design Philosophy

### Core Principles
1. **Performance First**: Sub-millisecond systems are mandatory
2. **Zero-GC Runtime**: Allocations are bugs, not features
3. **Data-Oriented**: Favor arrays over objects, batches over individuals
4. **Measure Everything**: If it's not measured, it's not optimized
5. **Fail Gracefully**: Adaptive systems better than hard limits

### When to Break Rules
- **Use GC in Editor/Tools**: CSV exports, GUI, debug tools (marked USE_DEBUG)
- **Object-Oriented for Rare Code**: Settings classes, system base classes
- **Flexibility Over Speed**: Settings framework (only accessed on change)

---

## ðŸ“ž Support Resources

### If Systems Don't Run
1. Check enabled state: `SystemManager.IsEnabled<T>()`
2. Verify tick rate: System.Rate must match expected frequency
3. Check dependencies: Parallel scheduler may delay execution

### If Performance Degrades
1. Toggle advanced statistics (F3) to see per-system times
2. Check GC allocations (should be 0-8KB)
3. Verify adaptive renderer isn't throttling (yellow warning text)
4. Export CSV and analyze frame-by-frame

### If Visuals Don't Update
1. Ensure AdaptiveMultiMeshRenderSystem is enabled
2. Check entity count vs MaxCapacity (1M default)
3. Verify entities have Position + RenderTag + Visible components
4. Toggle visualization (F4)

### If Crashes Occur
1. Check buffer capacity (should be dynamic, not fixed)
2. Verify thread-safe buffer flushing before Apply()
3. Look for out-of-bounds archetype access
4. Check entity ID recycling isn't breaking

---

## ðŸŽ¨ User Preferences & Style

### Coding Preferences
- **Clear over clever**: Readable code > micro-optimizations
- **Document intent**: Why, not just what
- **Complete examples**: Working code, not pseudocode
- **Performance context**: Always explain cost/benefit

### Communication Style
- **Bottom-line up front**: Answer first, explanation second
- **Show, don't tell**: Code examples over theory
- **Measure, don't guess**: Benchmarks prove claims
- **Options, not mandates**: Present alternatives with tradeoffs

---

## ðŸ”– Quick Reference

### Hotkeys (ECSControlPanel)
- **F1**: Toggle movement systems
- **F2**: Toggle rendering
- **F3**: Toggle advanced statistics (10-25% FPS cost)
- **F4**: Toggle tick scheduling visualization
- **F5-F9**: Stress tests (spawn, churn, archetype)
- **F10**: Manual system trigger
- **F11**: Save system trigger
- **+/- 1K/10K/100K/1M**: Spawn entities

### Performance Quick-Check
```
Good: 0-8KB GC, <1ms ECS, 60+ FPS
Acceptable: 0-24KB GC, 1-5ms ECS, 30-60 FPS  
Investigate: >24KB GC, >5ms ECS, <30 FPS
```

### Common Commands
```csharp
// Create entity
var id = world.CreateEntity();

// Add components (deferred)
structuralBuffer.AddComponent(id, new Position(x, y, z));
structuralBuffer.AddComponent(id, new Velocity(vx, vy, vz));

// Modify component (immediate via buffer)
valueBuffer.SetComponent(id, newPosition);

// Query entities
var archetypes = world.GetArchetypesWithComponents<Position, Velocity>();
foreach (var arch in archetypes)
{
    var pos = arch.GetComponentSpan<Position>();
    var vel = arch.GetComponentSpan<Velocity>();
    // Process spans...
}

// Destroy entity (deferred)
structuralBuffer.DestroyEntity(id);
```

---

## ðŸ“Š Metrics to Track

### Per-Frame
- ECS update time (should be <5ms)
- GC allocations (should be 0-8KB)
- Frame time total (target 16.67ms for 60 FPS)

### Per-Second
- Entity creation rate
- Component addition rate
- System execution counts

### Per-Session
- Peak entity count achieved
- Average FPS over session
- Total GC collections (should be minimal)
- Memory high-water mark

---

*This context file is maintained by Claude to provide continuity across conversations. Update triggers: major architectural changes, performance milestones, critical bug fixes, or explicit user request ("update context").*

*Last comprehensive review: October 26, 2025 (reviewed all 40+ project conversations)*