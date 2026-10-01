# Ref Parameters

Mist provides three ways to pass parameters by reference, each with different guarantees about what the callee can do with the value. These range from a full mutable reference to a storage location (`ref`) to ownership-aware specifiers that enforce borrowing rules at compile time (`borrowed` and `readonly ref`).

| Specifier | Callee can read | Callee can mutate | Caller blocked during call | Emitted to C# |
|-----------|:-:|:-:|:-:|----------------|
| `readonly ref` | ✓ | ✗ | No | erased (compile-time only) |
| `borrowed` | ✓ | ✓ | Yes — no access until return | erased (compile-time only) |
| `ref` | ✓ | ✓ | No | C# `ref` |

## `readonly ref` Parameters

The `readonly ref` specifier declares that the callee receives a live, read-only view of the caller's variable. The callee can read the value and call readonly methods on it, but cannot mutate it. The view is non-storable — it cannot be assigned to a field or returned from the function.

Because `readonly ref` is an ownership-tier constraint, it is enforced entirely at compile time. The emitted C# contains no `ref` keyword — the parameter is passed by value at the runtime level.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ref-parameters/readonly-ref.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ref-parameters/readonly-ref.cs}}
```
{{#endtab }}
{{#endtabs }}

While a `readonly ref` borrow is active, the owner enters the *LentReadonly* state. In this state the owner can still read and write its own variable, but cannot move it or lend it as `borrowed` until the readonly borrow ends. After the call returns, the owner regains full access.

## `borrowed` Parameters

The `borrowed` specifier grants the callee exclusive mutable access to the caller's variable for the duration of the call. While the borrow is active, the caller cannot read, write, or lend the variable — only one mutable borrow is permitted at a time. This prevents data races and aliasing bugs at compile time.

Like `readonly ref`, `borrowed` is erased in the emitted C#. The enforcement is purely compile-time.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ref-parameters/borrowed.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ref-parameters/borrowed.cs}}
```
{{#endtab }}
{{#endtabs }}

A `borrowed` recipient can re-lend the value to other functions — as `readonly ref`, `frozen`, or even `borrowed` again — but only one outstanding mutable borrow exists at any point in the call chain.

## `ref` Parameters

The `ref` specifier creates a true mutable reference to a storage location, mirroring C#'s `ref` parameter semantics. Unlike `readonly ref` and `borrowed`, `ref` is not erased — it emits directly as a C# `ref` parameter. The caller must explicitly pass the argument with the `ref` keyword.

Use `ref` when you need to mutate a value type in place (since value types are copied on assignment) or when you need to reassign the caller's variable to a different object entirely.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ref-parameters/ref-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ref-parameters/ref-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

With `ref`, the caller's storage location is directly aliased. Mutations through the reference are immediately visible to the caller — there is no copy. This is essential for value types like structs, where pass-by-value would create an independent copy that the callee's mutations wouldn't affect.

Because the reference aliases storage, what you pass with `ref` has to be storage: a variable, a field, an array or span element, or a result that is itself returned by `ref`. A computed value has nowhere to alias, so passing one — a call that returns a value, a property, an element of a `string` or a `List<T>` — is refused with `MIST_PA_REF_NOT_A_LOCATION`. The same holds for a `ref` local's initializer and for a `ref` return.

## `ref` with Reference Types

When `ref` is applied to a reference type parameter, the callee can reassign the caller's variable to point to a different object entirely. Without `ref`, the callee could mutate the object's fields but could not change which object the caller's variable refers to.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/ref-parameters/ref-class.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/ref-parameters/ref-class.cs}}
```
{{#endtab }}
{{#endtabs }}

## Choosing Between the Three

- Use **`readonly ref`** when a function needs to observe a value without modifying it, and you want the compiler to enforce that guarantee. The caller retains access during the call.
- Use **`borrowed`** when a function needs exclusive mutable access and you want the compiler to prevent the caller from accessing the value concurrently. This is the strongest ownership-based guarantee.
- Use **`ref`** when you need true pass-by-reference semantics at runtime — mutating value types in place, reassigning the caller's variable, or working with ref returns and ref locals.

The ownership specifiers (`readonly ref` and `borrowed`) are part of Mist's compile-time safety system and have zero runtime cost. The `ref` specifier maps directly to C#'s `ref` and carries the same runtime semantics.
