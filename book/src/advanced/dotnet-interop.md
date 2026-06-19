# .NET Interop

Mist compiles to C# and runs on the .NET runtime, which means you have access to the entire .NET ecosystem — SDK framework types, NuGet packages, and any .NET assembly. The **Assembly Type Provider** reads type metadata directly from .NET assembly DLLs and surfaces those types into the Mist type system. You don't need to write bindings or wrapper code — just reference the types and use them.

## How It Works

When the Mist compiler encounters a type it doesn't recognize among your Mist source files, it queries the Assembly Type Provider. The provider uses `System.Reflection.MetadataLoadContext` to inspect assembly metadata without loading assemblies into the runtime. It then projects CLR types into Mist's type system — classes become `ClassType`, structs become `StructType`, interfaces become `InterfaceType`, and enums become `EnumType`.

Resolution is lazy: types are materialized only when first referenced, not eagerly loaded at startup. This keeps compilation fast even when hundreds of SDK assemblies are available.

## SDK Assemblies (Available by Default)

All assemblies that ship with `Microsoft.NET.Sdk` for the target framework (defaulting to `net10.0`) are available without any configuration. This includes the entire `System.*` namespace hierarchy:

- `System.Collections.Generic` — `List<T>`, `Dictionary<TKey, TValue>`, `HashSet<T>`, etc.
- `System.Linq` — LINQ extension methods
- `System.IO` — file and stream operations
- `System.Text` — `StringBuilder`, encoding utilities
- `System.Threading.Tasks` — `Task`, `Task<T>`, async infrastructure

The compiler discovers the SDK installation automatically (via `dotnet --list-sdks` or well-known paths) and loads all reference assemblies from the target framework pack.

## Adding NuGet Packages

To use types from third-party libraries, add a `[packages]` table to your `mist.toml`:

```toml
name = "MyProject"

[packages]
Newtonsoft.Json = "13.0.3"
Humanizer.Core = "2.14.1"
```

Each entry is a package name mapped to an exact version string. The compiler downloads packages from nuget.org (using the standard NuGet cache at `~/.nuget/packages/`) and makes their types available. Transitive dependencies are resolved automatically.

Package names must match the pattern `[a-zA-Z0-9\-_\.]+` and cannot exceed 128 characters. Version strings must be non-empty exact versions (version ranges are not supported).

## Framework References

Beyond the base SDK, .NET provides additional shared frameworks like ASP.NET Core. To make types from these frameworks available, add a `[framework_references]` table to your `mist.toml`:

```toml
name = "MyWebApp"

[packages]
Newtonsoft.Json = "13.0.3"

[framework_references]
"Microsoft.AspNetCore.App" = true
```

Each entry is a shared framework name mapped to a boolean. Set it to `true` to enable the framework; `false` (or omitting the entry) excludes it. The compiler locates the matching reference assembly pack from the installed .NET SDK and makes all its public types available.

Known shared frameworks:
- `Microsoft.AspNetCore.App` — ASP.NET Core types (routing, HTTP, middleware)
- `Microsoft.WindowsDesktop.App` — WPF and Windows Forms (Windows only)

When a framework reference is enabled, its types are available without any `[packages]` entry:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/framework-reference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/framework-reference.cs}}
```
{{#endtab }}
{{#endtabs }}

## Quiet-by-Default Builds

When your project declares packages or framework references, the compiler runs a **Package Resolution Stage** (Stage 1.5) between the Loading stage and the Lexing stage. By default, this stage is completely silent — no headers, detail lines, or timing information are printed when everything resolves successfully. Only error diagnostics are surfaced.

This keeps normal build output clean:

```
$ mist build
Stage 0 - Cleaning
Stage 1 - Loading
Stage 2 - Lexing
...
```

### The `--verbose` Flag

When you need to see what the resolver is doing, pass `--verbose`:

```
$ mist build --verbose
Stage 0 - Cleaning
Stage 1 - Loading
Stage 1.5 - Package Resolution
  Restored Newtonsoft.Json 13.0.3 (net10.0)
  Resolved Microsoft.AspNetCore.App 10.0.0
  Elapsed: 342ms | 2 succeeded, 0 failed
