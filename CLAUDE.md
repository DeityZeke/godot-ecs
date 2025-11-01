# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**GodotECS** (UltraSim) is a high-performance Entity-Component-System (ECS) framework for Godot 4.5 with C#. Designed to handle 1M+ entities efficiently using archetype-based storage, parallel execution, and zero-allocation patterns.

**Key Technologies:**
- Godot 4.5, C# (.NET 8.0/9.0 for Android)
- Jolt Physics
- Forward Plus renderer

## Development Commands

### Building and Running
```bash
# Build C# assemblies
dotnet build GodotECS.sln

# Open in Godot Editor
godot --editor

# Run the project
godot
```

### Debug Configuration
- `USE_DEBUG.txt` in root enables performance profiling and statistics tracking
- To disable debug mode, delete or rename this file

### In-Game Debug Controls
- **F12**: Toggle ECS Control Panel (systems, entities, performance stats)
- **F11**: Manual save world state
- **9**: Quick save
- **0**: Quick load

### Project Structure
```
/UltraSim/        - Core ECS framework (engine-independent)
/Client/          - Client-side systems (Godot UI, rendering)
/Server/          - Server-side systems (movement, AI, game logic)
/Scenes/          - Godot scene files
/Saves/ECS/       - Saved world states
```

## Critical Architecture Patterns

### 1. CommandBuffer for Entity Creation (MOST IMPORTANT)

**ALWAYS use CommandBuffer when creating entities with multiple components.**

**Problem**: Adding components one-by-one causes "archetype thrashing":
```csharp
// WRONG: Entity moves through 4 archetypes (SLOW!)
var entity = world.CreateEntity();
world.AddComponent(entity, new Position());  // Move to archetype 1
world.AddComponent(entity, new Velocity());  // Move to archetype 2
world.AddComponent(entity, new RenderTag()); // Move to archetype 3
// Result: 100k entities = 300k archetype transitions = 10-20ms
```

**Solution**: Create entity with all components at once:
```csharp
// CORRECT: Entity created directly in final archetype (10x FASTER!)
var buffer = new CommandBuffer();
buffer.CreateEntity(e => e
    .Add(new Position { X = 0, Y = 0, Z = 0 })
    .Add(new Velocity { X = 1, Y = 0, Z = 0 })
    .Add(new RenderTag()));
buffer.Apply(world);
// Result: 100k entities = 100k operations = 1-2ms
```

**Why it matters**: This is the difference between 1-2ms and 10-20ms for 100k entities.

### 2. IOProfile System (Format Selection via Composition)

The IOProfile system determines WHERE files are saved and WHAT format to use.

**Key Insight**: Format selection happens by **choosing which IOProfile implementation to return**, NOT via enums.

```csharp
// In WorldECS.cs or custom host
public IIOProfile? GetIOProfile()
{
    // Option 1: Return null for default (binary)
    return null;

    // Option 2: Use binary format explicitly
    return BinaryIOProfile.Instance;

    // Option 3: Use config format (human-readable)
    return ConfigIOProfile.Instance;

    // Option 4: Use custom Godot profile
    return new GodotIOProfile();
}
```

**Available Built-in Profiles:**
- `BinaryIOProfile` - Fast, compact .bin files
- `ConfigIOProfile` - Human-readable .cfg files (INI format)
- `DefaultIOProfile` - Backward compatible, uses binary

**Creating Custom Profiles:**
```csharp
public class MyCustomProfile : BaseIOProfile
{
    public MyCustomProfile()
        : base("MyFormat", "./custom/path", maxThreads: 1) { }

    public override string GetFullPath(string filename) =>
        Path.Combine(BasePath, filename + ".custom");

    public override IWriter CreateWriter(string fullPath) =>
        new MyCustomWriter(fullPath);  // Your IWriter implementation

    public override IReader CreateReader(string fullPath) =>
        new MyCustomReader(fullPath);  // Your IReader implementation
}
```

**Why This Design:**
- No enums - just implement the interface
- New formats don't require modifying core code
- Engine-specific writers can hook into native save systems
- Type-safe at compile time

### 3. World Tick Pipeline

The `World.Tick(delta)` method processes operations in strict phases:

```
Phase 1:   Disable & Destroy Systems
Phase 1.5: Entity Operations (destroy first, then create)
Phase 2:   Create & Enable Systems
Phase 3:   Component Operations (remove first, then add)
Phase 4:   Run Systems (parallel batches)
Phase 5:   Rendering
Phase 6:   Auto-save (if enabled)
Phase 7:   Fire Events
```

**All structural changes MUST go through deferred queues:**
- `world.EnqueueEntityCreate(...)` / `world.EnqueueEntityDestroy(...)`
- `world.EnqueueComponentAdd(...)` / `world.EnqueueComponentRemove(...)`
- `world.EnqueueSystemCreate(...)` / `world.EnqueueSystemDestroy(...)`

