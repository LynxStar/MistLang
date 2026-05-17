# Top-Level Statements

In the [Hello, World!](hello-world.md) chapter, you wrote a program with a class, a `Main` method, and a namespace declaration. That structure is fine for larger applications, but it's a lot of ceremony for a simple script. Top-level statements let you skip the boilerplate and write executable code directly at file scope.

## A Simpler Entry Point

Instead of wrapping your code in a class and `static void Main`, you can write statements directly in a `.mist` file:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/getting-started/top-level-statements/hello-tls.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/getting-started/top-level-statements/hello-tls.cs}}
```
{{#endtab }}
{{#endtabs }}

That's it — no class, no `Main` method. The compiler treats the file's statements as the program's entry point and emits them as C# top-level statements. The .NET runtime handles the rest.

You can use all the same constructs you'd use inside a method body: variable declarations, control flow, expression statements, and more.

## One File Per Project

A project may have at most **one** file containing top-level statements. If the compiler finds statements at file scope in more than one file, it reports an error listing all the conflicting files:

```
error MIST_EP_0001: Multiple files contain top-level statements. Only one file
per project may have top-level statements. Files: Program.mist, Script.mist
```

All other `.mist` files in the project must use the traditional class-and-method structure.

## Entry Point Conflicts

When a project has both a top-level statement file and a class with `static void Main`, the compiler needs to know which one is the entry point. Unlike some languages that pick one silently, Mist treats this as a compile error unless you resolve it explicitly.

### Top-Level Statements + Main Method

If your project has a file with top-level statements and a class with `static void Main`, and no `entry_point` field in `mist.toml`, the compiler reports:

```
error MIST_EP_0002: Project has both top-level statements (in 'Program.mist') and
a static void Main (in 'MyApp.Launcher'). Add entry_point = "top-level" to
mist.toml to resolve this conflict.
```

A top-level statement file must always be the entry point when present — you cannot have top-level statements in a project without them being the start point. To resolve the conflict, add `entry_point = "top-level"` to your `mist.toml`. The `Main` method is then compiled as a normal static method (callable by other code, but not the program's start point).

### Multiple Main Methods

If your project has multiple classes with `static void Main` (and no top-level statements), the compiler also reports an error:

```
error MIST_EP_0004: Multiple classes contain static void Main: MyApp.Program,
MyApp.Launcher. Add entry_point = "MyApp.Program" to mist.toml to specify
which one to use.
```

You resolve this by setting `entry_point` to the fully qualified class name of the desired entry point.

## The `entry_point` Field

The `entry_point` field in `mist.toml` disambiguates which code runs at startup. It accepts two kinds of values:

### Using Top-Level Statements as the Entry Point

```toml
name = "MyApp"
entry_point = "top-level"
```

This tells the compiler to use the top-level statement file as the program's entry point. Any `static void Main` methods in the project are compiled as regular static methods.

### Using a Specific Class as the Entry Point

```toml
name = "MyApp"
entry_point = "MyApp.Launcher"
```

This tells the compiler to use the `Main` method in `MyApp.Launcher` as the entry point. This form is only valid when no top-level statement file exists — if a TLS file is present, you must use `"top-level"` (or remove the top-level statements).

### When You Don't Need `entry_point`

If your project has exactly one entry point candidate — either a single top-level statement file or a single class with `static void Main` — the compiler resolves it automatically. You only need the `entry_point` field when there's ambiguity.

## The Implicit `args` Variable

Inside a top-level statement file, the variable `args` is implicitly available with type `string[]`. It contains the command-line arguments passed to the program — the same arguments you'd receive as the parameter to `static void Main(string[] args)`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/getting-started/top-level-statements/args-example.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/getting-started/top-level-statements/args-example.cs}}
```
{{#endtab }}
{{#endtabs }}

You don't need to declare `args` — it's always in scope in a top-level statement file.

## Return Statements and Exit Codes

A `return` statement in a top-level statement file sets the process exit code. Return an integer to indicate success or failure to the calling process:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/getting-started/top-level-statements/return-example.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/getting-started/top-level-statements/return-example.cs}}
```
{{#endtab }}
{{#endtabs }}

A bare `return;` without a value exits with code 0 (success). Returning a non-integer expression is a type error.

## File-Scoped Namespaces

Top-level statement files can include a file-scoped namespace declaration. The namespace applies to type declarations in the file but not to the statements themselves (which become the entry point regardless of namespace):

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/getting-started/top-level-statements/namespace-example.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/getting-started/top-level-statements/namespace-example.cs}}
```
{{#endtab }}
{{#endtabs }}

In this example, `Helper` belongs to the `MyApp` namespace and is accessible from other files via `using MyApp;`. The top-level statements run as the entry point without any namespace wrapping.

**Block namespace declarations** (e.g., `namespace Foo { ... }`) are not allowed in top-level statement files. The compiler reports an error if you try:

```
error MIST_EP_0006: Top-level statements cannot coexist with block namespace
declarations in the same file. Use a file-scoped namespace (namespace X;) instead.
```

## What's Next

You now know how to write simple programs without boilerplate. The next chapters cover the building blocks you'll use in both top-level statements and traditional programs: [primitive types](../basics/primitive-types.md), [variables](../basics/variables.md), and [control flow](../basics/control-flow.md).
