# Switch Expressions

> **⚠️ Unvetted Content:** From this chapter onward, the book content is auto-generated from spec documents and sample code using Opus and has not been manually reviewed for accuracy. Descriptions may be incorrect or misleading. All code snippets are at least valid compilable Mist, and the `samples/` directory in the repository contains working programs that fully demonstrate each feature end-to-end — trust the code over the prose when in doubt.

Switch expressions provide a concise way to produce a value from multi-branch logic. Unlike traditional switch statements, a switch expression is an expression — it evaluates to a value and can be used anywhere an expression is expected, such as variable assignments or method arguments. The syntax uses a postfix `switch` keyword followed by a set of pattern arms.

Switch expressions in Mist must be exhaustive — the compiler requires that every possible value of the scrutinee is handled by at least one arm. If the arms don't cover all cases, the compiler reports an error. The `_` discard pattern serves as a catch-all to satisfy exhaustiveness when you can't (or don't want to) enumerate every possibility.

## Postfix Switch Syntax

A switch expression is written as `value switch { arms }`, where each arm maps a pattern to a result expression separated by `=>`. Arms are separated by commas. The `_` at the end is the catch-all arm — it matches anything not handled by the preceding arms, ensuring the switch is exhaustive.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/postfix-syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/postfix-syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

The switch expression evaluates `num` against each arm in order. The first arm whose pattern matches determines the result.

## Constant Pattern Arms

Constant patterns match a specific literal value — integers, strings, or booleans. Each arm compares the scrutinee against the constant and produces the corresponding result.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/constant-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/constant-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

Integer and string constants require a `_` catch-all arm to ensure exhaustiveness — the compiler can't prove that a finite set of constants covers all possible values. Boolean switches with both `true` and `false` arms are exhaustive without a catch-all.

## Regex Pattern Arms

Regex literals can be used directly as switch arms when the scrutinee is a `string`. Each arm tests whether the string matches the pattern. Since regex patterns can never prove they cover all possible strings, a `_` catch-all is always required.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/regex-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/regex-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

Arms are evaluated top-to-bottom — the first regex that matches wins. This makes ordering significant when patterns overlap.

## Type Pattern Arms

When the scrutinee is a type union, you can use type patterns to narrow the value and bind it to a variable. Each arm matches one constituent type of the union.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/type-patterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/type-patterns.cs}}
```
{{#endtab }}
{{#endtabs }}

In Mist, `int n` in a switch arm reads naturally — "if the value is an int, bind it to n." When all constituent types of the union are covered, the switch is exhaustive without a catch-all.

## Discard Pattern

The discard pattern `_` serves as a catch-all arm that matches any value not handled by preceding arms. Mist also supports `var _` as an alternative spelling with identical behavior.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/discard-pattern.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/discard-pattern.cs}}
```
{{#endtab }}
{{#endtabs }}

Both `_` and `var _` match any value without binding it. The `var` form becomes useful when you want to capture the value — `var x` binds the matched value to `x` for use in the result expression.

## When Guards

A `when` guard adds an additional boolean condition to a switch arm. The arm only matches if both the pattern and the guard condition are satisfied.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/when-guards.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/when-guards.cs}}
```
{{#endtab }}
{{#endtabs }}

The `when` keyword follows the pattern and precedes `=>`. The guard expression can reference any variable bound by the pattern (like `n` above). Guards are evaluated in order, so `n >= 100` is checked before `n >= 50`.

## Relational and Logical Patterns

Relational patterns (`<`, `<=`, `>`, `>=`) compare the scrutinee against a constant value directly in the pattern position. Logical patterns (`and`, `or`) combine multiple patterns into compound conditions.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/relational-logical.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/relational-logical.cs}}
```
{{#endtab }}
{{#endtabs }}

Relational patterns eliminate the need for `when` guards in many numeric scenarios. The `and` combinator creates range checks — `>= 80 and < 90` matches values in the half-open interval [80, 90). Arms are evaluated top-to-bottom, so order matters when ranges overlap.
