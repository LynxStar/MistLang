<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist Ownership System

This file provides detailed reference for Mist's ownership and substructural type system. Include this context when working with owned types, lending, move semantics, or drop semantics.

## Overview

Mist's ownership system is opt-in. By default, bindings are unrestricted (shared references, like normal C#). You opt into ownership tracking per-binding or per-type to get move semantics, lending, and automatic resource cleanup.

## Ownership Tiers

| Tier | Keyword | Access | Aliasing | Use Case |
|------|---------|--------|----------|----------|
| Unrestricted | (none) | Full | Unlimited | Default — shared references |
| Owned | `owned` | Full (owned types) or readonly (non-owned types) | None — exclusive | Unique ownership, move semantics |
| Borrowed | `borrowed` | Exclusive read/write | Temporary exclusive | Mutation without taking ownership |
| Frozen | `frozen` | Read-only | Temporary shared | Read-only snapshot |
| Readonly ref | `readonly ref` | Read-only | Temporary shared | Lightest lending tier |

## Owned Bindings

An `owned` binding has exclusive access to its value. Assignment moves the value:

```mist
owned var engine = new Engine(1000, 80);
engine.Rev(500);           // mutable access (Engine is an owned type)

owned var e2 = engine;     // MOVE — engine is now dead
// engine.Rev(100);        // ERROR: use-after-move
```

### Owned Types vs Non-Owned Types in Owned Bindings

When you put a value in an `owned` binding, what methods you can call depends on whether the type itself is declared `owned`:

**Owned type** (`public owned class Engine`) — full mutable access. The type opts into the "cooperation contract": all fields follow ownership discipline, so mutations are safe.

**Non-owned type** (`public class Widget`) — readonly-only access. Mutable methods are hidden from the owned binding because the type hasn't guaranteed field safety.

```mist
// Non-owned type in owned binding: readonly methods only
owned var w = new Widget(10, "alpha");
var val = w.GetValue();    // OK — readonly
// w.SetValue(99);         // ERROR — mutable method hidden

// Owned type in owned binding: full access
owned var engine = new Engine(1000, 80);
engine.Rev(500);           // OK — Engine is owned, cooperation contract satisfied
```

## Move Semantics

Assignment from an owned binding always moves:

```mist
owned var a = new Engine(1000, 80);
owned var b = a;    // move: a → b, a is dead
var c = b;          // move: owned → unrestricted, b is dead
ConsumeEngine(b);   // move into parameter, caller's binding is dead
```

Move is elided when a value is returned from a function (the caller receives ownership).

## Lending (Temporary Access)

Owned bindings can lend their value temporarily without giving up ownership:

```mist
owned var engine = new Engine(2000, 90);

// Lend as readonly ref — owner enters LentReadonly state
AcceptReadonly(engine);      // fn(readonly ref Engine e)
// Owner back to Live after call returns

// Lend as frozen — owner enters LentFrozen state
AcceptFrozen(engine);        // fn(frozen Engine e)
engine.Rev(50);              // OK — owner is Live again after freeze returns

// Lend as borrowed — exclusive temporary access
AcceptBorrowed(engine);      // fn(borrowed Engine e) { e.Rev(100); }
// Owner is Live again after borrow returns
```

### Recipient Capabilities

| Recipient Tier | Read | Write | Re-lend as readonly ref | Re-lend as frozen | Re-lend as borrowed |
|----------------|------|-------|------------------------|-------------------|---------------------|
| `readonly ref` | ✓ | ✗ | ✓ | ✓ | ✗ |
| `frozen` | ✓ | ✗ | ✓ | ✓ | ✗ |
| `borrowed` | ✓ | ✓ | ✓ | ✓ | ✓ |

## This-Tier (Method Receiver Annotations)

Methods can declare what ownership tier they require of `this`:

```mist
public class Account
{
    public owned string Name;
    public owned int Balance;

    // Inferred this-tier: borrowed (writes to owned field)
    public void Deposit(owned int amount) { this.Balance = this.Balance + amount; }

    // Inferred this-tier: readonly ref (only reads)
    public string Summary() { return $"Account({Name})"; }

    // Explicit this-tier annotation
    public string PeekName() with frozen { return this.Name; }
    public string Close() with owned { return $"Closed: {Name}"; }  // consumes receiver
}
```

## IClonable — Bridging Shared to Owned

Types implementing `IClonable<T>` can be auto-cloned when assigned to an owned binding:

```mist
public class Config : IClonable<Config>
{
    public int Timeout;
    public string Host;

    public owned Config Clone() { return new Config(Timeout, Host); }
}

var shared = new Config(30, "localhost");
owned var mine = shared;   // implicit .Clone() inserted by compiler
// shared is still usable — mine is an independent copy
```

Implicit clone also fires at owned-parameter boundaries and when cloning from lent tiers (borrowed, frozen, readonly ref).

## Field Provenance

When assigning to a field on an owned binding, the source must satisfy provenance:

1. **Constructor call** (`new T(...)`) — fresh graph, always valid
2. **Owned-returning method** — returns ownership
3. **Move from owned binding** — transferred ownership
4. **IClonable source** — auto-clone produces fresh ownership

