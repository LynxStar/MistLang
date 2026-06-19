---
inclusion: manual
---

<!-- MIST-MANAGED-STEERING: installed by `mist steering`. Do not edit — this file is overwritten on every reinstall. Do not add this marker to your own steering files; any file containing it is treated as Mist-managed and may be deleted. -->

# Mist .NET Interop

How to consume .NET — SDK types, NuGet packages, and any .NET assembly — from Mist. Include this when adding dependencies or calling into existing C#/.NET code.

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

### CLR primitives become Mist Standard types automatically

`System.String` ↔ `string`, `System.Int32` ↔ `int`, etc. — bidirectional and recursive, so `Dictionary<string, List<int>>` just works. You never write `System.Int32`.

### `ref` and `in` parameters

CLR `ref` parameters appear as Mist `ref` — pass with `ref` at the call site. CLR `in` parameters also appear as Mist `ref` (the emitter writes the correct `in` in generated C#). CLR mutable `ref` *returns* are supported and bind to a `ref` local; `ref readonly` returns are hidden.

### Other hidden members

The visibility filter hides what Mist can't express, rather than erroring: pointer types, events, non-public members, and any method whose signature references an excluded type. The rest of the type's API remains usable. `ref struct` types (`Span<T>`, `ReadOnlySpan<T>`) are projected as stack-only structs.

## What Works As Expected

These project faithfully and need no special handling:

- **Generics** — full type-parameter info; constraints (`class`, `struct`, `new()`, base type, interface) translate; ones with no Mist equivalent (`unmanaged`, `notnull`) are dropped but the emitted C# stays correct.
- **Inheritance & interfaces** — full hierarchies project; assignability to base types and implemented interfaces works. Abstract types can't be `new`'d; sealed types can't be inherited.
- **Operators & conversions** — `op_Addition`, `op_Equality`, etc. project to Mist operators; `op_Implicit`/`op_Explicit` project to Mist cast operators.
- **`foreach`** over any `IEnumerable<T>`.
