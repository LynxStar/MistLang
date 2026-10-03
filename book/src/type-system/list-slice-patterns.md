# List and Slice Patterns

List patterns test a countable value positionally against an ordered sequence of element sub-patterns, and slice patterns match a contiguous run of elements within a list pattern. They mirror C# 11 list patterns and work everywhere the grammar accepts a pattern: the operand of an `is` expression and the pattern of a `switch` arm. For the individual sub-pattern forms (constant, relational, type, `var`, discard, property, tuple), see [Pattern Matching](pattern-matching.md).

## Fixed-Length List Patterns

A list pattern with no slice matches only when the receiver length equals the number of element sub-patterns and every element matches the value at its offset. A length mismatch fails the match outright.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/fixed-length.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/fixed-length.cs}}
```
{{#endtab }}
{{#endtabs }}

## Element Sub-Patterns

Each element position accepts any pattern. You can mix constants, relational patterns, the discard `_`, and `var` bindings freely. A `var` element binds the element at its offset to a new variable, and the elements it binds carry the receiver's element type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/element-subpatterns.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/element-subpatterns.cs}}
```
{{#endtab }}
{{#endtabs }}

Each element sub-pattern is checked against the element type, and a slice's sub-pattern against the slice type, exactly as a pattern on a value of that type would be. An element pattern that no element could ever match is a compile-time error at that element:

```mist
// error MIST_PA_PATTERN_CONSTANT_MISMATCH: A constant of type 'string' cannot be compared with a value of type 'int'.
// error MIST_PA_PATTERN_NEVER_MATCHES: A value of type 'int' can never be 'string', so this pattern never matches.
int[] numbers = [1, 2, 3];
if (numbers is ["x", ..] || numbers is [string first, ..])
{
}
```

## The Slice Pattern `..`

A slice pattern `..` inside a list pattern matches zero or more contiguous elements not consumed by the surrounding element sub-patterns. With a slice present, the pattern matches when the receiver length is at least the number of element sub-patterns: the patterns before the slice match from the start and the patterns after it match from the end. A bare `..` matches the intervening elements without binding them.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/slice-discard.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/slice-discard.cs}}
```
{{#endtab }}
{{#endtabs }}

## Slice Binding with `.. var name`

Adding a `var` binding to the slice (`.. var middle`) binds the matched contiguous sub-sequence to a variable. Because slicing an array produces a new array, the bound variable is itself an array of the receiver's element type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/slice-binding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/slice-binding.cs}}
```
{{#endtab }}
{{#endtabs }}

A slice sub-pattern requires a sliceable receiver. The binding follows the slice-result type: an array slice binds an array, a span slice binds a span, and a string slice binds a `string`.

## List Patterns in `switch` Arms

List patterns are valid `switch`-arm patterns, which makes them a natural way to branch on a collection's shape. The empty pattern `[]`, single-element `[_]`, and pair `[_, _]` distinguish lengths, and a `_` catch-all keeps the switch exhaustive.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/switch-arms.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/switch-arms.cs}}
```
{{#endtab }}
{{#endtabs }}

## The Empty List Pattern `[]`

The empty list pattern `[]` matches only a zero-length receiver. It is the shape test for "is this collection empty?"

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/list-slice-patterns/empty-list.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/list-slice-patterns/empty-list.cs}}
```
{{#endtab }}
{{#endtabs }}

## At Most One Slice Per List Pattern

A list pattern may contain at most one slice pattern. A single slice is what makes "match from both ends" unambiguous; a second slice would leave the boundary between them undefined. Two slices in one list pattern is a compile-time error:

```mist
// error MIST_PM_LIST_PATTERN_MULTIPLE_SLICES: A list pattern can spell at most one '..' slice, because the slice is what divides the elements counted from the front from those counted from the back.
if (numbers is [.. var a, .. var b])
{
}
```

You may still nest a list pattern (with its own single slice) inside a slice sub-pattern, since each list pattern is considered independently.