```mist
owned var container = new Container(new Widget(1, "first"));  // new = provenance OK
container.Replace(replacement);  // owned parameter = provenance OK
container.Inner = shared;        // IClonable = provenance OK (auto-clone)
```

## Owned Fields on Non-Owned Types (Jagged Ownership)

A non-owned type can have individual `owned` fields:

```mist
public class Document
{
    public owned string Content;  // owned field
    public int Version;           // non-owned field

    public void UpdateContent(owned string newContent)
    {
        this.Content = newContent;      // provenance via owned parameter
        this.Version = this.Version + 1;
    }

    public void BumpVersion() { this.Version = this.Version + 1; }  // no ownership semantics
}
```

## Partial Moves and Field-Level State

Individual fields can be moved independently:

```mist
owned var pair = new TwoFields("alpha", "beta");
owned var extracted = pair.Left;  // Left is Moved, Right is still Live
Console.WriteLine(pair.Right);    // OK — sibling still accessible
pair.Left = "restored";           // restore: Left returns to Live
```

## Substructural Modifiers (Drop Semantics)

| Modifier | Drop Behavior | Aliasing | Forgetting |
|----------|--------------|----------|------------|
| `linear` | Exactly once (compiler-enforced) | Not allowed | Not allowed |
| `affine` | At most once (only in `with` blocks) | Not allowed | Allowed outside `with` |
| `relevant` | At least once (fires for every reference) | Allowed | Not allowed |

### Linear Types

```mist
public linear class FileHandle
{
    int Fd;
    public FileHandle(int fd) { this.Fd = fd; }
    public void Drop() { Console.WriteLine($"Closing fd {Fd}"); }
}

{
    var f = new FileHandle(1);
    // compiler inserts f.Drop() at scope exit — exactly once
}
```

Drop is elided when a linear value is moved (the recipient becomes responsible).

### Drop with Return Value

Linear types can return a value from Drop (container dies, value escapes):

```mist
public linear class LinearBox<T> where T : linear
{
    T Value;
    public LinearBox(T value) { this.Value = value; }
    public T Drop() { return Value; }  // yields the contained value
}

var box = new LinearBox<FileHandle>(new FileHandle(42));
var handle = box.Drop();  // box consumed, handle captured
```

### Affine Types

```mist
public affine class AuthToken
{
    string Token;
    public AuthToken(string token) { this.Token = token; }
    public void Drop() { Console.WriteLine($"Revoking {Token}"); }
}

// Drop only fires in with-blocks
with(new AuthToken("abc")) { }  // Drop fires at block exit

var token = new AuthToken("xyz");
// token falls out of scope — NO Drop call (affine allows forgetting)
```

### Relevant Types

```mist
public relevant class SharedResource
{
    string Id;
    bool Released;
    public void Drop()
    {
        if (Released) { return; }  // idempotent
        this.Released = true;
        Console.WriteLine($"Releasing {Id}");
    }
}

var res = new SharedResource("conn-1");
var alias = res;  // aliasing allowed
// Drop fires for BOTH alias and res (idempotent)
```

### Drop Ordering

Drops fire in reverse declaration order within a scope:

```mist
var first = new FileHandle(10);
var second = new FileHandle(11);
var third = new FileHandle(12);
// drops: third → second → first
```

### Synthetic Drop

Types containing linear/affine/relevant fields get a compiler-synthesized `Drop()` that calls field drops in declaration order:

```mist
public linear class Session
{
    FileHandle Handle;
    FileHandle Log;
    // Compiler synthesizes: Drop() { Handle.Drop(); Log.Drop(); }
}
```

## IDisposable CLR Types in With-Blocks

CLR types that implement `System.IDisposable` are automatically recognized as droppable and work in `with` blocks without any Mist-side declaration. The emitter generates `.Dispose()` — not `.Drop()` — for these types.

```mist
using System.IO;

// StreamWriter implements IDisposable — use directly in with-block.
with (var writer = new StreamWriter("log.txt"))
{
    writer.WriteLine("entry");
}
// Emits: writer.Dispose() in the finally block
```

### Precedence Rules

- **Explicit `Drop()` wins** — if the CLR type also has a `Drop()` method, the emitter calls `.Drop()` instead.
- **Mist types are not auto-droppable** — a Mist-declared type implementing `IDisposable` must still declare `Drop()` explicitly. This preserves Mist's richer drop semantics (return values, chaining, substructural disciplines).
- **IAsyncDisposable alone does not qualify** — only `System.IDisposable` triggers auto-recognition.

### Synthetic Drop Exclusion

Fields typed as IDisposable-droppable CLR types are excluded from synthetic drop inference. The owning type's drop logic (or `with`-block) handles their lifetime — individual fields are not disposed separately by the synthesized `Drop()`.

## Value Types and Ownership

Value types (structs, primitives) satisfy all lending tiers because they are copied — no aliasing occurs:

```mist
var v = new Vec2(3, 4);
AcceptOwned(v);       // copy — original unaffected
AcceptBorrowed(v);    // copy
AcceptFrozen(v);      // copy
AcceptReadonly(v);    // copy
```