Stage 2 - Lexing
...
```

The verbose output includes:
- One line per successfully restored package (name, version, selected TFM)
- One line per successfully resolved framework reference (name, version)
- A note for any package that contributed zero assemblies
- An elapsed-time and success/failure summary

The `--verbose` and `--quiet` flags are mutually exclusive — supplying both produces a parse error.

### The `mist restore` Command

If you only want to check that dependencies resolve without running a full build, use `mist restore`:

```
$ mist restore
Restored Newtonsoft.Json 13.0.3 (net10.0)
Resolved Microsoft.AspNetCore.App 10.0.0
2 succeeded, 0 failed
```

`mist restore` always prints the full resolver detail (you don't need `--verbose`). It resolves packages and framework references but does not lex, type-check, or emit code. It exits with code 0 on success and non-zero if any dependency fails.

## Package Resolution Diagnostics

When a package cannot be resolved, the compiler emits a stable diagnostic identifier so you can quickly identify the category of failure. These diagnostics are always surfaced regardless of whether `--verbose` is supplied.

### `MIST_PKG_001` — Incompatible Target Framework

**Trigger:** A declared package version provides no assemblies compatible with the project's target framework.

**Meaning:** The package was found and downloaded, but it does not ship assemblies for your TFM. The diagnostic lists the framework targets the package does offer.

```
error MIST_PKG_001: Package 'EmbedIO' version '3.5.2' does not support net10.0.
  Available frameworks: net6.0, net7.0, net8.0
```

**Fix:** Check if a newer version of the package supports your target framework, or lower your target framework.

### `MIST_PKG_002` — Package Not Found

**Trigger:** The declared package name or version cannot be found on any configured NuGet feed.

**Meaning:** Either the package name is misspelled, the version doesn't exist, or the feed isn't reachable. The diagnostic names every feed that was searched.

```
error MIST_PKG_002: Package 'Newtonsoft.Jsn' version '13.0.3' not found.
  Searched feeds: https://api.nuget.org/v3/index.json
```

**Fix:** Verify the package name and version in your `mist.toml`. Check nuget.org for the correct spelling.

### `MIST_PKG_003` — Network Failure or Timeout

**Trigger:** A connection or network failure occurs while restoring a package, or the restore does not complete within 100 seconds.

**Meaning:** The NuGet feed could not be reached, or the download was too slow. The diagnostic indicates which category (connection failure vs. timeout).

```
error MIST_PKG_003: Failed to restore 'Newtonsoft.Json' version '13.0.3': connection failure.
```

```
error MIST_PKG_003: Failed to restore 'LargePackage' version '1.0.0': restore timeout (100s).
```

**Fix:** Check your network connection. If the package is already cached at `~/.nuget/packages/`, it will be used without a network request.

## Framework Reference Diagnostics

### `MIST_FWREF_001` — Framework Not Installed

**Trigger:** An enabled framework reference is not installed on the host machine for the target framework.

**Meaning:** The compiler looked for the reference assembly pack at the expected path and didn't find it. The diagnostic names the framework, the target framework, and the path that was searched.

```
error MIST_FWREF_001: Framework 'Microsoft.AspNetCore.App' is not installed for net10.0.
  Searched: /usr/local/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/
```

**Fix:** Install the .NET SDK version that includes the required shared framework. For ASP.NET Core, this is typically included with the .NET SDK installer.

### `MIST_FWREF_002` — Unknown Framework Name

**Trigger:** The enabled framework name does not correspond to a known shared framework.

**Meaning:** The framework name is likely misspelled or refers to something that isn't a .NET shared framework.

```
error MIST_FWREF_002: Unknown shared framework 'Microsoft.AspNet.App'.
  Known frameworks: Microsoft.AspNetCore.App, Microsoft.WindowsDesktop.App
