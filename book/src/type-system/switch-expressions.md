# Switch Expressions

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

When a parenthesized switch is the receiver of a member access or call, its arms determine the receiver's type before the member is resolved. A switch whose arms produce strings can therefore produce an `int` through `.Length` in an explicitly typed local, return, or argument. The expected type applies to that final member result. When the switch itself supplies the value to a typed position, its arms bind against that position's target type.

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

String constants require a `_` catch-all arm to ensure exhaustiveness — a finite set of constants never covers every string. Over an integer, a `char`, a `bool` or an enum the compiler searches the arms' value sets exactly: constant arms alone leave a gap, and the warning names a value no arm matches, but relational arms that partition the type (`< 0`, `0`, `> 0`) are exhaustive without a catch-all, as are boolean switches with both `true` and `false` arms. An optional scrutinee such as `bool?` also needs its `void` case: `true` and `false` arms cover only the present values.

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

An arm whose every value the earlier arms already match can never run, and is a compile-time error. Earlier arms count together, endpoints count exactly, and an arm with a `when` guard covers nothing, since its guard may fail. Here every value above 10 is already above 0:

```mist
// error MIST_PA_SWITCH_ARM_UNREACHABLE: This switch arm is unreachable: an earlier arm already matches everything it could.
var size = count switch
{
    > 0 => "some",
    > 10 => "many",
    _ => "none",
};
```

A `double` keeps its catch-all reachable after `< 0.0` and `>= 0.0`, because `NaN` matches neither.

## Tuple Positional Pattern Arms

Tuple positional patterns match tuple elements by position using parenthesized syntax. Each element in the pattern corresponds to the element at the same position in the tuple, and can use any subpattern — constants, relational, logical, discard, or var patterns. This provides a concise way to branch on multiple tuple elements simultaneously without referencing `Item1`, `Item2`, etc.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/tuple-positional.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/tuple-positional.cs}}
```
{{#endtab }}
{{#endtabs }}

Each arm's pattern is a tuple positional pattern that tests both elements of `coord`. The switch evaluates arms top-to-bottom — the first matching pattern determines the result. A catch-all arm using `(var _, var _)` ensures exhaustiveness when earlier arms don't cover all possible values. Without one, the compiler checks the product of the element domains: over `(bool, bool)`, arms `(true, _)` and `(false, true)` leave `(false, false)` uncovered and the switch is reported with that cell named, while arms that together cover every combination — including relational partitions of integer elements such as the nine quadrant cells — are exhaustive. An element whose pattern the compiler cannot read, such as a property pattern, still needs a catch-all.

## Tuple Positional Patterns in Switch Arms

Tuple positional patterns can also match enum values by position, which is particularly useful for state-machine transitions and multi-field matching. Instead of the verbose `{ Item1: ..., Item2: ... }` property pattern workaround, you write the patterns directly in parenthesized form — each element matches the corresponding tuple position.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/switch-expressions/tuple-positional-switch.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/switch-expressions/tuple-positional-switch.cs}}
```
{{#endtab }}
{{#endtabs }}

Each arm matches a specific combination of enum values in the tuple. You can mix enum constants with discard patterns (`_`) to match "any value" in a position — for example, `(Season.Winter, _)` matches any tuple where the first element is `Season.Winter` regardless of the second element. A plain `_` catch-all at the end covers all remaining combinations.
