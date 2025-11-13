# Component Signature Dynamic Sizing - Implementation Note

**Date**: 2025-11-12
**Issue**: #3 (HIGH) - Fixed 256-byte allocation wastes 32x memory
**Priority**: HIGH
**Estimated Effort**: 2-3 hours

---

## Current Problem

**ComponentSignature.cs** allocates **256 bytes (32 ulongs)** for every signature, regardless of actual usage:

```csharp
public ComponentSignature(int maxComponentCount = 2048)
{
    _bits = new ulong[(maxComponentCount + 63) / 64];  // Always 32 ulongs = 256 bytes
    Count = 0;
}
```

**Current Usage**:
- Project has ~26 component types
- Needs only 1 ulong (8 bytes) to store signatures
- **32x memory waste per signature**

**Impact**:
- 100 archetypes × 256 bytes = 25.6 KB (current)
- 100 archetypes × 8 bytes = 0.8 KB (optimal)
- **24.8 KB waste** (96.9% wasted memory)

---

## Recommended Solution: Dynamic Sizing

### Implementation Plan

```csharp
public sealed class ComponentSignature : IEquatable<ComponentSignature>
{
    private readonly ulong[] _bits;
    public int Count { get; }

    // Option 1: Query ComponentManager for max ID (RECOMMENDED)
    public ComponentSignature()
    {
        int maxId = ComponentManager.GetHighestTypeId();
        int neededUlongs = (maxId / 64) + 1;
        _bits = new ulong[neededUlongs];
        Count = 0;
    }

    // Option 2: Explicit size (for testing/special cases)
    public ComponentSignature(int requiredCapacity)
    {
        int neededUlongs = (requiredCapacity / 64) + 1;
        _bits = new ulong[neededUlongs];
        Count = 0;
    }

    // Private constructor for cloning (existing)
    private ComponentSignature(ulong[] bits, int count)
    {
        _bits = bits;
        Count = count;
    }

    // ... rest of implementation unchanged
}
```

### Add to ComponentManager

```csharp
public static class ComponentManager
{
    private static int _highestTypeId = 0;

    public static int GetTypeId<T>()
    {
        int id = TypeId<T>.Value;
        if (id > _highestTypeId)
            _highestTypeId = id;
        return id;
    }

    public static int GetHighestTypeId() => _highestTypeId;
}
```

---

## Memory Savings

### Current Project (26 component types)
- **Before**: 256 bytes per signature
- **After**: 8 bytes per signature
- **Savings**: 248 bytes per signature (96.9%)

### Future Growth
| Component Types | Ulongs Needed | Bytes | Capacity | Headroom |
|-----------------|---------------|-------|----------|----------|
| 26 (current) | 1 | 8 | 64 types | 38 slots |
| 80 | 2 | 16 | 128 types | 48 slots |
| 200 | 4 | 32 | 256 types | 56 slots |
| 500 | 8 | 64 | 512 types | 12 slots |

Even with 500 component types, still 4x smaller than current fixed 256-byte allocation!

---

## Compatibility Considerations

### Equals() and GetHashCode()

Already handle variable-length arrays correctly:

```csharp
public bool Equals(ComponentSignature? other)
{
    if (other == null || other._bits.Length != _bits.Length)  // ✓ Length check
        return false;

    for (int i = 0; i < _bits.Length; i++)
        if (_bits[i] != other._bits[i])
            return false;

    return true;
}
```

**Edge Case**: Signatures with different capacities but same components?
- Signature A: [Position=1] → 1 ulong: `0b0000...0010`
- Signature B: [Position=1] → 2 ulongs: `0b0000...0010, 0b0000...0000`

**Solution**: These will NOT match (different lengths). This is CORRECT behavior - they should be different signatures.

**Alternative**: Normalize during comparison (more complex, not recommended).

---

## Testing Requirements

1. **Backward Compatibility Test**
   - Load existing saves (256-byte signatures)
   - Verify they still work with dynamic signatures

2. **Growth Test**
   - Register 1-100 component types sequentially
   - Verify signatures auto-expand as needed

3. **Performance Test**
   - Benchmark signature creation (should be faster)
   - Benchmark Equals/GetHashCode (should be faster - less data to hash)

4. **Memory Test**
   - Create 10,000 archetypes
   - Measure memory usage before/after
   - Verify 30x+ reduction

---

## Migration Path

### Phase 1: Add GetHighestTypeId() (Non-Breaking)
```csharp
// ComponentManager.cs
public static int GetHighestTypeId() => _highestTypeId;
```

### Phase 2: Update ComponentSignature Constructor (Breaking)
```csharp
// ComponentSignature.cs
public ComponentSignature()
{
    int maxId = ComponentManager.GetHighestTypeId();
    int neededUlongs = Math.Max(1, (maxId / 64) + 1);  // At least 1 ulong
    _bits = new ulong[neededUlongs];
}
```

### Phase 3: Remove Old Constructor (Cleanup)
```csharp
// Delete:
public ComponentSignature(int maxComponentCount = 2048) { ... }
```

---

## Alternative: Conservative Fixed Size (Simpler)

If dynamic sizing is too complex for now:

```csharp
public ComponentSignature(int maxComponentCount = 128)  // Down from 2048
{
    _bits = new ulong[(maxComponentCount + 63) / 64];  // 2 ulongs = 16 bytes
}
```

**Result**:
- Supports 128 component types (5x current usage)
- 16 bytes per signature (instead of 256)
- **16x memory reduction** with NO dynamic logic

---

## Decision

**Recommended**: Dynamic sizing (Option 1)
- Best memory efficiency
- Scales automatically
- Clean implementation

**Fallback**: Conservative fixed 128 (Option 2)
- Simpler implementation
- Still 16x improvement
- Good enough for most projects

---

## Action Items

- [ ] Implement ComponentManager.GetHighestTypeId()
- [ ] Update ComponentSignature constructor
- [ ] Add unit tests (backward compat, growth, performance)
- [ ] Update CLAUDE.md with new pattern
- [ ] Benchmark memory usage (before/after)
- [ ] Update audit to reflect fix

**Priority**: HIGH (not critical, but easy win)
**Effort**: 2-3 hours
**Benefit**: 16-32x memory reduction
