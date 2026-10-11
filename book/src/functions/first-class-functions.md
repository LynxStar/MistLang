# First-Class Functions

Mist treats functions as values. You can store them in variables, pass them as arguments, return them from other functions, and compose them freely. This is enabled by the `func` type family and lambda expressions — a concise syntax for creating anonymous functions inline.

## The `func` Keyword

The `func` keyword declares a function type. It uses angle brackets to specify parameter types followed by the return type (always the last type argument):

- `func<TResult>` — zero parameters, returns `TResult`
- `func<T1, TResult>` — one parameter of type `T1`, returns `TResult`
- `func<T1, T2, TResult>` — two parameters, returns `TResult`
- Up to 16 parameters are supported

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/func-keyword.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/func-keyword.cs}}
```
{{#endtab }}
{{#endtabs }}

The `func` keyword is a lowercase alias for the `Func` class family in the standard library, following the same pattern as `int` for `Int32` and `string` for `String`. You can use either form — `func<int, int>` and `Func<int, int>` resolve to the same type.

## The `Func` Type Family

The standard library declares `Func<params TArgs, TResult>` as an intrinsic family: the parameter pack contains the inputs, and the final argument is the result. These are structural types — two `Func` types with the same type arguments are always the same type, regardless of how they were declared.

The type arguments follow a consistent convention:
- All type arguments except the last are parameter types
- The last type argument is always the return type
- `Func<int, string, bool>` means "takes an `int` and a `string`, returns a `bool`"

## The `Action` Alias

`Action` is a type alias for function types that return `void`:

- `Action` = `func<void>` — zero parameters, no return value
- `Action<T>` = `func<T, void>` — one parameter, no return value
- `Action<T1, T2>` = `func<T1, T2, void>` — two parameters, no return value

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/action-alias.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/action-alias.cs}}
```
{{#endtab }}
{{#endtabs }}

`Action` is purely a convenience — it resolves to the same `Func` type with `void` as the last type argument. You can use `func<string, void>` and `Action<string>` interchangeably.

## Qualified CLR Callable Names

`System.Func<TResult>` and `System.Func<T1, ..., TResult>` resolve to the same structural types as `func<...>`. `System.Action` and `System.Action<T1, ...>` resolve to functions returning `void`. Qualified CLR names support zero through sixteen parameters, including when `using Standard;` is present.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/clr-callable-names.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/clr-callable-names.cs}}
```
{{#endtab }}
{{#endtabs }}

## Lambda Expressions

Lambdas are anonymous functions created with the `=>` operator. They come in two forms:

### Expression Lambdas

An expression lambda has a single expression as its body. The result of the expression is the return value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/expression-lambda.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/expression-lambda.cs}}
```
{{#endtab }}
{{#endtabs }}

Parameter syntax:
- Single parameter: `x => expr` (no parentheses needed)
- Multiple parameters: `(x, y) => expr`
- Zero parameters: `() => expr`
- Typed parameters: `(int x, int y) => expr`

### Block Lambdas

A block lambda has a brace-delimited body with statements. Use `return` to produce a value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/block-lambda.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/block-lambda.cs}}
```
{{#endtab }}
{{#endtabs }}

Block lambdas can contain any statements — conditionals, loops, variable declarations, and multiple return paths. When the return type is inferred, the return expressions must share a unique supported common type. Numeric widening, shared nominal bases, and established implicit conversion targets use the same inference as array elements and switch arms. Returns inside a nested lambda or local function belong to that callable, not the enclosing lambda. An explicit function target supplies the result type directly.

## Type Inference

Mist infers lambda types bidirectionally:

**Target-type inference** — when the target type is known (e.g., a `func<int, int>` variable), parameter types are inferred from the target:

```mist
func<int, int> f = x => x * 2;  // x is inferred as int
```

**`var` with explicit parameters** — when you use `var` and provide typed parameters, the compiler infers the full `func` type from the parameter types and the body's return type:

```mist
var f = (int x) => x * 2;  // inferred as func<int, int>
```

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/type-inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/type-inference.cs}}
```
{{#endtab }}
{{#endtabs }}

You cannot use `var` with untyped parameters — the compiler needs at least one direction to infer from:

```mist
var f = x => x * 2;  // Error: cannot infer parameter types
```

## Higher-Order Functions

Functions that accept or return other functions are called higher-order functions. The `func` type makes this natural:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/higher-order.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/higher-order.cs}}
```
{{#endtab }}
{{#endtabs }}

A function parameter typed as `func<int, int>` accepts any lambda, method group, or variable of that type. A function returning `func<int, int>` can return a lambda that captures enclosing state (a closure).

## Method Groups

When a named method's signature matches the expected `func` type, you can use the method name directly without wrapping it in a lambda:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/method-groups.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/method-groups.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler verifies that the method's parameter types and return type match the target `func` type. If they don't match, you get a compile-time error.

A generic method works the same way. Its type arguments come from the target: each type parameter is the type standing at the positions of the `func` type that mention it, the result included, so `func<int, int> same = Same;` is `Same<int>`. Where the target cannot settle them -- `Pick<T, R>(T | R)` against `func<int | string, string>` does not say which constituent is `T` -- state them after the name, with no call:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/generic-method-groups.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/generic-method-groups.cs}}
```
{{#endtab }}
{{#endtabs }}

Stated type arguments are read as such only where the expression ends there -- before `;`, `,`, `)`, `]` or `}` -- which is what keeps `Use(a < b, c > d)` two comparisons. A generic method converts by exact signature like any other, and an inferred or stated type argument is held to the method's `where` clauses.

A synchronous method group keeps its Mist `func` signature even when the method completes async work internally. The compiler adapts the task-returning C# implementation so the delegate returns the completed value. Instance receivers are captured once when the function value is formed, including receivers produced by a call. Generic targets retain their selected type arguments. An explicitly async method instead fills a task-returning function type.

## Static Lambdas

The `static` modifier on a lambda prevents it from capturing any enclosing variables. This guarantees no heap allocation for a closure object:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/static-lambda.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/static-lambda.cs}}
```
{{#endtab }}
{{#endtabs }}

A static lambda can only access:
- Its own parameters
- Static fields and methods
- Constants and type names

Attempting to reference an enclosing local variable, instance field, or `this` from a static lambda produces a compile-time error (`MIST_TC_0113`).

## Async Lambdas

The `async` modifier creates a lambda that returns `Task<T>` and can use `await`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/async-lambda.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/async-lambda.cs}}
```
{{#endtab }}
{{#endtabs }}

Async lambdas follow the same structured concurrency rules as async methods. You can also combine `static` and `async`: `static async (x) => await Compute(x)`.

A non-async lambda that calls an async or internally promoted method completes the call before returning its result. Its synchronous delegate signature stays unchanged; the C# backend uses a blocking completion adapter.

## Capture Semantics

Lambdas capture enclosing variables by reference. Mutations inside the lambda are visible outside, and vice versa:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/capture.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/capture.cs}}
```
{{#endtab }}
{{#endtabs }}

The closure shares the variable with the enclosing scope — it doesn't copy the value at the time of creation.

### Capture Restrictions

Mist's ownership system restricts what types can be captured to prevent safety violations:

| Type Category | Capturable? | Reason |
|---|---|---|
| Value types (`int`, `bool`, structs) | ✅ Yes | Copied into closure safely |
| Reference types (classes) | ✅ Yes | Shared reference is safe |
| `ref struct` types | ❌ No | Cannot escape the stack |
| `owned` types | ❌ No | Ownership transfer into closure is ambiguous |
| `ref` / `readonly ref` / `frozen ref` | ❌ No | Borrowed references cannot outlive their scope |

Attempting to capture a restricted type produces a compile-time error (`MIST_TC_0110` through `MIST_TC_0112`).

## Type Aliases for Function Types

You can define type aliases for `func` types to give them meaningful names:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/functions/first-class-functions/type-alias.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/functions/first-class-functions/type-alias.cs}}
```
{{#endtab }}
{{#endtabs }}

Mist type aliases are structurally transparent — the alias `Predicate<int>` above, `func<int, bool>`, and `Func<int, bool>` are all the same type. A qualified CLR delegate such as `System.Predicate<int>` retains its nominal identity, while lambda inference and invocation use its closed `Invoke` signature. Lambda inference sees through aliases, so you can assign a lambda to a `Predicate<int>` variable and the parameter type is inferred as `int`.

## Pure Function Types

Writing `pure` before a function type, as in `pure func<int, int>`, `pure Action<int>` or an alias of one, makes a promise part of the type: calling a value of that type is pure. A lambda converts to it only when its body keeps the pure rules, and a method group only when its method is `pure`. That is what lets a `pure` member call a function it was handed. A pure function type converts implicitly to the same type without `pure`, never the other way, and the promise is erased in the emitted C#.

Before a member's return type, `pure` belongs to the member rather than the type, so a member that returns a pure function names it through an alias, such as `type Step = pure func<int, int>;`. [Pure Function Types](../advanced/purity.md#pure-function-types) in the purity chapter has the full rules and an example.

## Comparison with C#

Mist's first-class functions are designed to be familiar to C# developers while simplifying the model:

| Concept | C# | Mist |
|---|---|---|
| Function type | `Func<T, TResult>`, `Action<T>`, named delegates | `func<T, TResult>`, `Action<T>` (alias only) |
| Declaration keyword | `delegate` | None — `func` is a type, not a declaration |
| Lambda syntax | `(x) => x + 1` | `x => x + 1` (identical) |
| Block lambda | `(x) => { return x + 1; }` | `(x) => { return x + 1; }` (identical) |
| Static lambda | `static (x) => x + 1` | `static x => x + 1` (identical) |
| Async lambda | `async (x) => await F(x)` | `async x => await F(x)` (identical) |
| Method groups | `Func<int, int> f = MyMethod;` | `func<int, int> f = MyMethod;` |
| Named delegates | `delegate bool Predicate<T>(T item);` | `type Predicate<T> = func<T, bool>;` |
| Multicast delegates | `event EventHandler Click;` | Not supported (events use a different model) |
| Anonymous methods | `delegate(int x) { return x; }` | Not supported (superseded by lambdas) |

Key differences:
- **No `delegate` keyword.** Mist uses the `func` type family for all function types. Named delegates are expressed as type aliases.
- **Structural typing.** Two `func` types with the same signature are always the same type. C# delegates are nominally typed — `Func<int, bool>` and `Predicate<int>` are different types even though they have the same shape.
- **Unified type family.** `Action<T>` is just an alias for `func<T, void>`, not a separate type hierarchy.
- **Capture restrictions.** Mist enforces ownership rules on captured variables that C# does not — ref structs, owned types, and borrowed references cannot be captured.
- **Auto-await in sync lambdas.** A non-async lambda calling an async method automatically awaits the result, consistent with Mist's structured concurrency model.