```

**Fix:** Use one of the recognized shared framework names. The common ones are `Microsoft.AspNetCore.App` and `Microsoft.WindowsDesktop.App`.

## CLR-to-Mist Type Mapping

The Assembly Type Provider translates CLR types into Mist types following these rules:

| CLR Construct | Mist Projection |
|---------------|-----------------|
| Class | `ClassType` with namespace, base type, interfaces |
| Struct (non-ref) | `StructType` |
| Ref struct (`Span<T>`, etc.) | `StructType` with `IsRefStruct = true` (stack-only) |
| Interface | `InterfaceType` |
| Enum | `EnumType` with named constants and underlying type |
| Generic type | Mist generic type with same arity and parameter names |
| Generic constraints | Translated where possible (`class`, `struct`, `new()`, base types) |

### Intrinsic Type Substitution

CLR primitive types are automatically substituted with their Mist Standard Library equivalents. When a .NET method returns `System.String`, you see `Standard.String` in Mist. When a method takes `System.Int32`, you pass a Mist `int`. The mapping is bidirectional — the same table drives both directions:

| CLR Type | Mist Type |
|----------|-----------|
| `System.Int32` | `Standard.Int32` (`int`) |
| `System.Int64` | `Standard.Int64` (`long`) |
| `System.Boolean` | `Standard.Boolean` (`bool`) |
| `System.String` | `Standard.String` (`string`) |
| `System.Double` | `Standard.Double` (`double`) |
| `System.Single` | `Standard.Single` (`float`) |
| `System.Decimal` | `Standard.Decimal` (`decimal`) |
| `System.Char` | `Standard.Char` (`char`) |
| `System.Byte` | `Standard.Byte` (`byte`) |

This substitution is recursive — `List<int>` in CLR becomes `List<Standard.Int32>` in Mist, and `Dictionary<string, List<int>>` becomes `Dictionary<Standard.String, List<Standard.Int32>>`.

## Reference Types as `T | void` Unions

Mist has no `null`. Instead, the Assembly Type Provider projects all CLR reference type returns as `T | void` unions. This forces you to handle the possibility of absence before using the value.

This applies to:
- Method return types that are classes, interfaces, strings, arrays, or delegates
- `Nullable<T>` value types (e.g., `int?` in C# becomes `int | void` in Mist)

Non-nullable value types (e.g., `int`, `bool`, `double`) are projected as plain Mist types without the `| void` union — they cannot be null at the CLR level.

### Narrowing Before Use

Because reference type returns are `T | void`, you must narrow the type before accessing members. The most common patterns are:

1. **Guard with a condition** — use `ContainsKey`, `Count > 0`, or similar checks, then access the value in the guarded scope
2. **Pattern matching** — use `is` to narrow the union to the concrete type
3. **Void-coalescing** — use `??` to provide a fallback value

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/nullable-handling.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/nullable-handling.cs}}
```
{{#endtab }}
{{#endtabs }}

## Using CLR Types

Import CLR namespaces with `using` directives, then use types by their simple names. Mist-declared types take priority in name resolution — CLR types are searched only when no Mist type matches.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/collection-usage.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/collection-usage.cs}}
```
{{#endtab }}
{{#endtabs }}

You can also reference types by their fully qualified name without a `using` directive:

```csharp
var list = new System.Collections.Generic.List<int>();
```

> Note: Fully qualified references work without `using` directives, but the `using` form is preferred for readability.

## Cross-Assembly Inheritance

CLR inheritance hierarchies that span multiple assemblies are fully resolved. When a class like `MemoryStream` (in `System.IO`) inherits from `Stream` (in `System.Runtime`), the Mist type projector connects the full chain through the Assembly Type Provider's shared cache. This means:

- A projected derived type is **assignable** to any ancestor type in its chain
- **Inherited members** declared on base types are callable on derived instances
- Multi-level chains work: `NetworkStream → Stream → MarshalByRefObject → object`

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/cross-assembly-inheritance.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/cross-assembly-inheritance.cs}}
```
{{#endtab }}
{{#endtabs }}

In this example, `MemoryStream` is assigned to a variable of type `Stream`. The `Length` and `CanRead` properties are declared on `Stream` (a different assembly than `MemoryStream`) but are callable through the derived type because the inheritance chain is fully connected.

### Inherited Member Resolution

When a derived type overrides or shadows a base member, the Assembly Type Provider projects only the most-derived visible signature — you won't see duplicate base definitions. If the most-derived definition happens to be hidden (because it uses an unsupported type) but an ancestor's definition is visible, the nearest visible ancestor's signature is exposed instead.

Members inherited through multiple levels of the chain are accessible on the most-derived type:

```mist
// NetworkStream → Stream → MarshalByRefObject
// Read() and Write() are declared on Stream, callable on NetworkStream
```

## Visibility Filter

Not all CLR members are visible in Mist. The Assembly Type Provider hides members that use CLR features Mist does not support. This means you can still use the supported surface of a type without encountering errors from unsupported members.

### What Gets Hidden

| CLR Feature | Why It's Hidden |
|-------------|-----------------|
| `out` parameters | Mist has no `out` parameter semantics; methods like `TryGetValue` are excluded |
| `ref readonly` return types | Mist cannot express `ref readonly` locals for CLR-originated references; the emitted C# would require `ref readonly` binding |
| Pointer types (`int*`, `void*`) | Mist does not expose unsafe pointer operations |
| Members referencing excluded types | If a method's signature uses a non-public type or pointer type in any position, the entire method is hidden |
| Non-public members | Only `public` members are projected; `internal`, `protected`, and `private` are excluded |
| Events | Mist does not yet support the .NET event model |

### Individual-Overload Hiding

Visibility filtering is applied **per overload**, not per method group. When a method group (like `Encoding.GetBytes`) contains overloads with different signatures, only the overloads that use genuinely unsupported types are hidden — the rest remain callable.

For example, `System.Text.Encoding.GetBytes` has many overloads:
- `GetBytes(string)` → **visible** (uses only supported types)
- `GetBytes(char*, int, byte*, int)` → **hidden** (pointer parameters)
- `GetBytes(ReadOnlySpan<char>, Span<byte>)` → **visible** (ref structs are projected — see below)

This means you can always call the `string` overload even though some sibling overloads use pointer types. Similarly, `Socket.Send(byte[])` and `Console.ReadLine()` remain callable regardless of what other overloads exist on those types.

When **every** overload in a method group uses unsupported types (e.g., a method where every variant takes a pointer parameter), the entire method name projects no symbol.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/overload-filtering.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/overload-filtering.cs}}
```
{{#endtab }}
{{#endtabs }}

### Why Hide Instead of Error?

The alternative would be to emit a compile error whenever you try to use a type that has unsupported members. But most types have a mix of supported and unsupported members — `Dictionary<TKey, TValue>` has `TryGetValue` (hidden due to `out` param) alongside `Add`, `ContainsKey`, and the indexer (all visible). Hiding unsupported members lets you use the rest of the API without friction.

## Ref Structs (Span, ReadOnlySpan)

CLR ref struct types — including `System.Span<T>` and `System.ReadOnlySpan<T>` — are projected as Mist **stack-only structs** with `StructType.IsRefStruct = true`. They are fully usable:

- You can declare them as local variables
- You can construct them, index them, call their members (`Slice`, `Length`, etc.)
- You can iterate them with `foreach`
- Method overloads that accept or return ref struct types are **not hidden** — they are projected alongside other overloads

### Known Limitation: No Escape Analysis

Mist does not perform ref-struct escape or lifetime analysis for projected CLR ref structs. If you misuse a ref struct (store it in a heap location, box it, capture it in a closure, or use it as a generic type argument), the error is reported by the **downstream C# compiler** when the transpiled code is compiled — not by Mist's own checks.

Full first-party ref-struct safety (stackalloc plus escape analysis) is tracked as future work. In the meantime, the C# compiler serves as the safety backstop.

### CLR Ref Parameters

CLR `ref` parameters map directly to Mist `ref` parameters. When a .NET method declares a `ref` parameter, it appears in Mist as a `ref` parameter — you must pass the argument with the `ref` keyword at the call site, just like Mist-defined methods:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/ref-interop.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/ref-interop.cs}}
```
{{#endtab }}
{{#endtabs }}

Intrinsic type substitution applies through the ref unwrapping — `ref System.Int32` in CLR becomes `ref Standard.Int32` in Mist.

### CLR `in` Parameters

CLR `in` parameters are projected as Mist `ref` parameters. The `in` keyword in C# marks a parameter as ref-readonly, but from Mist's perspective it behaves like a `ref` parameter — you pass the argument with `ref` at the call site. The compiler handles emitting the correct `in` keyword in the generated C# automatically.

### CLR Ref Returns

CLR methods with mutable `ref` returns are supported. The method appears with a `ref` return type in Mist, and you can bind the result to a `ref` local:

```mist
ref int slot = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
slot = 99; // mutates the value in the dictionary
```

### CLR `ref readonly` Returns

CLR methods with `ref readonly` returns remain hidden. The emitted C# would require binding the result to a `ref readonly` local, which Mist cannot currently express for CLR-originated references. These methods are excluded by the visibility filter.

### Working Around Hidden Members

When a method you need is hidden (like `Dictionary.TryGetValue`), use alternative patterns:

- **`ContainsKey` + indexer** instead of `TryGetValue`
- **LINQ methods** instead of methods with `out` parameters
- **Wrapper methods** in a Mist helper class that call the hidden API via the C# emitter

## The `mist inspect` Command

When you need to understand what the type provider sees for a particular CLR type, use `mist inspect`:

```
$ mist inspect System.Text.Encoding
System.Text.Encoding (class)

Visible Properties:
  BodyName : string                    (inherited from System.Text.Encoding)
  EncodingName : string                (inherited from System.Text.Encoding)
  HeaderName : string                  (inherited from System.Text.Encoding)
  WebName : string                     (inherited from System.Text.Encoding)

Visible Methods:
  GetBytes(string) : byte[]
  GetBytes(char[]) : byte[]
  GetBytes(char[], int, int) : byte[]
  GetBytes(ReadOnlySpan<char>, Span<byte>) : int
  GetString(byte[]) : string
  GetString(byte[], int, int) : string
  GetString(ReadOnlySpan<byte>) : string
  Equals(object) : bool                (inherited from System.Object)
  GetHashCode() : int                  (inherited from System.Object)
  ToString() : string                  (inherited from System.Object)

Hidden Members:
  GetBytes(char*, int, byte*, int) : int   HIDDEN: pointer parameter (char*)
  GetByteCount(char*, int) : int           HIDDEN: pointer parameter (char*)
```

The output shows:
- The type's fully-qualified name and kind (class, struct, interface, or enum)
- Visible properties and methods with their projected Mist types, in ascending ordinal order by name
- For inherited members, the declaring base type is indicated
- Hidden members with the reason they are excluded

Run `mist inspect` from a directory containing a `mist.toml` — the command uses the same SDK, package, and framework reference assemblies the build would use. If the type name cannot be resolved, the command prints a message and exits with a non-zero exit code.

## Generic Types and Constraints

Generic CLR types are projected with their full type parameter information. Constraints are translated where Mist has equivalents:

| CLR Constraint | Mist Equivalent |
|----------------|-----------------|
| `where T : class` | `where T : class` |
| `where T : struct` | `where T : struct` |
| `where T : new()` | Constructor constraint |
| `where T : BaseType` | `where T : BaseType` (projected Mist type) |
| `where T : IInterface` | `where T : IInterface` (projected Mist type) |

Constraints with no Mist equivalent (e.g., `unmanaged`, `notnull`) are omitted — the type parameter remains less constrained in Mist than in C#. The C# emitter still produces the correct constrained code.

## Operators and Conversions

CLR types that define operator methods (`op_Addition`, `op_Equality`, etc.) have those operators projected as Mist operator symbols. You can use `+`, `-`, `==`, `<`, and other operators naturally on types that define them.

Implicit and explicit conversion operators (`op_Implicit`, `op_Explicit`) are projected as Mist cast operators, enabling `as` casts between types that define conversions.

## Inheritance and Interfaces

CLR inheritance hierarchies are fully projected. If class `C` inherits from `B` which inherits from `A`, the Mist type for `C` is assignable to variables of type `B` or `A`. Base class members are accessible on derived types.

Interface implementations are also projected — a CLR type implementing `IComparable<T>` is assignable to `IComparable<T>` in Mist.

Abstract types cannot be instantiated with `new` (the compiler reports an error), and sealed types cannot be inherited from in Mist code.

## Iterating CLR Collections

CLR types implementing `IEnumerable<T>` can be iterated with `foreach`. The compiler resolves the element type from the `IEnumerable<T>` implementation and emits a standard C# `foreach` loop:

```csharp
var list = new List<int>();
list.Add(1);
list.Add(2);
list.Add(3);

foreach (int x in list)
{
    Console.WriteLine(x);
}
```

> Note: `foreach` over CLR collections uses the same syntax as Mist-native enumerables. The compiler handles the protocol translation automatically.

## Summary

The Assembly Type Provider gives you access to the full .NET ecosystem from Mist:

- **SDK types** are available immediately — no configuration needed
- **NuGet packages** are added via `[packages]` in `mist.toml`
- **Framework references** are enabled via `[framework_references]` in `mist.toml`
- **Type mapping** is automatic — CLR types become idiomatic Mist types
- **Null safety** is enforced — reference returns are `T | void`, requiring narrowing
- **Cross-assembly inheritance** is resolved — derived types are assignable to base types across assemblies
- **Inherited members** are callable on derived types
- **Per-overload hiding** — only overloads using genuinely unsupported types are hidden
- **Ref structs** are projected as stack-only structs and remain usable
- **Unsupported features** are hidden per-overload, not errored — use what works without friction
- **`mist inspect`** shows exactly which members are visible and which are hidden, and why
- **`mist restore`** dumps the full dependency resolution output on demand
- **Generics, operators, inheritance** all work as expected
