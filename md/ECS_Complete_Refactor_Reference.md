# ECS Architecture Refactor - Complete Reference Guide

**Date:** October 26, 2025  
**Purpose:** Comprehensive guide for implementing the complete ECS refactor  
**Status:** Planning Complete, Ready for Implementation

---

## ðŸ“‹ Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architecture Vision](#architecture-vision)
3. [Final Folder Structure](#final-folder-structure)
4. [Consolidation Decisions](#consolidation-decisions)
5. [Bootstrap Pattern](#bootstrap-pattern)
6. [Export Configuration](#export-configuration)
7. [Implementation Phases](#implementation-phases)
8. [File Reduction Summary](#file-reduction-summary)
9. [Critical Rules & Constraints](#critical-rules--constraints)
10. [Migration Checklist](#migration-checklist)

---

## ðŸ“Š Executive Summary

### Goals:
- âœ… Reduce core files from ~50 to ~21-25
- âœ… Eliminate all duplicate code (Position, Velocity, command buffers, etc.)
- âœ… Clear separation of concerns (Core/Server/Client)
- âœ… Support single-player, LAN, multiplayer, and dedicated server modes
- âœ… Maintain ACC-inspired runtime configurability
- âœ… Keep Core/ pure C# (zero Godot dependencies)

### Key Decisions:
- **One Godot Project** - Everything in one project, separated by folders
- **Bootstrap Pattern** - Godot nodes kick off Core ECS
- **Manager Consolidation** - EntityManager, ComponentManager, ArchetypeManager
- **World-Integrated Saves** - World coordinates persistence, managers save themselves
- **Component Extraction** - All components in individual files, organized by category
- **Assets at Root** - `Assets/` for client, `ServerAssets/` for server

---

## ðŸŽ¯ Architecture Vision

### Minecraft-Style Multiplayer Model:

```
Mode 1: Single-Player
  â”œâ”€ Server (invisible, localhost)
  â””â”€ Client (connected to localhost)

Mode 2: LAN Host
  â”œâ”€ Server (visible on LAN)
  â”œâ”€ Client (localhost, hosting player)
  â””â”€ Other Clients (connected via LAN)

Mode 3: Multiplayer Client
  â””â”€ Client only (connects to remote server)

Mode 4: Dedicated Server
  â””â”€ Server only (headless, no rendering)
```

### Core Principles:
1. **Server is Always Authoritative** - Server owns game state
2. **Client is Visualization + Input** - Rendering, UI, input handling
3. **Core is Pure Logic** - ECS lives here, no Godot dependencies
4. **Bootstrap Nodes Connect Godot to Core** - Thin adapter layer

---

## ðŸ“ Final Folder Structure

```
UltraSim/                              # Single Godot project
â”œâ”€â”€ project.godot                      # Godot project file
â”‚
â”œâ”€â”€ Core/                              # Pure C# ECS (NO Godot dependencies)
â”‚   â”œâ”€â”€ Entities/
â”‚   â”‚   â”œâ”€â”€ Entity.cs                  # (45 lines) Pure handle struct
â”‚   â”‚   â””â”€â”€ EntityManager.cs           # (300 lines) Absorbs EntityBuilder
â”‚   â”‚
â”‚   â”œâ”€â”€ Components/
â”‚   â”‚   â”œâ”€â”€ ComponentSignature.cs      # (60 lines) Keep as value type
â”‚   â”‚   â”œâ”€â”€ ComponentManager.cs        # (250 lines) Absorbs Registry + List
â”‚   â”‚   â”‚
â”‚   â”‚   â”œâ”€â”€ Core/                      # Always included
â”‚   â”‚   â”‚   â”œâ”€â”€ Position.cs
â”‚   â”‚   â”‚   â”œâ”€â”€ Velocity.cs
â”‚   â”‚   â”‚   â”œâ”€â”€ RenderTag.cs
â”‚   â”‚   â”‚   â””â”€â”€ Visible.cs
â”‚   â”‚   â”‚
â”‚   â”‚   â”œâ”€â”€ Movement/
â”‚   â”‚   â”‚   â””â”€â”€ PulseData.cs
â”‚   â”‚   â”‚
â”‚   â”‚   â”œâ”€â”€ Rendering/
â”‚   â”‚   â”‚   â”œâ”€â”€ CameraPosition.cs
â”‚   â”‚   â”‚   â””â”€â”€ ChunkComponent.cs
â”‚   â”‚   â”‚
â”‚   â”‚   â”œâ”€â”€ AI/
â”‚   â”‚   â”‚   â””â”€â”€ AIComponent.cs
â”‚   â”‚   â”‚
â”‚   â”‚   â””â”€â”€ Testing/                   # Optional (#if INCLUDE_ECS_TESTS)
â”‚   â”‚       â”œâ”€â”€ Temperature.cs
â”‚   â”‚       â”œâ”€â”€ Health.cs
â”‚   â”‚       â””â”€â”€ Lifetime.cs
â”‚   â”‚
â”‚   â”œâ”€â”€ Archetypes/
â”‚   â”‚   â”œâ”€â”€ Archetype.cs               # (225 lines)
â”‚   â”‚   â””â”€â”€ ArchetypeManager.cs        # (200 lines)
â”‚   â”‚
â”‚   â”œâ”€â”€ Systems/
â”‚   â”‚   â”œâ”€â”€ BaseSystem.cs              # (240 lines)
â”‚   â”‚   â”œâ”€â”€ SystemManager.Core.cs      # (200 lines) Renamed from SystemManager.cs
â”‚   â”‚   â”œâ”€â”€ SystemManager.Debug.cs     # (158 lines)
â”‚   â”‚   â””â”€â”€ SystemManager.Scheduling.cs # (300 lines) Renamed from SystemManager_TickScheduling.cs
â”‚   â”‚
â”‚   â”œâ”€â”€ Commands/
â”‚   â”‚   â””â”€â”€ CommandBuffer.cs           # (220 lines) MERGED: StructuralCB + ThreadLocalCB
â”‚   â”‚
â”‚   â”œâ”€â”€ Settings/
â”‚   â”‚   â”œâ”€â”€ ISetting.cs                # (30 lines)
â”‚   â”‚   â”œâ”€â”€ BaseSettings.cs            # (135 lines)
â”‚   â”‚   â”œâ”€â”€ FloatSetting.cs            # (75 lines)
â”‚   â”‚   â”œâ”€â”€ IntSetting.cs              # (60 lines)
â”‚   â”‚   â”œâ”€â”€ BoolSetting.cs             # (45 lines)
â”‚   â”‚   â”œâ”€â”€ EnumSetting.cs             # (75 lines)
â”‚   â”‚   â””â”€â”€ StringSetting.cs           # (60 lines)
â”‚   â”‚
â”‚   â”œâ”€â”€ Networking/
â”‚   â”‚   â”œâ”€â”€ Protocol.cs                # Network protocol definitions
â”‚   â”‚   â”œâ”€â”€ Serialization.cs           # Network serialization
â”‚   â”‚   â””â”€â”€ Messages.cs                # Network message types
â”‚   â”‚
â”‚   â”œâ”€â”€ Misc/
â”‚   â”‚   â”œâ”€â”€ Utilities.cs               # (150 lines) Math/gameplay helpers
â”‚   â”‚   â””â”€â”€ TickRate.cs                # (135 lines)
â”‚   â”‚
â”‚   â””â”€â”€ World/
â”‚       â”œâ”€â”€ World.cs                   # (200 lines) Main orchestration + World.Paths
â”‚       â”œâ”€â”€ World.Persistence.cs       # (150 lines) Save/load coordination
â”‚       â””â”€â”€ World.AutoSave.cs          # (50 lines) Auto-save timer
â”‚
â”œâ”€â”€ Server/                            # Server-specific code
â”‚   â”œâ”€â”€ ServerBootstrap.cs             # Node - kicks off server
â”‚   â”œâ”€â”€ ServerWorld.cs                 # Wraps Core.World + networking
â”‚   â”œâ”€â”€ ServerNetworking.cs            # Server-side networking
â”‚   â””â”€â”€ Console/
â”‚       â””â”€â”€ ServerConsole.cs           # Console output (for headless mode)
â”‚
â”œâ”€â”€ Client/                            # Client-specific code
â”‚   â”œâ”€â”€ ClientBootstrap.cs             # Node - kicks off client
â”‚   â”œâ”€â”€ ClientWorld.cs                 # Wraps Core.World + prediction
â”‚   â”œâ”€â”€ ClientNetworking.cs            # Client-side networking
â”‚   â”‚
â”‚   â”œâ”€â”€ Rendering/
â”‚   â”‚   â”œâ”€â”€ AdaptiveMultiMeshRenderSystem.cs
â”‚   â”‚   â””â”€â”€ CameraController.cs
â”‚   â”‚
â”‚   â”œâ”€â”€ Input/
â”‚   â”‚   â””â”€â”€ InputManager.cs
â”‚   â”‚
â”‚   â””â”€â”€ UI/
â”‚       â””â”€â”€ ECSControlPanel/
â”‚           â”œâ”€â”€ ECSControlPanel.cs
â”‚           â””â”€â”€ ECSControlPanel.tscn
â”‚
â”œâ”€â”€ Scenes/                            # Main entry scenes
â”‚   â”œâ”€â”€ Standalone.tscn                # Single-player + LAN
â”‚   â”œâ”€â”€ ClientOnly.tscn                # Multiplayer client
â”‚   â””â”€â”€ ServerOnly.tscn                # Dedicated server
â”‚
â”œâ”€â”€ Assets/                            # Visual/audio resources (client-only)
â”‚   â”œâ”€â”€ Textures/
â”‚   â”œâ”€â”€ Materials/
â”‚   â”œâ”€â”€ Meshes/
â”‚   â”œâ”€â”€ Shaders/
â”‚   â””â”€â”€ Audio/
â”‚
â”œâ”€â”€ ServerAssets/                      # Server-only minimal resources
â”‚   â””â”€â”€ ConsoleFont.ttf
â”‚
â”œâ”€â”€ Tests/                             # Optional (#if INCLUDE_ECS_TESTS)
â”‚   â”œâ”€â”€ StressTests/
â”‚   â”‚   â”œâ”€â”€ StressTestModule.cs
â”‚   â”‚   â”œâ”€â”€ StressTestTypes.cs
â”‚   â”‚   â”œâ”€â”€ StressTestManager.cs
â”‚   â”‚   â”œâ”€â”€ SpawnStressTest.cs
â”‚   â”‚   â”œâ”€â”€ ChurnStressTest.cs
â”‚   â”‚   â””â”€â”€ ArchetypeStressTest.cs
â”‚   â”‚
â”‚   â””â”€â”€ UnitTests/
â”‚       â”œâ”€â”€ Phase1Test.cs
â”‚       â””â”€â”€ TickRateTestSystems.cs
â”‚
â”œâ”€â”€ Examples/                          # Optional (#if INCLUDE_ECS_EXAMPLES)
â”‚   â””â”€â”€ Systems/
â”‚       â”œâ”€â”€ MovementSystem.cs
â”‚       â”œâ”€â”€ OptimizedMovementSystem.cs
â”‚       â”œâ”€â”€ PulsingMovementSystem.cs
â”‚       â””â”€â”€ OptimizedPulsingMovementSystem.cs
â”‚
â””â”€â”€ Scripts/                           # User modding folder (RunUO-style)
    â”œâ”€â”€ Components/
    â””â”€â”€ Systems/
```

**Core File Count: ~21-25 files** (down from 50+)

---

## ðŸ”„ Consolidation Decisions

### 1. EntityManager Absorbs EntityBuilder âœ…

**Before:**
```
Entity.cs          (45 lines)
EntityBuilder.cs   (45 lines)
```

**After:**
```
Entity.cs                 (45 lines) - Pure handle struct
EntityManager.cs          (300 lines) - Contains internal EntityComposer
```

**EntityManager API:**
```csharp
public sealed class EntityManager
{
    // Simple creation
    public Entity Create();
    
    // Complex creation (absorbed EntityBuilder functionality)
    public Entity Create(Action<IEntityComposer> setup);
    
    // Deferred creation
    public void EnqueueCreate(Action<IEntityComposer> setup);
    
    // Internal composer (not exposed publicly)
    public interface IEntityComposer
    {
        IEntityComposer Add<T>(T component) where T : struct;
    }
    
    private class EntityComposer : IEntityComposer { ... }
}
```

**World API:**
```csharp
// Simple
var entity = world.CreateEntity();

// Complex (uses internal composer)
var entity = world.CreateEntity(e => {
    e.Add(new Position(0, 0, 0));
    e.Add(new Velocity(1, 0, 0));
    e.Add(new RenderTag());
});

// Deferred
world.EnqueueEntityCreate(e => { ... });
```

---

### 2. ComponentManager Absorbs Registry + List âœ…

**Before:**
```
ComponentSignature.cs      (60 lines)
ComponentTypeRegistry.cs   (90 lines)
ComponentList.cs           (75 lines)
ComponentManager.cs        (NEW)
```

**After:**
```
ComponentSignature.cs      (60 lines) - Keep as pure value type
ComponentManager.cs        (250 lines) - Absorbs Registry + List
```

**ComponentManager Owns:**
```csharp
public sealed class ComponentManager
{
    // 1. Type Registry (absorbed from ComponentTypeRegistry)
    private Dictionary<Type, int> _typeToId;
    public int GetTypeId<T>() where T : struct;
    public Type GetTypeById(int id);
    
    // 2. Storage Abstraction (absorbed ComponentList - internal)
    internal interface IComponentList { ... }
    internal class ComponentList<T> : IComponentList { ... }
    
    // 3. Component Operations
    public void Add<T>(Entity e, T component) where T : struct;
    public void Remove<T>(Entity e) where T : struct;
    public ref T Get<T>(Entity e) where T : struct;
    
    // 4. Inspection API (new - inspired by ACC)
    public IReadOnlyDictionary<Type, object> GetAllComponents(Entity e);
    
    // 5. Save/Load
    public void Save(ConfigFile config, string section);
    public void Load(ConfigFile config, string section);
}
```

---

### 3. World Integrates Save/Load âœ…

**Before:**
```
SaveSystem.cs              (pretends to be a system)
ECSPaths.cs                (separate file)
```

**After:**
```
World.cs                   (includes World.Paths nested class)
World.Persistence.cs       (save/load coordination)
World.AutoSave.cs          (auto-save timer)
```

**World.Paths (Static Nested Class):**
```csharp
public partial class World
{
    public static class Paths
    {
        public static string GetBasePath();
        public static string GetSystemFolder(string systemName);
        public static string GetWorldSave(string filename);
        internal static void EnsureDirectoriesExist();
    }
}

// Usage:
string path = World.Paths.GetSystemFolder("OptimizedMovementSystem");
```

**World.Persistence API:**
```csharp
public partial class World
{
    public void Save(string filename = "world.sav");
    public void Load(string filename = "world.sav");
    public void QuickSave();
    public void QuickLoad();
    
    // Events
    public event Action OnBeforeSave;
    public event Action OnAfterSave;
    
    private void SaveWorldState(ConfigFile config);
    private void LoadWorldState(ConfigFile config);
}
```

**Managers Save Themselves:**
```csharp
public void Save(string filename)
{
    var config = new ConfigFile();
    
    SaveWorldState(config);       // World's own data
    _entities.Save(config);       // EntityManager saves itself
    _archetypes.Save(config);     // ArchetypeManager saves itself
    _components.Save(config);     // ComponentManager saves itself
    _systems.SaveAll();           // SystemManager saves all systems
    
    config.Save(World.Paths.GetWorldSave(filename));
}
```

---

### 4. Command Buffers Unified âœ…

**Before:**
```
StructuralCommandBuffer.cs    (130 lines) - Entity creation/destruction
ThreadLocalCommandBuffer.cs   (155 lines) - Thread-safe component ops + entity destruction (DUPLICATE!)
```

**After:**
```
CommandBuffer.cs              (220 lines) - Unified, thread-safe
```

**Unified CommandBuffer:**
```csharp
public sealed class CommandBuffer
{
    // From StructuralCommandBuffer
    public void CreateEntity(Action<IEntityComposer> setup);
    
    // From ThreadLocalCommandBuffer
    public void AddComponent<T>(int entityIdx, T component) where T : struct;
    public void RemoveComponent<T>(int entityIdx) where T : struct;
    public void DestroyEntity(Entity entity);
    
    // Thread-safety (TLS + pooling)
    [ThreadStatic] private static List<Command> _threadLocalBuffer;
    private ConcurrentBag<List<Command>> _pool;
    
    // Execution
    public void FlushAll(World world);
    public void Clear();
}
```

---

### 5. All Components Extracted âœ…

**Before:**
```
Components.cs              - Position, Velocity, RenderTag, Visible, PulseData
ExampleSystems.cs          - Position (DUP!), Velocity (DUP!), ChunkComponent, CameraPosition, AIComponent
ArchetypeStressTest.cs     - Temperature, Health, Lifetime
```

**After:**
```
Components/
â”œâ”€â”€ Core/                  - Position, Velocity, RenderTag, Visible
â”œâ”€â”€ Movement/              - PulseData
â”œâ”€â”€ Rendering/             - CameraPosition, ChunkComponent
â”œâ”€â”€ AI/                    - AIComponent
â””â”€â”€ Testing/               - Temperature, Health, Lifetime
```

**Each component in individual file:**
```csharp
// Components/Core/Position.cs
namespace UltraSim.Core.Components
{
    /// <summary>
    /// 3D position in world space.
    /// </summary>
    public struct Position
    {
        public float X, Y, Z;

        public Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
```

**Zero duplicates, clear ownership, easy to find.**

---

### 6. Tests & Examples Isolated âœ…

**Before:** Mixed with production code

**After:**
```
Tests/                     # Optional (#if INCLUDE_ECS_TESTS)
Examples/                  # Optional (#if INCLUDE_ECS_EXAMPLES)
```

**Conditional Compilation:**
```csharp
#if INCLUDE_ECS_TESTS
// Test code
#endif
```

**Project Settings:**
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>$(DefineConstants);INCLUDE_ECS_TESTS;INCLUDE_ECS_EXAMPLES</DefineConstants>
</PropertyGroup>
```

---

### 7. Obsolete Files Deleted âŒ

**Delete these files:**
- âŒ `RenderSystem.cs` - Obsolete (replaced by AdaptiveMultiMeshRenderSystem)
- âŒ `MultiMeshRenderSystem.cs` - Move to Examples/ (simplified version)
- âŒ `Components.cs` - Components extracted to individual files
- âŒ `ECSPaths.cs` - Absorbed into World.Paths
- âŒ `SaveSystem.cs` - Replaced by World.Persistence.cs

---

## ðŸš€ Bootstrap Pattern

### How Godot Connects to Core ECS

**The Pattern:**
```
Godot Scene
  â†“
Bootstrap Node (Godot Node)
  â†“
ServerWorld or ClientWorld (C# wrapper)
  â†“
Core.World (Pure C# ECS)
  â†“
Systems Run
```

### ServerBootstrap.cs
```csharp
using Godot;
using UltraSim.Core.ECS;

/// <summary>
/// Godot Node that kicks off the server.
/// Bridges Godot's _Process() to Core ECS.
/// </summary>
public partial class ServerBootstrap : Node
{
    private ServerWorld _server;
    
    [Export] public int Port = 7777;
    
    public override void _Ready()
    {
        GD.Print($"[Server] Starting on port {Port}...");
        
        _server = new ServerWorld();
        _server.Start(Port);
        
        GD.Print("[Server] Ready");
    }
    
    public override void _Process(double delta)
    {
        // Push Godot's frame tick to ECS
        _server?.Tick((float)delta);
    }
    
    public override void _ExitTree()
    {
        _server?.Shutdown();
    }
}
```

### ClientBootstrap.cs
```csharp
using Godot;
using UltraSim.Core.ECS;

/// <summary>
/// Godot Node that kicks off the client.
/// Bridges Godot's _Process() and _Input() to Core ECS.
/// </summary>
public partial class ClientBootstrap : Node3D
{
    private ClientWorld _client;
    
    [Export] public bool ConnectToServer = false;
    [Export] public string ServerIP = "127.0.0.1";
    [Export] public int ServerPort = 7777;
    
    public override void _Ready()
    {
        GD.Print("[Client] Starting...");
        
        _client = new ClientWorld();
        
        if (ConnectToServer)
        {
            GD.Print($"[Client] Connecting to {ServerIP}:{ServerPort}");
            _client.Connect(ServerIP, ServerPort);
        }
        
        GD.Print("[Client] Ready");
    }
    
    public override void _Process(double delta)
    {
        // Push Godot's frame tick to ECS
        _client?.Tick((float)delta);
    }
    
    public override void _Input(InputEvent @event)
    {
        // Forward input to client
        _client?.HandleInput(@event);
    }
}
```

### ServerWorld.cs (Wrapper)
```csharp
using UltraSim.Core.ECS;

namespace UltraSim.Server
{
    /// <summary>
    /// Server-side wrapper for Core.World.
    /// Adds networking and server-specific logic.
    /// </summary>
    public class ServerWorld
    {
        private World _world;  // Core ECS
        private ServerNetworking _networking;
        
        public void Start(int port)
        {
            _world = new World();
            
            // Register server-side systems
            _world.Systems.Register(new MovementSystem());
            _world.Systems.Register(new PhysicsSystem());
            _world.Systems.Register(new ServerSyncSystem(_networking));
            
            _networking = new ServerNetworking(port);
            _networking.Start();
        }
        
        public void Tick(float delta)
        {
            // 1. Process client inputs
            ProcessClientInputs();
            
            // 2. Run authoritative simulation
            _world.Tick(delta);
            
            // 3. Send state to clients
            _networking.BroadcastState(_world);
        }
        
        public void Shutdown()
        {
            _networking?.Stop();
            _world?.Save("autosave_shutdown.sav");
        }
    }
}
```

### ClientWorld.cs (Wrapper)
```csharp
using UltraSim.Core.ECS;
using Godot;

namespace UltraSim.Client
{
    /// <summary>
    /// Client-side wrapper for Core.World.
    /// Adds rendering, input, and client prediction.
    /// </summary>
    public class ClientWorld
    {
        private World _world;  // Core ECS (predicted)
        private ClientNetworking _networking;
        
        public ClientWorld()
        {
            _world = new World();
            
            // Register client-side systems
            _world.Systems.Register(new MovementSystem());  // Predicted
            _world.Systems.Register(new AdaptiveMultiMeshRenderSystem());
            _world.Systems.Register(new CameraController());
            _world.Systems.Register(new InputSystem());
        }
        
        public void Connect(string ip, int port)
        {
            _networking = new ClientNetworking(ip, port);
            _networking.OnServerState += ReconcileState;
            _networking.Connect();
        }
        
        public void Tick(float delta)
        {
            // 1. Send input to server
            _networking?.SendInput();
            
            // 2. Run predicted simulation
            _world.Tick(delta);
            
            // 3. Reconcile with server state (if received)
            _networking?.ProcessMessages();
        }
        
        public void HandleInput(InputEvent @event)
        {
            // Forward to input system
        }
        
        private void ReconcileState(ServerState state)
        {
            // Client-side prediction reconciliation
        }
    }
}
```

---

## ðŸŽ¬ Scene Configuration

### Standalone.tscn (Single-Player + LAN)
```
Root (Node3D)
â”œâ”€â”€ ServerBootstrap          # Runs local server
â”‚   â””â”€â”€ Port: 7777
â”œâ”€â”€ ClientBootstrap          # Connects to localhost
â”‚   â”œâ”€â”€ ConnectToServer: true
â”‚   â”œâ”€â”€ ServerIP: "127.0.0.1"
â”‚   â””â”€â”€ ServerPort: 7777
â”œâ”€â”€ Camera3D
â””â”€â”€ WorldEnvironment
```

**Exports to:** `Game.exe` (full game)

---

### ClientOnly.tscn (Multiplayer Client)
```
Root (Node3D)
â”œâ”€â”€ ClientBootstrap          # Client only
â”‚   â”œâ”€â”€ ConnectToServer: true
â”‚   â”œâ”€â”€ ServerIP: ""         # User enters via menu
â”‚   â””â”€â”€ ServerPort: 7777
â”œâ”€â”€ Camera3D
â””â”€â”€ UI/
    â””â”€â”€ ConnectMenu          # UI to enter server IP
```

**Exports to:** `Client.exe` (multiplayer client)

---

### ServerOnly.tscn (Dedicated Server)
```
Root (Node)                  # NO Node3D! Headless
â”œâ”€â”€ ServerBootstrap
â”‚   â””â”€â”€ Port: 7777
â””â”€â”€ ServerConsoleUI          # Minimal console output
    â”œâ”€â”€ RichTextLabel        # Shows server logs
    â””â”€â”€ LineEdit             # Console commands
```

**Exports to:** `Server.exe` (dedicated server)

---

## ðŸ“¦ Export Configuration

### Export Preset: "Standalone"
```
Name: Standalone
Main Scene: Scenes/Standalone.tscn
Export Mode: Release

Include:
  âœ… Core/**/*
  âœ… Server/**/*
  âœ… Client/**/*
  âœ… Scenes/**/*
  âœ… Assets/**/*

Exclude:
  (none)

Result: ~150 MB (code + assets)
```

### Export Preset: "Client"
```
Name: Client
Main Scene: Scenes/ClientOnly.tscn
Export Mode: Release

Include:
  âœ… Core/**/*
  âœ… Client/**/*
  âœ… Scenes/ClientOnly.tscn
  âœ… Assets/**/*

Exclude:
  âŒ Server/**/*
  âŒ Scenes/ServerOnly.tscn
  âŒ Scenes/Standalone.tscn

Result: ~150 MB (no server code)
```

### Export Preset: "Server"
```
Name: Server
Main Scene: Scenes/ServerOnly.tscn
Export Mode: Headless

Include:
  âœ… Core/**/*
  âœ… Server/**/*
  âœ… Scenes/ServerOnly.tscn
  âœ… ServerAssets/**/*

Exclude:
  âŒ Client/**/*
  âŒ Assets/**/*           â† Saves massive space!
  âŒ Scenes/ClientOnly.tscn
  âŒ Scenes/Standalone.tscn

Result: ~5 MB (no client, no assets)
```

---

## ðŸ”¢ Implementation Phases

### Phase 1: Component Extraction (2 hours)
**Priority:** CRITICAL | **Risk:** LOW | **Impact:** HIGH

**Tasks:**
1. Create `Core/Components/` folder structure
2. Extract each component to individual file
3. Delete duplicate definitions
4. Update all `using` statements
5. Test compilation

**Result:**
- Zero duplicate components
- Clear component ownership
- Easy to find any component

---

### Phase 2: Command Buffer Unification (3 hours)
**Priority:** HIGH | **Risk:** LOW | **Impact:** HIGH

**Tasks:**
1. Create unified `Core/Commands/CommandBuffer.cs`
2. Merge functionality from both old command buffers
3. Update all systems using command buffers
4. Delete `StructuralCommandBuffer.cs` and `ThreadLocalCommandBuffer.cs`
5. Test stress tests

**Result:**
- 2 files â†’ 1 file
- No duplicate entity destruction logic
- Cleaner API

---

### Phase 3: World-Integrated Save/Load (2 hours)
**Priority:** HIGH | **Risk:** LOW | **Impact:** HIGH

**Tasks:**
1. Create `Core/World/World.Persistence.cs` partial class
2. Create `Core/World/World.AutoSave.cs` partial class
3. Add `World.Paths` static nested class to `World.cs`
4. Add `Save()/Load()` methods to managers
5. Delete `SaveSystem.cs` and `ECSPaths.cs`
6. Update WorldECS to use `world.Save()`

**Result:**
- Clean API: `world.Save()`
- Natural ownership
- No fake systems

---

### Phase 4: Test & Example Isolation (1 hour)
**Priority:** MEDIUM | **Risk:** NONE | **Impact:** MEDIUM

**Tasks:**
1. Create `Tests/` and `Examples/` folders
2. Move test files
3. Move example files
4. Add `#if INCLUDE_ECS_TESTS` guards
5. Update project settings

**Result:**
- Production: 21 files
- With tests: 29 files
- Optional inclusion

---

### Phase 5: Manager Extraction (6 hours)
**Priority:** MEDIUM | **Risk:** MEDIUM | **Impact:** HIGH

**Tasks:**
1. Create `Core/Entities/EntityManager.cs`
   - Extract entity allocation from World
   - Absorb EntityBuilder as internal EntityComposer
   - Implement IEntityComposer interface
2. Create `Core/Components/ComponentManager.cs`
   - Extract component operations from World
   - Absorb ComponentTypeRegistry
   - Absorb ComponentList (make internal)
3. Create `Core/Archetypes/ArchetypeManager.cs`
   - Extract archetype management from World
4. Update `World.cs` to delegate to managers
5. Extensive testing

**Result:**
- World.cs: 565 lines â†’ 200 lines (65% reduction)
- Clear boundaries
- Easy to test

---

### Phase 6: Folder Restructure (4 hours)
**Priority:** HIGH | **Risk:** MEDIUM | **Impact:** ORGANIZATIONAL

**Tasks:**
1. Create final folder structure (Core/Server/Client/Scenes/Assets)
2. Move all files to correct locations
3. Create Bootstrap nodes
4. Create three main scenes (Standalone/ClientOnly/ServerOnly)
5. Update all file references and imports
6. Test all scenes

**Result:**
- Clean organizational structure
- Bootstrap pattern implemented
- Multi-mode support ready

---

### Phase 7: Cleanup & Polish (2 hours)
**Priority:** LOW | **Risk:** NONE | **Impact:** CLARITY

**Tasks:**
1. Delete obsolete files (RenderSystem, old tests)
2. Rename SystemManager partials for consistency
3. Update documentation
4. Final testing pass
5. Verify export configurations

**Result:**
- No dead code
- Consistent naming
- Up-to-date docs

---

## ðŸ“Š File Reduction Summary

### Before Refactor:
```
Production Files:     ~50
Test Files:           ~10
Example Files:        ~8
Documentation:        ~15
â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
Total:                ~83 files
```

### After Refactor:
```
Core:                 21 files  (down from 50)
Server:               4 files
Client:               8 files
Tests:                8 files   (optional)
Examples:             5 files   (optional)
Docs:                 10 files  (cleaned up)
â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
Production Build:     33 files  (no tests/examples)
With Tests:           41 files
With All:             46 files
```

### Key Reductions:
- âœ… World.cs: 565 lines â†’ 200 lines (65% reduction)
- âœ… Entity files: 2 â†’ 1 (EntityBuilder absorbed)
- âœ… Component files: 3 locations â†’ organized folders (0 duplicates)
- âœ… Command buffers: 2 â†’ 1 (unified)
- âœ… Paths: ECSPaths.cs â†’ World.Paths (nested class)
- âœ… Saves: SaveSystem.cs â†’ World.Persistence.cs (integrated)

---

## ðŸš¨ Critical Rules & Constraints

### Rule 1: Core/ is Pure C# (ZERO Godot Dependencies)

**NEVER do this in Core/:**
```csharp
using Godot;  // âŒ FORBIDDEN in Core/

public class World
{
    private Node _node;  // âŒ NO Godot types
    
    public void Tick(float delta)
    {
        GD.Print("test");  // âŒ NO Godot calls
    }
}
```

**Enforcement:**
```csharp
// At top of every Core/ file:
#if GODOT
#error "Core cannot depend on Godot!"
#endif
```

**Why:** Core must be portable, testable, and independent.

---

### Rule 2: Bootstrap Pattern is Sacred

**Bootstrap nodes are the ONLY connection between Godot and Core:**

```
Godot Scene
  â†“ (Bootstrap Node)
ServerBootstrap.cs OR ClientBootstrap.cs
  â†“
ServerWorld.cs OR ClientWorld.cs  (Wrapper)
  â†“
Core.World.cs  (Pure ECS)
```

**Never skip the wrapper layer!**

---

### Rule 3: Server/Client Never Cross-Reference

```csharp
// In Server/
using UltraSim.Client;  // âŒ FORBIDDEN

// In Client/
using UltraSim.Server;  // âŒ FORBIDDEN
```

**Only Core/ is shared.**

---

### Rule 4: Components are Pure Data (No Logic)

```csharp
// âœ… GOOD
public struct Position
{
    public float X, Y, Z;
}

// âŒ BAD
public struct Position
{
    public float X, Y, Z;
    
    public void Move(float dx, float dy)  // âŒ No methods!
    {
        X += dx;
        Y += dy;
    }
}
```

**Logic belongs in Systems, not Components.**

---

### Rule 5: Each Manager Owns Its Domain

```
EntityManager    â†’ Entity lifecycle (create, destroy, recycle)
ComponentManager â†’ Component types, operations, storage
ArchetypeManager â†’ Archetype instances, entityâ†’archetype mapping
SystemManager    â†’ System registration, scheduling, execution
World            â†’ Orchestration, coordination, persistence
```

**No cross-domain leakage.**

---

## âœ… Migration Checklist

### Pre-Flight (Before Starting):
- [ ] Backup current project (zip or git commit)
- [ ] Read this document completely
- [ ] Understand Bootstrap pattern
- [ ] Review final folder structure
- [ ] Prepare for 16-20 hours of work

### Phase 1: Component Extraction
- [ ] Create `Core/Components/` folder structure
- [ ] Create Core/, Movement/, Rendering/, AI/, Testing/ subfolders
- [ ] Extract Position, Velocity, RenderTag, Visible to Core/
- [ ] Extract PulseData to Movement/
- [ ] Extract CameraPosition, ChunkComponent to Rendering/
- [ ] Extract AIComponent to AI/
- [ ] Extract Temperature, Health, Lifetime to Testing/
- [ ] Delete `Components.cs`
- [ ] Remove duplicates from `ExampleSystems.cs`
- [ ] Remove duplicates from `ArchetypeStressTest.cs`
- [ ] Update all `using` statements
- [ ] Test compilation
- [ ] Run game - verify entities spawn correctly

### Phase 2: Command Buffer Unification
- [ ] Create `Core/Commands/CommandBuffer.cs`
- [ ] Copy CreateEntity logic from StructuralCommandBuffer
- [ ] Copy AddComponent/RemoveComponent/DestroyEntity from ThreadLocalCommandBuffer
- [ ] Implement unified Command enum
- [ ] Implement thread-local storage + pooling
- [ ] Update all systems using command buffers
- [ ] Delete `StructuralCommandBuffer.cs`
- [ ] Delete `ThreadLocalCommandBuffer.cs`
- [ ] Test compilation
- [ ] Run stress tests - verify no crashes

### Phase 3: World-Integrated Save/Load
- [ ] Create `Core/World/` folder
- [ ] Move `World.cs` to `Core/World/World.cs`
- [ ] Create `World.Persistence.cs` partial class
- [ ] Create `World.AutoSave.cs` partial class
- [ ] Add `World.Paths` static nested class
- [ ] Implement Save()/Load() methods
- [ ] Add Save()/Load() to EntityManager
- [ ] Add Save()/Load() to ComponentManager
- [ ] Add Save()/Load() to ArchetypeManager
- [ ] Delete `SaveSystem.cs`
- [ ] Delete `ECSPaths.cs`
- [ ] Update WorldECS to use `world.Save()`
- [ ] Test save/load functionality
- [ ] Verify settings files still work

### Phase 4: Test & Example Isolation
- [ ] Create `Tests/` folder
- [ ] Create `Examples/` folder
- [ ] Move test files to Tests/
- [ ] Move example systems to Examples/
- [ ] Add `#if INCLUDE_ECS_TESTS` to test components
- [ ] Update project settings for conditional compilation
- [ ] Test with tests enabled
- [ ] Test with tests disabled
- [ ] Verify production build excludes tests

### Phase 5: Manager Extraction
- [ ] Create `Core/Entities/EntityManager.cs`
- [ ] Extract entity allocation from World
- [ ] Absorb EntityBuilder as internal EntityComposer
- [ ] Implement IEntityComposer interface
- [ ] Delete `EntityBuilder.cs`
- [ ] Create `Core/Components/ComponentManager.cs`
- [ ] Absorb ComponentTypeRegistry
- [ ] Absorb ComponentList (make internal)
- [ ] Delete `ComponentTypeRegistry.cs`
- [ ] Delete `ComponentList.cs`
- [ ] Create `Core/Archetypes/ArchetypeManager.cs`
- [ ] Extract archetype management from World
- [ ] Update World to delegate to managers
- [ ] Test entity creation
- [ ] Test component operations
- [ ] Test archetype movement
- [ ] Run full stress tests

### Phase 6: Folder Restructure
- [ ] Create Core/, Server/, Client/ folders
- [ ] Move ECS files to Core/
- [ ] Create Server/ServerBootstrap.cs
- [ ] Create Server/ServerWorld.cs
- [ ] Create Client/ClientBootstrap.cs
- [ ] Create Client/ClientWorld.cs
- [ ] Create Scenes/ folder
- [ ] Create Standalone.tscn
- [ ] Create ClientOnly.tscn
- [ ] Create ServerOnly.tscn
- [ ] Move rendering systems to Client/
- [ ] Create Assets/ folder
- [ ] Create ServerAssets/ folder
- [ ] Update all file references
- [ ] Test Standalone scene
- [ ] Test ClientOnly scene
- [ ] Test ServerOnly scene

### Phase 7: Cleanup & Polish
- [ ] Delete RenderSystem.cs (obsolete)
- [ ] Move MultiMeshRenderSystem to Examples/
- [ ] Rename SystemManager partials for consistency
- [ ] Update README documentation
- [ ] Create export configurations
- [ ] Test Standalone export
- [ ] Test Client export
- [ ] Test Server export (headless)
- [ ] Verify file sizes (Server should be ~5MB)
- [ ] Final stress test pass
- [ ] Git commit with message "Complete architecture refactor"

---

## ðŸŽ¯ Success Criteria

### After Phase 1-4 (Quick Wins):
- âœ… Zero duplicate component definitions
- âœ… All components in organized folders
- âœ… One unified CommandBuffer
- âœ… World-integrated save/load
- âœ… Tests in separate folder
- âœ… Production build: 33 files
- âœ… `world.Save()` works
- âœ… All systems compile and run

### After Phase 5-7 (Complete):
- âœ… World.cs under 250 lines
- âœ… Clear manager boundaries
- âœ… Each file has one clear purpose
- âœ… No obsolete files
- âœ… Consistent naming
- âœ… Bootstrap pattern implemented
- âœ… Three scene modes work
- âœ… Export configurations tested
- âœ… All tests pass
- âœ… Documentation updated

---

## ðŸ“š Key References

### Inspiration Sources:
- **ACC (RunUO)** - Runtime system configurability
- **Unity DOTS** - ECS architecture patterns
- **Minecraft** - Single/multiplayer architecture
- **Valheim** - Client/server model

### Design Patterns Used:
- **Manager Pattern** - Clear domain ownership
- **Bootstrap Pattern** - Godot â†’ Core bridging
- **Partial Classes** - Logical code separation
- **Nested Classes** - World.Paths for related functionality
- **Command Pattern** - Deferred structural changes

---

## ðŸŽ‰ Final Notes

**This is a LARGE refactor** (~16-20 hours of work), but it will result in:
- 40% fewer files
- 65% smaller World.cs
- Zero duplicates
- Crystal clear architecture
- Support for single/multi/dedicated server modes
- Production-ready codebase

**The refactor is front-loaded pain for long-term gain.**

Each phase is independently valuable - you can stop after any phase and have a better codebase than before.

**Recommended approach:** Tackle Phases 1-4 first (8 hours). This gives immediate value with minimal risk.

**Good luck!** ðŸš€

---

**End of Reference Document**