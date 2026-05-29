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

## CLR-to-Mist Type Mapping

The Assembly Type Provider translates CLR types into Mist types following these rules:

| CLR Construct | Mist Projection |
|---------------|-----------------|
| Class | `ClassType` with namespace, base type, interfaces |
| Struct (non-ref) | `StructType` |
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

## Visibility Filter

Not all CLR members are visible in Mist. The Assembly Type Provider hides members that use CLR features Mist does not support. This means you can still use the supported surface of a type without encountering errors from unsupported members.

### What Gets Hidden

| CLR Feature | Why It's Hidden |
|-------------|-----------------|
| `out` parameters | Mist has no `out` parameter semantics; methods like `TryGetValue` are excluded |
| `ref` parameters (excluding `in`) | Mist's `ref` is a separate feature with different semantics; CLR `ref` params are excluded |
| `ref` return types | Mist does not support returning references to storage locations |
| Pointer types (`int*`, `void*`) | Mist does not expose unsafe pointer operations |
| `ref struct` types (`Span<T>`, `ReadOnlySpan<T>`) | Ref structs have stack-only lifetime constraints that Mist's type system does not model |
| Members referencing excluded types | If a method's signature uses an excluded type in any position, the entire method is hidden |
| Non-public members | Only `public` members are projected; `internal`, `protected`, and `private` are excluded |
| Events | Mist does not yet support the .NET event model |

### Why Hide Instead of Error?

The alternative would be to emit a compile error whenever you try to use a type that has unsupported members. But most types have a mix of supported and unsupported members — `Dictionary<TKey, TValue>` has `TryGetValue` (hidden due to `out` param) alongside `Add`, `ContainsKey`, and the indexer (all visible). Hiding unsupported members lets you use the rest of the API without friction.

### `in` Parameters Are Not Hidden

The `in` keyword in C# marks a parameter as ref-readonly — the callee receives a reference but cannot mutate it. Mist treats `in` parameters as regular pass-by-value parameters (the reference semantics are a C# optimization detail). Methods with `in` parameters remain visible.

### Working Around Hidden Members

When a method you need is hidden (like `Dictionary.TryGetValue`), use alternative patterns:

- **`ContainsKey` + indexer** instead of `TryGetValue`
- **LINQ methods** instead of methods with `out` parameters
- **Wrapper methods** in a Mist helper class that call the hidden API via the C# emitter

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
- **Type mapping** is automatic — CLR types become idiomatic Mist types
- **Null safety** is enforced — reference returns are `T | void`, requiring narrowing
- **Unsupported features** are hidden, not errored — use what works without friction
- **Generics, operators, inheritance** all work as expected
