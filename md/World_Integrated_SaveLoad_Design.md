# World-Integrated Save/Load Architecture

## 🎯 Core Concept

**World is the orchestrator** - it coordinates everything, including saves:
- ✅ Delegates entity operations to EntityManager
- ✅ Delegates component operations to ComponentManager
- ✅ Delegates archetype operations to ArchetypeManager
- ✅ Orchestrates system ticks via SystemManager
- ✅ **Orchestrates saves by telling managers to save themselves**

---

## 🏗️ Architecture Design

```
World (Orchestrator)
├── Tick(delta) ──────────→ Updates systems
├── Save(path) ───────────→ Tells everyone to save
│   ├── SaveWorldState() ─→ Saves World's own data
│   ├── _entities.Save() ─→ EntityManager saves itself
│   ├── _archetypes.Save() → ArchetypeManager saves itself
│   ├── _components.Save() → ComponentManager saves itself
│   └── _systems.SaveAll() → SystemManager saves all systems
│
└── Load(path) ───────────→ Tells everyone to load
    ├── LoadWorldState() ─→ Loads World's own data
    ├── _entities.Load() ─→ EntityManager loads itself
    ├── _archetypes.Load() → ArchetypeManager loads itself
    ├── _components.Load() → ComponentManager loads itself
    └── _systems.LoadAll() → SystemManager loads all systems
```

**Key insight:** Each manager knows how to save/load its OWN data. World just coordinates.

---

## 📂 File Structure Using Partial Classes

```
World.cs              ← Main orchestration (200 lines)
World.Persistence.cs  ← Save/load logic (150 lines)
World.AutoSave.cs     ← Auto-save timer logic (50 lines)
```

**Total: ~400 lines split across 3 clear, focused files**

---

## 💻 Implementation

### World.cs (Main)
```csharp
namespace UltraSim.ECS
{
    /// <summary>
    /// Core ECS World - orchestrates all ECS operations including persistence.
    /// </summary>
    public sealed partial class World
    {
        // Managers
        private readonly EntityManager _entities;
        private readonly ComponentManager _components;
        private readonly ArchetypeManager _archetypes;
        private readonly SystemManager _systems;

        // State
        public int EntityCount => _entities.Count;
        public int ArchetypeCount => _archetypes.Count;
        
        // Events
        public event Action? OnBeforeSave;
        public event Action? OnAfterSave;
        public event Action? OnBeforeLoad;
        public event Action? OnAfterLoad;

        public World()
        {
            _entities = new EntityManager(this);
            _components = new ComponentManager(this);
            _archetypes = new ArchetypeManager(this);
            _systems = new SystemManager(this);
        }

        /// <summary>
        /// Main ECS update loop.
        /// </summary>
        public void Tick(float delta)
        {
            // Update auto-save timer (in World.AutoSave.cs)
            UpdateAutoSave(delta);
            
            // Apply deferred changes
            ApplyDeferredChanges();
            
            // Update systems
            _systems.Update(delta);
            
            // Emit events
            OnFrameComplete?.Invoke();
        }

        // Delegate entity operations
        public Entity CreateEntity() => _entities.Create();
        public void DestroyEntity(Entity e) => _entities.Destroy(e);
        
        // Delegate component operations
        public void AddComponent<T>(Entity e, T component) where T : struct 
            => _components.Add(e, component);
        
        public void RemoveComponent<T>(Entity e) where T : struct 
            => _components.Remove<T>(e);
        
        // ... other delegations ...
    }
}
```

---

