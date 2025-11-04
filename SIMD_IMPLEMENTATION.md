# SIMD Implementation Plan

## Architecture Overview

### Delegate-Based Design (Zero-Cost Abstraction)
- Function pointers assigned once at startup or when showcase mode changes
- No runtime switch statements in hot paths
- Categories: **Core ECS** vs **Systems**

---

## Files to SIMD-ify

### **HIGH PRIORITY - Core ECS Operations** (Category: Core)

#### 1. **Movement Operations**
**File:** `Server/ECS/Systems/OptimizedMovementSystem.cs`
- **Line 112-117**: Position += Velocity * delta (scalar loop)
- **Target**: Lines 112-117 in `Update()` method
- **Operations**:
  - 3 float multiplies per entity (vel * delta)
  - 3 float adds per entity (pos += result)
- **SIMD Benefit**: Process 4 entities (SSE), 8 entities (AVX2), or 16 entities (AVX-512) per iteration
- **Estimated Speedup**: 2-4x (SSE), 4-8x (AVX2), 8-16x (AVX-512)

#### 2. **Pulsing Movement**
**File:** `Server/ECS/Systems/OptimizedPulsingMovementSystem.cs`
- **Operations**: Sin/Cos calculations, position updates
- **SIMD Benefit**: Vectorized sin/cos via lookup tables, parallel position math
- **Estimated Speedup**: 3-6x (lookup table + SIMD)

#### 3. **Utilities Math Functions**
**File:** `UltraSim/ECS/Utilities.cs`
- **FastSin/FastCos** (lines 115-147): Already optimized with lookup tables
  - Can SIMD-ify for batch operations (process 4/8/16 angles at once)
- **RandomPointInSphere** (lines 83-106): SIMD-ify trig and vector math
- **Estimated Speedup**: 2-4x for batch operations

---

### **MEDIUM PRIORITY - Systems** (Category: Systems)

#### 4. **Entity Spawner**
**File:** `Server/ECS/Systems/EntitySpawnerSystem.cs`
- **Operations**: Batch entity creation with random positions/velocities
- **SIMD Benefit**: Vectorized RNG, parallel position/velocity initialization
- **Estimated Speedup**: 2-3x

#### 5. **Rendering System**
**File:** `Client/ECS/Systems/AdaptiveMultiMeshRenderSystem.cs`
- **Operations**: Transform matrix calculations, position updates to GPU
- **SIMD Benefit**: Vectorized matrix math, batch position copies
- **Estimated Speedup**: 2-4x

---

### **LOW PRIORITY - Archetype Core** (Category: Core)

#### 6. **Archetype Operations**
**File:** `UltraSim/ECS/Archetype.cs`
- **Potential**: Component array copies during archetype transitions
- **SIMD Benefit**: Vectorized memcpy for bulk component moves
- **Note**: Already pretty fast, defer until after high-priority items

---

## Implementation Phases

### **Phase 1: Infrastructure** (Current)
- ✅ Create `SimdMode` enum
- ✅ Create `SimdManager` with hardware detection
- ✅ Create `SimdShowcasePanel` for benchmarking
- ⏳ Create `SimdOperations` delegate registry

### **Phase 2: Core Movement (Highest Impact)**
- Implement `ApplyVelocity` SIMD variants (Scalar, SSE, AVX2, AVX-512)
- Integrate into `OptimizedMovementSystem`
- Benchmark: Measure performance at 1M, 5M, 9M entities

### **Phase 3: Pulsing Movement**
- Implement `ApplyPulse` SIMD variants
- Integrate into `OptimizedPulsingMovementSystem`
- Benchmark: Measure sin/cos performance gains

### **Phase 4: Utilities Batch Operations**
- Create `BatchFastSin`, `BatchFastCos` SIMD variants
- Create `BatchRandomPointInSphere` SIMD variant
- Use in spawner system

### **Phase 5: Systems (Spawner, Rendering)**
- SIMD-ify spawner position/velocity initialization
- SIMD-ify rendering transform updates
- Benchmark: Measure spawn time for 100k entities

---

## SIMD Operation Registry Design

