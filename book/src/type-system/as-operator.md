# The As Operator

The `as` operator performs safe type conversion. Unlike cast operators which invoke user-defined conversion logic, `as` operates purely on type relationships — inheritance, union membership, and the `unknown` type. It never throws. If a conversion cannot be proven safe at compile time, the result is an optional type (`T?`) that you handle with pattern matching, `??`, or `?.`.

The key insight: the result type of `as` depends on what the compiler can prove statically.

| Conversion kind | Example | Result type |
|---|---|---|
| Identity | `dog as Dog` | `Dog` |
| Widening (derived → base) | `dog as Animal` | `Animal` |
| Narrowing (base → derived) | `animal as Dog` | `Dog?` |
| Union extraction | `union as int` | `int?` |
| Unknown narrowing | `unknown as int` | `int?` |
| Unrelated types | `dog as string` | **Compile error** |

## Identity Conversions

When the source and target types are the same, `as` is a no-op. The result type is `T` — no optional wrapping, no runtime check.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/identity.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/identity.cs}}
```
{{#endtab }}
{{#endtabs }}

Identity conversions are provably safe — the compiler knows the value is already the target type.

## Widening Conversions

When converting from a derived type to a base type (or to an implemented abstract type), the conversion always succeeds. The result type is the bare target type `T`, not `T?`. No runtime check is needed because every derived instance is already a valid base instance.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/widening.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/widening.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler uses the same `IsDirectlyAssignable` check that governs normal assignment. If the source type is assignable to the target type without user-defined operators, the `as` conversion is provable and yields bare `T`.

## Narrowing Conversions

When converting from a base type to a derived type, the conversion might fail at runtime — the base variable might hold a different derived type. In this case, `as` returns `T?` (i.e., `T | void`). If the runtime value matches, you get the value; otherwise, you get `void`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

The pattern here is: attempt the conversion with `as`, then check the result with `is` to extract the value. This is safe — no exceptions, no unexpected null.

## Union Narrowing

When a value has a union type (`A | B | C`) and you want to extract one constituent, `as` performs a runtime tag check. The result type is `T?` because the union might currently hold a different constituent.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/union-narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/union-narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

This complements `is` pattern matching: use `is` when you want to branch and bind inside a conditional, use `as` when you want the optional value as a standalone expression.

## Unknown Narrowing

The `unknown` type is Mist's top type — any non-void value can be assigned to `unknown`, but you can't use the value without narrowing it back to a concrete type. The `as` operator provides a way to do this: `unknownValue as T` yields `T?`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/unknown-narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/unknown-narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

Since `unknown` can hold any type, the compiler cannot prove the target type matches. The result is always `T?`.

## Composing with `??`

Because non-provable `as` conversions produce `T?`, they compose naturally with the void-coalescing operator `??` to provide a fallback value when the conversion fails:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/compose-void-coalescing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/compose-void-coalescing.cs}}
```
{{#endtab }}
{{#endtabs }}

The pattern `expr as T ?? fallback` is idiomatic Mist for "try to convert, use a default if it fails." The `??` eliminates the `void` possibility, giving you a plain `T` result.

## Composing with `?.`

The optional chaining operator `?.` works on the result of `as` to conditionally access members when the conversion succeeds:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/compose-optional-chaining.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/compose-optional-chaining.cs}}
```
{{#endtab }}
{{#endtabs }}

Parentheses are needed around the `as` expression because `?.` binds tighter than `as`. The result of `(animal as Dog)?.Breed` is `string?` — the breed if the animal is a dog, or `void` if it isn't.

## Difference from Cast Operators

The `as` operator and cast expressions (`(T)expr`) serve different purposes:

| | `as` operator | Cast expression |
|---|---|---|
| **Mechanism** | Type hierarchy check | User-defined operator call |
| **Failure mode** | Returns `void` (no exception) | Compile error if no operator exists |
| **User-defined operators** | Never invoked | Required for unrelated types |
| **Use case** | "Is this value a T?" | "Convert this value to T using custom logic" |

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/as-operator/vs-cast-operators.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/as-operator/vs-cast-operators.cs}}
```
{{#endtab }}
{{#endtabs }}

`as` checks whether a value already _is_ the target type (or a subtype). Cast operators _transform_ a value into a different type using logic you define. If two types have no inheritance relationship, no union membership, and no `unknown` involvement, `as` won't work between them — you need a cast operator for that.

## Invalid Conversions

The compiler rejects `as` expressions between types with no relationship:

```
Dog dog = new Dog("Rex");
string? s = dog as string;  // ERROR: no type relationship between Dog and string
```

The diagnostic tells you the types are unrelated and suggests using an explicit cast if a user-defined conversion is what you intended. This compile-time rejection prevents writing `as` conversions that would always produce `void`.

Additionally, `as void` and using `as` on a void-typed expression are both compile errors — they have no meaningful semantics.

## Precedence

The `as` operator has the same precedence as `is`:

- Binds **tighter** than equality: `x as int == 5` parses as `(x as int) == 5`
- Binds **looser** than arithmetic: `x + y as int` parses as `(x + y) as int`
- Is **non-associative**: `x as T1 as T2` is a parse error — write `(x as T1) as T2` instead
