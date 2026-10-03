# Optional Types

Optional types represent values that may be present or absent. In Mist, there is no `null` — instead, absence is expressed using `void`. An optional type is simply a union of some type `T` with `void`, written concisely as `T?`.

This chapter covers the `T?` suffix syntax, the `Optional<T>` standard library alias, and three operators for working with optional values: `?.` (optional chaining), `??` (void-coalescing), and `??=` (void-coalescing assignment).

## The `T?` Suffix

Appending `?` to any type creates an optional type — syntactic sugar for `T | void`. The variable can hold either a value of type `T` or `void` (absence).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/optional-types/optional-suffix.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/optional-types/optional-suffix.cs}}
```
{{#endtab }}
{{#endtabs }}

The `?` suffix works on any type — primitives, classes, generics, and an element of a variadic pack (`TArgs[1]?`, see [Variadics](./variadics.md#pack-elements-in-optionals-and-unions)). `int?` is `int | void`, `MyClass?` is `MyClass | void`, `Result<int>?` is `Result<int> | void`.

**Precedence:** The `?` suffix binds tighter than `|` but looser than `[]`. This means:
- `int[]?` is `(int[]) | void` — an optional array
- `int?[]` is `(int | void)[]` — an array of optionals
- `int? | string` is `(int | void) | string` — a three-way union

Applying `?` to a type that already contains `void` is a compile-time error — there's no such thing as "double optional" in Mist.

## The `Optional<T>` Type Alias

The standard library provides `Optional<T>` as a named alias for `T | void`. It's interchangeable with `T?` — use whichever reads better in context.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/optional-types/optional-alias.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/optional-types/optional-alias.cs}}
```
{{#endtab }}
{{#endtabs }}

`Optional<T>`, `T?`, and `T | void` are all the same type structurally. You can assign between them freely — no conversion needed.

Since `Optional<T>` is a transparent type alias (not a wrapper class), there are no `.HasValue` or `.Value` members. Use pattern matching, `??`, or `?.` to work with the value.

## Optional Chaining (`?.`)

The `?.` operator conditionally accesses a member or calls a method on an optional value. If the receiver is `void`, the entire expression short-circuits to `void` without evaluating the member access.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/optional-types/optional-chaining.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/optional-types/optional-chaining.cs}}
```
{{#endtab }}
{{#endtabs }}

The result type of a `?.` expression is always optional — if the method returns `R`, the `?.` expression returns `R | void`. This makes sense: the receiver might be `void`, so the result might be too.

You can chain multiple `?.` operators in a single expression (e.g., `a?.b?.c?.Method()`). If any link in the chain is `void`, the rest short-circuits and the entire expression evaluates to `void`.

The `?.` operator requires the receiver to be an optional type. Using `?.` on a non-optional value is a compile-time error — use plain `.` instead.

## Void-Coalescing (`??`)

The `??` operator provides a fallback value when the left operand is `void`. If the left side has a value, it's returned directly; if it's `void`, the right side is evaluated and returned instead.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/optional-types/void-coalescing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/optional-types/void-coalescing.cs}}
```
{{#endtab }}
{{#endtabs }}

The result type of `??` is the non-void portion of the left operand's type. If the left is `int | void` and the right is `int`, the result is `int` — the void possibility has been eliminated.

The `??` operator uses short-circuit evaluation: the right operand is only evaluated when the left is `void`. This is important when the right side has side effects.

`??` is right-associative, so `a ?? b ?? c` is parsed as `a ?? (b ?? c)`. This lets you chain multiple fallbacks naturally.

A common pattern combines `?.` with `??` to provide a default when optional chaining produces void:

```mist
int result = item?.GetValue() ?? -1;
```

## Void-Coalescing Assignment (`??=`)

The `??=` operator assigns a value to a variable only if that variable is currently `void`. After the assignment, the compiler narrows the variable's type to exclude `void` for all subsequent code in the same scope.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/optional-types/void-coalescing-assignment.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/optional-types/void-coalescing-assignment.cs}}
```
{{#endtab }}
{{#endtabs }}

The flow narrowing is the key benefit of `??=`. After `x ??= 100`, the compiler knows `x` cannot be `void` — either it already had a value, or it was just assigned one. This means you can use `x` as a plain `int` without any further checks.

Rules for `??=`:
- The left operand must be a mutable variable with an optional type
- The right operand must be assignable to the non-void portion of the left's type
- `??=` is a statement — it cannot be used as a sub-expression
- The right operand is only evaluated if the left is `void` (short-circuit semantics)
