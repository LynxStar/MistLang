# Accessibility

Mist provides four accessibility levels that control where a declaration can be referenced. These mirror a subset of C#'s accessibility model:

| Modifier | Accessible from |
|---|---|
| `public` | Anywhere |
| `private` | The declaring type only (including its nested types) |
| `protected` | The declaring type and types derived from it |
| `internal` | Anywhere within the same compilation |

Mist deliberately omits C#'s two combined levels, `protected internal` and `private protected`. When consuming C#/CLR types, these collapse without broadening: `protected internal` maps to `protected`, and `private protected` maps to `private`.

## The Four Levels

A single class can use all four levels to control visibility of its members:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/four-levels.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/four-levels.cs}}
```
{{#endtab }}
{{#endtabs }}

- `public` members are accessible from any code that can see the type.
- `private` members are accessible only within the declaring type's body (and its nested types).
- `protected` members are accessible from the declaring type and any type that inherits from it.
- `internal` members are accessible from any type within the same compilation (project).

## Protected Access

A `protected` member is visible to derived types, allowing base classes to share state with their subclasses without exposing it publicly:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/protected-access.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/protected-access.cs}}
```
{{#endtab }}
{{#endtabs }}

`Car` can read `speed` because it derives from `Vehicle`. Code outside the inheritance chain cannot access `speed` directly.

## Internal Access and the Compilation Boundary

`internal` visibility is scoped to the current compilation -- that is, the set of source files compiled together as one project. Any type within the same compilation can access `internal` members, regardless of inheritance. Types in other compilations (external assemblies) cannot.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/internal-access.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/internal-access.cs}}
```
{{#endtab }}
{{#endtabs }}

`LoggerFactory` can access `Logger.prefix` because both types are in the same compilation. If `Logger` were published as a library, consumers in other projects would not be able to reach `prefix`.

## Default Accessibility

When no modifier is written, Mist assigns a default based on the enclosing scope:

| Declaration position | Default |
|---|---|
| Top-level type (namespace/global) | `internal` |
| Class or struct member | `private` |
| Interface member | `public` |
| Enum member | `public` |
| Nested type | `private` |

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/defaults.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/defaults.cs}}
```
{{#endtab }}
{{#endtabs }}

`TopLevelService` has no modifier, so it defaults to `internal` -- visible within the compilation but not externally. The `counter` field has no modifier, so it defaults to `private`.

## Sealed Types

The `sealed` modifier prevents a class or struct from being inherited. This is useful for types that are not designed for extension.

### Sealed Classes

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/sealed-class.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/sealed-class.cs}}
```
{{#endtab }}
{{#endtabs }}

Attempting to derive from `Config` produces a compile-time error.

### Sealed Structs

Because Mist structs are inheritable (unlike C#'s value-type structs), `sealed` is meaningful on structs as well:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/sealed-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/sealed-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

A sealed struct cannot be used as a base struct in an inheritance declaration.

## Nested-Type Accessibility Domains

A nested type's accessibility domain is the intersection of its own declared accessibility and the accessibility domain of its enclosing type. A `private` nested type is visible only within the enclosing type, but it can access the enclosing type's `private` members:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/nested-types.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/nested-types.cs}}
```
{{#endtab }}
{{#endtabs }}

`Inner` is `private` to `Outer`, so external code cannot instantiate it. But `Inner` can read `Outer.secret` because a nested type is within the declaring type's accessibility domain.

## Protected in Sealed Types (Warning)

Declaring a `protected` member inside a `sealed` type is legal but produces a compiler warning. Since a sealed type cannot be inherited, the `protected` accessibility is unnecessary -- the member is effectively `private`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/accessibility/protected-in-sealed.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/accessibility/protected-in-sealed.cs}}
```
{{#endtab }}
{{#endtabs }}

The code compiles and runs, but the compiler emits a warning similar to C#'s CS0628. To silence it, change the member to `private` instead.

## Analog-Mapping Decisions

Mist has several constructs without a direct C# equivalent. Their accessibility behavior is determined by the closest C# analog:

| Mist construct | Closest C# analog | Accessibility behavior |
|---|---|---|
| Inheritable struct | C# class | `protected` and `internal` are fully enforced on struct members |
| Sealed struct | Sealed C# class | Prevents struct inheritance; `sealed` accepted with no diagnostic |
| Type alias of a sealed type | Transparent alias | Allowed -- aliases do not create new types |
| Refinement over a sealed type | Value narrowing | Allowed -- refinements narrow values, they do not extend types |
| Newtype wrapping a sealed type | New nominal type | Error -- newtypes build a new type on the sealed type |

### CLR Projection

When consuming types from external C#/.NET assemblies, Mist maps CLR accessibility to its four-level model without broadening access:

| CLR accessibility | Mist accessibility |
|---|---|
| `public` | `public` |
| `family` (protected) | `protected` |
| `family-or-assembly` (protected internal) | `protected` |
| `private` | `private` |
| `assembly` (internal) | `private` |
| `family-and-assembly` (private protected) | `private` |

The `assembly` and `family-and-assembly` levels map to `private` rather than `internal` because a consuming compilation cannot meaningfully access members internal to a different assembly. This ensures the projection never broadens access.
