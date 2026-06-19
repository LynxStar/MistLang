---
inclusion: manual
---

<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own steering files; any file containing it is treated as Mist-managed and may be deleted. -->

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

## Build Output: Quiet by Default

Builds are **quiet by default**. When packages or framework references are declared, the Package Resolution stage always runs (between Loading and Lexing), but its detail output is suppressed unless you opt in with `--verbose`. Error diagnostics (`MIST_PKG_*`, `MIST_FWREF_*`) always surface regardless of verbosity.

```bash
mist build                 # quiet — no resolution output unless errors
mist build --verbose       # prints Stage 1.5 header, per-package/framework detail, elapsed time
mist run --verbose         # same for run
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

Exits 0 on full success, non-zero on any resolution failure or when run outside a project folder.

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

## What Works As Expected

These project faithfully and need no special handling:

- **Generics** — full type-parameter info; constraints (`class`, `struct`, `new()`, base type, interface) translate; ones with no Mist equivalent (`unmanaged`, `notnull`) are dropped but the emitted C# stays correct.
- **Inheritance & interfaces** — full hierarchies project across assembly boundaries; assignability to base types and implemented interfaces works. Abstract types can't be `new`'d; sealed types can't be inherited.
- **Operators & conversions** — `op_Addition`, `op_Equality`, etc. project to Mist operators; `op_Implicit`/`op_Explicit` project to Mist cast operators.
- **`foreach`** over any `IEnumerable<T>`.
