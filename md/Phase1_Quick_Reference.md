# Phase 1 Quick Reference Card

## 📂 Component Location Guide

| Component | Location | Purpose |
|-----------|----------|---------|
| **Position** | `Components/Core/Position.cs` | 3D world position |
| **Velocity** | `Components/Core/Velocity.cs` | 3D velocity vector |
| **RenderTag** | `Components/Core/RenderTag.cs` | Marks entity as renderable |
| **Visible** | `Components/Core/Visible.cs` | Marks entity as visible |
| **PulseData** | `Components/Movement/PulseData.cs` | Pulsing movement data |
| **CameraPosition** | `Components/Rendering/CameraPosition.cs` | Camera tracking |
| **ChunkComponent** | `Components/Rendering/ChunkComponent.cs` | Spatial chunk + LOD |
| **AIComponent** | `Components/AI/AIComponent.cs` | AI state + behavior |
| **Temperature** | `Components/Testing/Temperature.cs` | Test component |
| **Health** | `Components/Testing/Health.cs` | Test component |
| **Lifetime** | `Components/Testing/Lifetime.cs` | Test component |

---

## 🎯 Common Tasks

### Add New Component
```csharp
// 1. Create file: Components/[Category]/NewComponent.cs

namespace UltraSim.ECS.Components
{
    public struct NewComponent
    {
        public float Value;
    }
}

// 2. Use it anywhere:
using UltraSim.ECS.Components;

structuralBuffer.AddComponent(entity, new NewComponent { Value = 42f });
```

### Use Existing Component
```csharp
using UltraSim.ECS.Components;  // Add this import

var positions = archetype.GetComponentSpan<Position>(posId);
positions[i].X += velocities[i].X * deltaTime;
```

### Create Test Component
```csharp
// Use #if directive for test components

#if INCLUDE_ECS_TESTS
namespace UltraSim.ECS.Components
{
    public struct TestComponent { /* ... */ }
}
#endif
```

---

## 🔍 Where Things Are

### Core Components (Essential)
```
Components/Core/
├── Position.cs       ← XYZ world position
├── Velocity.cs       ← XYZ velocity vector
├── RenderTag.cs      ← Render flag
└── Visible.cs        ← Visibility flag
```

### Movement Components
```
Components/Movement/
└── PulseData.cs      ← Pulsing behavior (Speed, Frequency, Phase)
```

### Rendering Components
```
Components/Rendering/
├── CameraPosition.cs ← Camera XYZ
└── ChunkComponent.cs ← Chunk LOD & state
```

### AI Components
```
Components/AI/
└── AIComponent.cs    ← AI state (IsActive, TimeSinceLastDecision, CurrentState)
                      ← AIState enum (Idle, Moving, Attacking, Fleeing)
```

### Testing Components (Conditional)
```
Components/Testing/
├── Temperature.cs    ← Temperature value
├── Health.cs         ← Health points
└── Lifetime.cs       ← Remaining seconds
```

---

## ⚡ Fast Lookup

**Need Position?** → `Components/Core/Position.cs`  
**Need AI?** → `Components/AI/AIComponent.cs`  
**Need Camera?** → `Components/Rendering/CameraPosition.cs`  
**Need Test component?** → `Components/Testing/[Name].cs`

---

## 🛠️ Troubleshooting

### Error: "Position not found"
**Fix:** Add `using UltraSim.ECS.Components;`

### Error: "Duplicate type"
**Fix:** Remove old definition (check ExampleSystems.cs, ArchetypeStressTest.cs)

### Test components not compiling
**Fix:** Add `#define INCLUDE_ECS_TESTS` to project or wrap in `#if INCLUDE_ECS_TESTS`

---

## ✅ Validation Quick Check

```bash
# Should show 11 files
find Components -name "*.cs" | wc -l

# Should return empty (no duplicates)
grep -r "public struct Position" --exclude-dir=Components *.cs

# Should list ~15 files
grep -l "using UltraSim.ECS.Components;" *.cs | wc -l
```

---

## 📊 Stats

- **Total Components:** 11
- **Categories:** 5 (Core, Movement, Rendering, AI, Testing)
- **Duplicates:** 0 (eliminated 5)
- **Files Updated:** 15
- **Breaking Changes:** 0

---

## 🎓 Key Principles

1. **One component = One file** (no more Component.cs megafile)
2. **Use categories** (Core, Movement, Rendering, AI, Testing)
3. **Single namespace** (`UltraSim.ECS.Components` for all)
4. **Test protection** (`#if INCLUDE_ECS_TESTS` for test components)
5. **Import once** (`using UltraSim.ECS.Components;` everywhere)

---

## 🚀 What's Next

After Phase 1, proceed with:
- **Phase 2:** Merge Command Buffers (3 hours)
- **Phase 3:** SaveSystem → SaveManager (2 hours)
- **Phase 4:** Isolate Tests (1 hour)

---

**Phase 1: COMPLETE** ✅  
**All components organized and accessible!**
