# Ownership

Mist's ownership system controls how references are shared and mutated. It provides compile-time guarantees about aliasing, mutation, and lifetime — preventing data races and use-after-free errors without a garbage collector or runtime borrow checker. Ownership annotations are enforced entirely at compile time and erased in the emitted C#, so there is zero runtime cost.

The system defines five ownership tiers, ordered from least restrictive to most restrictive. Each tier specifies what operations are permitted on a binding and whether the binding can be stored (e.g., assigned to a field or captured in a closure).

## Unrestricted (Default)

The default tier when no ownership annotation is present. Unrestricted bindings have standard reference semantics — multiple variables can alias the same object, and any of them can read or mutate it. This is the familiar behavior from C# and most garbage-collected languages.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/unrestricted.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/unrestricted.cs}}
```
{{#endtab }}
{{#endtabs }}

Since unrestricted is the default, the emitted C# is identical to the Mist source (minus any ownership annotations elsewhere). Unrestricted bindings are storable — you can assign them to fields, return them from functions, and capture them in closures without restriction.

## Owned

An `owned` binding represents sole ownership of a value. The compiler guarantees that exactly one binding owns the object at any time. Owned bindings have full control: they can read, write, lend to other tiers, or transfer ownership via a move.

You declare owned variables with the `owned` keyword before the type:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/owned.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/owned.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that the `owned` keyword disappears in the emitted C#. Ownership is enforced at compile time — the runtime code is plain C# with no overhead.

### Owned Type Declarations

You can declare a type as `owned class` to indicate that instances of this type must always be held in an owned binding. All fields of an owned type must themselves be owned types or value types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/owned-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/owned-type.cs}}
```
{{#endtab }}
{{#endtabs }}

The `owned` modifier on the class declaration is erased in C# — it's a compile-time contract that the Mist compiler enforces. An owned binding must receive its value from a constructor call, an owned-returning function, or a move from another owned binding.

## Move Semantics

When you assign an owned value to another owned binding, ownership *transfers* — the source variable enters a "moved" state and can no longer be accessed. The compiler emits a diagnostic error on any use-after-move.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/move-semantics.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/move-semantics.cs}}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, moves look like ordinary assignments. The safety guarantee is purely compile-time: if you try to use `h1` after the move to `h2`, the Mist compiler rejects the program with a use-after-move error. The C# runtime never sees invalid access because the compiler prevents it from being emitted.

Attempting to use a moved variable produces a compile-time error:

```mist
{{#include ../../snippets/advanced/ownership/use-after-move.mist}}
```

## Borrowed

A `borrowed` parameter receives exclusive mutable access to a value for the duration of the call. While the borrow is active, the owner cannot access the variable at all — it transitions to the "LentRef" state. Only one mutable borrow is permitted at a time.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/borrowed.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/borrowed.cs}}
```
{{#endtab }}
{{#endtabs }}

The `borrowed` keyword is erased in C# — at runtime it's a normal parameter pass. The compile-time guarantee is that no other code can access the lent variable while the borrower holds it, preventing concurrent mutation bugs.

A borrowed recipient can re-lend the value further — as `readonly ref`, `frozen`, or even `borrowed` to another function:

```mist
{{#include ../../snippets/advanced/ownership/re-lend.mist}}
```

## Frozen

A `frozen` parameter provides an immutable view of a value. While the frozen borrow is active, the owner transitions to the "LentFrozen" state — it can still read the value and take additional readonly refs or frozen borrows, but it cannot mutate, move, or lend as borrowed until the frozen borrow ends.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/frozen.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/frozen.cs}}
```
{{#endtab }}
{{#endtabs }}

Frozen borrows are non-storable — you cannot assign a frozen reference to a field or return it from a function. This ensures the immutability guarantee is scoped to the call duration.

## Readonly Ref

A `readonly ref` parameter provides a live readonly view of a value. The callee can read but not mutate. Unlike `frozen`, a readonly ref does not block the owner from mutating — the owner transitions to the "LentReadonly" state where it retains read and write access.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/readonly-ref.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/readonly-ref.cs}}
```
{{#endtab }}
{{#endtabs }}

Readonly refs are the lightest lending tier — they provide a read-only guarantee to the callee without restricting the owner. Like frozen and borrowed, readonly refs are non-storable.

## Lending Rules Summary

An owned binding can lend its value to any of the three lending tiers. Each lending tier places different restrictions on the owner while the borrow is active:

| Lending Tier | Owner State | Owner Can Read | Owner Can Write | Owner Can Lend Again |
|---|---|---|---|---|
| `readonly ref` | LentReadonly | Yes | Yes | Yes (readonly ref, frozen) |
| `frozen` | LentFrozen | Yes | No | Yes (readonly ref, frozen) |
| `borrowed` | LentRef | No | No | No |

After the lending call returns, the owner transitions back to the Live state with full access restored.

## Value Types Satisfy All Tiers

Value types (structs) satisfy any ownership tier because they are copied on assignment and parameter passing. There is no aliasing concern — each binding holds its own independent copy.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/value-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/value-types.cs}}
```
{{#endtab }}
{{#endtabs }}

Because structs are passed by value, the ownership annotations are semantically satisfied by the copy — the original is never aliased. This makes value types universally compatible with all ownership tiers without any special handling.