### World.Persistence.cs (Save/Load)
```csharp
using Godot;

namespace UltraSim.ECS
{
    public sealed partial class World
    {
        /// <summary>
        /// Saves the entire world state to disk.
        /// </summary>
        public void Save(string filename = "world.sav")
        {
            OnBeforeSave?.Invoke();
            
            string savePath = ECSPaths.GetWorldSavePath(filename);
            
            GD.Print($"[World] 💾 Saving world to: {savePath}");
            
            var config = new ConfigFile();
            
            // 1. Save World's own state
            SaveWorldState(config);
            
            // 2. Tell managers to save themselves
            _entities.Save(config, "Entities");
            _archetypes.Save(config, "Archetypes");
            _components.Save(config, "Components");
            
            // 3. Save to disk
            var error = config.Save(savePath);
            if (error == Error.Ok)
            {
                GD.Print($"[World] ✅ World saved successfully");
            }
            else
            {
                GD.PrintErr($"[World] ❌ Failed to save world: {error}");
                return;
            }
            
            // 4. Tell systems to save (settings + state)
            _systems.SaveAllSystems();
            
            OnAfterSave?.Invoke();
        }

        /// <summary>
        /// Loads the entire world state from disk.
        /// </summary>
        public void Load(string filename = "world.sav")
        {
            OnBeforeLoad?.Invoke();
            
            string loadPath = ECSPaths.GetWorldSavePath(filename);
            
            if (!FileAccess.FileExists(loadPath))
            {
                GD.PrintErr($"[World] ❌ Save file not found: {loadPath}");
                return;
            }
            
            GD.Print($"[World] 📂 Loading world from: {loadPath}");
            
            var config = new ConfigFile();
            var error = config.Load(loadPath);
            
            if (error != Error.Ok)
            {
                GD.PrintErr($"[World] ❌ Failed to load world: {error}");
                return;
            }
            
            // 1. Load World's own state
            LoadWorldState(config);
            
            // 2. Tell managers to load themselves
            _entities.Load(config, "Entities");
            _archetypes.Load(config, "Archetypes");
            _components.Load(config, "Components");
            
            // 3. Tell systems to load
            _systems.LoadAllSystems();
            
            GD.Print($"[World] ✅ World loaded successfully");
            OnAfterLoad?.Invoke();
        }

        /// <summary>
        /// Saves World-specific state (free list, versions, etc.)
        /// </summary>
        private void SaveWorldState(ConfigFile config)
        {
            config.SetValue("World", "EntityCount", EntityCount);
            config.SetValue("World", "ArchetypeCount", ArchetypeCount);
            config.SetValue("World", "SaveTimestamp", Time.GetUnixTimeFromSystem());
            
            // Auto-save settings
            config.SetValue("World", "AutoSaveEnabled", AutoSaveEnabled);
            config.SetValue("World", "AutoSaveInterval", AutoSaveInterval);
            
            GD.Print($"[World] 💾 Saved world state: {EntityCount} entities, {ArchetypeCount} archetypes");
        }

        /// <summary>
        /// Loads World-specific state.
        /// </summary>
        private void LoadWorldState(ConfigFile config)
        {
            if (!config.HasSection("World"))
            {
                GD.PrintErr("[World] ⚠️ No World section found in save file");
                return;
            }
            
            // Load auto-save settings
            AutoSaveEnabled = (bool)config.GetValue("World", "AutoSaveEnabled", false);
            AutoSaveInterval = (float)config.GetValue("World", "AutoSaveInterval", 60f);
            
            var savedEntityCount = (int)config.GetValue("World", "EntityCount", 0);
            var savedArchetypeCount = (int)config.GetValue("World", "ArchetypeCount", 0);
            var timestamp = (long)config.GetValue("World", "SaveTimestamp", 0);
            
            GD.Print($"[World] 📂 Loaded world state from {DateTimeOffset.FromUnixTimeSeconds(timestamp):yyyy-MM-dd HH:mm:ss}");
            GD.Print($"[World]    Entities: {savedEntityCount}, Archetypes: {savedArchetypeCount}");
        }

        /// <summary>
        /// Quick save to most recent save file.
        /// </summary>
        public void QuickSave() => Save("quicksave.sav");

        /// <summary>
        /// Quick load from most recent save file.
        /// </summary>
        public void QuickLoad() => Load("quicksave.sav");
    }
}
```

---

