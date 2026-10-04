# Drop Semantics

Mist provides deterministic cleanup through a unified `Drop()` method. Unlike languages that rely on garbage collection for finalization or separate patterns like `IDisposable`, Mist ties cleanup directly to a type's substructural discipline — the type declaration itself determines when and how often `Drop()` fires.

The three substructural modifiers are:

| Modifier | Drop called... | Owned? | Can alias? | Can forget? |
|---|---|---|---|---|
| `linear` | Exactly once at scope exit | Yes | No | No |
| `relevant` | At least once at scope exit (idempotent) | No | Yes | No |
| `affine` | At most once, only in `with` blocks | Yes | No | Yes (outside `with`) |

Types without a substructural modifier can still declare `Drop()` — it only fires inside `with` blocks (zero or more times).

## Linear Types

A `linear` type guarantees exactly-once cleanup. The compiler inserts a `Drop()` call at scope exit for every linear local that hasn't been moved or returned. This makes resource leaks structurally impossible.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/linear-auto-drop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/linear-auto-drop.cs}}
```
{{#endtab }}
{{#endtabs }}

Linear types imply `owned` — they have single-holder move semantics. You cannot alias a linear value; assigning it to another variable is a move.

### Drop Elision

The compiler is smart about when to insert `Drop()`. If a linear value is moved to a function parameter, into another local, or returned from the function, the caller's `Drop()` is elided — the new owner takes responsibility.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/drop-elision.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/drop-elision.cs}}
```
{{#endtab }}
{{#endtabs }}

When multiple linear variables exist in the same scope, they are dropped in reverse declaration order (last declared, first dropped). The emitted C# uses nested `try/finally` blocks to ensure drops fire even on exception.

### Cleanup Follows the Path Taken

Whether a local still owns its value depends on the path the program took. A `Drop()` on one branch of an `if`, a `return` on one branch, or a hand-off inside a loop leaves the other paths still owing the release, and each value is released exactly once on whichever path actually runs. A local whose value was consumed can be given a fresh one, and that new value is owed a release of its own.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/path-sensitive-drop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/path-sensitive-drop.cs}}
```
{{#endtab }}
{{#endtabs }}

The program prints:

```text
Lease(1) released
Lease(1) released
Lease(2) released
Kept Lease(3)
Lease(5) released
Lease(6) released
Lease(3) released
Lease(4) released
```

When ownership is the same on every path, the emitted C# releases the local unconditionally in its `finally`, or not at all when the value is always handed on before anything can fail. When it differs between paths, a flag beside the local records whether it still owns its value: the flag is cleared at the exact point the value is handed on, set again once a fresh value has been assigned, and read by the `finally` before releasing. Because the release sits in a `finally`, an exception thrown while the value is owned still releases it. An exception from the initializer of a new value releases nothing, because no new value exists yet. A value passed as an argument belongs to the callee once the call starts, so a throw from inside the callee is the callee's to clean up.

### Drop with a Return Value

`Drop()` can return a value. This lets a container die while yielding something to the caller — the box is consumed, but its contents escape.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/drop-return-value.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/drop-return-value.cs}}
```
{{#endtab }}
{{#endtabs }}

After calling `Drop()`, the variable is consumed — any subsequent access is a compile-time error. When the compiler auto-inserts `Drop()` at scope exit, non-void return values are discarded.

## Relevant Types

A `relevant` type guarantees at-least-once cleanup. Unlike linear types, relevant values are aliasable — you can have multiple references to the same object. `Drop()` fires at scope exit for every reference, so the implementation must be idempotent.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/relevant-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/relevant-type.cs}}
```
{{#endtab }}
{{#endtabs }}

`Task<T>` in the standard library is a relevant type — its `Drop()` performs `await`, ensuring tasks are always awaited. Since `Task<T>` is aliasable (you can pass it to multiple functions), the `await` is idempotent — awaiting a completed task is a no-op.

## Affine Types

An `affine` type allows at-most-once cleanup. `Drop()` only fires inside `with` blocks — outside a `with`, you can let an affine value fall out of scope without cleanup. This is useful for one-shot tokens where cleanup is optional.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/affine-with-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/affine-with-block.cs}}
```
{{#endtab }}
{{#endtabs }}

Affine types imply `owned` — like linear types, they have single-holder move semantics and cannot be aliased.

## Synthetic Drop

When a type contains fields with `Drop()` methods but doesn't define its own, the compiler synthesizes one. The synthetic `Drop()` calls each droppable field's `Drop()` in declaration order.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/synthetic-drop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/synthetic-drop.cs}}
```
{{#endtab }}
{{#endtabs }}

This inference cascades transitively — if type A contains type B which contains type C (which has `Drop()`), then both B and A get synthetic `Drop()` methods. If you provide a manual `Drop()`, the compiler appends drops for any fields you didn't explicitly handle.

## Interaction with Ownership

Drop semantics build on the existing ownership system:

- **Lending** (`readonly ref`, `frozen`, `borrowed`) does not transfer drop responsibility. The owner still drops at scope exit, and the lease itself is never dropped.
- **Moving** (passing by value to a function) transfers drop responsibility to the callee.
- **Moving into another local** (`var b = a;` or `b = a;`) transfers drop responsibility to the destination.
- **Returning** transfers drop responsibility to the caller.

Linear and affine types are always owned — the substructural modifier implies `owned`. You don't need to write `owned linear class` — just `linear class`.

## IDisposable Interop

.NET has its own cleanup pattern — `System.IDisposable` with a `Dispose()` method. Mist bridges this automatically: CLR types that implement `IDisposable` are recognized as droppable and work in `with` blocks without any wrapper code.

### Automatic Recognition

When the compiler projects a CLR type from a .NET assembly, it checks whether the type implements `System.IDisposable` (directly or transitively through an interface chain). If it does, the type is marked as droppable — you can use it in `with` blocks just like a Mist type with `Drop()`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/idisposable-with-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/idisposable-with-block.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitter generates `.Dispose()` — not `.Drop()` — in the `finally` block. This means .NET resource cleanup happens through the standard IDisposable path, exactly as C#'s `using` statement would.

### Explicit Drop Takes Precedence

If a type has both an explicit `Drop()` method and implements `IDisposable`, the explicit `Drop()` wins. The emitter emits `.Drop()` in this case, not `.Dispose()`. This gives you full control when wrapping CLR types with custom cleanup logic.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/drop-semantics/idisposable-explicit-drop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/drop-semantics/idisposable-explicit-drop.cs}}
```
{{#endtab }}
{{#endtabs }}

### Mist Types Are Not Auto-Droppable

This automatic recognition applies only to CLR types imported from .NET assemblies. Mist types that implement `IDisposable` are **not** automatically droppable — you must declare an explicit `Drop()` method to make them usable in `with` blocks.

This is intentional. Mist's drop semantics are more expressive than IDisposable (supporting return values, chaining, and substructural disciplines). Automatically conflating `Dispose()` with `Drop()` for Mist types would bypass these design choices. If you want a Mist type to be droppable, declare `Drop()` explicitly — call `Dispose()` from within it if you need both patterns.

## Discipline Summary

| Feature | Linear | Relevant | Affine | Unrestricted |
|---|---|---|---|---|
| Auto-drop at scope exit | Yes (exactly once) | Yes (every reference) | No | No |
| Drop in `with` blocks | Yes | Yes | Yes (only mechanism) | Yes |
| Can be forgotten | No | No | Yes (outside `with`) | Yes |
| Can alias | No | Yes | No | Yes |
| Implies `owned` | Yes | No | Yes | No |
| Drop must be idempotent | No | Yes | No | N/A |
