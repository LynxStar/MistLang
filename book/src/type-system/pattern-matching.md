# Pattern Matching

Pattern matching in Mist lets you test values against shapes — types, properties, ranges, and structural layouts — and extract data in a single expression. Patterns appear on the right side of the `is` keyword inside `if` conditions. The compiler emits equivalent C# pattern matching syntax, so there is no runtime overhead beyond what C# itself provides.

## Type Patterns with Binding

The most common pattern tests whether a value is a specific type and binds the narrowed result to a new variable. The syntax is `value is Type name` — if the check succeeds, `name` is available inside the `if` block with the narrowed type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/type-binding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/type-binding.cs}}
```
{{#endtab }}
{{#endtabs }}

Inside the `if` block, `c` is a `Circle` — the compiler has narrowed the type from `Shape`. You can access `Circle`-specific members like `Radius` without any cast.

## Bare Type Patterns

When you only need to check whether a value is a certain type without binding it to a variable, use the bare type pattern: `value is Type`. This returns a boolean without introducing a new name.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/bare-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/bare-type.cs}}
```
{{#endtab }}
{{#endtabs }}

Bare type patterns are useful when you need to branch on type but don't need to access type-specific members.

## The `var` Pattern

The `var` pattern always matches and binds the value to a new variable without performing any type check. It's useful when you want to capture an intermediate result for readability or when combining with other patterns in logical expressions.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/var-pattern.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/var-pattern.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `var score` captures the return value of `ComputeScore()` and the `and > 0` relational pattern adds a condition. The `var` pattern itself never fails — it's the combinators that provide the filtering.

## Property Patterns

Property patterns match against an object's properties using subpatterns inside braces. The syntax is `value is { PropertyName: subpattern }`. You can match multiple properties by separating them with commas.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/property-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/property-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

Each property in the braces is checked against its subpattern. Here `Radius: > 5` uses a relational subpattern — the match succeeds only when the `Radius` property is greater than 5. You can combine multiple property checks:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/multi-property.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/multi-property.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `Width` and `Height` must satisfy their respective subpatterns for the overall match to succeed.

## Relational Patterns

Relational patterns compare a value against a constant using `>`, `<`, `>=`, or `<=`. They can appear standalone in an `is` expression or as subpatterns inside property patterns and list patterns.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/relational.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/relational.cs}}
```
{{#endtab }}
{{#endtabs }}

Relational patterns are most powerful when combined with logical combinators to express ranges, as shown above with `> 0 and < 100`.

## Logical Combinators

The `and`, `or`, and `not` combinators compose patterns into more complex conditions. They follow standard precedence: `not` binds tightest, then `and`, then `or`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/logical-combinators.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/logical-combinators.cs}}
```
{{#endtab }}
{{#endtabs }}

Use `and` to require multiple conditions simultaneously (range checks), `or` to accept any of several alternatives, and `not` to negate a pattern. These combinators work with any subpattern — relational, type, property, or constant.

## List Patterns

List patterns match arrays by element position. Each element in the pattern brackets is a subpattern that the corresponding array element must satisfy. The slice pattern `..` matches zero or more remaining elements.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/pattern-matching/list-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/pattern-matching/list-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

The pattern `[> 0, > 0]` matches an array of exactly two elements where both are positive. The pattern `[> 0, ..]` matches an array of one or more elements where the first is positive — the `..` slice absorbs any remaining elements regardless of count or value.

List patterns compose with all other subpatterns. You can use relational patterns, logical combinators, or even nested property patterns inside the element positions.