### World.AutoSave.cs (Auto-Save Timer)
```csharp
using Godot;

namespace UltraSim.ECS
{
    public sealed partial class World
    {
        // Auto-save configuration
        public bool AutoSaveEnabled { get; set; } = false;
        public float AutoSaveInterval { get; set; } = 60f; // Default: 1 minute
        
        private float _autoSaveTimer = 0f;
        private int _autoSaveCounter = 0;

        /// <summary>
        /// Updates the auto-save timer. Called every frame from Tick().
        /// </summary>
        private void UpdateAutoSave(float delta)
        {
            if (!AutoSaveEnabled)
                return;
            
            _autoSaveTimer += delta;
            
            if (_autoSaveTimer >= AutoSaveInterval)
            {
                PerformAutoSave();
                _autoSaveTimer = 0f;
            }
        }

        /// <summary>
        /// Performs an auto-save with a unique filename.
        /// </summary>
        private void PerformAutoSave()
        {
            _autoSaveCounter++;
            string filename = $"autosave_{_autoSaveCounter % 3}.sav"; // Keep 3 rotating auto-saves
            
            GD.Print($"[World] 🕐 Auto-save triggered (#{_autoSaveCounter})");
            Save(filename);
        }

        /// <summary>
        /// Enables auto-save with specified interval.
        /// </summary>
        public void EnableAutoSave(float intervalSeconds = 60f)
        {
            AutoSaveEnabled = true;
            AutoSaveInterval = intervalSeconds;
            _autoSaveTimer = 0f;
            
            GD.Print($"[World] ✅ Auto-save enabled (interval: {intervalSeconds}s)");
        }

        /// <summary>
        /// Disables auto-save.
        /// </summary>
        public void DisableAutoSave()
        {
            AutoSaveEnabled = false;
            GD.Print("[World] ⏸️ Auto-save disabled");
        }
    }
}
```

---

## 🔧 Manager Interface for Save/Load

Each manager needs these methods:

### EntityManager
```csharp
public sealed class EntityManager
{
    private Stack<int> _freeIndices;
    private List<int> _entityVersions;
    
    public void Save(ConfigFile config, string section)
    {
        // Save free indices
        config.SetValue(section, "FreeIndices", _freeIndices.ToArray());
        
        // Save entity versions
        config.SetValue(section, "EntityVersions", _entityVersions.ToArray());
        
        GD.Print($"[EntityManager] 💾 Saved: {_entityVersions.Count} entities, {_freeIndices.Count} free indices");
    }
    
    public void Load(ConfigFile config, string section)
    {
        if (!config.HasSection(section))
            return;
        
        // Load free indices
        var freeIndices = (int[])config.GetValue(section, "FreeIndices", Array.Empty<int>());
        _freeIndices = new Stack<int>(freeIndices);
        
        // Load entity versions
        var versions = (int[])config.GetValue(section, "EntityVersions", Array.Empty<int>());
        _entityVersions = new List<int>(versions);
        
        GD.Print($"[EntityManager] 📂 Loaded: {_entityVersions.Count} entities, {_freeIndices.Count} free indices");
    }
}
```

### ComponentManager
```csharp
public sealed class ComponentManager
{
    public void Save(ConfigFile config, string section)
    {
        // Save component type registry
        // Save component data per archetype
        GD.Print("[ComponentManager] 💾 Component data saved");
    }
    
    public void Load(ConfigFile config, string section)
    {
        // Load component type registry
        // Load component data per archetype
        GD.Print("[ComponentManager] 📂 Component data loaded");
    }
}
```

### ArchetypeManager
```csharp
public sealed class ArchetypeManager
{
    private List<Archetype> _archetypes;
    
    public void Save(ConfigFile config, string section)
    {
        config.SetValue(section, "ArchetypeCount", _archetypes.Count);
        
        // Save each archetype's signature and entity list
        for (int i = 0; i < _archetypes.Count; i++)
        {
            _archetypes[i].Save(config, $"{section}_Arch_{i}");
        }
        
        GD.Print($"[ArchetypeManager] 💾 Saved {_archetypes.Count} archetypes");
    }
    
    public void Load(ConfigFile config, string section)
    {
        if (!config.HasSection(section))
            return;
        
        int count = (int)config.GetValue(section, "ArchetypeCount", 0);
        
        // Load each archetype
        for (int i = 0; i < count; i++)
        {
            // Reconstruct archetype from saved data
            // (Implementation details depend on your archetype structure)
        }
        
        GD.Print($"[ArchetypeManager] 📂 Loaded {count} archetypes");
    }
}
```

---

## 🎮 Usage Examples

### Manual Save/Load
```csharp
// In your game code
world.Save("level1_complete.sav");
world.Load("level1_complete.sav");

// Quick save/load (F5/F9)
world.QuickSave();
world.QuickLoad();
```

### Auto-Save
```csharp
// Enable auto-save every 60 seconds
world.EnableAutoSave(60f);

// Change interval
world.AutoSaveInterval = 120f; // 2 minutes

// Disable
world.DisableAutoSave();
```

