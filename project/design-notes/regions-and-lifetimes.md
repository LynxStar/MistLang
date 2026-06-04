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

### 1. Lifetime (IDroppable — linear-with-auto-drop)

"This thing must be explicitly consumed before it goes out of scope."

- Implemented as an interface: `IDroppable` (or similar)
- Types must explicitly implement the interface — no duck typing
- Independent of regions — IDroppable works the same inside or outside a `with` block
- Compiler enforces: at scope exit, the value must have been dropped (consumed), moved (transferred), or returned
- For Tasks: drop = await. For files: drop = close. For locks: drop = release.
- If not explicitly consumed: compiler inserts auto-drop (scope-exit await, auto-close, etc.)

Note on type discipline: Rust's ownership is *affine* (use at most once, silent drop is permitted — `mem::forget` is safe). Mist's `IDroppable` is *linear* in intent (must be consumed) but ergonomically relaxed via compiler-inserted auto-drop. The key difference: in Rust, dropping is invisible and optional (the compiler silently deallocates, and `forget` is a valid choice). In Mist, drop is always explicit in the emitted code — the compiler just saves you from writing it by hand. There is no `forget` escape hatch.

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

---

## Region Kind Declarations

~~Superseded by the revised model below (2026-06-02 evening). Kept for historical context.~~

---

## Revised Model: `region` as Its Own Kind (2026-06-02)

### Core Realization: Drop ≠ Region

Drop semantics and region semantics are *independent*:

- **Drop** is a type-level concern. A type implements `IDroppable` and defines its own cleanup logic. A `Task` can hold a reference to its scheduler and notify it during drop — no region magic needed. This is just interface dispatch.
- **Regions** are about *boundary enforcement* — knowing what entered, detecting when something tries to leave, and bulk discard when the region itself ends.

These two things *interact* (a region might invoke `IDroppable.drop()` on its registered values during bulk discard), but they're not the same mechanism.

### `region` Is a Type Kind

`region` is a declaration keyword at the same level as `class` and `struct`. It declares a new type that is:

- Semantically like a class: has fields, methods, constructors, state, can implement interfaces
- But with compiler-enforced special behavior when placed into a structured `with` context

```mist
region Arena
{
    private byte* _buffer;
    private int _offset;

    pub Arena(Size capacity)
    {
        _buffer = sys_alloc(capacity);
        _offset = 0;
    }

    // Normal methods — Arena is just a type with state
    pub T* alloc<T>() { ... }
    pub void reset() { _offset = 0; }
}
```

Outside of a `with` block, a region is just an object. You can store it in a variable, pass it around, call methods on it. The special behavior only activates when it's placed into a structured context.

### The Three Special Behaviors (Compiler-Enforced)

When a `region` instance is activated via `with`, three things become special:

#### 1. Enter (Registration)

When a value matching the region's declared filter is *created* within the `with` scope, the region is notified and the value becomes *associated* with that region instance.

```mist
region Arena
{
    governs<T> where T : any;

    enter(T item)
    {
        // called by compiler when a governed value is created in this region's context
        self.register(item);
    }
}
```

#### 2. Send (Traversal Detection)

When a region-associated value attempts to cross the region boundary (escape the `with` scope), the compiler checks whether this is allowed. If the region defines a `send` policy, it's evaluated:

```mist
region Arena
{
    send(T item, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .ancestor => .deny,          // can't escape upward
            .child => .allow_borrow,     // children can borrow
            .sibling => .deny,           // can't move sideways
        }
    }
}
```

If no `send` is defined, the default is `deny` — nothing escapes.

#### 3. Discard (Region Itself Drops)

