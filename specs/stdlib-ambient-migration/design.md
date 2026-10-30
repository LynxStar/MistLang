# Design Document: stdlib-ambient-migration

## Overview

This design migrates `Console`, `Math`, and `Convert` from ambient types (synthesized on-demand by `CSharpAmbientTypeResolver`) to proper Standard Library intrinsic types declared in `.mist` source files. After migration, these types follow the same pattern as `String`, `Array`, and all numeric primitives: declared in `Source/Standard/Bootstrap/`, marked `intrinsic`, loaded by `StdlibLoader`, and mapped to their CLR equivalents via `CSharpTypeMap`.

The migration touches four layers:
1. **Stdlib declarations** — new `.mist` files declaring the types and their intrinsic methods
2. **Type map** — `CSharpTypeMap` entries mapping `Standard.Console` → `System.Console`, etc.
3. **Intrinsic emission** — `IntrinsicEmissionTable` entries for static method calls (e.g., `Console.WriteLine` → `System.Console.WriteLine`)
4. **Ambient resolver cleanup** — removing Console/Math/Convert registrations from `CSharpAmbientTypeResolver`

The ambient type system (`IAmbientTypeResolver`, `CSharpAmbientTypeResolver`) remains in the codebase for future use but will have no registered types after this migration.

## Architecture

### Current Flow (Ambient Types)

```mermaid
graph LR
    A[User code: Console.WriteLine] --> B[SemanticModelBuilder Pass 2]
    B --> C[IAmbientTypeResolver.ResolveType]
    C --> D[CSharpAmbientTypeResolver builds ClassType + members]
    D --> E[Symbol registered with SymbolOrigin.Backend]
    E --> F[CSharpEmitter emits 'Console.WriteLine' with 'using System;']
```

### Target Flow (Stdlib Types)

```mermaid
graph LR
    A[User code: Console.WriteLine] --> B[SemanticModelBuilder Pass 1]
    B --> C[StdlibLoader loads Console.mist]
    C --> D[Parser produces ClassDeclarationNode with intrinsic modifier]
    D --> E[Symbol registered with SymbolOrigin.Source in Standard namespace]
    E --> F[CSharpEmitter resolves 'Console' via CSharpTypeMap]
    F --> G[Emits 'System.Console.WriteLine' via IntrinsicEmissionTable]
```

### Key Architectural Decisions

1. **Static methods use IntrinsicEmissionTable, not TryGetIntrinsicStaticMethodTemplate**: The current `TryGetIntrinsicStaticMethodTemplate` in the emitter only handles `Type.Of`. For Console/Math/Convert, the emitter will use the same `IntrinsicEmissionTable.TryGetStaticMethod` path, but the emitter's postfix suffix handler needs to be extended to check static method intrinsics for any stdlib type (not just `Type`). Alternatively, since `CSharpTypeMap` will map `Console` → `System.Console`, the emitter can simply emit `System.Console.WriteLine(...)` directly — the CLR type name is the correct C# emission. This is the simpler approach and matches how the emitter already handles other backend-backed types.

2. **No IntrinsicEmissionTable entries needed for Console/Math/Convert**: Unlike `String.Substring` or `Regex.IsMatch` which require template-based rewriting (e.g., `{0}.Substring({1})`), Console/Math/Convert methods map 1:1 to their CLR equivalents with identical names and signatures. The emitter just needs to emit `System.Console.WriteLine(arg)` — which it will do naturally once `CSharpTypeMap` maps `Console` → `System.Console`. The static method call `Console.WriteLine(x)` becomes `System.Console.WriteLine(x)` through the existing `TryGetMappedName` path in `EmitPrimary`/`EmitPostfixSuffix`.

3. **SymbolOrigin changes from Backend to Source**: After migration, these types will have `SymbolOrigin.Source` since they come from parsed `.mist` files. This is the correct behavior — they are no longer "ambient" types injected by the backend.

4. **User code changes `using System;` → `using Standard;`**: Since the types move from the `System` namespace (ambient) to the `Standard` namespace (stdlib), user code must update its using directive. This is a breaking change for existing programs but aligns with the stdlib-first architecture.

## Components and Interfaces

### New Stdlib Files

Three new `.mist` files in `Source/Standard/Bootstrap/`:

| File | Type | Methods |
|------|------|---------|
| `Console.mist` | `intrinsic public class Console` | `WriteLine(String)`, `Write(String)` |
| `Math.mist` | `intrinsic public class Math` | `Abs(Int32)`, `Max(Int32, Int32)`, `Min(Int32, Int32)` |
| `Convert.mist` | `intrinsic public class Convert` | `ToInt32(String)` |

