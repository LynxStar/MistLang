# Control Flow

Mist provides the standard control flow constructs you'd expect from a C-family language: conditionals with `if`/`else`, counting loops with `for`, condition-based loops with `while`, collection iteration with `foreach`, and early exit with `break`. The syntax maps directly to C#, so if you've written control flow in C# or Java, you'll feel right at home.

## If / Else If / Else

The `if` statement evaluates a boolean condition and executes the corresponding block. You can chain multiple conditions with `else if` and provide a fallback with `else`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/if-else.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/if-else.cs}}
```
{{#endtab }}
{{#endtabs }}

The condition must be enclosed in parentheses and the body must use braces. Each branch is a block — there is no braceless single-statement form.

## While Loops

A `while` loop repeatedly executes its body as long as the condition evaluates to `true`. The condition is checked before each iteration.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/while-loop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/while-loop.cs}}
```
{{#endtab }}
{{#endtabs }}

## For Loops

The `for` loop provides a compact syntax for counted iteration. It takes an initializer, a condition, and an iterator expression, separated by semicolons.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/for-loop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/for-loop.cs}}
```
{{#endtab }}
{{#endtabs }}

The initializer declares a loop variable scoped to the `for` block. The condition is evaluated before each iteration, and the iterator runs after each iteration completes.

## Foreach Loops

The `foreach` loop iterates over each element in a collection. You declare the element type and name, followed by `in` and the collection expression. The collection expression must implement `IEnumerable<TElement>` from the Standard namespace — the compiler verifies this at compile time and infers the iteration variable's type from the resolved `TElement`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/foreach-loop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/foreach-loop.cs}}
```
{{#endtab }}
{{#endtabs }}

The loop variable is scoped to the `foreach` block and takes on the value of each element in sequence.

### User-Defined Enumerables

You can iterate over any user-defined type that implements `IEnumerable<TElement>` from the standard library. This requires two abstract types:

```mist
// From the standard library
abstract IEnumerator<TElement>
{
    bool MoveNext();
    TElement Current { get; }
}

abstract IEnumerable<TElement>
{
    type Enumerator : IEnumerator<TElement>;
    Enumerator GetEnumerator();
}
```

`IEnumerator<TElement>` defines the iteration protocol — `MoveNext()` advances to the next element (returning `false` when exhausted) and `Current` yields the current value. `IEnumerable<TElement>` declares an [associated type](../type-system/associated-types.md) slot `Enumerator` that must implement `IEnumerator<TElement>`, and a `GetEnumerator()` method that returns it.

When you write `foreach (int n in collection)`, the compiler calls `GetEnumerator()` on the collection, then repeatedly calls `MoveNext()` and reads `Current` until the enumerator is exhausted.

Here's a complete example — a `Countdown` type that counts down from a starting value:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/foreach-enumerable.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/foreach-enumerable.cs}}
```
{{#endtab }}
{{#endtabs }}

`Countdown` implements `IEnumerable<int>` with a nested struct `Enumerator` that satisfies the associated type slot. The `foreach` loop desugars into a `GetEnumerator()` call followed by a `while (enumerator.MoveNext())` loop reading `enumerator.Current`.

## Break

The `break` statement exits the innermost enclosing loop immediately. Execution continues with the first statement after the loop.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/break.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/break.cs}}
```
{{#endtab }}
{{#endtabs }}

This loop prints `i = 0`, `i = 1`, and `i = 2`, then exits when `i` reaches 3.

## Nested Control Flow

Control flow constructs can be freely nested. Here's a common pattern — a loop containing a conditional that filters which iterations produce output:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/control-flow/nested.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/control-flow/nested.cs}}
```
{{#endtab }}
{{#endtabs }}

This iterates over an array and accumulates only the elements greater than 5. Since Mist's control flow maps directly to C#, the emitted code is identical — the compiler passes these constructs through without transformation.