### With Events
```csharp
// Hook into save events
world.OnBeforeSave += () => 
{
    GD.Print("🎬 Pausing game for save...");
    GetTree().Paused = true;
};

world.OnAfterSave += () => 
{
    GD.Print("▶️ Resuming game...");
    GetTree().Paused = false;
};
```

### In WorldECS.cs
```csharp
public override void _Ready()
{
    _world = new World();
    
    // Enable auto-save
    _world.EnableAutoSave(60f);
    
    // Manual save hotkeys
}

public override void _Input(InputEvent @event)
{
    if (@event.IsActionPressed("quick_save")) // F5
        _world.QuickSave();
    
    if (@event.IsActionPressed("quick_load")) // F9
        _world.QuickLoad();
    
    if (@event.IsActionPressed("manual_save")) // F11
        _world.Save($"manual_{DateTime.Now:yyyyMMdd_HHmmss}.sav");
}
```

---

## 📊 Benefits of This Approach

### ✅ Clean Ownership
```
World owns the save lifecycle
  ↓
World tells managers to save
  ↓
Managers save their own data
  ↓
World coordinates everything
```

### ✅ Simple API
```csharp
// Before (with SaveSystem):
RunManual<SaveSystem>();  // ❌ Confusing - what does this do?

// After (World-integrated):
world.Save();             // ✅ Crystal clear
world.EnableAutoSave(60f); // ✅ Obvious behavior
```

### ✅ Encapsulation
Each manager knows how to save/load **its own data**. World doesn't need to know implementation details.

### ✅ Maintainability
Adding a new manager? Just implement `Save()` and `Load()` methods. World automatically calls them.

### ✅ Separation via Partial Classes
```
World.cs              ← Orchestration (clean)
World.Persistence.cs  ← Save/load logic (focused)
World.AutoSave.cs     ← Auto-save timer (isolated)
```
Each file has ONE clear purpose, but they're all part of World.

---

## 🎯 Comparison: SaveManager vs World-Integrated

### With Separate SaveManager:
```csharp
public class SaveManager
{
    public void SaveWorld(World world) { ... }
    public void LoadWorld(World world) { ... }
}

// Usage:
world.SaveManager.SaveWorld(world); // ❌ Awkward - passing world to its own property
```

### With World-Integrated (Your Idea):
```csharp
public partial class World
{
    public void Save() { ... }
    public void Load() { ... }
}

// Usage:
world.Save(); // ✅ Natural and clean
```

**Winner:** World-integrated is cleaner! 🏆

---

## 🚀 Migration Path

### Step 1: Create Partial Files
1. Create `World.Persistence.cs`
2. Create `World.AutoSave.cs`
3. Move save logic from current SaveSystem

### Step 2: Add Manager Interfaces
1. Add `Save()/Load()` to EntityManager
2. Add `Save()/Load()` to ComponentManager
3. Add `Save()/Load()` to ArchetypeManager

### Step 3: Update WorldECS
```csharp
// Replace:
_world.EnqueueSystemCreate(new SaveSystem());

// With:
_world.EnableAutoSave(60f);
```

### Step 4: Delete SaveSystem
1. Remove SaveSystem.cs
2. Update all references to use `world.Save()`

---

## ✅ Final Architecture

```
World (Orchestrator & Persistence Coordinator)
├── World.cs              ← Main logic + delegation
├── World.Persistence.cs  ← Save/load coordination
└── World.AutoSave.cs     ← Auto-save timer

Managers (Self-Serializing)
├── EntityManager         ← Saves own data
├── ComponentManager      ← Saves own data
├── ArchetypeManager      ← Saves own data
└── SystemManager         ← Saves all systems

Result: Clean, maintainable, obvious
```

---

## 💡 Why This Is Better

**World is already responsible for:**
- Entity lifecycle → Delegates to EntityManager
- Component operations → Delegates to ComponentManager  
- System updates → Delegates to SystemManager
- Frame timing → Tick() orchestration

**So it makes perfect sense for World to also handle:**
- Save orchestration → Tells managers to save
- Load orchestration → Tells managers to load
- Auto-save timing → Built-in timer

**This is the Coordinator pattern at its best!** ✨

---

## 🎉 Your Insight Was Spot-On

You correctly identified that:
1. World is the natural owner of save/load
2. Managers should save themselves (not SaveSystem)
3. World should "push" the save command down
4. This is cleaner than a separate SaveManager

**This is excellent architectural thinking!** 👏