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

Linear and affine structs, including ref structs and constructed structs, move like their class counterparts: initialization or assignment to another local, an `owned` argument, a `with owned` receiver call, storage into an owned field or array literal, return, and `Drop()` consume the source binding, so later reads, writes, passes and member accesses are errors even though the backend physically copies the value.

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

An `owned` type additionally enforces a **field-type restriction**: every field must have a type that is another `owned type`, implements `IClonable<T>`, or is a value type. This guarantees full ownership of the entire object graph. See [Owned Fields](#owned-fields) for details. The `owned` modifier applies to classes and structs; applying it to an abstract type is an error.

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

## Owned Fields

So far, ownership has been about the *binding* — `owned var x` makes the entire value graph of `x` uniquely owned. But what about the individual fields inside an object? The `owned` modifier can be placed on a field declaration to opt that specific field into ownership semantics, regardless of whether the containing type is itself an `owned type`.

This unlocks **jagged ownership**: some fields of an object participate in ownership discipline (provenance-checked writes, extraction only through owned bindings) while other fields remain freely shared. You get the safety of ownership exactly where you need it, without forcing the entire type to be declared `owned`.

### Owned Field Declaration

Mark a field with the `owned` specifier to make it ownership-protected:

```mist
public class Document
{
    // This field is ownership-protected
    public owned string Content;

    // This field is freely shared — no ownership tracking
    public int Version;

    public Document(owned string content, int version)
    {
        this.Content = content;
        this.Version = version;
    }
}
```

An `owned` field enforces two rules:

1. **Provenance on writes** — any value written into the field must come from an owned source (a constructor call, a move from an owned binding, an owned-returning function, or a clone via `IClonable<T>`). This is the same provenance rule as field assignment on owned bindings, but now it applies to the specific field at all times. Where the source is a shared value of a clonable type, the compiler inserts the `.Clone()` itself — at a field initializer, in a constructor or method assignment, and in an object initializer — so the field receives an independent copy. A shared value whose type cannot be cloned is refused at the write.
2. **Extraction requires an owned parent** — moving out of, borrowing, freezing, or taking a readonly ref of the field is only permitted when the parent binding is owned. Through a shared parent, you can write (with provenance) and read: a read of a clonable field through a shared parent is compiled as a clone, and a read of a field whose type cannot be cloned is refused, since the only way to hand it out would be to alias it.

The `owned` modifier is valid on fields of any reference type. It can also appear on value-type fields (structs and primitives) without error, though it has no behavioral effect since value types are copied. Static fields cannot be marked `owned` — ownership requires an instance-level owner.

### Jagged Ownership

The power of owned fields is that ownership can be *partial*. A type doesn't need to commit fully to being an `owned type` — it can protect specific fields while leaving others freely aliasable:

```mist
// Non-owned type with selective ownership protection
public class Connection
{
    // Protected — only mutated through ownership
    public owned Socket Handle;

    // Freely shared — anyone can read or write
    public string Label;
    public int RetryCount;
}
```

Through an owned binding to a `Connection`, you get full access to all fields including the owned `Handle`. Through a shared binding, you can still write to `Handle` (provided you supply an owned source) and clone-read it (if the type is `IClonable`), but you cannot move it out or take a reference to it. The non-owned fields `Label` and `RetryCount` are unrestricted regardless of binding tier.

This jagged pattern is the common case — most types have a mix of "sensitive state that must not be aliased" and "metadata that can be freely shared."

### The This-Tier System

When a method touches owned fields, the compiler needs to know what level of access the caller must provide. Rather than the blunt "hide all mutable methods" rule, owned fields use a precise **this-tier** system.

The compiler infers the minimum ownership tier required on `this` for each method by analyzing its body:

- A method that **writes to** or **moves out of** an owned field requires at least `borrowed` access.
- A method that **freezes** an owned field requires at least `frozen` access.
- A method that only **reads** owned fields requires at least `readonly ref` access.
- A method that doesn't touch owned fields requires no tier — it's callable from any binding.

If a method calls a helper on `this`, the helper's tier requirement composes transitively: calling a `borrowed`-requiring helper makes the outer method also require `borrowed`. Passing `this` to a parameter composes the same way: a method that lends `this` to a `borrowed` parameter requires `borrowed` access, and one that lends it to a `frozen` parameter requires `frozen`. A method whose declared tier is lower than such a lend is refused at the lend, as a method declared `with frozen` cannot pass `this` where `borrowed` is needed. Inside a constructor `this` is owned, so a constructor may lend it at any tier.

```mist
public class Account
{
    public owned int Balance;

    // Inferred: requires 'borrowed' (writes to owned field)
    public void Deposit(owned int amount)
    {
        this.Balance = this.Balance + amount;
    }

    // Inferred: requires 'readonly ref' (only reads owned field)
    public string Summary()
    {
        return $"Balance={Balance}";
    }
}
```

At the call site, the compiler checks that the receiver's binding tier satisfies the method's requirement:

| Caller binding | Can call methods requiring |
|---|---|
| `owned` | Any tier (None, ReadonlyRef, Frozen, Borrowed, Owned) |
| `borrowed` | None, ReadonlyRef, Frozen, Borrowed |
| `frozen` | None, ReadonlyRef, Frozen |
| `readonly ref` | None, ReadonlyRef |
| shared (no tier) | None only |

A shared binding can call `Summary()` (no ownership needed) but cannot call `Deposit()` (requires `borrowed`). An owned binding can call both.

### Explicit `with` Annotation

Sometimes you want to declare a method's this-tier explicitly rather than relying on inference. Use the `with` clause:

```mist
public class Resource
{
    public owned Handle Data;

    // Explicitly declares frozen tier
    public string Peek() with frozen
    {
        return Data.Describe();
    }

    // Explicitly declares owned tier — consumes the receiver
    public Handle Consume() with owned
    {
        return this.Data;
    }
}
```

The `with` annotation overrides inference. If you declare a tier that is *less* restrictive than what the body actually needs (e.g., `with frozen` but the body writes to an owned field), the compiler reports an error. Over-constraining is always valid — you can declare `with owned` on a method that only reads, if you want to communicate that calling it consumes the receiver.

A method declared `with owned` consumes `this`: the caller's binding is moved into the method and becomes dead after the call. This is analogous to passing the receiver as an `owned` parameter.

### The `owned type` Field-Type Restriction

When a type is declared `owned`, the compiler implicitly treats **all** its instance fields as `owned`. This means every field gets provenance enforcement and extraction restrictions automatically — you don't need to mark each one.

But `owned type` goes further with a **field-type restriction**: every field of an `owned type` must have a type that is:

- Another `owned type`, or
- A type implementing `IClonable<T>`, or
- A value type (struct or primitive).

This guarantees that `owned type` provides **full** ownership of the entire object graph. No field can hold an arbitrary shared reference that might alias external state.

```mist
// Valid: all fields satisfy the restriction
owned class Vault
{
    public owned string Secret;     // String implements IClonable<String>
    public int AccessCount;         // Value type — always accepted
    public owned Vault Inner;       // Another owned type — accepted
}

// Invalid: Widget is not owned and not IClonable
// owned class Broken
// {
//     public Widget data;  // ERROR: MIST_OWN_FIELD_TYPE_VIOLATION
// }
```

The restriction is evaluated after `IClonable<T>` auto-derivation, so types that become clonable through the derivation mechanism are accepted.

### Solving Static Aliasing and Non-Owned Constructors

Owned fields solve two long-standing problems directly through their provenance enforcement:

**The static aliasing problem**: A static field holds a shared reference. Without owned fields, code could read a static and store it into a field of an owned object, silently introducing an external alias into the owned graph. With owned fields, the provenance rule blocks this: a static read is not an owned source, so writing it into an owned field is a compile error.

**The non-owned constructor problem**: A constructor of a non-owned type might store its parameters into fields without provenance checking. When that type is later held in an owned binding, those fields might contain shared references. With owned fields, the provenance rule applies at the field level — if a field is `owned`, every assignment to it (including in constructors) must come from an owned source, regardless of the containing type's status.

Both problems dissolve because the `MoveBoundaryResolver` applies uniformly to all owned-field assignments. No special case is needed — the rule is simply "to write into an owned field, provide an owned value."

### Per-Field State Tracking and Partial Moves

When an instance is held in an owned binding, each owned field has its own independent ownership state. You can move a value out of one field while leaving sibling fields untouched:

```mist
public class Pair
{
    public owned string Left;
    public owned string Right;
}

owned var p = new Pair("alpha", "beta");

// Move Left out — Left enters Moved state, Right is still Live
owned var extracted = p.Left;

// Sibling field is unaffected
Console.WriteLine(p.Right);  // fine — Right is Live

// Restore Left by writing a new owned value
p.Left = "restored";  // Left returns to Live
```

**State transitions for fields** mirror the standard ownership states: Live, LentReadonly, LentFrozen, LentRef, and Moved. Each field transitions independently.

**Reading into a shared destination is a move**: `var x = p.Left` moves the value out of `Left` (the field enters Moved), and so does passing `p.Left` to an owned parameter or storing it into another owned field. Reading it into an *owned* binding is different when the field's type implements `IClonable<T>`: the compiler inserts a `.Clone()` there, the binding gets an independent copy and the field stays Live. For a copy anywhere else, write `p.Left.Clone()` yourself. A field whose value cannot change, such as a `string`, is never tracked: reading it is a copy.

**Leases on a field** work as leases on a binding do. `frozen var f = p.Left` leaves `Left` lent frozen until the block that declared `f` ends: `Left` may be read and frozen again but not written, borrowed or moved, while `Right` is untouched. A `borrowed` lease locks the field entirely for its duration. A lease on the parent constrains every field in the same way.

**Composite state** restricts whole-object operations. The parent's composite state is the most restrictive state among all its owned fields. If any field is Moved or lent borrowed, you cannot pass the parent as a whole to another binding or parameter — the object is incomplete, and even a plain read of the whole is refused. If a field is lent frozen, the parent may still be read, frozen or viewed as a whole, but not moved, borrowed or written. You can always access sibling fields and restore the moved field.

**Method calls look at the fields they touch**: calling a method on the parent is refused when the method reads or writes an owned field that is moved or lent borrowed, or writes one that is lent frozen, counting the fields its helpers reach on `this`. A method that touches only Live fields is callable while a sibling is out.

**Sibling independence**: field A being borrowed does not prevent operations on field B. Each field is tracked independently. The composite state only restricts operations on the parent *as a whole*.

**Field restoration**: writing a new owned value into a Moved field brings it back to Live. The parent binding itself remains alive (addressable on the stack) even when its composite state is Moved — this is what makes restoration possible.

### Ownership Inference on Local Bindings

With owned fields and the this-tier system in place, the compiler can often determine the right ownership tier for a local variable without an explicit annotation. When you declare a variable with `var`, the compiler infers the minimum tier that satisfies all uses:

```mist
// Inferred as 'owned' — new yields ownership, and Deposit requires borrowed
var acct = new Account("Alice", 100);
acct.Deposit(50);  // requires borrowed → var infers owned (which satisfies borrowed)

// Inferred as 'shared' — no ownership operations needed
var label = "hello";
var alias = label;  // both coexist, no move → shared
```

The inference algorithm tries tiers from most permissive to least: `owned` → `borrowed` → `frozen` → `readonly ref` → `shared`. It selects the first tier that doesn't produce a contradiction. `shared` is always a valid fallback — it never conflicts because it imposes no restrictions.

The inference operates within a single function scope and computes a fixed-point over all `var` bindings simultaneously. Variables that depend on each other's tiers (e.g., `var y = x` constrains both) are resolved together.

If you want a specific tier for documentation or API clarity, use an explicit specifier: `owned var x = ...`. Explicit annotations override inference and are validated against all uses — if you declare `owned` but the variable is used in a way that contradicts ownership, the compiler reports an error.

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

Passing an owned binding to an *ordinary* parameter — one declared neither `owned` nor `borrowed`, `frozen` or `readonly ref` — is not a move, but it is a handoff: the callee may keep what it was given, so the compiler no longer treats the binding as the only holder of its value. The binding stays usable, and so does its value: you can read it, mutate it and pass it again. What it can no longer do is make an ownership claim — move into another `owned` binding, an `owned` parameter, an `owned` field or a `with owned` receiver, or be returned from an `owned`-returning method — until it is reassigned a fresh owned value. The same holds for an `owned` field of an owned binding handed to such a parameter: the holder cannot be moved or returned owned until that field is written afresh. A lent parameter gives the value back when the call returns and leaves the binding Live; a value of a type that cannot change, such as a `string`, is never affected.

```text
error MIST_PA_OWNED_RETURN_NOT_OWNED: 'Make' returns owned but this value is not: 'c' was handed to parameter 'c' of 'Keep' without a move, so another holder may still reach it.
```

In the emitted C#, a move is just an ordinary assignment. There is no nulling of the source and no runtime move tracking — the safety was all proven at compile time.

## IClonable: Bridging Shared to Owned

Sometimes you have a value that is *not* owned — a shared reference, or a value lent to you — and you need to produce an owned value from it. The bridge is the standard-library abstract type `IClonable<T>`:

```mist
abstract IClonable<T>
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

A source that already owns its value is not cloned again: a construction, a call returning `owned` (an explicit `Clone()` among them), an owned binding, and a cast whose conversion hands over an owned value -- a newtype's conversion that runs a `Clone` body (see [Clonable Class Sources](../type-system/newtypes.md#clonable-class-sources)), or a conversion operator declared `owned`. So `owned Jaguar kept = (Jaguar)tiger;` runs one `Clone`, not two. A newtype's field-by-field copy is not such a source: the copy's fields are still the operand's, so an owned destination clones it once.

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
- **Owned fields** let you protect individual fields with ownership semantics. Writes require provenance; extraction requires an owned parent binding.
- The **this-tier system** infers per-method access requirements from owned-field usage, replacing blanket method hiding with precise call-site checks. Use `with {tier}` to annotate explicitly.
- **Jagged ownership** — a type with some fields `owned` and some not — gives you targeted protection without requiring `owned type` on the whole class.
- An **`owned type`** additionally enforces a field-type restriction: all fields must be owned types, implement `IClonable<T>`, or be value types — guaranteeing full graph ownership.
- **Per-field state tracking** enables partial moves and sibling independence: move one field out while the rest remain accessible, and restore it later.
- **Ownership inference** on `var` bindings finds the minimum tier satisfying all uses, with `shared` as the ultimate fallback.
- **Moves** transfer ownership and leave the source dead; using a moved binding is a compile error.
- **`IClonable<T>`** bridges shared values to owned ones, with the compiler inserting clones implicitly at move boundaries and auto-deriving the abstract type when all fields are clonable.
- The whole system is compile-time only; the sole runtime artifact is an occasional `.Clone()` call.