### Modified Components

| Component | Change |
|-----------|--------|
| `CSharpTypeMap` | Add entries: `Standard.Console` → `System.Console`, `Standard.Math` → `System.Math`, `Standard.Convert` → `System.Convert` |
| `CSharpAmbientTypeResolver` | Remove Console/Math/Convert registrations; `HasNamespace("System")` returns `false` |
| `CSharpEmitter` | Extend `TryGetIntrinsicStaticMethodTemplate` to handle Console/Math/Convert static calls, OR rely on `CSharpTypeMap` name resolution |
| `IntrinsicEmissionTable` | Add static method entries for Console/Math/Convert |
| Sample files | Change `using System;` → `using Standard;` |
| Test files | Remove `AmbientTypePropertyTests`, `CSharpAmbientTypeResolverStdlibTests`; add stdlib-based tests |

### Emission Strategy for Static Method Calls

The emitter currently handles static method calls in `EmitPostfixSuffix` → `MethodCallSuffixPostfixSuffixNode`. When the target is a type name (e.g., `Console`), the flow is:

1. `EmitPrimary` emits the identifier as-is: `"Console"`
2. `EmitPostfixSuffix` processes the method call suffix `.WriteLine(x)`
3. It checks `TryGetIntrinsicStaticMethodTemplate` — currently only handles `Type.Of`
4. Falls through to emit `Console.WriteLine(x)`

After migration, two approaches are viable:

**Approach A: Extend TryGetIntrinsicStaticMethodTemplate** — Add Console/Math/Convert to the type-name mapping in that method, and add entries to `IntrinsicEmissionTable.StaticMethods`. The templates would be `"System.Console.{methodName}({args})"`.

**Approach B: Rely on CSharpTypeMap in EmitPrimary** — When `EmitPrimary` encounters an identifier that resolves to a backend-backed stdlib type, emit the mapped CLR name instead of the Mist name. So `Console` emits as `System.Console`, and the method call suffix naturally produces `System.Console.WriteLine(x)`.

**Chosen: Approach B** — This is simpler, requires no IntrinsicEmissionTable entries for these types, and follows the same pattern used for `Type` → `System.Type` (which already goes through `CSharpTypeMap.TryGetMappedName`). The emitter's `IdentifierExpressionPrimaryNode` case in `EmitPrimary` will check `CSharpTypeMap.TryGetMappedName(identName)` when the identifier resolves to a type symbol, and emit the mapped name if one exists.

However, looking more closely at the existing code, the `TryGetMappedName` check already exists in the type-reference emission path (line 5372 of `CSharpEmitter.cs`). For expression contexts (where `Console` appears as a receiver in `Console.WriteLine`), the identifier flows through `EmitPrimary` which currently just returns the lexeme. We need to add a mapped-name check there for identifiers that resolve to type symbols.

## Data Models

### Console.mist

```mist
namespace Standard;

intrinsic public class Console
{
    intrinsic public static Void WriteLine(String value);
    intrinsic public static Void Write(String value);
}
```

### Math.mist

```mist
namespace Standard;

intrinsic public class Math
{
    intrinsic public static Int32 Abs(Int32 value);
    intrinsic public static Int32 Max(Int32 val1, Int32 val2);
    intrinsic public static Int32 Min(Int32 val1, Int32 val2);
}
```

### Convert.mist

```mist
namespace Standard;

intrinsic public class Convert
{
    intrinsic public static Int32 ToInt32(String value);
}
```

### CSharpTypeMap Additions

```csharp
// New entries in BackingTypes dictionary:
["Standard.Console"] = "System.Console",
["Standard.Math"] = "System.Math",
["Standard.Convert"] = "System.Convert",
```

### Emitter Change: EmitPrimary Identifier Resolution

In the `IdentifierExpressionPrimaryNode` case of `EmitPrimary`, after the existing dependent-type-parameter checks:

```csharp
case IdentifierExpressionPrimaryNode ident:
{
    var identName = ident.Name.Lexeme;
    // ... existing dependent type parameter checks ...
    
    // NEW: Check if this identifier resolves to a stdlib type that has
    // a CSharpTypeMap mapping (e.g., Console → System.Console).
    var resolvedType = _model.GetTypeOf(ident);
    if (resolvedType is ClassType ct && ct.IsBackendBacked && ct.ContainingNamespace == "Standard")
    {
        var mapped = CSharpTypeMap.TryGetMappedName(identName);
        if (mapped is not null)
            return mapped;
    }
    if (resolvedType is StructType st && st.IsBackendBacked && st.ContainingNamespace == "Standard")
    {
        var mapped = CSharpTypeMap.TryGetMappedName(identName);
        if (mapped is not null)
            return mapped;
    }
    
    return identName;
}
```

