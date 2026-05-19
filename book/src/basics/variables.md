# Variables

Variables in Mist store values that you can use and modify throughout your program. Mist provides explicit type annotations for clarity and `var` for type inference when the type is obvious from context.

## Explicit Type Annotations

The most direct way to declare a variable is with an explicit type. The syntax is `Type name = expression;` — you state the type, give the variable a name, and assign an initial value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/variables/explicit-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/variables/explicit-types.cs}}
```
{{#endtab }}
{{#endtabs }}

The type appears before the variable name, just like in C#. This form is useful when you want to be explicit about what a variable holds, or when the type isn't obvious from the right-hand side.

## Type Inference with `var`

When the type is clear from the initializer expression, you can use `var` to let the compiler infer it. The syntax is `var name = expression;` — the compiler determines the type from the assigned value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/variables/type-inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/variables/type-inference.cs}}
```
{{#endtab }}
{{#endtabs }}

The inferred type is determined at compile time — `message` is a `string`, `total` is an `int`, and `origin` is a `Point`. Using `var` reduces repetition without sacrificing type safety.

## Scoping Rules

Variables in Mist are scoped to the enclosing block, delimited by `{` and `}`. A variable declared inside a block is not accessible outside that block.

```mist
{{#include ../../snippets/basics/variables/scoping.mist}}
```

This applies to all block-introducing constructs: `if`/`else`, `while`, `for`, `foreach`, and plain brace-delimited blocks. Each block creates a new scope, and variables declared within it are destroyed when the block ends.

## Mutability

Variables declared with an explicit type or `var` are mutable by default. You can reassign them freely within their scope:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/variables/mutability.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/variables/mutability.cs}}
```
{{#endtab }}
{{#endtabs }}

There is no separate `let` or `const` keyword for local immutability — all local variables are reassignable. For immutability at the type level, Mist provides ownership modifiers and readonly contexts, covered in the [Advanced Features](../advanced/ownership.md) chapters.
