# Attributes

Attributes are metadata annotations you attach to declarations — classes, structs, enums, abstract types, methods, constructors, and fields. They map directly to CLR attributes, so the entire .NET attribute ecosystem is available: serialization control, deprecation warnings, test framework markers, validation rules, and anything else that uses `System.Attribute` under the hood.

The Mist compiler does not interpret attribute contents. It parses the syntax, attaches the annotation to the declaration's AST node, and emits the corresponding C# attribute verbatim. The CLR and downstream tooling (Roslyn, test runners, reflection) handle validation and discovery at compile time or runtime.

## Basic Syntax

An attribute is written in square brackets before the declaration it annotates. The simplest form is just the attribute name with no arguments:

```mist
[Serializable]
class Config { ... }
```

Attributes can appear on methods too — this is how test frameworks discover test methods, how deprecation warnings are attached, and how many .NET APIs configure behavior:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/attributes/basic-attributes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/attributes/basic-attributes.cs}}
```
{{#endtab }}
{{#endtabs }}

When an attribute has no arguments, it is emitted without parentheses — `[Serializable]` rather than `[Serializable()]`. This matches standard C# convention.

## Attribute Arguments

Attributes accept arguments in parentheses, just like method calls. There are two kinds:

- **Positional arguments** — passed by position, matching the attribute's constructor parameters
- **Named arguments** — use `Name = value` syntax to set properties on the attribute instance

Positional arguments must come before named arguments in the same argument list.

Supported argument types include string literals, integer literals (decimal, hex, binary, and negated), floating-point literals, character literals, and boolean literals (`true`, `false`).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/attributes/attribute-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/attributes/attribute-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

In this example, `[Range(1, 100)]` uses two positional arguments, `[Obsolete("use ValidateV2", true)]` mixes a string and a boolean positional argument, and `[Display(Name = "User Score")]` uses a named argument.

## Multiple Attributes

You can attach multiple attributes to a single declaration in two ways:

1. **Comma-separated in one bracket pair** — `[Serializable, Obsolete("combined")]` emits as a single attribute list
2. **Separate bracket pairs** — each `[...]` on its own line, emitted as individual attribute annotations

Both forms are valid and produce equivalent CLR metadata. Use whichever reads more clearly for the situation — related attributes often read well together in one list, while unrelated attributes are clearer on separate lines.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/attributes/multiple-attributes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/attributes/multiple-attributes.cs}}
```
{{#endtab }}
{{#endtabs }}

## Type Arguments

Attributes support two forms of type usage:

### Type Literal Arguments

When a CLR attribute constructor expects a `System.Type` parameter, pass the type name directly as a type literal. Mist does not have a `typeof` keyword — instead, type names in expression position are type literals that emit as `typeof(T)` in C#:

```mist
[DebuggerTypeProxy(List<int>)]
class CustomList { }
```

This emits `[DebuggerTypeProxy(typeof(List<int>))]` in C#. Generic types work too — `List<int>` emits as `typeof(List<int>)`.

### Generic Attribute Type Parameters

C# 11 introduced generic attributes — attribute classes with type parameters. In Mist, you write these with angle brackets on the attribute name:

```mist
public class TaggedAttribute<T> : Attribute { }

[Tagged<int>]
class TypedConfig { }
```

This emits `[Tagged<int>]` in C# — no `typeof` wrapping, because the type is a generic parameter of the attribute itself, not a constructor argument. The attribute class is an ordinary generic class deriving from `System.Attribute`; the `Attribute` suffix is dropped at the use site, as in C#.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/attributes/type-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/attributes/type-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

## Supported Declaration Targets

Attributes can be placed on the following declarations:

| Declaration | Example |
|-------------|---------|
| Class | `[Serializable] class Config { }` |
| Struct | `[Serializable] struct Point { }` |
| Enum | `[Flags] enum Permissions { }` |
| Abstract type | `[Obsolete] abstract Animal { }` |
| Method | `[Fact] public void TestAdd() { }` |
| Constructor | `[Obsolete] MyClass() { }` |
| Field | `[Range(0, 100)] public int Score;` |

Attributes are only recognized in declaration position — before modifiers and the declaration keyword. A `[` in expression position (array literals, index access) is parsed as its normal expression meaning, not as an attribute.

## Qualified Names

Attribute names can be qualified with a namespace path using dot notation:

```mist
[System.Serializable]
class Config { }
```

This is useful when you want to be explicit about which attribute you mean without adding a `using` directive, or when resolving ambiguity between attributes from different namespaces.
