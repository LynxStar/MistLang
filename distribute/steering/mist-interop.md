<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist .NET Interop

How to consume .NET — SDK types, NuGet packages, shared frameworks, and any .NET assembly — from Mist. Include this when adding dependencies, consuming existing C#/.NET code, or diagnosing resolution issues.

Mist compiles to C# and runs on .NET, so the entire .NET ecosystem is reachable. The compiler's **Assembly Type Provider** reads type metadata directly from assembly DLLs and projects CLR types into Mist's type system. There are no bindings or wrappers to write — reference a type and use it.

## SDK Types Are Available by Default

All assemblies shipped with the .NET SDK for the target framework (default `net10.0`) are available with no configuration — the whole `System.*` hierarchy (`System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Text`, `System.Threading.Tasks`, etc.).

Import with `using` and use the simple name, exactly as in C#:

```mist
using Standard;
using System.Collections.Generic;

var list = new List<int>();
list.Add(1);
foreach (int x in list) { Console.WriteLine(x); }
```

Fully qualified names work without a `using` too:

```mist
var list = new System.Collections.Generic.List<int>();
```

Name resolution prefers Mist-declared types; CLR types are searched only when no Mist type matches.

## Adding NuGet Packages

Add a `[packages]` table to `mist.toml`. Each entry maps a package name to an **exact** version string (version ranges are not supported):

```toml
name = "MyProject"

[packages]
Newtonsoft.Json = "13.0.3"
Humanizer.Core = "2.14.1"
```

The compiler downloads from nuget.org into the standard cache (`~/.nuget/packages/`) and resolves transitive dependencies automatically. Package names must match `[a-zA-Z0-9\-_\.]+` and be ≤ 128 characters.

> This is the Mist equivalent of `<PackageReference>` in a `.csproj`. There is no separate `dotnet add package` step — declare it in `mist.toml` and build.

## Adding Framework References

To access additional .NET shared frameworks beyond the base SDK (e.g., ASP.NET Core), add a `[framework_references]` table to `mist.toml`. Each entry maps a framework name to a boolean (`true` to enable, `false` to disable):

```toml
name = "MyWebApp"

[packages]
Newtonsoft.Json = "13.0.3"

[framework_references]
"Microsoft.AspNetCore.App" = true
```

The compiler locates the installed shared-framework reference assemblies for the target framework and makes all their types available. Known shared frameworks:

- `Microsoft.AspNetCore.App` — ASP.NET Core (routing, HTTP, middleware, etc.)
- `Microsoft.WindowsDesktop.App` — WPF and Windows Forms (Windows only)
- `Microsoft.NETCore.App` — the base framework (loaded automatically by the SDK; rarely needs explicit declaration)

The framework must be installed on the host machine for the target framework. If not installed, the compiler emits `MIST_FWREF_001`. If the name is not a recognized shared framework, the compiler emits `MIST_FWREF_002`.

## Passing MSBuild Properties to the Generated csproj

To inject arbitrary MSBuild properties into the generated `.csproj` (e.g., for .NET User Secrets, connection strings, or custom build configuration), add a `[csproj_properties]` table to `mist.toml`. Each entry is a key-value pair where both the key and value must be strings:

```toml
name = "MyApp"

[packages]
Npgsql.EntityFrameworkCore.PostgreSQL = "9.0.4"

[csproj_properties]
UserSecretsId = "my-app-secrets-id"
DockerDefaultTargetOS = "Linux"
```

The compiler emits these as `<PropertyGroup>` entries in the generated `.csproj`:

```xml
<PropertyGroup>
  <UserSecretsId>my-app-secrets-id</UserSecretsId>
  <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
</PropertyGroup>
```

### Common Use Cases

- **User Secrets** — `UserSecretsId` enables `dotnet user-secrets` for local development secrets (connection strings, API keys) without checking them into source control. Access them via `IConfiguration` from `Microsoft.Extensions.Configuration`.
- **Docker settings** — `DockerDefaultTargetOS`, `DockerfileContext`, etc.
- **Custom build properties** — any MSBuild property your build pipeline needs.

### Rules