This ensures that when `Console` appears as a receiver expression (e.g., `Console.WriteLine(...)`), it emits as `System.Console`, producing the correct C# output `System.Console.WriteLine(...)`.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### PBT Applicability Assessment

This feature is primarily a **migration/refactoring task** that moves type declarations from one mechanism (ambient resolver) to another (stdlib source files). The key characteristics:

- The stdlib declarations are **structural** — fixed files with fixed content (3 types, ~8 methods total)
- The `CSharpTypeMap` entries are a **finite, small set** (3 new entries)
- The emitter change is a **deterministic path** through a fixed set of type names
- The sample migration is a **one-time file update**

The input space that matters (which type × which method) is small and fully enumerable. Argument values passed to these methods do not affect the type-name emission logic — only the receiver type name matters. Running 100+ iterations with random arguments would not find more bugs than testing each type/method combination once.

**Conclusion: PBT is not applicable to this feature.** The testing strategy uses example-based unit tests and integration tests instead. This is the correct approach for a migration where correctness means "the same CLR output is produced through a different internal path."

## Error Handling

### Compilation Errors for Stale `using System;`

After migration, programs that still use `using System;` to access Console/Math/Convert will receive:
- **MIST0293** if no ambient types remain in the System namespace (since `HasNamespace("System")` returns `false`)
- **MIST_UNDEFINED_TYPE* (historical scheme)* if the type name cannot be resolved in any imported namespace

The fix is straightforward: change `using System;` to `using Standard;`.

### Stdlib Loading Failures

If `Console.mist`, `Math.mist`, or `Convert.mist` fail to parse during stdlib loading, `StdlibLoader` reports a `DiagnosticId.StdlibParse` error. This is existing behavior — no new error handling is needed.

### Backward Compatibility

There is no graceful fallback. Programs must update their using directives. This is an intentional breaking change documented in the requirements.

## Testing Strategy

### Unit Tests

**CSharpTypeMap tests** (example-based):
- Verify `TryGetMappedName("Console")` returns `"System.Console"`
- Verify `TryGetMappedName("Math")` returns `"System.Math"`
- Verify `TryGetMappedName("Convert")` returns `"System.Convert"`
- Verify `GetKeyword` works for ClassType instances with `ContainingNamespace = "Standard"` and `DisplayName = "Console"/"Math"/"Convert"`

**CSharpAmbientTypeResolver tests** (example-based):
- Verify `ResolveType("System", "Console")` returns `null`
- Verify `ResolveType("System", "Math")` returns `null`
- Verify `ResolveType("System", "Convert")` returns `null`
- Verify `HasNamespace("System")` returns `false`
- Verify the resolver still compiles and can be instantiated (future-use preservation)

**Semantic model resolution tests** (example-based):
- Compile a program with `using Standard;` referencing `Console.WriteLine("hello")` — verify no undefined-type diagnostics
- Compile a program with `using Standard;` referencing `Math.Abs(-1)` — verify no undefined-type diagnostics
- Compile a program with `using Standard;` referencing `Convert.ToInt32("42")` — verify no undefined-type diagnostics
- Compile a program with `using System;` referencing `Console.WriteLine("hello")` — verify undefined-type/namespace diagnostic is produced
- Verify resolved symbols for Console/Math/Convert have `SymbolOrigin.Source`

### Integration Tests

**Emitter output verification** (batched, 1 compile + 1 run):
- Compile a program that calls `Console.WriteLine`, `Math.Abs`, `Math.Max`, `Math.Min`, `Convert.ToInt32` with `using Standard;`
- Verify the emitted C# source contains `System.Console.WriteLine`, `System.Math.Abs`, `System.Math.Max`, `System.Math.Min`, `System.Convert.ToInt32`
- Run the compiled program and verify correct stdout output

**Sample compilation** (batched):
- Compile all updated sample programs and verify they produce exit code 0
- This validates the end-to-end migration without per-sample process spawning

### Test Process Spawning

Per the test-process-spawning policy:
- The integration test batches all verification into a single compile + single run
- No per-iteration process spawning
- Estimated: 1 compile (~2s) + 1 run (~0.5s) = ~2.5s total for the integration test class

