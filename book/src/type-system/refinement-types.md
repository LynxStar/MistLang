# Refinement Types

Refinement types let you express value constraints directly in the type system. A refinement type narrows an existing base type by attaching a predicate — only values satisfying that predicate belong to the refined type. This gives you compile-time rejection of invalid literals and runtime validation via pattern matching, without writing manual guard code.

Unlike [type aliases](./type-aliases.md) (which are transparent names) or [newtypes](./newtypes.md) (which create distinct wrapper types), refinement types keep the same underlying representation as their base type but restrict which values are valid.

## Simple Refinements

A simple refinement type applies a single relational predicate to a base type using the syntax `type Name = BaseType predicate;`. The predicate uses a comparison operator and a constant value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/simple.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/simple.cs}}
```
{{#endtab }}
{{#endtabs }}

The `type` declaration itself produces no C# output — refinement types are erased to their base type. `AdultAge` becomes `int` everywhere in the emitted code. The compiler verifies at compile time that literal values like `25` and `1` satisfy the predicates `>= 18` and `> 0` respectively.

## Compound Refinements

A compound refinement type combines multiple predicates using the `and` combinator. All predicates must hold simultaneously for a value to satisfy the type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/compound.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/compound.cs}}
```
{{#endtab }}
{{#endtabs }}

The compound predicate `>= 0 and <= 100` constrains the value to the inclusive range [0, 100]. The compiler rejects literals outside this range at compile time — assigning `-1` or `101` to a `Percentage` variable would produce a diagnostic error.

## Property Pattern Refinements

A refinement type can constrain properties of a class or struct type using property pattern syntax. This lets you express structural invariants on complex types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/property-pattern.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/property-pattern.cs}}
```
{{#endtab }}
{{#endtabs }}

The property pattern `{ Start: >= 0 }` constrains the `Start` property of `Range`. In the emitted C#, the `is ValidRange` check becomes a direct property comparison (`r.Start >= 0`), and the binding variable is assigned the original value.

## Compile-Time and Runtime Enforcement

Refinement constraints are enforced in two ways depending on context:

- **Compile time**: When you assign a literal value to a refinement-typed variable, the compiler checks the predicate statically. Invalid literals are rejected with a diagnostic error before the program runs.
- **Runtime**: When the value isn't known at compile time (e.g., user input or computed values), you validate it using the `is` pattern matching expression. The `is` check emits the predicate as a runtime condition.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/enforcement.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/enforcement.cs}}
```
{{#endtab }}
{{#endtabs }}

The `is AdultAge validAge` expression emits as the predicate condition `userInput >= 18`. If the condition holds, the binding variable `validAge` is assigned the scrutinee value. For compound refinements, the emitted condition joins all predicates with `&&` (e.g., `x >= 0 && x <= 100` for `Percentage`).

## Refinements as Variable, Parameter, Return, and Field Types

Refinement types can appear anywhere a regular type can — as variable types, method parameters, return types, and class fields. When used as a parameter type, the compiler emits a runtime guard at method entry that throws `ArgumentException` if the value violates the predicate.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/all-positions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/all-positions.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that the parameter `AdultAge age` emits as `int age` with a guard at method entry: `if (!(age >= 18)) throw new ArgumentException(...)`. This ensures that even if a caller passes an unchecked `int`, the constraint is enforced at runtime. Variable types, field types, and return types are simply erased to the base type without guards — the compiler relies on static analysis and `is` checks to ensure correctness at those positions.

## Arithmetic Produces the Base Type

When you perform arithmetic on a refined value, the result has the base type rather than the refinement type. This is because arithmetic can produce values outside the refinement's valid range — adding 5 to an `AdultAge` might still be a valid adult age, but the compiler can't prove it statically.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/arithmetic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/arithmetic.cs}}
```
{{#endtab }}
{{#endtabs }}

The expressions `a + 5` and `a - 2` both produce `int` even though `a` is `AdultAge`. To re-establish the refinement after computation, validate the result with `is AdultAge`. This design prevents unsound assumptions — subtracting from an `AdultAge` could produce a value below 18, so the type system conservatively widens to the base type.