**Never modify entity/component structure during system Update()** - use queues or CommandBuffer.

### 4. System Implementation

```csharp
public class MySystem : BaseSystem
{
    public override int SystemId => GetHashCode();
    public override string Name => "My System";

    // CRITICAL: Declare dependencies for parallel batching
    public override Type[] ReadSet => new[] { typeof(Position), typeof(Velocity) };
    public override Type[] WriteSet => new[] { typeof(Position) };

    // Optional: Set update frequency (default is EveryFrame)
    public override TickRate Rate => TickRate.Tick100ms; // 10 Hz

    public override void Update(World world, double delta)
    {
        // EFFICIENT: Use Span<T> for zero-allocation iteration
        var archetype = world.GetArchetype<Position, Velocity>();
        var positions = archetype.GetComponentSpan<Position>();
        var velocities = archetype.GetComponentSpan<Velocity>();

        for (int i = 0; i < positions.Length; i++)
        {
            positions[i].X += velocities[i].X * delta;
            positions[i].Y += velocities[i].Y * delta;
            positions[i].Z += velocities[i].Z * delta;
        }
    }
}
```

**ReadSet/WriteSet enable automatic parallel execution:**
- SystemManager analyzes read/write conflicts
- Systems with no conflicts run in parallel (same batch)
- Systems with conflicts run sequentially (different batches)
- Maximizes parallelism while preventing data races

### 5. Settings System

Systems can expose runtime-configurable settings:

```csharp
public class MySystemSettings : SettingsManager
{
    public FloatSetting SpeedMultiplier { get; }
    public BoolSetting Freeze { get; }

    public MySystemSettings()
    {
        SpeedMultiplier = RegisterFloat("Speed Multiplier", 1.0f,
            min: 0.0f, max: 10.0f, step: 0.1f);
        Freeze = RegisterBool("Freeze Movement", false);
    }
}

public class MySystem : BaseSystem
{
    private MySystemSettings _settings = new();
    public override SettingsManager? GetSettings() => _settings;

    public override void Update(World world, double delta)
    {
        if (_settings.Freeze.Value) return;
        float speed = _settings.SpeedMultiplier.Value;
        // ... use speed
    }
}
```

**Settings are automatically:**
- Displayed in ECS Control Panel (F12)
- Persisted to .cfg files
- Loaded when World.Load() is called

## Performance Optimization Patterns

### 1. Chunk-based Parallel Processing
```csharp
const int CHUNK_SIZE = 65536;
Parallel.For(0, chunkCount, i => {
    int start = i * CHUNK_SIZE;
    int end = Math.Min(start + CHUNK_SIZE, positions.Length);

    for (int j = start; j < end; j++)
    {
        // Process entities in cache-friendly chunks
    }
});
```

### 2. ThreadLocal CommandBuffers
```csharp
private ThreadLocal<CommandBuffer> _buffers = new(() => new CommandBuffer());

public override void Update(World world, double delta)
{
    Parallel.ForEach(chunks, chunk => {
        var buffer = _buffers.Value!;
        buffer.CreateEntity(e => e.Add(new Position()));
        // Buffer is thread-safe per thread
    });

    // Apply all buffers after parallel work
    foreach (var buffer in _buffers.Values)
        buffer.Apply(world);
}
```

### 3. TickRate Optimization
Not all systems need to run every frame:
- `EveryFrame`: 60 FPS (16.7ms) - rendering, input
- `Tick100ms`: 10 Hz - chunk loading, LOD updates
- `Tick1s`: 1 Hz - AI decisions, spawning
- `Manual`: Only when explicitly triggered

This reduces CPU usage for low-priority systems.

## File Locations Reference

**Core ECS Framework:**
- [UltraSim/ECS/World/World.cs](UltraSim/ECS/World/World.cs) - Main world manager
- [UltraSim/ECS/Archetype.cs](UltraSim/ECS/Archetype.cs) - SoA component storage
- [UltraSim/ECS/ArchetypeManager.cs](UltraSim/ECS/ArchetypeManager.cs) - Archetype caching
- [UltraSim/ECS/CommandBuffer.cs](UltraSim/ECS/CommandBuffer.cs) - **CRITICAL for performance**
- [UltraSim/ECS/Systems/BaseSystem.cs](UltraSim/ECS/Systems/BaseSystem.cs) - System base class
- [UltraSim/ECS/Systems/SystemManager.cs](UltraSim/ECS/Systems/SystemManager.cs) - Batching & parallel execution

**IOProfile System:**
- [UltraSim/IO/IIOProfile.cs](UltraSim/IO/IIOProfile.cs) - Profile interface
- [UltraSim/IO/DefaultIOProfile.cs](UltraSim/IO/DefaultIOProfile.cs) - BinaryIOProfile, ConfigIOProfile, BaseIOProfile
- [UltraSim/IO/ConfigFile.cs](UltraSim/IO/ConfigFile.cs) - INI-style config handler
- [Client/IO/GodotIOProfile.cs](Client/IO/GodotIOProfile.cs) - Example Godot integration

