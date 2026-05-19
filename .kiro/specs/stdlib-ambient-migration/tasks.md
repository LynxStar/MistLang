# Implementation Plan: stdlib-ambient-migration

## Overview

Migrate Console, Math, and Convert from ambient types (synthesized by `CSharpAmbientTypeResolver`) to Standard Library intrinsic types declared in `.mist` source files. The implementation follows Approach B from the design: rely on `CSharpTypeMap` in `EmitPrimary` for identifier resolution — no `IntrinsicEmissionTable` entries needed since these methods map 1:1 to their CLR equivalents.

## Tasks

- [x] 1. Create stdlib declarations and add CSharpTypeMap entries
  - [x] 1.1 Create `Source/Standard/Bootstrap/Console.mist`
    - Declare `intrinsic public class Console` in the `Standard` namespace
    - Declare `intrinsic public static Void WriteLine(String value)` method
    - Declare `intrinsic public static Void Write(String value)` method
    - Follow the same pattern as `Array.mist` and other existing stdlib files
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.2 Create `Source/Standard/Bootstrap/Math.mist`
    - Declare `intrinsic public class Math` in the `Standard` namespace
    - Declare `intrinsic public static Int32 Abs(Int32 value)` method
    - Declare `intrinsic public static Int32 Max(Int32 val1, Int32 val2)` method
    - Declare `intrinsic public static Int32 Min(Int32 val1, Int32 val2)` method
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 1.3 Create `Source/Standard/Bootstrap/Convert.mist`
    - Declare `intrinsic public class Convert` in the `Standard` namespace
    - Declare `intrinsic public static Int32 ToInt32(String value)` method
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 1.4 Add CSharpTypeMap entries for Console, Math, and Convert
    - Add `["Standard.Console"] = "System.Console"` to `BackingTypes` dictionary
    - Add `["Standard.Math"] = "System.Math"` to `BackingTypes` dictionary
    - Add `["Standard.Convert"] = "System.Convert"` to `BackingTypes` dictionary
    - _Requirements: 4.1, 4.2, 4.3_

- [x] 2. Modify emitter to resolve stdlib type names via CSharpTypeMap in EmitPrimary
  - [x] 2.1 Extend `EmitPrimary` to check `CSharpTypeMap.TryGetMappedName` for type identifiers
    - In the `IdentifierExpressionPrimaryNode` case of `EmitPrimary`, after existing dependent-type-parameter checks, add logic to check if the identifier resolves to a stdlib type with a CSharpTypeMap mapping
    - When the identifier resolves to a `ClassType` or `StructType` with `IsBackendBacked` and `ContainingNamespace == "Standard"`, call `CSharpTypeMap.TryGetMappedName(identName)` and emit the mapped name if non-null
    - This ensures `Console` emits as `System.Console`, producing correct C# output like `System.Console.WriteLine(...)`
    - _Requirements: 4.4, 8.1, 8.2, 8.3_

- [x] 3. Remove ambient type registrations from CSharpAmbientTypeResolver
  - [x] 3.1 Remove Console, Math, and Convert registrations from `CSharpAmbientTypeResolver`
    - Remove the three `RegisterType` calls for Console, Math, and Convert in the constructor
    - Remove the `BuildConsole()`, `BuildMath()`, and `BuildConvert()` private methods
    - Keep the `IAmbientTypeResolver` interface and `CSharpAmbientTypeResolver` class intact for future use
    - `HasNamespace("System")` should now return `false` since no types remain registered
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 4. Checkpoint - Ensure compiler builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Migrate all samples from `using System;` to `using Standard;`
  - [x] 5.1 Delete the `samples/AmbientTypes/` directory
    - Remove the entire `samples/AmbientTypes/` directory (Program.mist, mist.toml, bin/, out/)
    - _Requirements: 6.2_

  - [x] 5.2 Update `samples/AllFeatures/Program.mist`
    - Change `using System;` to `using Standard;`
    - Remove the call to `AmbientTypes.Program.RunDemo()`
    - _Requirements: 6.3, 6.4_

  - [x] 5.3 Update `samples/Classes/Point.mist`
    - Change `using System;` to `using Standard;`
    - _Requirements: 6.5_

  - [x] 5.4 Update `samples/Primitives/StringUtils.mist`
    - Change `using System;` to `using Standard;`
    - Replace `Convert.ToString(doubled)` with string interpolation `$"{doubled}"`
    - _Requirements: 6.6_

  - [x] 5.5 Update all remaining sample files that use `using System;`
    - Change `using System;` to `using Standard;` in all other sample `.mist` files (Simpliest, Interfaces, Overloading, Inheritance, GenericTypes, GenericTraits, ShapeInterfaces, PatternMatching, CastOperators, Namespaces, Properties, GenericConstraints, DependentTypes, UnknownType, UnknownReadonlySemantics, Newtypes, EnumReadonlyRestriction, RegexLiterals, StructuredContext, DeepOwnershipTypes, RefParameters, NestedScoping, ArrayLiterals, ArrayBoundsFlowAnalysis, etc.)
    - _Requirements: 6.7_

