# .NET Interop

Mist compiles to C# and runs on the .NET runtime, which means you have access to the entire .NET ecosystem — SDK framework types, NuGet packages, and any .NET assembly. The **Assembly Type Provider** reads type metadata directly from .NET assembly DLLs and surfaces those types into the Mist type system. You don't need to write bindings or wrapper code — just reference the types and use them.

## How It Works

When the Mist compiler encounters a type it doesn't recognize among your Mist source files, it queries the Assembly Type Provider. The provider uses `System.Reflection.MetadataLoadContext` to inspect assembly metadata without loading assemblies into the runtime. It then projects CLR types into Mist's type system — classes become `ClassType`, structs become `StructType`, interfaces become `AbstractType`, and enums become `EnumType`.

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

When your project declares packages or framework references, the compiler runs a **Package Resolution Stage** (Stage 1.5) between the Loading stage and the Lexing stage. By default, this stage is completely silent when everything resolves: no header, detail lines, or timing information.

This keeps normal build output clean (stage detail lines are elided below):

```
$ mist build
Stage 0 - Cleaning
...
Stage 1 - Loading
...
Stage 2 - Lexing
...
```

### When a Dependency Fails

A package or framework reference that fails to resolve is reported as soon as Stage 1.5 ends, before Stage 2 begins, with one diagnostic per failing dependency, even when two failures share a diagnostic identifier. Diagnostics go to stderr, so they appear whatever the verbosity: in a default build, with `--quiet`, and in `mist run` and `mist test`, which show no progress unless you pass `--verbose`.

```
$ mist build
Stage 0 - Cleaning
...
Stage 1 - Loading
...
error MIST_PKG_002: Package 'Newtonsoft.Jsn' version '13.0.3' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.
Stage 2 - Lexing
...
Stage 5 - Analysis
...
Build failed  1 error(s), 0 warning(s)  Total: 2.31s
```

The build does not stop at the failure. Lexing, parsing, binding and analysis still run against everything that did resolve, so errors in your source are reported in the same run, after the summary as usual; if your code uses a type from the package that failed, expect an unresolved-type error for it too. The summary counts the dependency error, which is not printed a second time, and the build fails before Stage 6 emits anything, so `mist run` and `mist test` launch nothing.

### The `--verbose` Flag

When you need to see what the resolver is doing, pass `--verbose`:

```
$ mist build --verbose
...
Stage 1.5 - Package Resolution
    Restored Newtonsoft.Json 13.0.3 (net10.0)
    Resolved Microsoft.AspNetCore.App 10.0.0
    342 ms
    2 succeeded, 0 failed
    Took 0.34s

Stage 2 - Lexing
...
```

The verbose output includes:
- One line per successfully restored package (name, version, selected TFM)
- One line per successfully resolved framework reference (name, version)
- A note for any package that contributed zero assemblies
- The stage's elapsed whole milliseconds and a success/failure summary

A failure under `--verbose` is printed inside the stage, after its header and before its counts. The `--verbose` and `--quiet` flags are mutually exclusive — supplying both produces a parse error.

### The `mist restore` Command

If you only want to check that dependencies resolve without running a full build, use `mist restore`:

```
$ mist restore
Restored Newtonsoft.Json 13.0.3 (net10.0)
Resolved Microsoft.AspNetCore.App 10.0.0
2 succeeded, 0 failed
```

