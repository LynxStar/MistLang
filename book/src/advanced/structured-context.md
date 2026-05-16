# Structured Context

Structured context is Mist's mechanism for passing values through a call chain without threading them as explicit parameters. You push a value onto a per-type context stack, and any method in the call chain that declares a `with` clause for that type can read it. The compiler verifies at compile time that every required context value is available at each call site, eliminating the runtime surprises of service-locator patterns.

Context values are keyed by their exact nominal type — there is no inheritance lookup. Pushing a `Config` value satisfies methods that require `Config`, but not methods that require a `DetailedConfig` subclass (or vice versa). This makes context resolution predictable and explicit.

## Context Declaration: `with var`

The `with var` form declares a context value with an inferred type and binds it to a local variable. The value is pushed onto the context stack for the remainder of the enclosing scope.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/with-var.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/with-var.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler infers the context type from the initializer expression. The local variable `greeting` is usable as a normal variable in the declaring scope, and the value is simultaneously available to any called method that declares `with string`.

## Context Declaration: `with Type`

The `with Type identifier = expression` form uses an explicit type annotation. This is useful when you want to control the exact type used as the context key — for example, pushing a derived class value but keying it as the base type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/with-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/with-type.cs}}
```
{{#endtab }}
{{#endtabs }}

The declared type (`Config`) determines the context key. If you have a `DetailedConfig` that extends `Config`, you can push it with an explicit `Config` annotation to satisfy consumers that require `Config`.

## Context Declaration: `with Type _`

The `with Type _ = expression` form pushes a value onto the context stack without creating a local binding. Use this when you need to provide context to called methods but don't need to reference the value yourself.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/with-type-discard.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/with-type-discard.cs}}
```
{{#endtab }}
{{#endtabs }}

The discard `_` tells the compiler you don't need a local variable — the value is pushed directly without an intermediate binding.

## Context Declaration: `with expr`

The `with expression` form is the most concise — it pushes the expression's value onto the context stack with no local binding and no explicit type annotation. The context type is inferred from the expression.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/with-expr.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/with-expr.cs}}
```
{{#endtab }}
{{#endtabs }}

This form is ideal for fire-and-forget context setup where you construct a value solely to provide it as context.

## Context Block: `with(expression) { }`

A context block pushes a value for a specific block scope only. When the block exits, the previous context value for that type is automatically restored. This is useful for temporarily overriding context.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/context-block.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/context-block.cs}}
```
{{#endtab }}
{{#endtabs }}

The output of this program is `10`, `99`, `10` — the context block temporarily shadows the outer value, and the original is restored when the block exits. The compiler emits a `try`/`finally` to guarantee the pop happens even if an exception is thrown.

## Method `with` Clause

A method declares its context requirements using one or more `with` clauses after the parameter list. Each clause specifies a type and an identifier that becomes a read-only local variable bound to the current context value for that type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/method-with-clause.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/method-with-clause.cs}}
```
{{#endtab }}
{{#endtabs }}

Multiple `with` clauses are chained after the parameter list. Each one peeks the corresponding type from the context stack at method entry. The compiler verifies at the call site that all required context types are available — if a caller doesn't have `int` context in scope when calling `Describe()`, compilation fails.

## Type-Keyed Context

Context values are keyed by their exact nominal type. Different types maintain independent context stacks, even if they are structurally similar. This means pushing a `Config` value does not affect the `string` context, and pushing an `IntHolder` does not affect the `StringHolder` context.

```mist
{{#include ../../snippets/advanced/structured-context/type-keyed.mist}}
```

Type aliases resolve to their underlying type for context keying. A `type Name = string` alias shares the same context key as `string` — pushing a `Name` value satisfies methods that require `string` context.

## Context Propagation

Context propagates transparently through intermediate methods. A method that doesn't declare a `with` clause for a given type still passes that context through to any methods it calls. The context stack is thread-local, so values pushed in one scope are visible to all methods called within that scope.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/structured-context/propagation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/structured-context/propagation.cs}}
```
{{#endtab }}
{{#endtabs }}

`Intermediary.Forward()` has no `with` clause, yet it can call `Consumer.GetMessage()` because the `string` context pushed in `Main` remains on the stack throughout the call chain. The compiler tracks context availability transitively — it knows that `Forward()` is called from a scope where `string` context exists.

## Compile-Time Verification

The compiler statically verifies that every method call site has the required context available. If you call a method that declares `with Config cfg` from a scope where no `Config` has been pushed, the compiler reports an error. This catches missing context at compile time rather than failing at runtime with an empty-stack exception.

```mist
{{#include ../../snippets/advanced/structured-context/compile-time-verification.mist}}
```

The compiler also verifies context through transitive calls — if `MethodA()` calls `MethodB()` which requires `int` context, then any caller of `MethodA()` must also have `int` context available, even though `MethodA()` itself doesn't declare a `with int` clause.