- [x] 6. Checkpoint - Ensure all samples compile
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Remove obsolete tests and add new stdlib-based tests
  - [x] 7.1 Delete `AmbientTypePropertyTests` test class
    - Remove `Test/Mist.Tests/Semantics/AmbientTypePropertyTests.cs`
    - The ambient type registrations it validates (Console, Math, Convert) no longer exist
    - _Requirements: 7.1_

  - [x] 7.2 Delete `CSharpAmbientTypeResolverStdlibTests` test class
    - Remove `Test/Mist.Tests/Backend/CSharpAmbientTypeResolverStdlibTests.cs`
    - The registrations it validates no longer exist
    - _Requirements: 7.2_

  - [x] 7.3 Add CSharpTypeMap tests for new entries
    - Verify `TryGetMappedName("Console")` returns `"System.Console"`
    - Verify `TryGetMappedName("Math")` returns `"System.Math"`
    - Verify `TryGetMappedName("Convert")` returns `"System.Convert"`
    - Verify `GetKeyword` works for ClassType instances with `ContainingNamespace = "Standard"` and `DisplayName = "Console"/"Math"/"Convert"`
    - _Requirements: 7.5_

  - [x] 7.4 Add CSharpAmbientTypeResolver post-cleanup tests
    - Verify `ResolveType("System", "Console")` returns `null`
    - Verify `ResolveType("System", "Math")` returns `null`
    - Verify `ResolveType("System", "Convert")` returns `null`
    - Verify `HasNamespace("System")` returns `false`
    - Verify the resolver still compiles and can be instantiated (future-use preservation)
    - _Requirements: 7.4_

  - [x] 7.5 Add stdlib resolution tests for Console, Math, and Convert
    - Compile a program with `using Standard;` referencing `Console.WriteLine("hello")` — verify no undefined-type diagnostics
    - Compile a program with `using Standard;` referencing `Math.Abs(-1)` — verify no undefined-type diagnostics
    - Compile a program with `using Standard;` referencing `Convert.ToInt32("42")` — verify no undefined-type diagnostics
    - Compile a program with `using System;` referencing `Console.WriteLine("hello")` — verify undefined-type/namespace diagnostic is produced
    - Verify resolved symbols for Console/Math/Convert have `SymbolOrigin.Source`
    - _Requirements: 7.3, 7.4, 8.5_

  - [x] 7.6 Add integration test for emitter output verification (batched: 1 compile + 1 run)
    - Compile a single program that calls `Console.WriteLine`, `Math.Abs`, `Math.Max`, `Math.Min`, `Convert.ToInt32` with `using Standard;`
    - Verify the emitted C# source contains `System.Console.WriteLine`, `System.Math.Abs`, `System.Math.Max`, `System.Math.Min`, `System.Convert.ToInt32`
    - Run the compiled program and verify correct stdout output
    - Use batched approach per test-process-spawning policy: 1 compile + 1 run
    - _Requirements: 4.4, 8.1, 8.2, 8.3, 8.4_

- [x] 8. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- PBT is not applicable to this feature — the input space (which type × which method) is small and fully enumerable. Example-based tests are the correct approach for a migration where correctness means "the same CLR output is produced through a different internal path."
- The integration test (7.6) uses the batched approach per the test-process-spawning policy: 1 compile + 1 run, no per-iteration process spawning.
- The `IAmbientTypeResolver` interface and `CSharpAmbientTypeResolver` class remain in the codebase for future use — only the Console/Math/Convert registrations are removed.
- The design chose Approach B (CSharpTypeMap in EmitPrimary) over Approach A (IntrinsicEmissionTable entries) because Console/Math/Convert methods map 1:1 to CLR equivalents with identical names and signatures.
- All sample files using `using System;` must be updated to `using Standard;` — this is an intentional breaking change.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3"] },
    { "id": 1, "tasks": ["1.4"] },
    { "id": 2, "tasks": ["2.1", "3.1"] },
    { "id": 3, "tasks": ["5.1", "5.2", "5.3", "5.4", "5.5"] },
    { "id": 4, "tasks": ["7.1", "7.2", "7.3", "7.4"] },
    { "id": 5, "tasks": ["7.5", "7.6"] }
  ]
}
```