- Keys must match `[a-zA-Z0-9\-_\.]+` and be at most 128 characters.
- Values must be non-empty strings (XML-escaped automatically on emit).
- Overriding compiler-managed properties (`OutputType`, `TargetFramework`, `RootNamespace`, `ImplicitUsings`, `Nullable`) emits a warning (`MIST_CSPROJ_PROP_MANAGED_OVERRIDE`) — do so only if you know what you're doing.

## Cross-Assembly Inheritance

CLR inheritance relationships that span assemblies are fully resolved. A projected derived type is assignable to its projected base type across assemblies, and inherited members are callable on the derived type:

```mist
using System.IO;

// MemoryStream (System.Runtime) inherits from Stream (System.Runtime.Extensions).
// The cross-assembly base type is resolved, so assignability and inherited members work.
var memory = new MemoryStream();
memory.WriteByte(72);

// Assign derived to base — works across assembly boundaries.
Stream stream = memory;

// Inherited members (declared on Stream) are callable via the base-typed variable.
Console.WriteLine(stream.Length);
Console.WriteLine(stream.CanRead);
```

Multi-level inheritance chains (`NetworkStream : Stream : MarshalByRefObject`) are resolved through all levels. If a base type or interface cannot be resolved from any loaded assembly, only that relationship is omitted — the type and its directly declared members remain usable.

## Named Delegates and `System.Delegate`

Named CLR delegates (types inheriting from `System.MulticastDelegate` other than `System.Func<...>` and `System.Action<...>`) are projected as distinct `DelegateType` values that preserve their CLR identity. They are **not** collapsed to bare `func<...>` — the compiler keeps the delegate's fully qualified name, namespace, and generic type parameters through projection, type checking, and C# emission.

`System.Func<...>` and `System.Action<...>` continue to project directly as `func<...>` types without any wrapper.

### Implicit Conversions

The following implicit conversions are supported:

- **func → named delegate** — a `func<...>` value is assignable to a named delegate variable if their signatures are compatible (same parameter count, pairwise compatible parameter types and return type).
- **lambda → named delegate** — a lambda expression can be passed where a named delegate is expected, with target-type inference for parameter types.
- **method group → named delegate** — a method group (method name used as a value) can be passed where a named delegate is expected if exactly one overload is signature-compatible.
- **named delegate → func** — a named-delegate-typed value is assignable to its equivalent `func<...>` type.
- **any delegate or func → System.Delegate** — any `func<...>` value, named-delegate value, lambda, or method group is assignable to `System.Delegate` (the base type of all CLR delegates).

### Nominal Distinctness

Two distinct named delegates are **NOT** implicitly convertible to each other, even when their signatures are identical. For example, you cannot assign a `Predicate<int>` to a `Comparison<int>`-typed variable or vice versa — they are nominally distinct. To convert between them, go through `func<...>` as an intermediate.

`System.Delegate` cannot be narrowed back to a specific delegate type or `func<...>` — it is the top type of the delegate hierarchy.

### Invocation

A value typed as a named delegate is directly invocable using standard call syntax, exactly like `func<...>` values:

```mist
System.Predicate<int> isEven = x => x % 2 == 0;
var result = isEven(4);  // true
```

A value typed as `System.Delegate` is **not** directly invocable — cast to a specific delegate or `func<...>` type first.

### Common Patterns

```mist
using System.Collections.Generic;

// Lambda → named delegate (Predicate<int>)
var numbers = new List<int> { 1, 2, 3, 4, 5 };
var evens = numbers.FindAll(x => x % 2 == 0);

// func value → named delegate variable
func<int, bool> checker = x => x > 0;
System.Predicate<int> pred = checker;

// Named delegate → func variable
func<int, bool> backToFunc = pred;

// Any → System.Delegate (e.g., ASP.NET Core MapGet)
app.MapGet("/hello", () => "Hello from Mist!");
```

### Emission

When the C# emitter encounters a func-to-named-delegate conversion at a call site, it emits `new DelegateName(expression)` to satisfy C#'s type system. When the target is `System.Delegate`, the lambda is emitted as-is (C# 11+ handles the conversion). When a named-delegate value flows to a `func<...>` target, it is emitted as-is.