```csharp
// UltraSim/ECS/SIMD/SimdOperations.cs

namespace UltraSim.ECS.SIMD
{
    /// <summary>
    /// Central registry for SIMD-optimized operations.
    /// Delegates are assigned once at startup based on hardware capability.
    /// </summary>
    public static class SimdOperations
    {
        // === CORE ECS OPERATIONS ===

        // Movement: Position += Velocity * delta
        public delegate void ApplyVelocityDelegate(Span<Position> positions, Span<Velocity> velocities, float delta);
        public static ApplyVelocityDelegate ApplyVelocity { get; private set; } = null!;

        // Pulsing: Position += direction * sin(phase) * amplitude
        public delegate void ApplyPulseDelegate(Span<Position> positions, Span<PulseData> pulseData, float delta);
        public static ApplyPulseDelegate ApplyPulse { get; private set; } = null!;

        // === SYSTEM OPERATIONS ===

        // Batch sin calculation (for spawner, pulsing, etc.)
        public delegate void BatchSinDelegate(ReadOnlySpan<float> input, Span<float> output);
        public static BatchSinDelegate BatchSin { get; private set; } = null!;

        // Batch random point generation
        public delegate void BatchRandomPointsDelegate(Span<Vector3> output, float radius);
        public static BatchRandomPointsDelegate BatchRandomPoints { get; private set; } = null!;

        /// <summary>
        /// Initialize Core ECS SIMD operations based on selected mode.
        /// Called once at startup and when showcase mode changes.
        /// </summary>
        public static void InitializeCore(SimdMode mode)
        {
            ApplyVelocity = mode switch
            {
                SimdMode.Simd512 => Core.ApplyVelocity_AVX512,
                SimdMode.Simd256 => Core.ApplyVelocity_AVX2,
                SimdMode.Simd128 => Core.ApplyVelocity_SSE,
                _ => Core.ApplyVelocity_Scalar
            };

            ApplyPulse = mode switch
            {
                SimdMode.Simd512 => Core.ApplyPulse_AVX512,
                SimdMode.Simd256 => Core.ApplyPulse_AVX2,
                SimdMode.Simd128 => Core.ApplyPulse_SSE,
                _ => Core.ApplyPulse_Scalar
            };
        }

        /// <summary>
        /// Initialize Systems SIMD operations based on selected mode.
        /// Called once at startup and when showcase mode changes.
        /// </summary>
        public static void InitializeSystems(SimdMode mode)
        {
            BatchSin = mode switch
            {
                SimdMode.Simd512 => Systems.BatchSin_AVX512,
                SimdMode.Simd256 => Systems.BatchSin_AVX2,
                SimdMode.Simd128 => Systems.BatchSin_SSE,
                _ => Systems.BatchSin_Scalar
            };

            BatchRandomPoints = mode switch
            {
                SimdMode.Simd512 => Systems.BatchRandomPoints_AVX512,
                SimdMode.Simd256 => Systems.BatchRandomPoints_AVX2,
                SimdMode.Simd128 => Systems.BatchRandomPoints_SSE,
                _ => Systems.BatchRandomPoints_Scalar
            };
        }
    }
}
```

---

## Example Implementation: ApplyVelocity

