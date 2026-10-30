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

A typed, inferred, or loop declaration named `_` evaluates its initializer without introducing a name that later expressions can read. Several discard declarations can appear in the same scope. Initializer validation, ownership, and scope-exit cleanup follow the same rules as named declarations.

An ordinary assignment used inside another expression produces the type of the storage it writes, after any implicit conversion. Assigning an `int` value to a `long` local therefore produces a `long` result, which `var` also infers as `long`. The same rule applies to fields, properties, and indexers, and the value being assigned is evaluated once. [Void-coalescing assignment](../type-system/optional-types.md) is a statement and cannot be used as a subexpression.

The left-hand side of an assignment must be somewhere a value can be stored: a variable, a field, an array or span element, or a result returned by `ref`, or else a property or indexer whose setter is available. A literal, the result of an ordinary call or operator, a type name, a dependent type parameter, a tuple literal, and a field, property, or indexer of a struct that is itself an unstored value are refused at the target with `MIST0210`. The value on the right is not judged against such a target, so the mistake is reported once.

## Scoping Rules

Variables in Mist are scoped to the enclosing block, delimited by `{` and `}`. A variable declared inside a block is not accessible outside that block.

```mist
{{#include ../../snippets/basics/variables/scoping.mist}}
```

This applies to all block-introducing constructs: `if`/`else`, `while`, `for`, `foreach`, and plain brace-delimited blocks. Each block creates a new scope, and variables declared within it are destroyed when the block ends.

A name declared twice in the same scope is refused at the repeated name in its source file. A child block may shadow a name from its enclosing scope; reads in that block refer to the child declaration.

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

To prevent reassignment, prefix the declaration with `readonly`:

```mist
readonly var name = "Alice";
// name = "Bob";  // ERROR: Cannot reassign readonly local variable 'name'
```

For more on readonly locals and the broader readonly system, see the [Readonly](../object-model/readonly.md) chapter.
