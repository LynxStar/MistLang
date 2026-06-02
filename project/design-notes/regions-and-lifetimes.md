# Regions and Lifetimes

Discussion notes from 2026-06-01. Pre-design exploration — not requirements.

---

## Core Motivation

Mist currently has no mechanism for deterministic cleanup. Tasks, file handles, connections — anything that needs "finalization" — can silently leak. The structured concurrency spec's scope-exit await is tracked in the semantic model but never emitted.

The goal: a general-purpose lifetime/region system that solves Tasks, disposable resources, and memory management under one unified model.

## Key Insight: GC Is Just an Allocator

The runtime's GC is not magic — it's an allocator implementation. Mist should treat allocation and scheduling as first-class, swappable abstractions:

- `new T()` is sugar for `allocator.New<T>()` where the allocator comes from the active `with` context
- The system-provided allocator (today: .NET GC) is just the default — selectable via comptime flag
- Arenas, pools, raw malloc — all valid allocator implementations
- The compiler supplies the intrinsic "system allocator" and "system scheduler" (kernel primitives). Everything else is user code.

## The Region Hierarchy

Every allocator/scheduler forms a region. Regions are hierarchical (tree structure):

```
System (intrinsic, compiler-provided)
├── System Allocator (raw VRAM/pages)
└── System Scheduler (OS threads)
    └── Entry point runs with default allocator + scheduler
        ├── Default Allocator (GC, jemalloc, arena — comptime selectable)
        └── Default Scheduler (threadpool, etc.)
            └── User code
                ├── Child regions (nested allocators/schedulers)
                └── Values (bounded by their creating region)
```

The `with` block establishes which region is active. A region's children are bounded by the parent — the parent outlives all children.

## Two Orthogonal Concerns

### 1. Lifetime (IDroppable / linear types)

"This thing must be explicitly consumed before it goes out of scope."

- Implemented as a trait: `IDroppable` (or similar)
- Only valid on `owned` types (single alias guarantee)
- Compiler enforces: at scope exit, the value must have been dropped (consumed), moved (transferred), or returned
- For Tasks: drop = await. For files: drop = close. For locks: drop = release.
- If not explicitly consumed: compiler inserts auto-drop (scope-exit await, auto-close, etc.)

### 2. Region Boundedness

"This thing cannot be used outside the region that created it."

- A value created in region X is branded with X's identity
- Passing it to code running in a different region is a compile-time error
- Cross-region transfer requires explicit mediation (Send through parent)

## Region Branding via Dependent Types

The region identity is a *value* at the type level (dependent type), not a type parameter:

```mist
Task<int> in schedulerB   // branded with schedulerB's identity
Task<int> in schedulerC   // branded with schedulerC's identity — incompatible
```

The `with` block assigns the active region identity. Values created under that block are branded with it.

## Cross-Region Rules

### Strict rule (current thinking):

Values branded with region X can ONLY be used in region X. No up, no down, no sideways. Crossing any boundary requires explicit Send through the parent.

### Nuanced rule (under exploration):

Different region *kinds* may have different boundary permeability:

| | Passive consumption (read) across boundary | Active operations across boundary |
|---|---|---|
| Allocator (space) | ✅ Child can read parent-allocated data | ❌ Child can't allocate/free in parent |
| Scheduler (time) | ✅ Child can observe completed results | ❌ Child can't spawn/await on parent |

Principle: **data flows down (parent → child) for reading. Control stays within your own region.**

Each region type could declare:
- `provides` — what children can passively access
- `reserves` — operations that only the owning region can perform

## Cross-Region Transfer (Send)

To move a value from region B to region C (siblings), go through the parent:

```mist
parent.Send(value, from: B, to: C)
```

Transfer semantics depend on the value type:
- Value types (int, structs): copy (safe, no aliasing)
- Owned reference types: deep clone into target allocator + drop in source
- Borrowed references: forbidden (can't escape their region)

This mirrors Rust's `Send` trait / Erlang's message passing.

## Open Questions

### For Niko (week of 2026-06-02):

1. If designing lifetimes from scratch for a GC'd runtime (where memory safety is free), what would you keep vs drop from Rust's model?
2. Does the annotation burden come from lifetimes themselves, or from variance rules and generic composition?
3. Regions as the unifying abstraction for memory + concurrency — explored academically (Cyclone, MLKit) but never shipped. What killed it? Tractability? Expressiveness?
4. Implicit region inference (compiler infers region brands from `with` block context) — tractable? Or does it inevitably surface in signatures?
5. The "provides/reserves" split for region boundary permeability — is there prior art for this kind of configurable hermeticism?

### Unresolved Design Tensions:

- **Hermetic vs permeable boundaries:** Should child→parent reading be allowed by default, or opt-in per region kind?
- **Nested Tasks (Task<Task<T>>):** If auto-drop only awaits one layer, inner tasks leak. Options: recursive unwrap, flatten at type level, or prevent construction of nested Tasks.
- **Annotation burden:** Can region branding stay fully implicit (inferred from `with` context), or do function signatures need region parameters for generic/library code?
- **Closures capturing across regions:** A lambda in region C capturing a value from region B — is this the same violation as passing it to a function? (Probably yes — closures are just another form of escape.)
- **The `await` keyword in sync context:** Is it meaningful (unwraps a Task variable you captured via `async` functor) or redundant (auto-await already does it for method calls)? Current answer: meaningful for variables, redundant for direct calls. But the ergonomics are confusing.

## Relationship to Existing Mist Features

| Existing Feature | How It Relates |
|---|---|
| Ownership tiers (owned/borrowed/frozen/ref) | Access control — who can touch. Regions add: where it can go. |
| `ref struct` restrictions | Already a form of region boundedness (stack-frame region). |
| Structured context (`with` blocks) | The mechanism for establishing active regions. Needs identity tracking. |
| Live task tracking (semantic model) | Already does the analysis. Missing: emitter pass + region branding. |
| Capture restrictions (lambda) | Already prevents some cross-region escapes (ref struct, owned, borrowed). |

## North Star

Time (scheduling) and space (allocation) are the two fundamental resources. Regions unify both under one model. The compiler enforces that resources don't outlive their region and don't cross region boundaries without explicit transfer. The only "magic" is the system-level intrinsic (kernel gives us pages + threads). Everything else — GC, threadpools, arenas — is user-space code implementing the region interface.
