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

The `foreach` loop iterates over each element in a collection. You declare the element type and name, followed by `in` and the collection expression.

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

> **Note:** The Mist compiler does not currently verify that the collection expression is actually enumerable. The `foreach` statement is emitted directly to the backend, which performs its own enumeration resolution. With the C# backend, this means the collection must satisfy C#'s foreach requirements (implement `IEnumerable<T>`, or have a duck-typed `GetEnumerator()` method). If the expression is not enumerable, you will get a backend compilation error rather than a Mist compiler error.

> **Design intent:** The intended design is to back `foreach` using `Iterable<TValue>` and `IEnumerator<TValue>` interfaces with associated types, similar to Rust's `IntoIterator` / `Iterator` pattern. The `Iterable<TValue>` interface will declare an associated type slot for the enumerator (e.g., `type Enumerator : IEnumerator<TValue>, struct;`), allowing the compiler to resolve the concrete enumerator type statically and enabling zero-allocation iteration without boxing. See the [Associated Types](../type-system/associated-types.md) chapter for details.

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
