# Stack-Only Spans

Mist provides `Span<T>` and `ReadOnlySpan<T>` — lightweight, stack-only views over contiguous memory. Combined with the `stackalloc` keyword, they let you allocate fixed-size buffers directly on the stack frame without involving the garbage collector. These types are [ref structs](../object-model/structs.md#ref-structs) and follow all the same heap-escape restrictions.

## The `stackalloc T[n]` Syntax

The `stackalloc` keyword allocates `n` elements of type `T` on the current stack frame and returns a `Span<T>` (or `ReadOnlySpan<T>` when the target binding is read-only):

```mist
Span<int> buffer = stackalloc int[4];
```

The allocated memory is zero-initialized and lives for the duration of the enclosing method — no allocation, no GC pressure, no heap involvement.

### Length: Known vs Unknown

When `n` is a compile-time integer literal, the compiler knows the span's length statically:

```mist
Span<int> known = stackalloc int[4];  // Length is known (4) at compile time
```

When `n` is a runtime expression, the length is unknown at compile time:

```mist
Span<int> dynamic = stackalloc int[count];  // Length is unknown until runtime
```

This distinction affects bounds checking: a known-length span gets full static bounds validation (the compiler rejects out-of-range constant indices), while an unknown-length span defers the upper bound to a runtime check — the same behavior arrays have today.

## The `Span<T>` Alias

`Span<T>` is a convenience alias for `Span<T, unknown>` — a span whose length is not tracked statically. This is the form you'll use most often in method signatures and variable declarations. The full two-parameter form `Span<T, Length>` is used internally by the compiler for static bounds analysis.

Similarly, `ReadOnlySpan<T>` aliases `ReadOnlySpan<T, unknown>`.

## Stampable Element Restriction

Only **stampable** types are valid as `stackalloc` element types. A type is stampable if it's safe to create N copies of its default value in contiguous memory:

| Element type | Allowed? |
|---|---|
| Primitive types (`int`, `bool`, `double`, etc.) | ✅ |
| Structs whose instance storage is recursively stampable | ✅ |
| Closed generics with stampable substituted storage | ✅ |
| Type parameters constrained by `where T : stampable` | ✅ |
| Reference types (classes, abstract types) | ❌ |
| Non-stampable structs (structs with reference-type fields) | ❌ |
| Ref structs | ❌ |
| Unconstrained type parameters | ❌ |

The compiler uses the same [stampability proof](../type-system/stampability.md) for bare arrays and stack allocation. The optional declaration modifier is not required; concrete and closed generic element types can qualify by inference. A `struct` constraint alone cannot prove the absence of reference-bearing fields.

## The Span API

### `Length` and Indexer

Every span exposes a `Length` property and an indexer for element access:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/stack-only-spans/stackalloc-basics.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/stack-only-spans/stackalloc-basics.cs}}
```
{{#endtab }}
{{#endtabs }}

### `Slice`

`Slice(start, length)` returns a new span over a sub-range of the same backing memory. Writes through the slice are visible through the original span (and vice versa) because both reference the same stack allocation.

### `foreach`

Spans implement the enumerator protocol, so you can iterate their elements with `foreach`:

```mist
foreach (int v in buffer)
{
    Console.WriteLine(v);
}
```

## Bounds Checking

For **known-length** spans (literal `stackalloc` size), the compiler performs full static bounds validation:

- A constant index outside `[0, Length)` is rejected at compile time.
- A non-constant index gets a lower-bound check (`>= 0`) at compile time and an upper-bound check at runtime.

For **unknown-length** spans (runtime-computed size), the compiler validates only the lower bound (`>= 0`) statically and defers the upper bound to the CLR at runtime — identical to array indexing behavior.

## `ReadOnlySpan<T>`

When the target binding is typed as `ReadOnlySpan<T>`, the compiler produces a read-only span. The indexer provides read access but rejects writes:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/stack-only-spans/readonly-span.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/stack-only-spans/readonly-span.cs}}
```
{{#endtab }}
{{#endtabs }}

Attempting to assign through a `ReadOnlySpan<T>` indexer produces a compile-time error.

## Frame-Escape Rule

Because `stackalloc` memory lives on the current stack frame, the resulting span **must not escape that frame**. The compiler enforces this with frame-escape analysis:

| Operation | Allowed? |
|---|---|
| Read/write through the span locally | ✅ |
| Pass the span as a method argument | ✅ |
| Iterate with `foreach` | ✅ |
| Slice and use the result locally | ✅ |
| Store in a ref struct local that was itself declared from stack memory | ✅ |
| Return the span, a slice of it, or a ref struct holding it | ❌ |
| Return a `ref` to one of its elements | ❌ |
| Store it in a local declared from heap memory or from nothing | ❌ |
| Store it in a parameter, in a field of `this`, or in a field of a ref struct declared from heap memory | ❌ |
| Pass it to a method that could store it in such a ref struct (a non-`readonly` method of one, or one taking it by `ref`) | ❌ |
| Store in a field of a heap object | ❌ |
| Store in a static variable | ❌ |
| Capture in a lambda or closure | ❌ |

The backing travels with the value: a slice, a ref struct constructed from the span, a field read from such a ref struct, a `switch` arm and a pattern binding all carry it. A ref struct local keeps the lifetime its declaration gave it, as C# requires, so a local declared from heap memory cannot later take a stackalloc-backed span even if it never leaves the method; declare it from `stackalloc` instead. If a stackalloc-backed span escapes the frame, the compiler rejects the program at the offending expression: `MIST0265` for a return, `MIST0266` for a store, `MIST0267` for a call that could store it.

Note that spans received as method **parameters** or obtained from `AsSpan()` on arrays are *not* frame-restricted — they reference heap memory and can be used freely within the existing ref-struct rules.

## Interop with .NET

`Standard.Span<T>` and `Standard.ReadOnlySpan<T>` are backed by `System.Span<T>` and `System.ReadOnlySpan<T>` respectively. When a .NET method returns a `System.Span<T>`, it projects into Mist as `Span<T>` (unknown length). Conversely, passing a Mist `Span<T>` to a .NET method that expects `System.Span<T>` requires no conversion — they are the same type at runtime.
