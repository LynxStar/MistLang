# Associated Types

Associated types let abstract types and traits declare abstract type slots — type members whose concrete definition is deferred to implementors. Where generic type parameters are chosen by the *caller* of a generic function or constructor, associated types are chosen by the *implementor* of an abstract type. This distinction makes associated types ideal for contracts where the implementing type determines a related type that callers shouldn't need to specify explicitly.

The primary motivating use case is zero-allocation iteration: an `Iterable<TValue>` abstract type declares an associated enumerator type slot, and each collection provides its own concrete struct enumerator. Generic code iterates without knowing the specific enumerator type, yet the compiler resolves it statically — no boxing, no virtual dispatch, no heap allocation for the enumerator.

## Declaration Syntax

An associated type slot is declared inside an abstract type or trait body using the `type Name : ConstraintList;` syntax. The colon and constraint list specify what the concrete type must satisfy, using the same constraint syntax as generic `where` clauses (see [Generic Constraints](./generic-constraints.md)).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/declaration-syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/declaration-syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

Here, `Output` is an associated type slot. Any type implementing `IProducer` must provide a concrete type named `Output` that implements `IStringable`. In the emitted C#, the slot becomes a synthesized type parameter with the constraint preserved as a `where` clause.

### Unconstrained Slots

If no constraints are needed, omit the colon entirely:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/unconstrained-slot.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/unconstrained-slot.cs}}
```
{{#endtab }}
{{#endtabs }}

An unconstrained slot places no restrictions on the concrete type — the implementor can provide any type.

### Multiple Constraints

A slot can require multiple constraints separated by commas. The concrete type must satisfy all of them:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/multiple-constraints.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/multiple-constraints.cs}}
```
{{#endtab }}
{{#endtabs }}

The constraint list supports the same forms as generic constraints: `struct`, `class`, abstract type references, trait references, base class references, `new()`, and combinations thereof.

### Trait Slots

Traits can also declare associated type slots with the same syntax. A type structurally conforming to the trait must provide the required nested type:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/trait-slot.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/trait-slot.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# is identical in shape — traits emit as C# interfaces, and the associated type slot becomes a type parameter with the same constraint propagation.

### Generic Type Parameters on Slots

Associated type slots can declare their own type parameters and where clauses:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/generic-slot.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/generic-slot.cs}}
```
{{#endtab }}
{{#endtabs }}

The slot's type parameters are independent of the enclosing abstract type's type parameters, though the constraint list can reference both. Here `Mapped<TOut>` is a higher-kinded slot — the implementor provides a generic type, and callers choose the type argument at each call site.

## Implementor Satisfaction

A class or struct satisfies an associated type slot by providing a concrete nested type that matches the slot's name and satisfies its constraints:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/implementor-abstract.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/implementor-abstract.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler verifies that `MessageProducer.Output` satisfies the constraint declared on the `Output` slot:
- It implements `IStringable` ✓

If the constraint is violated, the compiler emits an error naming the unsatisfied constraint, the concrete type, and the abstract type requiring it.

A struct kind constraint works the same way:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/implementor-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/implementor-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

Here the compiler verifies that `RangeIterator.Cursor` is declared as a `struct` ✓. Using a class would trigger a constraint violation error.

### What the Compiler Checks

For each associated type slot in an implemented abstract type, the type checker verifies:

1. **Existence** — The implementor provides a concrete type member with the same name (and arity, if the slot has type parameters). Missing slots produce a "does not provide a concrete type" error.
2. **Constraint satisfaction** — The concrete type satisfies every constraint in the slot's constraint list. Violations produce a "does not satisfy constraint" error identifying the specific failing constraint.
3. **Signature consistency** — Abstract type methods that reference the slot name in their signatures must be implemented with the concrete type in the corresponding positions.

## Generic Context Resolution

The real power of associated types emerges in generic code. When a type parameter is constrained by an abstract type with associated type slots, you can reference the associated type through dot notation on the type parameter:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/associated-types/generic-context.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/associated-types/generic-context.cs}}
```
{{#endtab }}
{{#endtabs }}

Inside this method, `T.Output` resolves to whatever concrete output type the caller's `T` provides. The compiler knows that `T.Output` satisfies `IStringable`, so it permits calling `Stringify()` without any casts.

In the emitted C#, notice how `T.Output` becomes a synthesized type parameter `TOutput` on the method, with the constraint propagated. At call sites the compiler substitutes the concrete type.

### What You Can Do with Associated Type References

Within a generic context, `T.SlotName` behaves like a type that satisfies the slot's constraints. You can:

- Declare local variables of type `T.SlotName`
- Use `T.SlotName` as a parameter type or return type
- Pass `T.SlotName` values to functions expecting the constraint abstract types
- Use `T.SlotName` as a generic type argument where the constraints are compatible

If you reference a name that isn't an associated type slot of any constraining abstract type (e.g., `T.NonExistent`), the compiler emits an error indicating that the type parameter doesn't have an associated type with that name.

## See Also

- [Generic Constraints](./generic-constraints.md) — The constraint syntax (`struct`, `class`, abstract type references, `new()`) used in associated type slot declarations is the same syntax used in generic `where` clauses. If you're unfamiliar with constraint syntax, start there.
