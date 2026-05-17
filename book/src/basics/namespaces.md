# Namespaces

Namespaces organize your code into logical groups and prevent naming conflicts between types. Mist supports the same namespace mechanics as C# — file-scoped declarations, block declarations, and `using` imports — plus automatic namespace inference from your project's directory structure.

## File-Scoped Namespace Declarations

A file-scoped namespace applies to the entire file. Place it at the top (after any `using` declarations) and terminate it with a semicolon. All types declared in the file belong to that namespace.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/file-scoped.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/file-scoped.cs}}
```
{{#endtab }}
{{#endtabs }}

File-scoped namespaces reduce indentation since you don't need braces around the entire file contents.

## Block Namespace Declarations

A block namespace uses braces to scope the enclosed types. This form is useful when you need multiple namespaces in a single file or want explicit visual grouping.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/block-namespace.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/block-namespace.cs}}
```
{{#endtab }}
{{#endtabs }}

You can declare multiple block namespaces in the same file:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/multiple-blocks.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/multiple-blocks.cs}}
```
{{#endtab }}
{{#endtabs }}

## Namespace Inference from Project Name

Files without an explicit `namespace` declaration are automatically placed under the project's **NamespaceRoot**. The NamespaceRoot is defined by the `namespace_root` field in `mist.toml`, or defaults to the project `name` if `namespace_root` is not specified.

For example, given this `mist.toml`:

```toml
name = "Namespaces"
```

A file at the project root with no namespace declaration receives the inferred namespace `Namespaces`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/inference.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler inserts the inferred namespace in the emitted C# even though you didn't write one in the Mist source.

## Nested Namespaces from Directory Structure

Subdirectories under the project root map to nested namespaces. A file in `Models/` receives the namespace `{NamespaceRoot}.Models`, and a file in `Models/Game/` receives `{NamespaceRoot}.Models.Game`.

Given this project layout:

```
Namespaces/
├── mist.toml          (name = "Namespaces")
├── Program.mist       → namespace Namespaces
├── Models/
│   ├── Player.mist    → namespace Namespaces.Models
│   └── Game/
│       └── GameState.mist → namespace Namespaces.Models.Game
```

A file in `Models/` with no explicit namespace:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/nested-dirs.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/nested-dirs.cs}}
```
{{#endtab }}
{{#endtabs }}

And a file nested deeper in `Models/Game/`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/deep-nested.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/deep-nested.cs}}
```
{{#endtab }}
{{#endtabs }}

## Using Declarations

To reference types from another namespace, add a `using` declaration at the top of the file. This imports all public types from the specified namespace.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/using-declarations.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/basics/namespaces/using-declarations.cs}}
```
{{#endtab }}
{{#endtabs }}

Without the `using Namespaces.Models;` declaration, the compiler would not resolve `Player` since it lives in a different namespace.

## Same-Namespace Type References

Types within the same namespace can reference each other directly without a `using` declaration. If two files share the same inferred or explicit namespace, their types are mutually visible.

In the example above, `Program.mist` and `AppConfig.mist` both live at the project root with no explicit namespace, so they both belong to the `Namespaces` namespace. `Program` can use `AppConfig` directly:

```mist
{{#include ../../snippets/basics/namespaces/same-namespace.mist}}
```

Similarly, `Player.mist` and `Item.mist` both live in the `Models/` directory, so they share the inferred namespace `Namespaces.Models` and can reference each other without imports.

## Global Usings

If you find yourself adding the same `using` declarations to every file in your project, you can promote them to **global usings**. Global usings are declared in your `mist.toml` project file — not in source files — and behave identically to having an explicit `using` at the top of every file in the project.

Add a `[global_usings]` section to your `mist.toml` with a `namespaces` array listing the namespaces you want imported everywhere:

```toml
{{#include ../../snippets/basics/namespaces/global-usings.toml}}
```

With this configuration, every `.mist` file in the project can reference types from `MyProject.Models` and `MyProject.Services` by simple name without writing `using` declarations:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/basics/namespaces/global-usings.mist}}
```
{{#endtab }}
{{#endtabs }}

A few things to keep in mind:

- **Project-scoped only.** Global usings apply exclusively to the project that declares them. If project A references project B, project A does *not* inherit project B's global usings. Each project controls its own implicit imports.
- **Redundant usings are fine.** If a file contains an explicit `using` for a namespace that is already globally imported, the compiler accepts it silently — no warning, no error.
- **Ambiguity rules still apply.** If two globally-used namespaces both export a type with the same name, referencing that type without qualification produces the same `MIST_AMBIGUOUS_TYPE` error as with explicit usings.