When the `with` block ends and the region falls out of scope (or the region's own `IDroppable.drop()` runs), all values still registered with it are bulk-discarded. The region defines how:

```mist
region Arena : IDroppable
{
    drop()
    {
        // bulk discard — free the whole buffer, everything in it is dead
        sys_free(_buffer);
    }
}
```

For regions that need per-item cleanup before bulk discard, they can iterate their tracked values:

```mist
region ConnectionPool : IDroppable
{
    private List<IPooledConnection> _active;

    drop()
    {
        for conn in _active
        {
            conn.close();   // per-item cleanup (conn itself implements IDroppable)
        }
        _active.clear();
    }
}
```

Notice: this is just normal `IDroppable` implementation. The region *is* a droppable type. Nothing special about the drop mechanism — it's the same interface any type uses.

### What Makes `region` Different From `class`

| Aspect | `class` | `region` |
|---|---|---|
| Has fields, methods, state | ✅ | ✅ |
| Can implement interfaces | ✅ | ✅ |
| Can be instantiated | ✅ | ✅ |
| Can be placed in `with` | ✅ (if IDisposable) | ✅ (primary use case) |
| `governs<T>` clause | ❌ | ✅ — declares which types the region tracks |
| `enter` hook | ❌ | ✅ — compiler calls on creation of governed types |
| `exit` hook | ❌ | ✅ — compiler calls when governed value leaves scope |
| `send` policy | ❌ | ✅ — compiler checks on traversal of governed values |
| Compiler-inserted association | ❌ | ✅ — values created in scope are branded |

The `governs`, `enter`, and `send` declarations are what make a `region` a region. Everything else is just a normal type.

### The `governs` Clause

The `governs` clause tells the compiler which types this region interacts with. Only values matching this constraint get registered on creation, notified on exit, and checked on traversal:

```mist
region Scheduler
{
    governs<T> where T : ITask;

    enter(T task) => self.enqueue(task);

    exit(T task) => self.dequeue(task);

    send(T task, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .ancestor => .allow,    // completed results can flow up
            _ => .deny,
        }
    }
}
```

```mist
region Arena
{
    governs<T> where T : any;   // everything created in scope is governed

    enter(T item) => self.track_allocation(item);

    exit(T item) => { }   // no-op — bulk-free handles everything

    send(T item, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .child => .allow_borrow,
            _ => .deny,
        }
    }
}
```

A region without a `governs` clause is just a class that happens to be used in `with` — no automatic registration or traversal checking.

### Three Kinds of Drop (2026-06-03 revision)

There are three distinct events related to value/region cleanup. They are independent and compose:

#### 1. Self-Managed Drop (value implements IDroppable)

The value has internal state that *it* needs to reconcile before it ceases to exist. The value is the actor. It does not need to know about any region.

```mist
struct Task<T> : IDroppable
{
    private TaskState _state;

    drop()
    {
        if _state == .running
        {
            self.await_completion();   // Task's own cleanup — no region reference needed
        }
    }
}

struct FileHandle : IDroppable
{
    drop() => self.close();
}
```

#### 2. Region-Exit (region is notified a governed value is leaving)

When a governed value dies or escapes, the *region* is notified via an `exit` hook. The region is the actor, not the value. The value can be completely passive.

```mist
region Scheduler
{
    governs<T> where T : ITask;

    exit(T task)
    {
        // Region learns a task is dying — dequeue it, record completion, etc.
        self.dequeue_or_record(task);
    }
}

region Arena
{
    governs<T> where T : any;

    exit(T item)
    {
        // Mark the slot reclaimable (or do nothing — bulk-free handles it)
    }
}
```

The value doesn't hold a reference to its region. The region already knows about the value (registered during `enter`). The compiler inserts the `exit` call when a governed value leaves scope.

#### 3. Region Bulk-Drop (region itself implements IDroppable)

When the region's own `with` block ends, the region's `IDroppable.drop()` runs. This is "tearing down the whole context":

```mist
impl IDroppable for Arena
{
    drop()
    {
        sys_free(_buffer);   // bulk-free everything — no per-item work needed
    }
}

impl IDroppable for Scheduler
{
    drop()
    {
        // Await all outstanding tasks before the scheduler goes away
        for task in _running { task.await_completion(); }
        for task in _pending { task.cancel(); }
    }
}
```

#### Ordering

When a governed value leaves scope:

1. **Self-managed drop** fires first (if the value implements `IDroppable`) — the value cleans itself up
2. **Region-exit** fires second — the region is informed the value is gone

When the region's `with` block ends:

1. For each remaining governed value: self-managed drop, then region-exit (same order as above)
2. **Region bulk-drop** fires last — the region tears itself down

#### Composition

A value can have any combination:

| Value type | Self-managed drop? | Region-exit? | Example |
|---|---|---|---|
| `Task<T>` in a scheduler | ✅ (awaits itself) | ✅ (scheduler dequeues it) | Both fire |
| `int` in an arena | ❌ | ✅ (arena marks slot free) | Only region-exit |
| `FileHandle` outside any region | ✅ (closes file) | ❌ | Only self-managed |
| `int` outside any region | ❌ | ❌ | Nothing happens |

~~The "reference back" pattern (value holds a reference to its region) is superseded by region-exit. The value never needs to know about its region — the compiler handles the notification.~~

### Full Example: Arena + Scheduler

```mist
region Arena
{
    governs<T> where T : any;
    
    private byte* _buffer;
    private int _offset;
    private List<IDroppable> _droppables;   // track items that need individual cleanup

    pub Arena(Size capacity)
    {
        _buffer = sys_alloc(capacity);
        _offset = 0;
        _droppables = new List<IDroppable>();
    }

    enter(T item)
    {
        // allocate from our buffer
        let ptr = _buffer + _offset;
        _offset += size_of(T);
        
        if item is IDroppable droppable
        {
            _droppables.add(droppable);
        }
    }

    exit(T item)
    {
        // Region-exit: item is dying — mark slot reclaimable
        // (or do nothing if we only bulk-free)
    }

    send(T item, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .child => .allow_borrow,
            _ => .deny,
        }
    }
}

// Arena implements IDroppable — its own cleanup (region bulk-drop)
impl IDroppable for Arena
{
    drop()
    {
        // Items that have their own cleanup needs get dropped first
        for item in _droppables.reversed()
        {
            item.drop();
        }
        // Then bulk-free the memory
        sys_free(_buffer);
    }
}
```

```mist
region ThreadPoolScheduler
{
    governs<T> where T : ITask;

    private Queue<ITask> _pending;
    private List<ITask> _running;

    enter(T task)
    {
        _pending.enqueue(task);
        self.maybe_dispatch();
    }

    exit(T task)
    {
        // Region-exit: task is dying or leaving scope — dequeue/record it
        _pending.remove(task);
        _running.remove(task);
    }

    send(T task, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .ancestor => if task.is_completed => .allow else .deny,
            _ => .deny,
        }
    }
}

impl IDroppable for ThreadPoolScheduler
{
    drop()
    {
        // Region bulk-drop: await/cancel everything before scheduler goes away
        for task in _pending { task.cancel(); }
        for task in _running { task.await_completion(); }
    }
}
```

### Usage

```mist
let arena = Arena(4.mb);
let pool = ThreadPoolScheduler(threads: 4);

with arena
with pool
{
    let data = new LargeBuffer(1024);   // → arena.enter(data) called
    let task = async computeResult();   // → pool.enter(task) called

    return data;                        // COMPILE ERROR: arena.send(data, .ancestor) → deny
    return task.result;                 // OK if task is completed: pool.send(task, .ancestor) → allow
}
// pool.drop() runs — awaits/cancels tasks
// arena.drop() runs — frees buffer (and calls IDroppable.drop() on tracked items first)
```

### Implications of This Model

1. **No `region kind` / `region kind Allocator`** — there's just `region`. Each region is its own concrete type. If you want polymorphism over regions, you use interfaces: `IAllocator`, `IScheduler`, etc.

2. **No `drop_strategy` on regions** — the region implements `IDroppable` and does whatever it wants in its `drop()`. An arena bulk-frees. A pool awaits tasks. A transaction commits. It's just code.

3. **No per-item `drop` policy on the region** — the region doesn't define what dropping a `Foo` means. `Foo` defines that (via `IDroppable`). The region is *notified* (via `exit`) but doesn't control what the value does during its own drop.

4. **No "reference back" from values to regions** — values don't hold references to their region. The region-exit mechanism handles notification. This eliminates a whole class of circular reference problems and simplifies value types.

5. **The compiler's job is simpler** — it only needs to:
   - Know which types match `governs`
   - Insert `enter` calls at creation sites within `with` scope
   - Insert `exit` calls when governed values leave scope
   - Check `send` at traversal points
   - Ensure the region itself is dropped (standard `IDroppable` enforcement)

6. **Regions compose via `with` stacking** — multiple regions active simultaneously. A value might match multiple regions' `governs` clauses. All matching regions get an `enter` call. All get an `exit` call. All must approve `send` for traversal to be allowed.

---

## Detecting Region Traversal

### What Counts as "Crossing a Boundary"?

The compiler must detect every point where a region-branded value might escape its region. These are the *traversal points*:

1. **Argument passing** — calling a function that runs in a different region context
2. **Return** — returning a value from a region scope to the caller's scope
3. **Capture** — a closure capturing a value from an outer region
4. **Assignment** — storing a region-branded value into a container that belongs to a different region
5. **Field initialization** — constructing an object in region Y with a field pointing to region X data
6. **Channel/queue send** — explicit cross-boundary messaging (runtime dispatch)

### The Compile-Time Check

At each traversal point, the compiler:

1. Determines the *source region* (where the value was branded)
2. Determines the *target region* (where it's going)
3. Computes the *relationship* (ancestor, child, sibling, self)
4. Invokes the source region kind's `send` policy for that direction
5. Result: allow, allow(copy), allow(borrow), or deny

```mist
with arena = Arena.create(1.mb)
{
    let x = new Foo();              // x branded with arena

    return x;                       // TRAVERSAL: arena → caller's region
                                    // arena.send(x, to: ancestor) → deny → COMPILE ERROR

    let copy = x.clone();           // clone into caller's allocator
    return copy;                    // copy is branded with caller's region → OK
}
```

### Static vs Dynamic Region Identity

For lexically scoped regions (`with` blocks), all traversal checks are fully static. The compiler knows at compile time which region is active at every point.

But what about regions with *dynamic extent*? Consider:

```mist
fn process(items: List<Foo>)
{
    with arena = Arena.create(1.mb)
    {
        for item in items           // items is from caller's region
        {
            let temp = transform(item);   // temp is in arena
            results.add(temp);            // TRAVERSAL: arena → results' region
        }
    }
}
```

If `results` was allocated in the caller's region, the compiler can detect the mismatch statically (arena ≠ caller region). But if `results` is passed in as a parameter, its region identity is only known at the call site.

Options:
- **Propagate region brands through signatures** (Rust-like lifetime annotations)
- **Infer region brands from `with` context** (implicit, limited to local reasoning)
- **Hybrid**: brands are implicit for local code, explicit only at module boundaries

### The Boundary Marker Pattern

For dynamic detection (when static analysis can't determine the region), the runtime can insert a thin *boundary marker* — a tag on the value that the receiving region checks:

```mist
// Comptime-generated check at traversal point
if (value.__region_id != target_region.id)
{
    // invoke send policy — may copy, may reject, may transform
    target_region.receive(value, from: value.__region_id);
}
```

Cost: one integer comparison per cross-region transfer. This is only needed for dynamically-determined regions (e.g., values in generic containers passed through interfaces). For lexically obvious cases, the compiler elides the check.

---

## Drop Semantics (Revised — Three-Event Model)

~~Previous version described a "reference back" pattern where values held references to their region. Superseded 2026-06-03 by the three-event model: self-managed drop, region-exit, and region bulk-drop. See "Three Kinds of Drop" section above.~~

### Drop Is Type-Owned

`IDroppable` is a standalone interface. Any type that needs deterministic cleanup implements it. This has *nothing* to do with regions — it works the same whether or not the value is inside a region:

```mist
struct FileHandle : IDroppable
{
    drop() => self.close();
}

struct Task<T> : IDroppable
{
    private TaskState _state;

    drop()
    {
        if _state == .running
        {
            self.await_completion();   // Task's own cleanup — no region reference
        }
    }
}
```

The compiler enforces: if a value implements `IDroppable`, it must be consumed, moved, or dropped before scope exit. If not explicitly handled, the compiler inserts auto-drop. This is entirely orthogonal to regions.

### How Drop and Regions Interact (But Don't Merge)

A region can *use* `IDroppable` in its bulk-drop implementation — it's just calling an interface method:

```mist
region ConnectionPool : IDroppable
{
    // ...
    drop()
    {
        for conn in _active
        {
            conn.drop();    // conn : IDroppable — normal interface call
        }
    }
}
```

The region is informed about individual value deaths via the `exit` hook — no reference-back needed:

```mist
region ConnectionPool
{
    governs<T> where T : IPooledConnection;

    enter(T conn)
    {
        _active.add(conn);
    }

    exit(T conn)
    {
        // Compiler calls this when conn leaves scope
        _active.remove(conn);
        self.return_to_pool(conn);
    }
}
```

Neither side has special compiler magic for this interaction beyond: compiler inserts `exit` calls at scope-exit for governed values, same way it inserts `drop()` calls for `IDroppable` values.

### What Regions Add That's Different From Drop

Drop answers: "what happens when *this value* dies?"

Regions answer:
- "What happens when something is *created* in my scope?" → `enter`
- "Is this value *allowed to leave*?" → `send`
- "What happens when a governed value *dies*?" → `exit` (region-exit notification)
- "What happens when *I* die and things are still registered with me?" → the region's own `IDroppable.drop()` (bulk-drop)

A value can be `IDroppable` without being in a region (file handle opened in plain scope).
A value can be in a region without being `IDroppable` (plain data allocated in an arena).
A value can be both (a task that implements `IDroppable` *and* is registered with a scheduler region).

---

## The Region Lifecycle (Simplified)

The compiler's responsibilities for regions are narrow and well-defined:

```
1. ACTIVATION (with block begins)
   - Region instance is pushed onto the active region context
   - Compiler notes: "this region's `governs` constraint is now active"

2. ENTER (value creation within scope)
   - Value is constructed
   - Compiler checks: does any active region's `governs` match this type?
   - If yes: insert call to region.enter(value)
   - If multiple regions match: insert enter call for each
   - The value is now associated with those regions (branded)

3. TRAVERSAL (value attempts to leave scope)
   - At each traversal point (return, argument to outer fn, capture, assignment across boundary):
   - Compiler determines the direction (ancestor/child/sibling)
   - Compiler evaluates the region's `send` policy
   - allow: proceed
   - allow_borrow: insert borrow with lifetime ≤ region scope
   - allow_copy: insert copy, original stays in region
   - deny: compile error

4. VALUE DROP (value leaves scope — two-step)
   a. Self-managed drop: if the value implements IDroppable, compiler inserts value.drop()
   b. Region-exit: if the value is governed, compiler inserts region.exit(value)
   - Self-managed drop always fires before region-exit

5. DEACTIVATION (with block ends — region bulk-drop)
   - For each remaining governed value: step 4 fires (self-managed drop then region-exit)
   - Region's own IDroppable.drop() runs (if region implements IDroppable)
   - Region instance is popped from active context
   - Any remaining references to values branded with this region → compile error if they exist
```

Note what's *not* here: no per-item drop policy on the region. No `drop_strategy`. No "reference back" from values to their region. The region's `exit` hook handles per-item notification. The region's `drop()` handles bulk teardown. Both are just code — the compiler's only job is inserting the calls at the right points.

---

## Interaction with Existing Ownership Tiers

Mist's existing ownership model (owned, borrowed, frozen, ref) interacts with regions through the `governs` clause. The region decides which ownership tiers it accepts:

```mist
// A region that only accepts owned values
region ExclusivePool
{
    governs<T> where T : IPoolable, T : owned;
}

// A region that accepts anything
region Arena
{
    governs<T> where T : any;
}
```

How each tier behaves when governed:

| Ownership Tier | Region Behavior |
|---|---|
| `owned` | Fully governable. Can be entered, tracked, sent (if policy allows). |
| `borrowed` | Can be governed if region permits. Borrows inherit source's region brand. Cannot outlive source region. |
| `frozen` | Immutable — regions may allow these to cross boundaries more freely via send policy. |
| `ref` | Mutable reference — send policies should typically deny boundary crossings. |

### Closures and Region Capture

A closure that captures a region-branded value is itself branded with that region:

```mist
with arena = Arena.create(1.mb)
{
    let x = new Foo();                    // x in arena
    let f = () => x.bar();               // f captures x → f is also branded with arena
    spawn_on_different_scheduler(f);      // TRAVERSAL: f (arena-branded) → different region
                                         // arena.send(f, to: sibling) → deny → COMPILE ERROR
}
```

This prevents the most common source of use-after-free in concurrent systems: a closure outliving the data it captures.

---

## Open Design Questions (2026-06-02)

### Syntax and Ergonomics

1. **The `region` keyword** — `region` as a type kind (like `class`, `struct`, `enum`). Does it need further qualification? A `region` without `governs` is basically a `class` that can go in `with`. Should that be allowed (gradual opt-in to region features)?

2. **`governs` clause syntax** — `governs<T> where T : constraint` is clear but introduces a new declaration form. Is it a member? A clause on the type declaration? Where does it sit syntactically?

   ```mist
   // Option A: member-like (current)
   region Arena
   {
       governs<T> where T : any;
       enter(T item) { ... }
       send(T item, ...) { ... }
   }

   // Option B: declaration clause
   region Arena governs<T> where T : any
   {
       enter(T item) { ... }
       send(T item, ...) { ... }
   }
   ```

3. **`enter`, `exit`, and `send` — methods or something new?** They look like methods but have special calling conventions (compiler inserts calls). Are they just methods with a well-known name (like `drop()` on `IDroppable`)? Or a new concept?

   Lean: they're just methods. The compiler knows to call them based on the `governs` clause, same way it knows to call `drop()` based on `IDroppable`. No new concept needed.

4. **Region parameters on functions** — when a function accepts a value that's associated with a region, does the signature need to declare that?

   ```mist
   // Option A: explicit (Rust-like lifetime annotations)
   fn process<'r>(item: Foo in 'r) { ... }

   // Option B: implicit (compiler infers from call site)
   fn process(item: Foo) { ... }

   // Option C: only declare when crossing boundaries
   fn transfer(item: Foo, target: Region) { ... }
   ```

5. **Multiple `governs` on one region** — can a region govern different types with different enter/send behavior?

   ```mist
   region GameFrame
   {
       governs<T> where T : IRenderable
       {
           enter(T item) => _renderQueue.add(item);
           send(T item, ...) => .deny;
       }

       governs<T> where T : IAudioSource
       {
           enter(T item) => _audioMixer.add(item);
           send(T item, ...) => .allow;   // audio can escape frame
       }
   }
   ```

### Semantic Questions

6. **Can a region exist without being in `with`?** Yes — it's just an object. The `governs`/`enter`/`send` behavior only activates when it's in a `with` context. Is this confusing? Or is it the right separation (construction vs activation)?

7. **Nested regions of the same concrete type** — two Arenas active simultaneously:

   ```mist
   with outer = Arena(4.mb)
   {
       with inner = Arena(1.mb)
       {
           let x = new Foo();   // inner.enter(x) — innermost wins
       }
   }
   ```

   Innermost matching region wins. Outer is shadowed. To explicitly target outer: `new Foo() in outer`?

8. ~~**The "reference back" pattern**~~ — **Resolved (2026-06-03).** The reference-back pattern is eliminated. Values don't hold references to their region. Instead, the compiler inserts `exit` calls when governed values leave scope. The region is notified without the value needing to know about its region.

9. **Multi-region governance** — a value matches multiple active regions' `governs` clauses. All get `enter` called. All get `exit` called. For `send`, must *all* approve or just *any*? Most restrictive wins (all must approve) seems safer.

10. **Region and interfaces** — if you want polymorphism over regions (e.g., "any allocator"), you'd define `IAllocator` as an interface and have multiple region types implement it. Does `with` work with interface-typed values?

    ```mist
    fn run(allocator: IAllocator)
    {
        with allocator   // allocator is interface-typed — which region's governs applies?
        {
            let x = new Foo();
        }
    }
    ```

    This requires dynamic dispatch for `enter`/`send`. Is that acceptable, or should `with` only work with concrete region types?

### Runtime Cost Questions

11. **Zero-cost for static regions** — lexical `with` blocks with concrete types should have zero overhead beyond the `enter` call. The `send` check is compile-time only. Confirm this holds when regions are behind interfaces.

12. **Cost of enter** — every allocation in a region scope calls `enter`. For an arena allocating thousands of objects, is this overhead acceptable? Could the compiler optimize `enter` away when it's trivial (e.g., just bumps a pointer)?

13. **Region identity for traversal checking** — when `send` is checked at compile time (lexical regions), no runtime cost. When regions are dynamic (interface-typed, passed as parameters), does traversal checking need a runtime component?

---

## Ambient Region Model (2026-06-03)

### The Core Insight: One Region Per Function

Every function body executes in exactly one ambient region. You don't know *which* region it is, but you know:

1. All parameters passed in are branded with the same region (`'a`)
2. All allocations (`new Foo()`) are branded with `'a`
3. Therefore everything in the function lives in the same region — no accidental cross-region movement is possible

Since everything is `'a`, there is no `send` violation possible within a function body that doesn't lexically introduce a second region. The `send` check only fires when there is a *visible second region* — i.e., you explicitly opened one with `with`.

### Why This Works

```mist
void Foo(Banana b)   // b is in 'a (whatever 'a is — Foo doesn't know or care)
{
    var x = new Apple();   // x is also in 'a — same region as b
    b = x;                 // fine — same region
    return x;             // fine — caller is also in 'a (it passed us 'a)

    with localArena = Arena(1.mb)   // introduces 'b — a NEW, visible region
    {
        var y = new Cherry();        // y is in 'b
        x = y;                       // ERROR: 'b → 'a requires send
        return y;                    // ERROR: 'b → 'a requires send
    }
}
```

The compiler doesn't need to know what `'a` *concretely* is. It just knows "there's one ambient region, and everything agrees on it." The only time a `send` check is needed is when you lexically introduce a second region via `with`. That's always visible in the function body.

### What This Gives Us

- **Implicit participation** — functions don't declare their region. They inherit it from the caller via the ambient context. User-defined regions with `governs any` work transparently.
- **Local reasoning for `send` violations** — they only appear at lexical `with` boundaries you wrote yourself. You can never get a surprise `send` error from ambient context you can't see.
- **No annotation in the common case** — most functions operate in a single region and need zero lifetime/region annotations.

### Analogy to Rust Lifetimes

In Rust, a function with no lifetime annotations assumes all borrows share the same lifetime. You only annotate when you have *multiple* lifetimes that need to be distinguished. Same principle here — you only annotate when you have *multiple* regions in one scope.

### When Annotations Are Needed

Functions that accept values from *different* regions simultaneously need to declare that:

```mist
// These two values might be in different regions — must be explicit
void Transfer<'a, 'b>(Foo in 'a source, Bar in 'b target)
{
    // compiler knows 'a ≠ 'b — send check required
    target.items.add(source);   // ERROR: 'a → 'b requires send
}
```

But the common case (everything in one ambient region) requires no annotation. The unifying `'a` is implicit and never needs to be named.

### How `enter`/`exit` Work Under This Model

Since `new Foo()` is always in the ambient region `'a`, the compiler emits a call through the ambient context — "allocate in the current region." At runtime, this dispatches to whatever concrete region is active. The function doesn't know *which* region it's calling `enter` on. It calls through the context slot.

Similarly, when a governed value leaves scope, the compiler emits an `exit` call through the ambient context. The function doesn't need to know the concrete region — it just calls through the slot.

### Multi-Region: Shadowing, Not Broadcasting

When multiple `with` blocks are stacked for the *same* context slot (e.g., nested allocators), the innermost shadows. There is only one active region per slot at any point. `new` resolves to the current allocator slot value. This eliminates the "value enters multiple regions" problem entirely for same-typed regions.

Different-typed regions (allocator + scheduler) occupy different slots and don't interfere. A `Banana` might only be governed by the allocator slot, not the scheduler slot — the `governs` constraint on each region determines which slot cares about which types.

### What Remains Unresolved: `send` Mechanics

The model above establishes *when* a `send` check occurs (only at lexical `with` boundaries where a second region is visible). What remains open:

1. **How do you express a cross-region transfer?** What does the syntax look like for moving a value from `'b` into `'a`? Is it a method call on the region? A keyword? An operator?

2. **What does the region's `send` policy return?** Current draft has `allow`, `allow_borrow`, `allow_copy`, `deny`. Are these the right set? What does `allow_copy` mean concretely — who allocates the copy, and in which region?

3. **Who initiates the transfer?** Does the source region release, or does the target region accept? Or is it a handshake through the parent?

4. **Can `send` be conditional on runtime state?** (e.g., "allow if the task is completed"). If so, is `send` a compile-time check or a runtime check? Or both — compile-time proves the check *exists*, runtime evaluates it?

5. **How does `send` interact with the ambient region model?** If everything in a function is `'a`, and you open a `with` block creating `'b`, and you want to move something from `'b` into `'a` — is that "sending to parent" or "sending to caller's ambient"? (Probably the same thing, but worth confirming.)

These are the next design questions to resolve.

---

## Two Region Kinds: Space and Time (2026-06-04)

### Core Realization: Every Region Is Either Space or Time

Every program is just operations that manipulate space via time. There is no third category. Transactions, rendering frames, undo scopes, security contexts — they all reduce to either "data containment" (space) or "operation sequencing" (time).

This means we don't need a general `region` keyword. We need exactly two kinds:

- **`space`** — intercepts *creation* (`new`). Governs types. Tracks values in memory.
- **`time`** — intercepts *execution* (method calls). Governs operations. Tracks what the program does.

### `space` — Heap Allocation Interception

A `space` region intercepts `new`. The `governs` clause filters which types it cares about. The interception point is always the same syntactic form: `new T()`.

```mist
public space Arena
{
    governs T : any;

    private byte* _buffer;
    private int _offset;

    public Arena(Size capacity)
    {
        _buffer = sys_alloc(capacity);
        _offset = 0;
    }

    enter(T item)
    {
        let ptr = _buffer + _offset;
        _offset = _offset + size_of(T);
        mem_copy(item, ptr);
    }

    exit(T item) { }

    send(T item, RegionRelation direction) -> SendResult
    {
        return match direction
        {
            .child => .allow_borrow,
            _ => .deny,
        };
    }
}

impl IDroppable for Arena
{
    drop() => sys_free(_buffer);
}
```

Usage:

```mist
public Summary ProcessBatch(List<RawInput> items)
{
    with arena = Arena(16.mb)
    {
        let parsed = items.Map(i => Parse(i));
        let result = Summarize(parsed);
        return result.Clone();   // clone into caller's space — arena denies direct escape
    }
}

// Parse doesn't know about the arena. new routes through ambient space slot.
public ParsedItem Parse(RawInput input)
{
    let tokens = new TokenList();    // → arena.enter(tokens)
    let ast = new AstNode();         // → arena.enter(ast)
    return new ParsedItem(tokens, ast);
}
```

The compiler sees `new TokenList()` → checks the ambient space slot → arena is active → arena governs `any` → emits `arena.enter(tokens)`.

`Parse` has no `with` declaration. It just calls `new` and the ambient context routes it.

### `time` — Method Call Interception

A `time` region intercepts method calls. The `governs` clause specifies both the type constraint (which objects are relevant) AND the specific methods to intercept:

```mist
public interface ITransactional
{
    Operation ToOperation();
}

public class Account : ITransactional
{
    public void Debit(Money amount) { /* ... */ }
    public void Credit(Money amount) { /* ... */ }
    public Money GetBalance() { return _balance; }
    public Operation ToOperation() { /* ... */ }
}

public time TransactionScope
{
    governs T : ITransactional { Debit, Credit, Transfer };

    private List<Operation> _log;
    private bool _committed;

    public TransactionScope(Database db)
    {
        _log = new List<Operation>();
        _committed = false;
    }

    enter(T target, string method, object[] args)
    {
        _log.Add(new Operation(target, method, args));
    }

    exit(T target, string method) { }

    send(T op, RegionRelation direction) -> SendResult
    {
        return match direction
        {
            .ancestor => if _committed { .allow } else { .deny },
            _ => .deny,
        };
    }

    public void Commit()
    {
        for op in _log { op.Execute(); }
        _committed = true;
    }

    public void Rollback()
    {
        _log.Clear();
    }
}

impl IDroppable for TransactionScope
{
    drop()
    {
        if not _committed { self.Rollback(); }
    }
}
```

Usage:

```mist
public void TransferFunds(Account from, Account to, Money amount)
{
    with tx = TransactionScope(db)
    {
        from.Debit(amount);     // Account : ITransactional, Debit is governed → tx.enter(from, "Debit", [amount])
        to.Credit(amount);      // Account : ITransactional, Credit is governed → tx.enter(to, "Credit", [amount])

        from.GetBalance();      // Account : ITransactional, but GetBalance NOT in governs list → no interception

        tx.Commit();
    }
    // if Commit wasn't called → tx.drop() → Rollback
}
```

The compiler sees `from.Debit(amount)` → `from` is `Account` → `Account : ITransactional` → active time slot governs `ITransactional { Debit, Credit, Transfer }` → `Debit` is in the list → wrap call with `enter`/`exit`.

Calling `from.GetBalance()` — even though `Account : ITransactional` — does NOT trigger the region because `GetBalance` is not in the governs method list.

### Comparison

| | `space` | `time` |
|---|---|---|
| Keyword | `space` | `time` |
| Intercepts | `new` (heap allocation) | method calls on governed types |
| `governs` filters on | the type being created | the type + specific methods |
| `governs` syntax | `governs T : Constraint` | `governs T : Constraint { Method1, Method2 }` |
| `enter` receives | the newly created value | the target + method + args |
| `exit` means | "this value is dying" | "this call completed" |
| `send` means | "can this value escape?" | "can this result escape?" |
| Bulk-drop means | free all tracked memory | rollback/cancel all tracked ops |
| Ambient slot | always filled (default allocator) | can be empty (not everything is transactional) |

### Why Two Kinds, Not One

A single `region` keyword with a unified `governs` clause can't cleanly express both:

- "intercept `new Foo()` and track the value" (space)
- "intercept `account.Debit()` and track the operation" (time)

These are fundamentally different interception points with different shapes:
- Space enter receives a *value* and tracks it for the region's lifetime
- Time enter receives a *call* (target + method + args) and logs it

The `send` semantics also differ:
- Space send asks "can this value leave the region?" — answers are about the value's lifetime
- Time send asks "can this operation's result escape?" — answers are about causality/consistency

### How Both Use the Ambient Model

Both kinds use the same ambient context mechanism:

1. `with arena = Arena(16.mb)` pushes `arena` into the space context slot
2. `with tx = TransactionScope(db)` pushes `tx` into the time context slot
3. Functions don't declare which regions they're in — they inherit from the caller
4. The compiler emits interception calls through the ambient slot at the appropriate points

For space: the interception point is every `new T()` where T matches the governs clause.
For time: the interception point is every `target.Method()` where target's type matches the governs clause and Method is in the method list.

Both are fully implicit to the called function. `Parse` doesn't know it's in an arena. `TransferFunds`'s callees don't know they're in a transaction (unless they also call governed methods, in which case they transitively participate).

### Shadowing

Both kinds shadow by slot. Inner `with` replaces the active region for that slot:

```mist
with outer = Arena(4.mb)
{
    // space slot = outer
    let a = new Foo();   // → outer.enter(a)

    with inner = Arena(1.mb)
    {
        // space slot = inner (outer is shadowed)
        let b = new Bar();   // → inner.enter(b)
    }
    // space slot = outer again

    let c = new Baz();   // → outer.enter(c)
}
```

Same for time:

```mist
with outerTx = TransactionScope(db)
{
    from.Debit(amount);   // → outerTx.enter(...)

    with innerTx = TransactionScope(db)
    {
        to.Credit(amount);   // → innerTx.enter(...), NOT outerTx
    }
}
```

### Open Questions (Space vs Time)

1. **Can you have multiple space slots active simultaneously?** An allocator + a memory pool that governs a subset of types? Or is there strictly one space slot?

2. **Can you have multiple time slots active simultaneously?** A transaction scope + a logging scope that watches different methods on the same types?

3. **If the time slot is empty and you call a governed method, what happens?** Nothing (methods work normally without a transaction)? Or compile error (you must be in a transaction to call Debit)?

4. **The `send` signature for time** — what exactly is being "sent"? The method's return value? Or the operation record? If `Debit` returns `void`, what does send even check?

5. **Time enter/exit shape** — is `enter(T target, string method, object[] args)` right? Or should it be more structured (e.g., a generated delegate/action type per governed method)?