## Build Output: Quiet by Default

Builds show **progress and a summary by default**. When packages or framework references are declared, the Package Resolution stage always runs (between Loading and Lexing), but its detail output is suppressed unless you opt in with `--verbose`. Error diagnostics (`MIST_PKG_*`, `MIST_FWREF_*`) always surface regardless of verbosity, `--quiet` included: one per failing dependency, on stderr, as soon as Stage 1.5 ends and before lexing begins. The compile still runs through analysis, so source errors are reported in the same run after the summary, which counts the dependency error without printing it again; then the build fails before emitting, and `run`/`test` launch nothing.

```bash
mist build                 # progress and summary; --verbose adds resolution details
mist build --verbose       # prints Stage 1.5 header, per-package/framework detail, elapsed time
mist run --verbose         # same for run (and test)
```

Verbose output includes:
- A `Stage 1.5 - Package Resolution` header
- One `Restored <name> <version> (<tfm>)` line per package
- One `Resolved <framework> <version>` line per framework reference
- A summary with success/failure counts and elapsed milliseconds

`--verbose` and `--quiet` are mutually exclusive; supplying both is a parse error.

## CLI Commands

### `mist restore`

Resolves all declared packages and framework references and prints the full resolver output unconditionally, without performing lexing, type checking, or emit. Useful for diagnosing dependency problems.

```bash
mist restore               # prints per-package and per-framework detail + summary
```

Exits 0 on full success, non-zero on any resolution failure or when run outside a project folder. Its diagnostics follow the summary.

### `mist inspect <TypeName>`

Shows which members of a CLR type are usable in Mist, which are hidden, and why:

```bash
mist inspect System.Text.Encoding
```

Output includes the fully qualified name, type kind, visible properties and methods with their projected Mist types, hidden members with reasons, and inherited members annotated with their declaring base type. Requires a Mist project file in the current directory (uses the same SDK, package, and framework reference assemblies the build would use).

Exits non-zero when the type cannot be resolved, the type name is omitted, or there is no project file.

## Key Differences From Calling .NET in C#

Most interop is transparent, but these behaviors differ from what a C# developer expects.

### Reference returns are `T | void`, not nullable references

Because Mist has no `null`, the type provider projects every CLR reference-type return (classes, interfaces, `string`, arrays, delegates) and every `Nullable<T>` as a `T | void` union. You must narrow before use. Non-nullable value types (`int`, `bool`, `double`, …) project as plain Mist types — no `| void`.

```mist
// A method returning a reference type yields T | void — narrow before using.
var dict = new Dictionary<string, string>();
// Guard, then index:
if (dict.ContainsKey("key")) {
    string val = dict["key"];
}
// Or coalesce:
string name = LookupName(id) ?? "anonymous";
```

### `out`-parameter methods are hidden

Mist has no `out` parameters, so any CLR method using one is **hidden** — notably `Dictionary.TryGetValue`, `int.TryParse`, etc. Work around it:

- Use `ContainsKey` + indexer instead of `TryGetValue`.
- Use LINQ instead of `out`-based APIs.
- Wrap the hidden API in a C# helper if needed.

### Optional parameters require explicit arguments

Mist has no optional parameters. CLR methods with default parameter values (e.g., `CancellationToken cancellationToken = default`) are projected with their full parameter count — you cannot omit trailing optional arguments as you would in C#.

Pass `default` for parameters where you'd normally rely on the default value:

```mist
// C#: await db.SaveChangesAsync();           — CancellationToken is optional
// Mist: must pass the argument explicitly
await db.SaveChangesAsync(default);           // default inferred as CancellationToken

// C#: Task.Delay(1000);                      — CancellationToken is optional
// Mist:
await Task.Delay(1000, default);
```

The bare `default` keyword is target-type-inferred from the parameter type, so you don't need to write `default(CancellationToken)` — just `default`.

### Per-overload visibility filtering