`mist restore` always prints the full resolver detail (you don't need `--verbose`). It resolves packages and framework references but does not lex, type-check, or emit code. With no later stage to run, its diagnostics follow the summary on stderr. It exits with code 0 on success and non-zero if any dependency fails. A `mist.toml` diagnostic comes first, before any resolution, and an error among them stops the command with exit code 1 and no summary.

## Package Resolution Diagnostics

When a package cannot be resolved, the compiler emits a stable diagnostic identifier so you can quickly identify the category of failure. These diagnostics are always surfaced, during Stage 1.5, regardless of whether `--verbose` is supplied (see [When a Dependency Fails](#when-a-dependency-fails)).

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
| Class | `ClassType` with namespace, base type, implemented interfaces |
| Struct (non-ref) | `StructType` |
| Ref struct (`Span<T>`, etc.) | `StructType` with `IsRefStruct = true` (stack-only) |
| Interface | `AbstractType` |
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

## Calling CLR Members

A CLR method is called the way C# calls it:

- **Optional parameters** can be left off the end of the argument list. The native method supplies its own default, so Mist never evaluates the default value.
- **`params` parameters** take their elements as a flat list, or one array or collection that already holds them. Both C# `params` arrays and C# 13 `params` collections such as `ReadOnlySpan<T>` work this way.
- **`System.Object` parameters and locals** accept any value, which is boxed where necessary, as in C#. Nothing converts from `System.Object` back to a narrower type implicitly.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/clr-call-shapes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/clr-call-shapes.cs}}
```
{{#endtab }}
{{#endtabs }}

When several overloads could take the same arguments, the compiler picks the one C# picks:

- A candidate called in normal form beats one that has to expand its `params` list.
- Among expanded candidates, one with more fixed parameters wins, and a span collection beats an array.
- A candidate that receives every one of its parameters beats one that relies on a default.

Arguments to CLR members are checked like arguments to Mist methods. A wrong supplied argument, a `params` element of the wrong type and a dictionary key of the wrong type are reported in your Mist source, not by the C# compiler.

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
| Pointer types (`int*`, `void*`) | Mist does not expose unsafe pointer operations |
| Members referencing excluded types | If a method's signature uses a non-public type or pointer type in any position, the entire method is hidden |
| Non-public members | `internal` and `private` members are not projected. `protected` members are projected as `protected`, so only a Mist type deriving from the CLR type can reach them |
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

The alternative would be to emit a compile error whenever you try to use a type that has unsupported members. But most types have a mix of supported and unsupported members — `System.Text.Encoding` has `GetBytes` overloads taking pointers (hidden) alongside the `string` and array overloads (visible). Hiding unsupported members lets you use the rest of the API without friction.

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

### CLR `out` Parameters

CLR `out` parameters are projected as Mist `ref` parameters, so a method such as `Dictionary.TryGetValue` is callable: you pass the argument with `ref` at the call site, and the compiler emits the `out` keyword in the generated C#.

### CLR Ref Returns

CLR methods with mutable `ref` returns are supported. The method appears with a `ref` return type in Mist, and you can bind the result to a `ref` local:

```mist
ref int slot = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
slot = 99; // mutates the value in the dictionary
```

### CLR `ref readonly` Returns

A CLR method, property or indexer that returns `ref readonly` hands back a reference you may read but not write through. Mist admits it as exactly that, a readonly reference: read it as a value, or hold it in a `readonly ref` local, which aliases the element rather than copying it:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/ref-readonly-results.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/ref-readonly-results.cs}}
```
{{#endtab }}
{{#endtabs }}

It prints `first=10, copied=1`: the builder's write shows through the alias and not through the copy. A readonly reference promises only that *you* will not write through it, which is all `ref readonly` promises, so the element can still change under you, as it does here.

Taking a writable reference through one is refused with `MIST_PA_READONLY_REF_WRITTEN`: binding it to a `ref` local, passing it as a `ref` or `out` argument, assigning to it, or returning it by `ref`. A `readonly ref` local is a readonly reference in the same way.

### Working Around Hidden Members

When a method you need is hidden, use alternative patterns:

- **A sibling overload** that uses only supported types, such as `Encoding.GetBytes(string)` in place of the pointer overloads
- **Wrapper methods** in a Mist helper class that call the hidden API via the C# emitter

## The `mist inspect` Command

When you need to understand what the type provider sees for a particular CLR type, use `mist inspect`:

An excerpt of its output (`...` marks lines left out here):

```
$ mist inspect System.Text.Encoding
System.Text.Encoding (class)

  VISIBLE:
    property ASCII: Encoding
    property BigEndianUnicode: Encoding
    property BodyName: string
    ...
    method GetBytes(ReadOnlySpan<char, int>, Span<byte, int>) -> int
    method GetBytes(char[]) -> byte[]
    method GetBytes(char[], int, int) -> byte[]
    ...
    method GetType() -> Type [from System.Object]
    method IsAlwaysNormalized() -> bool
    method IsAlwaysNormalized(NormalizationForm) -> bool
    method MemberwiseClone() -> Object [protected] [from System.Object]
    method RegisterProvider(EncodingProvider) -> void
    method ToString() -> string | void [from System.Object]
    method TryGetBytes(ReadOnlySpan<char, int>, Span<byte, int>, ref int) -> bool
    method TryGetChars(ReadOnlySpan<byte, int>, Span<char, int>, ref int) -> bool

  HIDDEN:
    Finalize: HIDDEN: finalizer [from System.Object]
    GetByteCount: HIDDEN: signature uses System.Char*
    GetBytes: HIDDEN: signature uses System.Char*
    GetCharCount: HIDDEN: signature uses System.Byte*
    GetChars: HIDDEN: signature uses System.Byte*
    GetString: HIDDEN: signature uses System.Byte*
```

The output shows:
- The type's fully-qualified name and kind (class, struct, interface, or enum)
- Visible properties, indexers and methods with their projected Mist types, in ascending ordinal order by name; a protected member is marked `[protected]`. An indexer is listed the way Mist declares one, so `System.Text.StringBuilder` shows `indexer this[int]: char`
- For inherited members, the declaring base type is indicated with `[from ...]`
- Hidden members with the reason they are excluded: non-public, an event, a finalizer, a signature type Mist cannot represent, an unresolvable generic constraint, or a member of a base the Mist type does not inherit
- For a type Mist aliases to a Standard library type, such as `System.String`, a note naming the Standard type, and that type's own members

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

## LINQ Callable Inference

Imported LINQ methods use their projected callable signatures to target lambdas. `Select` infers its result type from the selector body; `Where` chooses the predicate overload matching the lambda's parameter count. The receiver supplies the input type, and a generic method's other arguments can also supply lambda input types. Explicit method type arguments take precedence over inference; types already fixed by the extension receiver belong to the receiver's binding.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/linq-callable-inference.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/linq-callable-inference.cs}}
```
{{#endtab }}
{{#endtabs }}

This prints `2`, `4`, `6` for the mapped sequence and `2`, `3` for the filtered sequence. An incompatible lambda remains a source diagnostic.

## Delegates and func Conversion

Named CLR delegates — types like `Predicate<T>`, `Comparison<T>`, and `RequestDelegate` that inherit from `System.MulticastDelegate` — are projected as distinct types in Mist. Unlike the `System.Func<...>` and `System.Action<...>` families (which map directly to Mist's structural `func<...>` type), named delegates preserve their CLR identity through projection and emission.

This matters because many .NET APIs accept a specific named delegate rather than a generic `Func<...>`. For example, `List<T>.FindAll` expects a `Predicate<T>`, and `List<T>.Sort` expects a `Comparison<T>`. Mist handles the conversion implicitly — you pass a lambda, a `func<...>` value, or a method group, and the compiler wraps it in the correct delegate constructor.

### Implicit Conversions

The following conversions happen implicitly (no cast required):

| Source | Target | Allowed? |
|--------|--------|----------|
| `func<...>` or lambda | Named delegate with compatible signature | ✓ |
| Named delegate | `func<...>` with compatible signature | ✓ |
| `func<...>` or lambda | `System.Delegate` | ✓ |
| Named delegate | `System.Delegate` | ✓ |
| Named delegate A | Named delegate B (different type, same signature) | ✗ |
| `System.Delegate` | Named delegate or `func<...>` | ✗ |

Two types are "signature-compatible" when they have the same number of parameters, each corresponding parameter type matches, and the return type matches.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/delegate-func-conversion.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/delegate-func-conversion.cs}}
```
{{#endtab }}
{{#endtabs }}

### Nominal Distinctness

Named delegates are nominally distinct from each other, even when their signatures are identical. If two delegate types happen to share the same `func<...>` shape, you still cannot assign one to the other:

```
// Hypothetical example — this does NOT compile:
System.Predicate<int> pred = x => x > 0;
System.Converter<int, bool> conv = pred;  // ERROR: nominally distinct types
```

To convert between two named delegates with the same signature, go through `func<...>` as an intermediate:

```
func<int, bool> intermediate = pred;
System.Converter<int, bool> conv = intermediate;  // OK — func→delegate conversion
```

### System.Delegate (The Base Type)

`System.Delegate` is the root base type of all CLR delegates. Any delegate-typed or `func<...>` value is assignable to `System.Delegate`. This is how APIs like ASP.NET Core's `MapGet` work — they accept `System.Delegate` so that any lambda can be passed regardless of its signature:

```mist
// ASP.NET Core MapGet accepts System.Delegate
var builder = WebApplication.CreateBuilder();
var app = builder.Build();
app.MapGet("/", () => "Hello!");
```

A value typed as `System.Delegate` cannot be narrowed back to a specific delegate or `func<...>` type without an explicit cast, and it cannot be invoked directly — you must cast it first.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/delegate-system-delegate.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/delegate-system-delegate.cs}}
```
{{#endtab }}
{{#endtabs }}

### Invoking Delegate-Typed Values

A value whose type is a named delegate is directly invocable using standard call syntax, just like `func<...>` values. The compiler validates the argument types against the delegate's signature:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/dotnet-interop/delegate-invocation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/dotnet-interop/delegate-invocation.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler closes the delegate's `Invoke` parameters and result using its type arguments. It targets each argument with that closed parameter type and reports incorrect counts and incompatible types at the Mist source. The call above has type `bool`; the variable keeps the nominal type `System.Predicate<int>` in emitted C#.

## Inheritance and Interfaces

CLR inheritance hierarchies are fully projected. If class `C` inherits from `B` which inherits from `A`, the Mist type for `C` is assignable to variables of type `B` or `A`. Base class members are accessible on derived types.

Interface implementations are also projected — a CLR type implementing `IComparable<T>` is assignable to `IComparable<T>` in Mist.

A Mist type conforming to a CLR interface does not have to answer a member the interface implements with a default body. A call to that member gives the same result on the conforming value as through the interface. If the type declares the member itself, its own version wins on both routes. If a derived CLR interface declares its own body for the member, the most specific body runs. If it reabstracts the member, the conformer must answer it.

On a value of a CLR class you reach the members that the class and its base classes declare, as in C#. A member the class implements only explicitly for one of its interfaces is reachable through that interface, not through the class.

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

A Mist enumerable or enumerator can also reach `IEnumerable<T>` or `IEnumerator<T>` through an abstract of its own, such as `abstract ITicker : IEnumerator<int>`. It still receives the extra members the CLR interfaces require (the non-generic `Current` and `GetEnumerator`, `Reset` and `Dispose`), each emitted once. A `Dispose` or `Reset` the type or one of its abstracts already declares is kept.

## Summary

The Assembly Type Provider gives you access to the full .NET ecosystem from Mist:

- **SDK types** are available immediately — no configuration needed
- **NuGet packages** are added via `[packages]` in `mist.toml`
- **Framework references** are enabled via `[framework_references]` in `mist.toml`
- **Type mapping** is automatic — CLR types become idiomatic Mist types
- **Null safety** is enforced — reference returns are `T | void`, requiring narrowing
- **Cross-assembly inheritance** is resolved — derived types are assignable to base types across assemblies
- **Inherited members** are callable on derived types
- **CLR calls** take optional parameters, `params` lists and `System.Object` targets the way C# does, and are checked in your Mist source
- **Per-overload hiding** — only overloads using genuinely unsupported types are hidden
- **Ref structs** are projected as stack-only structs and remain usable
- **Unsupported features** are hidden per-overload, not errored — use what works without friction
- **`mist inspect`** shows exactly which members are visible and which are hidden, and why
- **`mist restore`** dumps the full dependency resolution output on demand
- **Generics, operators, inheritance** all work as expected
