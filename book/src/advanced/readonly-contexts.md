# Readonly Contexts

Mist provides two mechanisms for enforcing immutability at specific points in your code: the `frozen` parameter modifier and the `readonly` block statement. Both are compile-time guarantees — the compiler verifies that no mutation occurs, and the enforcement is erased in the emitted C#. There is no runtime overhead.

These constructs complement the ownership system introduced in the previous chapter. Where ownership tiers control *who* can access a value and *how*, readonly contexts let you create scoped regions where mutation is structurally impossible.

## The `frozen` Parameter Modifier

The `frozen` modifier on a parameter declares that the callee receives an immutable, non-storable view of the argument. The callee can read fields and call readonly methods on the parameter, but cannot mutate it, store it, or pass it to a function expecting a mutable reference.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/readonly-contexts/frozen-parameter.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/readonly-contexts/frozen-parameter.cs}}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the `frozen` keyword disappears entirely. The parameter becomes a plain `Pair p`. All immutability enforcement happens at compile time — if `FreezeAndRead` tried to call `p.SetX(10)` or assign to `p.X`, the Mist compiler would reject it with an error.

## The LentFrozen State

When you pass an owned variable to a `frozen` parameter, the owner transitions to the **LentFrozen** state for the duration of the call. While in this state, the owner cannot:

- **Mutate** the variable (assign to its fields or call mutating methods)
- **Move** the variable (transfer ownership elsewhere)
- **Lend as borrowed** (pass it to a `borrowed` parameter)

The owner *can* still lend the variable as `frozen` or `readonly ref` to other callees — these are compatible because they also guarantee no mutation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/readonly-contexts/lent-frozen.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/readonly-contexts/lent-frozen.cs}}
```
{{#endtab }}
{{#endtabs }}

The LentFrozen state is the compiler's way of ensuring that while a frozen borrow is outstanding, the original owner cannot invalidate the callee's assumption of immutability. Once the call returns, the owner transitions back to the Live state and regains full access.

## The `readonly` Block Statement

The `readonly { }` block creates a lexical scope where all field access is treated as immutable. Inside the block, you cannot assign to fields, call mutating methods on `this`, or perform any operation that would modify state. You can still read fields, call readonly methods, and work with local variables.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/readonly-contexts/readonly-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/readonly-contexts/readonly-block.cs}}
```
{{#endtab }}
{{#endtabs }}

In the emitted C#, the `readonly` keyword is stripped and the block becomes a plain `{ }` scope. The immutability guarantee exists only at compile time — the Mist compiler rejects any mutation attempt inside the block before code generation.

## Readonly Blocks in Constructors

A particularly useful application of `readonly` blocks is inside constructors. Constructors normally have *constructor privilege* — they can assign to readonly fields and call setters. A `readonly` block inside a constructor **suspends** this privilege, creating a region where even the constructor cannot mutate fields.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/readonly-contexts/readonly-constructor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/readonly-contexts/readonly-constructor.cs}}
```
{{#endtab }}
{{#endtabs }}

This pattern is useful when a constructor needs to perform validation or computation after field initialization but wants to guarantee it doesn't accidentally modify the fields it just set.

## When to Use Each Construct

Use `frozen` parameters when you want to accept a reference to an object and promise the caller you won't modify it. This is stronger than `readonly ref` — a frozen borrow also prevents the callee from storing the reference or passing it to something that might mutate it later.

Use `readonly` blocks when you want to create a safe region within a method where you can reason about immutability locally. This is especially valuable in methods that mix mutation and reading — you can isolate the reading portion in a `readonly` block to make the intent explicit and compiler-verified.

Both constructs have zero runtime cost. They exist purely to help you communicate and enforce immutability invariants at compile time.