```csharp
// UltraSim/ECS/SIMD/Core/ApplyVelocity.cs

using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using UltraSim.ECS.Components;

namespace UltraSim.ECS.SIMD.Core
{
    public static partial class CoreOperations
    {
        /// <summary>
        /// Scalar implementation: Process 1 entity per iteration.
        /// </summary>
        public static void ApplyVelocity_Scalar(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }

        /// <summary>
        /// SSE implementation: Process 4 floats per iteration.
        /// Layout: [X0, Y0, Z0, X1, Y1, Z1, X2, Y2, Z2, X3, Y3, Z3]
        /// </summary>
        public static void ApplyVelocity_SSE(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            if (!Sse.IsSupported)
            {
                ApplyVelocity_Scalar(positions, velocities, delta);
                return;
            }

            int i = 0;
            int simdLength = positions.Length - (positions.Length % 4);
            Vector128<float> vDelta = Vector128.Create(delta);

            // Process 4 entities at a time (12 floats)
            for (; i < simdLength; i += 4)
            {
                // Load positions (4 entities = 12 floats, but we process in 3 batches of 4)
                // X components: [X0, X1, X2, X3]
                // Y components: [Y0, Y1, Y2, Y3]
                // Z components: [Z0, Z1, Z2, Z3]

                // Note: This assumes Position is tightly packed (X, Y, Z sequential)
                // We'll need to use unsafe pointers or restructure to AoS -> SoA conversion

                // SIMPLIFIED VERSION: Process 1 component at a time
                Vector128<float> vPosX = Vector128.Create(
                    positions[i].X, positions[i+1].X, positions[i+2].X, positions[i+3].X);
                Vector128<float> vVelX = Vector128.Create(
                    velocities[i].X, velocities[i+1].X, velocities[i+2].X, velocities[i+3].X);

                Vector128<float> vPosY = Vector128.Create(
                    positions[i].Y, positions[i+1].Y, positions[i+2].Y, positions[i+3].Y);
                Vector128<float> vVelY = Vector128.Create(
                    velocities[i].Y, velocities[i+1].Y, velocities[i+2].Y, velocities[i+3].Y);

                Vector128<float> vPosZ = Vector128.Create(
                    positions[i].Z, positions[i+1].Z, positions[i+2].Z, positions[i+3].Z);
                Vector128<float> vVelZ = Vector128.Create(
                    velocities[i].Z, velocities[i+1].Z, velocities[i+2].Z, velocities[i+3].Z);

                // Compute: pos += vel * delta
                vPosX = Sse.Add(vPosX, Sse.Multiply(vVelX, vDelta));
                vPosY = Sse.Add(vPosY, Sse.Multiply(vVelY, vDelta));
                vPosZ = Sse.Add(vPosZ, Sse.Multiply(vVelZ, vDelta));

                // Store back (extract each component)
                positions[i].X = vPosX[0]; positions[i+1].X = vPosX[1];
                positions[i+2].X = vPosX[2]; positions[i+3].X = vPosX[3];

                positions[i].Y = vPosY[0]; positions[i+1].Y = vPosY[1];
                positions[i+2].Y = vPosY[2]; positions[i+3].Y = vPosY[3];

                positions[i].Z = vPosZ[0]; positions[i+1].Z = vPosZ[1];
                positions[i+2].Z = vPosZ[2]; positions[i+3].Z = vPosZ[3];
            }

            // Process remaining entities with scalar code
            for (; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X * delta;
                positions[i].Y += velocities[i].Y * delta;
                positions[i].Z += velocities[i].Z * delta;
            }
        }

        /// <summary>
        /// AVX2 implementation: Process 8 floats per iteration.
        /// </summary>
        public static void ApplyVelocity_AVX2(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            if (!Avx2.IsSupported)
            {
                ApplyVelocity_SSE(positions, velocities, delta);
                return;
            }

            int i = 0;
            int simdLength = positions.Length - (positions.Length % 8);
            Vector256<float> vDelta = Vector256.Create(delta);

            // Process 8 entities at a time
            for (; i < simdLength; i += 8)
            {
                Vector256<float> vPosX = Vector256.Create(
                    positions[i].X, positions[i+1].X, positions[i+2].X, positions[i+3].X,
                    positions[i+4].X, positions[i+5].X, positions[i+6].X, positions[i+7].X);
                Vector256<float> vVelX = Vector256.Create(
                    velocities[i].X, velocities[i+1].X, velocities[i+2].X, velocities[i+3].X,
                    velocities[i+4].X, velocities[i+5].X, velocities[i+6].X, velocities[i+7].X);

                // Same for Y and Z...

                vPosX = Avx.Add(vPosX, Avx.Multiply(vVelX, vDelta));
                // Store back...
            }

            // Remainder with SSE or scalar
            ApplyVelocity_SSE(positions.Slice(i), velocities.Slice(i), delta);
        }

        /// <summary>
        /// AVX-512 implementation: Process 16 floats per iteration.
        /// </summary>
        public static void ApplyVelocity_AVX512(Span<Position> positions, Span<Velocity> velocities, float delta)
        {
            if (!Avx512F.IsSupported)
            {
                ApplyVelocity_AVX2(positions, velocities, delta);
                return;
            }

            // Similar to AVX2 but with Vector512<float> and 16-wide operations
            // Fallback to AVX2 for remainder
            ApplyVelocity_AVX2(positions, velocities, delta);
        }
    }
}
```

---

## Integration Points

### 1. **SimdManager Hook**
When showcase mode changes, reinitialize delegates:

