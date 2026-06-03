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

- Implemented as an interface: `IDroppable` (or similar)
- Types must explicitly implement the interface — no duck typing
- Independent of regions — IDroppable works the same inside or outside a `with` block
- Compiler enforces: at scope exit, the value must have been dropped (consumed), moved (transferred), or returned
- For Tasks: drop = await. For files: drop = close. For locks: drop = release.
- If not explicitly consumed: compiler inserts auto-drop (scope-exit await, auto-close, etc.)
- Types that need to coordinate with their region at drop time hold a reference to it (the "reference back" pattern)

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
| `send` policy | ❌ | ✅ — compiler checks on traversal of governed values |
| Compiler-inserted association | ❌ | ✅ — values created in scope are branded |

The `governs`, `enter`, and `send` declarations are what make a `region` a region. Everything else is just a normal type.

### The `governs` Clause

The `governs` clause tells the compiler which types this region interacts with. Only values matching this constraint get registered on creation and checked on traversal:

```mist
region Scheduler
{
    governs<T> where T : ITask;

    enter(T task) => self.enqueue(task);

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

### Drop Is Type-Owned, Region Is Just a Participant

The `IDroppable` interface belongs to the *type*. The type's drop logic can reference its region (if it holds one), but the region doesn't define what drop means for the type:

```mist
struct Task<T> : IDroppable
{
    private Scheduler _scheduler;   // keeps a reference to its scheduling region
    private TaskState _state;

    drop()
    {
        if _state == .running
        {
            _scheduler.await_completion(self);   // inform the region
        }
    }
}
```

The Task knows about its scheduler. When it drops, it does its own cleanup, which *may* involve talking to the region. But the region doesn't reach into the task and define its drop behavior. The relationship is:

```
Type's IDroppable.drop()  →  may call into region  →  region responds
Region's own IDroppable.drop()  →  may iterate tracked values  →  calls their IDroppable.drop()
```

Both directions are just normal method calls. No compiler magic beyond the standard `IDroppable` enforcement.

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

    send(T item, to: RegionRelation direction) -> SendResult
    {
        match direction
        {
            .child => .allow_borrow,
            _ => .deny,
        }
    }
}

// Arena implements IDroppable — its own cleanup
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
        // Cancel pending, await running
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

3. **No per-item `drop` policy on the region** — the region doesn't define what dropping a `Foo` means. `Foo` defines that (via `IDroppable`). The region's `drop()` may *call* individual items' `drop()` as part of its cleanup, but that's a choice made by the region's implementation.

4. **The compiler's job is simpler** — it only needs to:
   - Know which types match `governs`
   - Insert `enter` calls at creation sites within `with` scope
   - Check `send` at traversal points
   - Ensure the region itself is dropped (standard `IDroppable` enforcement)

5. **Regions compose via `with` stacking** — multiple regions active simultaneously. A value might match multiple regions' `governs` clauses. All matching regions get an `enter` call. All must approve `send` for traversal to be allowed.

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

## Drop Semantics (Revised — Independent of Regions)

### Drop Is Type-Owned

`IDroppable` is a standalone interface. Any type that needs deterministic cleanup implements it. This has *nothing* to do with regions — it works the same whether or not the value is inside a region:

```mist
struct FileHandle : IDroppable
{
    drop() => self.close();
}

struct Task<T> : IDroppable
{
    private Scheduler _scheduler;

    drop()
    {
        if _state == .running
        {
            _scheduler.await_completion(self);
        }
    }
}
```

The compiler enforces: if a value implements `IDroppable`, it must be consumed, moved, or dropped before scope exit. If not explicitly handled, the compiler inserts auto-drop. This is entirely orthogonal to regions.

### How Drop and Regions Interact (But Don't Merge)

A region can *use* `IDroppable` in its implementation — it's just calling an interface method:

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

And a type's `IDroppable.drop()` can reference its region — it's just holding a normal reference:

```mist
struct Task<T> : IDroppable
{
    private Scheduler _scheduler;   // reference to the region it was created in

    drop()
    {
        _scheduler.notify_task_complete(self);
    }
}
```

Neither side has special compiler magic for this interaction. It's just objects calling methods on each other.

### What Regions Add That's Different From Drop

Drop answers: "what happens when *this value* dies?"

Regions answer:
- "What happens when something is *created* in my scope?" → `enter`
- "Is this value *allowed to leave*?" → `send`
- "What happens when *I* die and things are still registered with me?" → the region's own `IDroppable.drop()`

A value can be `IDroppable` without being in a region (file handle opened in plain scope).
A value can be in a region without being `IDroppable` (plain data allocated in an arena).
A value can be both (a task that implements `IDroppable` *and* is registered with a scheduler region).

### The "Reference Back" Pattern

For types that need to coordinate with their region at drop time, the standard pattern is: hold a reference to the region.

```mist
struct PooledConnection : IDroppable
{
    private ConnectionPool _pool;

    drop() => _pool.return_connection(self);   // inform region on individual drop
}
```

The region gives the value a reference to itself during `enter`:

```mist
region ConnectionPool
{
    governs<T> where T : IPooledConnection;

    enter(T conn)
    {
        conn.set_pool(self);    // hand the value a reference back
        _active.add(conn);
    }
}
```

This is explicit, visible, debuggable — no hidden compiler-generated callbacks.

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

4. VALUE DROP (value's own IDroppable, if any)
   - Independent of region — compiler enforces IDroppable normally
   - If the type holds a region reference and notifies it, that's just a method call

5. DEACTIVATION (with block ends)
   - Region's own IDroppable.drop() runs (if region implements IDroppable)
   - Region decides in its drop() what to do with remaining registered values
   - Region instance is popped from active context
   - Any remaining references to values branded with this region → compile error if they exist
```

Note what's *not* here: no per-item drop policy on the region. No `drop_strategy`. No compiler-inserted finalization of individual values. The region's `drop()` is just code — it can iterate its tracked values and call their `drop()` if it wants to, or it can bulk-free, or it can do nothing. That's an implementation choice, not a compiler concern.

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

3. **`enter` and `send` — methods or something new?** They look like methods but have special calling conventions (compiler inserts calls). Are they just methods with a well-known name (like `drop()` on `IDroppable`)? Or a new concept?

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

8. **The "reference back" pattern** — if a type wants to talk to its region at drop time, it holds a reference. But how does it *get* that reference? The region gives it during `enter`. Does this mean `enter` needs to be able to mutate the value?

   ```mist
   enter(T item)
   {
       item._pool = self;    // requires mutable access to item
   }
   ```

   This conflicts with the value potentially being constructed as `let` (immutable). Options: `enter` receives the value before binding completes, or the reference is injected by the constructor (type is aware of its region from birth).

9. **Multi-region governance** — a value matches multiple active regions' `governs` clauses. All get `enter` called. For `send`, must *all* approve or just *any*? Most restrictive wins (all must approve) seems safer.

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