**Integration:**
- [Server/ECS/WorldECS.cs](Server/ECS/WorldECS.cs) - Main Godot entry point (Node3D)
- [UltraSim/IHost.cs](UltraSim/IHost.cs) - Host abstraction with GetIOProfile()
- [Client/ECS/ECSControlPanel.cs](Client/ECS/ECSControlPanel.cs) - Debug UI panel (F12)

**Example Systems:**
- [Server/ECS/Systems/OptimizedMovementSystem.cs](Server/ECS/Systems/OptimizedMovementSystem.cs) - Parallel movement (1M entities in 2-3ms)
- [Server/ECS/Systems/OptimizedPulsingMovementSystem.cs](Server/ECS/Systems/OptimizedPulsingMovementSystem.cs) - Sine-wave movement

## Critical Requirements

### Line Endings
**CRITICAL**: All files MUST use LF line endings (enforced via .gitattributes).
- Git will automatically normalize on commit
- VS Code: Set `"files.eol": "\n"`
- This prevents cross-platform issues

### Component Rules
**Components MUST be structs** (value types, not classes):
```csharp
// CORRECT
public struct Position
{
    public float X, Y, Z;
}

// WRONG
public class Position  // Will not work!
{
    public float X, Y, Z;
}
```

**Why**: Structs enable SoA storage and cache-coherent iteration.

## Common Patterns & Best Practices

### Adding a New Component Type

1. Create component struct:
```csharp
public struct MyComponent
{
    public float Value;
    public int Count;
}
```

2. Components are automatically registered on first use (no manual registration needed)

3. Use in systems via archetype queries:
```csharp
var archetype = world.GetArchetype<MyComponent, OtherComponent>();
var myComponents = archetype.GetComponentSpan<MyComponent>();
```

### Adding a New System

1. Create system class extending `BaseSystem`
2. Declare `ReadSet` and `WriteSet` for parallel safety
3. Implement `Update(World world, double delta)`
4. Register in [WorldECS.cs](Server/ECS/WorldECS.cs) `_Ready()` method:
```csharp
_world.EnqueueSystemCreate(new MySystem());
_world.EnqueueSystemEnable<MySystem>();
```

### Debugging Performance

1. Press **F12** to open ECS Control Panel
2. Check system performance metrics:
   - Last Update Time (ms)
   - Average Update Time (ms)
   - Peak Update Time (ms)
3. Look for systems with high average times
4. Consider:
   - Reducing TickRate (e.g., EveryFrame → Tick100ms)
   - Adding chunk-based parallelization
   - Using Span<T> instead of foreach
   - Batching entity creation with CommandBuffer

### Save/Load System

**Quick Save/Load:**
```csharp
// In-game: Press 9 to quick save, 0 to quick load
// In code:
world.QuickSave();  // Saves to default location
world.QuickLoad();  // Loads from default location
```

**Custom Save/Load:**
```csharp
world.Save("my_save.ecs");
world.Load("my_save.ecs");
```

Saves are stored in: `Saves/ECS/`

## Architecture Deep Dive

### Why Archetypes?

Traditional ECS uses component pools with sparse arrays (entity → component lookup). This causes cache misses.

**Archetype-based ECS** groups entities with identical component signatures:
- All Position components are contiguous in memory (cache-friendly)
- All Velocity components are contiguous in memory
- Systems iterate over dense arrays (Span<T>)
- Downside: Moving components between archetypes requires copying

**Solution: CommandBuffer** batches entity creation to avoid archetype moves.

### Why Deferred Operations?

Modifying entity/component structure during iteration causes:
- Iterator invalidation
- Inconsistent state
- Race conditions in parallel systems

**Solution:** Queue all structural changes, process in dedicated phases.

### Why ReadSet/WriteSet?

Systems declare component dependencies to enable automatic parallelization:
- Systems with no conflicts run in parallel
- Conflicts detected by SystemManager
- Systems grouped into sequential batches
- Each batch runs systems in parallel

Example batching:
```
Batch 1 (parallel): MovementSystem (writes Position), AISystem (writes AI)
Batch 2 (parallel): RenderSystem (reads Position), PhysicsSystem (writes Velocity)
```

## Performance Expectations

- **Entity Creation**: With CommandBuffer ~100k in 1-2ms; without ~10-20ms
- **Movement** (1M entities): Parallel 2-3ms; single-threaded 8-12ms
- **Batching**: 3-5x speedup on 8-core systems

## Project History

The architecture emphasizes:
- Zero-allocation design (Span<T>, pooling, deferred operations)
- Scalability (1M entities in 2-3ms)
- Parallel safety (ReadSet/WriteSet conflict detection)
- Engine independence (SimContext abstraction)
- Extensibility (IOProfile composition, settings system)
- Developer experience (ECSControlPanel, F12 debug UI)