```csharp
// In SimdManager.cs
public static bool ShowcaseEnabled
{
    set
    {
        _showcaseEnabled = value;
        if (!_showcaseEnabled)
        {
            _coreMode = ConvertToSimdMode(_maxHardwareSupport);
            _systemsMode = ConvertToSimdMode(_maxHardwareSupport);
        }
        else
        {
            _coreMode = SimdMode.Scalar;
            _systemsMode = SimdMode.Scalar;
        }

        // REINITIALIZE DELEGATES
        SimdOperations.InitializeCore(_coreMode);
        SimdOperations.InitializeSystems(_systemsMode);
    }
}

public static bool SetMode(SimdCategory category, SimdMode mode)
{
    // ... existing code ...

    // REINITIALIZE DELEGATES
    if (category == SimdCategory.Core)
        SimdOperations.InitializeCore(mode);
    else
        SimdOperations.InitializeSystems(mode);

    return true;
}
```

### 2. **System Usage**
Replace scalar loop with delegate call:

```csharp
// In OptimizedMovementSystem.cs line 112-117
// BEFORE:
for (int i = start; i < end; i++)
{
    posSpan[i].X += velSpan[i].X * adjustedDelta;
    posSpan[i].Y += velSpan[i].Y * adjustedDelta;
    posSpan[i].Z += velSpan[i].Z * adjustedDelta;
}

// AFTER:
SimdOperations.ApplyVelocity(
    posSpan.Slice(start, end - start),
    velSpan.Slice(start, end - start),
    adjustedDelta
);
```

---

## Expected Performance Gains

| Operation | Scalar (1M entities) | SSE (128-bit) | AVX2 (256-bit) | AVX-512 (512-bit) |
|-----------|---------------------|---------------|----------------|-------------------|
| **Movement** | 8-12ms | 3-5ms (2-3x) | 1.5-3ms (4-6x) | 0.8-1.5ms (8-12x) |
| **Pulsing** | 15-20ms | 5-8ms (3x) | 2.5-5ms (6x) | 1.2-2.5ms (10x) |
| **Spawner (100k)** | 50-80ms | 25-40ms (2x) | 12-20ms (4x) | 6-10ms (8x) |

---

## Next Steps

1. **Create `SimdOperations.cs`** - Delegate registry
2. **Implement `ApplyVelocity` variants** - Scalar, SSE, AVX2, AVX-512
3. **Integrate into `OptimizedMovementSystem`** - Replace scalar loop
4. **Benchmark at 1M entities** - Measure actual gains
5. **Hook SimdManager to reinitialize delegates** - When showcase mode changes
6. **Repeat for other operations** - Pulsing, spawner, utilities

---

## Questions / Decisions

1. **SoA vs AoS Layout**: Current Position/Velocity are AoS (struct with X, Y, Z). SIMD works best with SoA (separate X[], Y[], Z[] arrays). Should we convert, or use gather/scatter intrinsics?
   - **Recommendation**: Keep AoS for now, use manual gather/scatter in SIMD code. Converting to SoA is a massive refactor.

2. **Unsafe Code**: Direct pointer manipulation would be faster than `Vector256.Create()` with 8 arguments. Enable unsafe code?
   - **Recommendation**: Start safe, optimize to unsafe later if needed.

3. **Testing Strategy**: How to validate SIMD correctness?
   - **Recommendation**: Unit tests comparing SIMD vs scalar results (within floating-point epsilon).

---

## File Summary

### Files to Create:
1. `UltraSim/ECS/SIMD/SimdOperations.cs` - Delegate registry
2. `UltraSim/ECS/SIMD/Core/ApplyVelocity.cs` - Movement SIMD variants
3. `UltraSim/ECS/SIMD/Core/ApplyPulse.cs` - Pulsing SIMD variants
4. `UltraSim/ECS/SIMD/Systems/BatchMath.cs` - Batch sin/cos/random

### Files to Modify:
1. `Server/ECS/Systems/OptimizedMovementSystem.cs` - Use `SimdOperations.ApplyVelocity`
2. `Server/ECS/Systems/OptimizedPulsingMovementSystem.cs` - Use `SimdOperations.ApplyPulse`
3. `Server/ECS/Systems/EntitySpawnerSystem.cs` - Use batch operations
4. `UltraSim/ECS/SIMD/SimdManager.cs` - Hook delegate reinitialization
5. `Server/ECS/WorldECS.cs` - Initialize `SimdOperations` at startup

---

**Total Estimated Implementation Time**: 6-8 hours (Phase 2 alone ~2-3 hours)
**Expected Overall Speedup (1M entities)**: 2-4x on AVX2, 4-8x on AVX-512
