# Nested Scoping

Every pair of braces (`{ }`) in Mist creates a new lexical scope. Variables declared inside a block are visible only within that block and any blocks nested inside it. When execution leaves the block, those variables cease to exist — they cannot be referenced from the enclosing scope or from sibling blocks.

This rule applies uniformly to all brace-delimited constructs: method bodies, control flow branches, `readonly` blocks, `with` context blocks, and standalone blocks written purely for scoping purposes.

## Lexical Scoping with Braces

A standalone block introduces a new scope without any control flow semantics. Variables declared inside are confined to that block:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/lexical-scoping.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/lexical-scoping.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# preserves the block structure exactly. The .NET runtime uses the same lexical scoping rules, so the block boundaries carry through unchanged.

## Variable Shadowing

An inner scope may declare a variable with the same name as a variable in an enclosing scope. The inner declaration *shadows* the outer one — for the duration of the inner scope, the name refers to the inner variable. When the inner scope exits, the outer variable is restored.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/shadowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/shadowing.cs}}
```
{{#endtab }}
{{#endtabs }}

Shadowing is not reassignment. The outer `x` is never modified — a completely new variable happens to share the same name within the inner scope. This distinction matters for ownership: shadowing an owned variable does not move or consume the original.

Shadowing works at any nesting depth. Each block can shadow variables from any enclosing scope:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/deep-shadowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/deep-shadowing.cs}}
```
{{#endtab }}
{{#endtabs }}

## Ownership and Nested Scopes

Ownership annotations interact with scoping in a predictable way: when a scope exits, any owned variables declared in that scope are dropped. Borrows created within a scope are released when the scope ends, returning the original owner to its prior state.

This means you can use nested blocks to limit the lifetime of a borrow:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/ownership-scopes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/ownership-scopes.cs}}
```
{{#endtab }}
{{#endtabs }}

Ownership keywords like `owned` and `frozen` are erased in the emitted C# — the block structure remains, but the enforcement is purely compile-time.

## Context and Nested Scopes

Structured context values declared with `with` are scoped to their enclosing block. When the block exits, the context value is popped from the context stack. This gives you precise control over how far a context value propagates:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/context-scopes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/context-scopes.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# shows the mechanism clearly: each `with` declaration pushes onto the context stack, and the compiler inserts a matching `Pop` at the end of the declaring scope. Nested blocks naturally create a stack discipline — inner context values shadow outer ones for the duration of the block, then the outer value is restored.

## Nested Type Declarations

Braces also create scoping boundaries for type declarations. A class, struct, interface, trait, or enum declared inside another type is scoped to its parent — accessible from outside only via the qualified name:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/nested-scoping/nested-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/nested-scoping/nested-types.cs}}
```
{{#endtab }}
{{#endtabs }}

Nested types can be nested to arbitrary depth. The compiler resolves names by walking outward through enclosing scopes — an inner type can reference sibling types and parent-scope types by simple name, while code outside the parent must use the fully qualified path.
