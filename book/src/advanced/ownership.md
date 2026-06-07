# Ownership

Ownership in Mist is a way to guarantee that a value has exactly one owner. An owned value is the sole, unique reference to its entire object graph — no other binding aliases it, and no one can mutate it behind your back. The compiler tracks ownership at every point in your program and rejects code that would create a second owner or use a value after it has been given away.

The defining idea is that **ownership is a property of a binding, not of a type**. Any non-substructural type — whether it is a plain class, a struct, or a type the author marked `owned` — can be held in an `owned` binding and gain unique-ownership tracking. You do not need to design a type up front to participate in ownership; you opt in at the point where you declare the variable.

The entire system is compile-time. Ownership annotations are erased in the emitted C# — there is no reference counting, no runtime state, and no hidden allocations. The only runtime artifact ownership ever introduces is the occasional `.Clone()` call inserted at a move boundary, which we cover at the end of this chapter.

## Binding-Level Ownership

You create an owned binding with the `owned` specifier on a variable declaration. The binding guarantees that the variable is the unique owner of the value and its whole object graph.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/owned-binding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/owned-binding.cs}}
```
{{#endtab }}
{{#endtabs }}

An owned binding must be initialized with a value that yields unique ownership. There are three sources the compiler accepts directly:

- A constructor call (`new Foo(...)`) — a freshly allocated value that no one else holds.
- A call to a function whose return type is declared `owned` — the callee handed you a fresh value.
- A move from another owned binding — ownership transfers from the source to the destination (see [Move Semantics](#move-semantics)).

In addition, a `default(T)` expression (or a bare `default` when the target type is known) counts as a unique-ownership source. `default` produces a freshly allocated, fully independent value graph, so it is equivalent to a constructor call for ownership purposes.

If you try to initialize an owned binding from a value that is not uniquely owned and cannot be cloned, the compiler reports an error — the binding cannot prove it is the sole owner.

### Owned Bindings Work for Any Non-Substructural Type

The `owned` specifier is not restricted to types that were designed for it. A plain class, a struct, or an `owned` type can all be held in an owned binding. The type's declaration does not have to mention ownership at all.

Substructural types are the one exception. Types declared `linear`, `affine`, or `relevant` already carry their own usage discipline through drop semantics, so layering ownership tracking on top is neither necessary nor sound. Applying the `owned` binding specifier to a substructural type is a compile error.

## Method Visibility on Owned Bindings

What you can *do* with a value held in an owned binding depends on whether the value's type opts into the ownership cooperation contract. This is where the distinction between an ordinary (non-owned) type and an `owned` type matters.

### Non-Owned Types Appear Readonly

When a non-owned type is held in an owned binding, the instance appears **readonly** from the binding's perspective. You can call readonly methods — methods that do not mutate any field of the receiver — but mutable methods are hidden. Calling a mutable method is a compile error.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/non-owned-readonly.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/non-owned-readonly.cs}}
```
{{#endtab }}
{{#endtabs }}

The reasoning is that an ordinary type made no promise about how its mutations behave. A mutable method might stash an external alias into a field, quietly breaking the uniqueness guarantee the owned binding depends on. Rather than trust an unverified type, the compiler hides its mutable surface. You still have two safe ways to change the value: declare the type `owned` (the next section), or assign its fields directly under the provenance rule (see [Field Assignment Provenance](#field-assignment-provenance)).

### Owned Types Get Full Access

When a type is declared `owned`, the compiler permits *all* method calls on it in an owned binding — both readonly and mutable. The full mutable surface is available.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/owned-type-full-access.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/owned-type-full-access.cs}}
```
{{#endtab }}
{{#endtabs }}

This is not blind trust. An `owned` type's mutable methods are available precisely because the compiler has *verified* that the type honors its cooperation contract, described next.

## The Cooperation Contract

Declaring a type `owned` is a behavioral promise: the type author guarantees that every mutation inside the type maintains ownership invariants. Concretely, every field assignment inside the type's methods and constructors must come from an owned source.

The compiler enforces this contract at the type's definition site. For each assignment `this.field = source` inside an `owned` type, the source must be one of: a move from an owned parameter or binding, a constructor call, a call returning an owned value, a `default` expression, or a value that can be cloned via `IClonable<T>`. A value-type or primitive source is always fine because copying it cannot introduce an alias.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/cooperation-contract.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/cooperation-contract.cs}}
```
{{#endtab }}
{{#endtabs }}

If a method or constructor in an `owned` type assigns a field from a non-owned source that cannot be cloned — for example, storing an unverified parameter directly into a field — the compiler reports a contract violation, naming the method, the field, and the offending source. The fix is to make the parameter `owned`, clone it, or construct a fresh value.

An `owned` type may declare fields of *any* type. There is no restriction that fields must themselves be owned types — the contract is about behavior, not structure. The `owned` modifier applies to classes and structs; applying it to an interface or trait is an error.

## Field Assignment Provenance

Beyond method calls, you can update a field of an owned-bound instance directly with an assignment. This is the mechanism that lets even non-owned types in owned bindings have their state changed safely. The rule is **provenance**: the source of a field assignment on an owned binding must itself be owned.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/field-provenance.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/field-provenance.cs}}
```
{{#endtab }}
{{#endtabs }}

The provenance rule treats sources exactly like an owned binding initializer:

- A constructor call, an owned-returning call, a move from an owned binding, or a `default` expression — accepted as owned.
- A value type (struct) or primitive — accepted without restriction, because copying it cannot introduce an alias.
- A non-owned reference value that implements `IClonable<T>` — accepted, with an implicit `.Clone()` inserted.
- Anything else — a non-owned reference with no way to become owned — is a compile error.

This single rule is why field assignment is safe even on a non-owned type's instance: the assignment cannot smuggle an external alias into the owned graph, because the source must already be (or become) owned.

## Move Semantics

Ownership can be transferred from one binding to another. This is a **move**: after the source gives ownership away, the source binding is dead and any further use of it is a compile error. This prevents two bindings from believing they each own the same value.

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

A move happens whenever an owned value flows to a new home:

- Assigning an owned binding to another binding (`owned var y = x;` or `var y = x;`) moves ownership; `x` becomes dead.
- Passing an owned binding to a parameter declared `owned` moves ownership into the callee.
- Returning an `owned` value transfers ownership to the binding at the call site.

After a move, reading, writing, passing, or taking a reference to the moved binding produces a **use-after-move** error that names the binding, where it was moved, and where the invalid access happened.

Moves interact with control flow. If a binding is moved in one branch of a conditional but not another, it is treated as moved at the join point after the conditional. Moving a binding inside a loop body is an error unless the binding is reassigned a new owned value within the same iteration after the move.

A move is only permitted when the binding is **Live** — the sole owner with exclusive access. If the binding currently has an outstanding lend (it is lent as `borrowed`, `frozen`, or `readonly ref`), moving it is rejected until the lend returns. This is part of the broader ownership state machine that governs lending, which is covered in [Readonly Contexts](readonly-contexts.md).

In the emitted C#, a move is just an ordinary assignment. There is no nulling of the source and no runtime move tracking — the safety was all proven at compile time.

## IClonable: Bridging Shared to Owned

Sometimes you have a value that is *not* owned — a shared reference, or a value lent to you — and you need to produce an owned value from it. The bridge is the standard-library interface `IClonable<T>`:

```mist
interface IClonable<T>
{
    owned T Clone();
}
```

`Clone()` returns an `owned T` — a fresh, fully independent value graph. Because the return is owned, the result is exactly what every move boundary needs.

### Implicit Cloning

You rarely call `Clone()` by hand. Wherever a value must move into an owned destination — an owned binding initializer, a field assignment on an owned binding, or an `owned` parameter — and the source is not already owned, the compiler checks whether the source's type implements `IClonable<T>`. If it does, the compiler inserts a `.Clone()` call automatically. If it does not, and the source is not already owned, the compiler reports an error suggesting you implement `IClonable<T>`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ownership/iclonable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ownership/iclonable.cs}}
```
{{#endtab }}
{{#endtabs }}

Implicit cloning is the single, uniform mechanism by which a non-owned value becomes owned. It applies identically at every move boundary, so you never have to remember different rules for bindings, fields, and parameters.

### Cloning from a Lent Value

When the source is a value held in a lent tier (`borrowed`, `frozen`, or `readonly ref`) whose type implements `IClonable<T>`, the compiler still inserts the implicit clone. Cloning reads *through* the reference to build a fresh value — it does not consume or move the underlying value, so the lender keeps its value after the lend returns. This lets you populate an owned destination from data you were only lent.

### Auto-Derivation

You do not have to write `Clone()` for most types. The compiler **auto-derives** `IClonable<T>` for a type when all of its fields have types that implement `IClonable<T>`. The bootstrap value types (`Int32`, `Boolean`, `Float64`, and the rest) and `String` implement `IClonable<T>` as base cases. The rule applies recursively: a type whose fields are all clonable is itself clonable, regardless of whether those fields are structs or classes.

If a type has even one field whose type is not clonable, the compiler does not auto-derive `IClonable<T>` for it. You must then implement `Clone()` manually, or the type is simply not clonable and cannot be used as a shared-to-owned source.

In the emitted C#, `IClonable<T>` is an ordinary interface with a `Clone()` method, and the compiler emits an explicit `.Clone()` call at each site where implicit cloning occurs — except for pure value-type structs, where C#'s value semantics already provide the copy and the call can be optimized away.

## Summary

- Ownership is a property of a **binding** (`owned var`), not of a type. Any non-substructural type can be owned.
- A **non-owned type** in an owned binding appears readonly — only readonly methods and provenance-checked field assignments are available.
- An **`owned` type** opts into a cooperation contract the compiler verifies, earning full mutable method access in owned bindings.
- **Field assignment provenance** requires the source of a field assignment on an owned binding to be owned (or a value type, or cloned).
- **Moves** transfer ownership and leave the source dead; using a moved binding is a compile error.
- **`IClonable<T>`** bridges shared values to owned ones, with the compiler inserting clones implicitly at move boundaries and auto-deriving the interface when all fields are clonable.
- The whole system is compile-time only; the sole runtime artifact is an occasional `.Clone()` call.
