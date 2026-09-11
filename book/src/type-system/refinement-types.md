# Refinement Types

Refinement types let you express value constraints directly in the type system. A refinement type narrows an existing base type by attaching a predicate — only values satisfying that predicate belong to the refined type. This gives you compile-time rejection of invalid values and runtime validation via pattern matching, without writing manual guard code.

Unlike [type aliases](./type-aliases.md) (which are transparent names) or [newtypes](./newtypes.md) (which create distinct wrapper types), refinement types keep the same underlying representation as their base type but restrict which values are valid.

Refinement predicates use the same clause syntax as [pattern matching](./pattern-matching.md) — relational operators, `and` combinators, and property patterns. The key difference is that pattern clauses test a value at runtime and branch on the result, while refinement predicates declare a constraint that the compiler enforces statically wherever possible.

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

The compiler checks literal assignments against the predicate at compile time. Since `AdultAge` (`>= 18`) is a stricter constraint than `Positive` (`> 0`), any value satisfying `AdultAge` also satisfies `Positive` — so an `AdultAge` can be assigned to a `Positive`, but not the other way around.

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

## Union Types with Refinements

A refinement type can use a union base type where each constituent has its own predicate. The value must satisfy at least one constituent's constraint to be valid.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/union-refinement.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/union-refinement.cs}}
```
{{#endtab }}
{{#endtabs }}

Each constituent in the union carries its own predicate. The literal `5` satisfies the `int >= 3 and <= 8` arm, while `50` satisfies the `byte >= 0 and < 64` arm.

## Property Pattern Refinements

A refinement type can constrain properties of a class or struct type using property pattern syntax. This lets you express structural invariants on complex types. Only `readonly` fields can participate in property pattern refinements — mutable fields could change after validation, breaking the invariant.

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

The property pattern `{ Start: >= 0 }` constrains the `Start` property of `Interval`. Because `Interval` is declared `readonly`, the compiler can guarantee the constraint holds for the lifetime of the value after validation.

## Everything is Compile-Time

Refinement constraints are enforced at compile time whenever possible. When you assign a literal value to a refinement-typed variable, the compiler checks the predicate statically — invalid literals are rejected with a diagnostic error before the program runs.

For values that aren't known at compile time (user input, computed values, function results), you validate them using the `is` pattern matching expression. The `is` check evaluates the predicate at runtime and branches on the result.

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

The `is AdultAge validAge` expression validates the predicate `>= 18` against `userInput`. If it holds, the binding variable `validAge` is available with the refined type.

## Pattern Matching — Narrowing and Widening

Refinement types integrate with Mist's flow-sensitive type narrowing. When a branch condition implies a refinement predicate, the compiler narrows the variable's type within that branch. Widening in the opposite direction — from a refinement type back to its base type — is always valid since a refined value is always a valid value of the base type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/pattern-narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/pattern-narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

The `if (x >= 18)` condition is recognized as implying the `AdultAge` predicate, so `x` can be assigned to an `AdultAge` variable within that branch. Subset relationships also apply: `DrinkingAge` (`>= 21`) implies `AdultAge` (`>= 18`), so a `DrinkingAge` can always be assigned to an `AdultAge`.

## Refinements as Variable, Parameter, Return, and Field Types

Refinement types can appear anywhere a regular type can — as variable types, method parameters, return types, and class fields. When used as a parameter type, every call is checked at compile time: the argument must be a value the compiler can prove satisfies the predicate, exactly as an assignment must.

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

Parameters, variables, fields, and return types all rely on the same static analysis: a literal is evaluated against the predicate, a refined value is checked by implication, and any other value must first be narrowed by an `is` check or by a condition that proves the predicate. Nothing is deferred to runtime, so a call that cannot be proven is a compile error at the call site rather than an exception in the callee.

## Arithmetic Produces the Base Type

When you perform arithmetic on a refined value, the result has the base type rather than the refinement type. This is because arithmetic can produce values outside the refinement's valid range — the compiler can't prove the result still satisfies the predicate.

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

## Ad-Hoc Refinement Constraints

Beyond named refinement types, Mist supports **ad-hoc refinement constraints** — `where` clauses that attach predicates directly to method parameters, type parameters, constructors, and indexers without declaring a named type.

### Method Parameter Constraints

A `where` clause on a method parameter constrains which values can be passed at that call site. The compiler verifies literals and known values at compile time.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-parameters.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-parameters.cs}}
```
{{#endtab }}
{{#endtabs }}

### Type Argument Constraints

Ad-hoc refinements can also constrain dependent type parameters. This is useful for types whose behavior depends on a compile-time integer value (like fixed-size collections).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-type-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-type-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

### Abstract Type Contracts

Abstract types can declare parameter refinements as part of their contract. Implementing classes may use the same constraint, a weaker constraint (accepting more values), or no constraint at all. A weaker constraint is valid because it accepts a superset of the abstract type's required inputs — this satisfies the Liskov substitution principle.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-abstracts.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-abstracts.cs}}
```
{{#endtab }}
{{#endtabs }}

An implementor cannot use a **stronger** constraint than the abstract type declares — that would reject values the abstract type promises to accept.
