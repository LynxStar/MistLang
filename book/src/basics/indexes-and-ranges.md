# Indexes and Ranges

Mist provides the two C# 8.0 "slicer" operators: the index-from-end operator `^` and the range operator `..`. Together they let you address elements relative to the end of a collection and extract contiguous slices without computing offsets by hand. Both operators transpile to native `System.Index`, `System.Range`, and slicing, so the compiled output behaves exactly like hand-written C# 8.0.

## Index From End: `^`

The `^` operator is a prefix operator applied to an integer. It produces a position counted from the end of a collection rather than the start. `^1` is the last element, `^2` the second-to-last, and `^0` is the position one past the last element (equal to the length).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/index-from-end.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/index-from-end.cs}}
```
{{#endtab }}
{{#endtabs }}

For an indexable collection of length `L`, `^k` resolves to the offset `L - k`. The operand can be any integer expression: a literal (`^1`), an identifier (`^count`), a member access (`^items.Length`), or a parenthesized expression (`^(count + 1)`).

## Ranges: `..`

The `..` operator produces a `Range` used for slicing. The start bound is inclusive and the end bound is exclusive, so `numbers[1..4]` selects the elements at offsets 1, 2, and 3. All four bound forms are supported: both bounds, start-only, end-only, and the full range.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/range-forms.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/range-forms.cs}}
```
{{#endtab }}
{{#endtabs }}

| Form | Meaning |
|------|---------|
| `a..b` | from `a` (inclusive) to `b` (exclusive) |
| `a..` | from `a` through to the end |
| `..b` | from the start up to `b` (exclusive) |
| `..` | the full range (every element) |

An absent bound means "the start of the sequence" (absent start) or "the end of the sequence" (absent end). A range with an absent bound is never ambiguous: the missing side is filled in from the collection's length.

### From-End Bounds in Ranges

Either bound of a range may itself be an index-from-end expression. Because `^` binds tighter than `..`, you can write `1..^1` to trim one element from each end, or `^3..` to take the final three elements.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/from-end-bounds.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/from-end-bounds.cs}}
```
{{#endtab }}
{{#endtabs }}

Range bounds are unary-level expressions. A bound composed with a binary operator needs parentheses: `..(n - 1)` is a range whose end is `n - 1`, whereas `..n - 1` parses as `(..n) - 1`, matching C# 8.0 precedence.

## Slicing Semantics

Slicing honors inclusive-start and exclusive-end, and the result type depends on what you slice.

### Arrays Produce a New Array

Slicing an array copies the selected elements into a new array. Mutating the slice does not affect the source, and vice versa.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/array-slice.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/array-slice.cs}}
```
{{#endtab }}
{{#endtabs }}

### Spans Produce a View

Slicing a `Span<T>` or `ReadOnlySpan<T>` produces a view over the same backing memory rather than a copy. Writes through the sliced span are visible through the original span. See [Stack-Only Spans](../advanced/stack-only-spans.md) for more on spans.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/span-slice.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/span-slice.cs}}
```
{{#endtab }}
{{#endtabs }}

### Strings Produce a New String

Slicing a `string` produces a new string containing the selected characters. A from-end index on a string reads a single character.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/string-slice.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/string-slice.cs}}
```
{{#endtab }}
{{#endtabs }}

## Storing Index and Range Values

An index-from-end expression is a first-class value of type `Index`, and a range expression is a first-class value of type `Range`. Both types live in the `Standard` namespace and map to `System.Index` and `System.Range`. You can store them in variables, parameters, fields, and return values, then reuse them in an element access.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/stored-index-range.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/stored-index-range.cs}}
```
{{#endtab }}
{{#endtabs }}

## Indexable and Sliceable Receivers

Index-from-end access requires a countable, indexable receiver, and range slicing requires a sliceable receiver:

- **Indexable** types expose a length (`Length` or `Count`) and an integer indexer: arrays, `Span<T>`, `ReadOnlySpan<T>`, `string`, and `List<T>`.
- **Sliceable** types produce a contiguous sub-sequence from a range: arrays, `Span<T>`, `ReadOnlySpan<T>`, `string`, and any countable type exposing a `Range` indexer or a `Slice(int, int)` method.

A type of your own becomes sliceable the way it does in C#. A range is handed whole to an indexer that takes a `Range`; failing one, it reaches a `Slice(int, int)` instance method as the start and the length, decoded against the type's `Length` (or `Count`, when it has no `Length`). The slice's type is whatever that indexer or method returns, and a list pattern's slice goes through the same member. Both routes need the count, and the count, the indexer and `Slice` must all be accessible where the slice is written.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/indexes-and-ranges/user-sliceable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/indexes-and-ranges/user-sliceable.cs}}
```
{{#endtab }}
{{#endtabs }}

Using `^` on a non-indexable receiver, or `..` on a non-sliceable receiver, is a compile-time error. The same holds for the operand of `^` and the bounds of `..`: an operand must be an integer, and each present range bound must be an integer or an `Index`.