When a method group contains overloads that use unsupported types, only the **individual offending overloads** are hidden — the remaining overloads in the same group stay callable. This means you can call `Encoding.GetBytes(string)` even though some sibling overloads use pointer types. The per-overload filter triggers for:

- **Pointer parameters** — any overload with a pointer type in its signature is hidden.
- **`ref readonly` returns** — any overload with a `ref readonly` return type is hidden.
- **References to excluded types** — any overload whose signature references a non-public type is hidden.

If every overload in a method group is excluded, the method name projects no symbol.

### CLR primitives become Mist Standard types automatically

`System.String` ↔ `string`, `System.Int32` ↔ `int`, etc. — bidirectional and recursive, so `Dictionary<string, List<int>>` just works. You never write `System.Int32`.

### `ref` and `in` parameters

CLR `ref` parameters appear as Mist `ref` — pass with `ref` at the call site. CLR `in` parameters also appear as Mist `ref` (the emitter writes the correct `in` in generated C#). CLR mutable `ref` *returns* are supported and bind to a `ref` local; `ref readonly` returns are hidden.

### Ref structs (`Span<T>`, `ReadOnlySpan<T>`)

Ref struct types (any CLR type carrying `IsByRefLikeAttribute`) are projected as Mist stack-only structs (`StructType.IsRefStruct = true`). Members whose signatures reference ref structs are visible and callable — they are not hidden by the visibility filter.

**Known limitation:** Mist does not perform ref-struct escape or lifetime analysis for projected CLR ref structs. If you misuse a projected ref struct in a way that violates CLR lifetime rules — for example storing it in a heap location, boxing it, capturing it in a closure, or using it as a generic type argument — the error is reported by the downstream C# compiler when the transpiled C# is compiled, not by Mist itself. Full first-party ref-struct safety (stackalloc plus escape analysis) is tracked as separate future work.

### Other hidden members

The visibility filter hides what Mist can't express, rather than erroring: pointer types, events, non-public members, and any method whose signature references an excluded type. The rest of the type's API remains usable.

## IDisposable Types Are Automatically Droppable

CLR types that implement `System.IDisposable` are automatically recognized as droppable during type projection. You can use them in `with` blocks without any wrapper code — the emitter generates `.Dispose()` (not `.Drop()`) in the cleanup path.

```mist
using System.IO;

// StreamWriter implements IDisposable — it works in with-blocks directly.
with (var writer = new StreamWriter("output.txt"))
{
    writer.WriteLine("Hello from Mist");
}
// writer.Dispose() is called automatically at block exit
```

### Rules

- **IDisposable detected transitively** — if the type implements `IDisposable` through an interface chain (e.g., `Stream : IDisposable`), it is still recognized.
- **Explicit Drop wins** — if a projected CLR type also has an explicit `Drop()` method, the emitter uses `.Drop()` instead of `.Dispose()`. This lets you wrap CLR types with custom cleanup.
- **IAsyncDisposable alone is not enough** — a type implementing only `IAsyncDisposable` (without `IDisposable`) is *not* auto-recognized as droppable.
- **Mist-declared types are unaffected** — this auto-recognition applies only to CLR types imported from .NET assemblies. Mist types that implement `IDisposable` must still declare an explicit `Drop()` method to be usable in `with` blocks.

### Synthetic Drop Inference Exclusion

Fields whose type is an IDisposable-droppable CLR type are excluded from synthetic drop inference. This prevents the compiler from generating spurious `.Drop()` calls on fields that should be disposed through their owning container's cleanup logic, not individually.

## What Works As Expected

These project faithfully and need no special handling:

- **Generics** — full type-parameter info; constraints (`class`, `struct`, `new()`, base type, interface) translate; ones with no Mist equivalent (`unmanaged`, `notnull`) are dropped but the emitted C# stays correct.
- **Inheritance & interfaces** — full hierarchies project across assembly boundaries; assignability to base types and implemented interfaces works. Abstract types can't be `new`'d; sealed types can't be inherited.
- **Operators & conversions** — `op_Addition`, `op_Equality`, etc. project to Mist operators; `op_Implicit`/`op_Explicit` project to Mist cast operators.
- **`foreach`** over any `IEnumerable<T>`.
