# Anonymous Types

Anonymous types let you create lightweight objects with named properties without declaring a type. They're useful when you need to group a few values together temporarily — projecting data for display, passing intermediate results, or building ad-hoc structures that don't warrant a full class declaration.

```mist
var point = new { X = 10, Y = 20 };
Console.WriteLine($"X={point.X} Y={point.Y}");
```

The compiler infers a structural type from the property names and their value types. Two anonymous type expressions with the same property names, types, and order produce the same type.

## Creation with Explicit Assignments

The basic form assigns values to named properties inside `new { ... }`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/anonymous-types/creation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/anonymous-types/creation.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# uses the same `new { ... }` syntax — the C# compiler handles generating the backing class, `Equals`, `GetHashCode`, and `ToString` automatically.

## Property Projections

When a property's name matches a variable or member access, you can use the shorthand projection form:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/anonymous-types/projection.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/anonymous-types/projection.cs}}
```
{{#endtab }}
{{#endtabs }}

Projections work with:
- **Simple variables** — `new { city }` creates a property named `city` with the variable's value
- **Member access** — `new { lang.Name }` creates a property named `Name` from the member's value
- **Mixed forms** — you can combine explicit assignments and projections in one expression

## Property Access

Access properties on anonymous type values using standard dot notation. The type checker resolves property names against the anonymous type's structure:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/anonymous-types/access.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/anonymous-types/access.cs}}
```
{{#endtab }}
{{#endtabs }}

Two variables with the same anonymous type shape (same property names, types, and order) can be assigned to each other.

## Interaction with `unknown`

Anonymous type values can be assigned to `unknown`-typed variables. You can narrow them back using property patterns:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/anonymous-types/unknown.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/anonymous-types/unknown.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitter lowers `unknown` to `object` in C#, and property patterns compile to C#'s native pattern matching which checks the runtime type.

## Pattern Matching

Property patterns destructure anonymous type values directly. The type checker validates property names against the anonymous type's structure:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/anonymous-types/pattern-matching.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/anonymous-types/pattern-matching.cs}}
```
{{#endtab }}
{{#endtabs }}

You can use `var` patterns to bind individual properties, or constant patterns to check specific values.

## Limitations

Anonymous types come with a few restrictions:

- **No use in method signatures** — you can't use an anonymous type as a parameter type or return type, because it has no denotable name.
- **No use as field types** — anonymous types are scoped to local contexts.
- **No use as generic type arguments** — for the same reason (no denotable name).
- **Properties are read-only** — once created, you cannot reassign individual properties on an anonymous type value.
- **Structural equality** — two anonymous types are the same type only if they share the same property names, types, and order. `new { X = 1, Y = 2 }` and `new { Y = 2, X = 1 }` are different types.
