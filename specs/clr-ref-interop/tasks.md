# Implementation Plan: CLR Ref Interop

## Overview

This plan implements support for CLR `ref` parameters, `in` parameters (projected as `ref`), and mutable `ref` returns in the Mist compiler's Assembly Type Provider. The work is surgical: modify `VisibilityFilter` to stop excluding these members, update `TypeProjector` to project them with correct `RefKind` and calling convention metadata, extend the `Symbol` model with `ClrCallingConvention`, and update the `CSharpEmitter` to emit `in` vs `ref` at call sites. A test fixture class, property-based tests, and a sample application verify correctness end-to-end.

## Tasks

- [x] 1. Add ClrCallingConvention enum and extend Symbol model
  - [x] 1.1 Create `ClrCallingConvention` enum
    - Create new file `Source/Mist/Semantics/Model/ClrCallingConvention.cs`
    - Define enum with values `Ref` and `In`
    - Add XML documentation explaining purpose (emitter uses it to choose `ref` vs `in` at C# call site)
    - _Requirements: 2.4, 3.4_

  - [x] 1.2 Add `ClrCallingConvention?` property to `Symbol` class
    - Add nullable `ClrCallingConvention` property to `Symbol` in `Source/Mist/Semantics/Model/SymbolTable.cs`
    - Default to `null` for Mist-native parameters
    - Add XML documentation explaining it is only meaningful for backend-projected ref parameters
    - _Requirements: 2.4, 3.4_

- [x] 2. Update VisibilityFilter to allow ref params and mutable ref returns
  - [x] 2.1 Modify `HasRefParameters` to allow ref parameters through
    - In `Source/Mist/Backends/CSharpTranspiler/VisibilityFilter.cs`, change `HasRefParameters` to return `false` unconditionally
    - CLR `ref` parameters are now supported; only `out` params remain excluded
    - Update the XML doc comment on the method and the class-level remarks to reflect the new behavior
    - _Requirements: 1.1, 1.2_

  - [x] 2.2 Modify `HasRefReturn` to only exclude `ref readonly` returns
    - Change `HasRefReturn` to check for `IsReadOnlyAttribute` on the return parameter
    - Mutable `ref` returns (`IsByRef == true` without `IsReadOnlyAttribute`) now pass through
    - `ref readonly` returns (with `IsReadOnlyAttribute`) remain excluded
    - Add private helper method `IsRefReadonlyReturn(MethodInfo)` that checks `ReturnParameter.GetCustomAttributesData()` for `System.Runtime.CompilerServices.IsReadOnlyAttribute`
    - Handle property getters similarly
    - _Requirements: 4.1, 4.2, 4.3, 6.3_

  - [x] 2.3 Write property tests for VisibilityFilter ref/in/out behavior
    - **Property 1: Ref and in parameters pass through the visibility filter**
    - **Property 2: Out parameters remain filtered**
    - **Property 5: Mutable ref returns pass through the visibility filter**
    - **Property 6: Ref readonly returns remain filtered**
    - **Validates: Requirements 1.1, 1.2, 1.3, 3.1, 4.1, 4.2, 4.3, 6.1, 6.2, 6.3**
    - Create test class `ClrRefVisibilityPropertyTests` in `Test/Mist.Tests/Backend/`
    - Use the test fixture assembly's types as input (real CLR metadata)
    - Instantiate `VisibilityFilter` and assert properties hold for all fixture methods

- [x] 3. Add test fixture class for ref interop scenarios
  - [x] 3.1 Create `RefInteropFixtures.cs` in `Test/Mist.Tests.InteropFixtures/`
    - Add class with public methods that have `ref` parameters (various types: int, string, generic)
    - Add class with public methods that have `in` parameters
    - Add class with public methods that have mutable `ref` return types
    - Add class with public methods that have `ref readonly` return types
    - Add class with public methods that have `out` parameters (for negative testing)
    - Add class with methods combining `ref` and `out` parameters
    - Add methods with `ref` parameters whose element type is a CLR intrinsic (e.g., `ref int`, `ref string`)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

- [x] 4. Update TypeProjector to project ref/in parameters and ref returns
  - [x] 4.1 Project CLR `ref` parameters as Mist `ref` parameters
    - In `TypeProjector.ProjectParametersWithResult` (or equivalent parameter projection method), detect `IsByRef && !IsOut` parameters
    - Unwrap the by-ref type via `GetElementType()` before projecting
    - Set `RefKind = RefKind.Ref` on the parameter symbol
    - For `in` parameters (`IsIn == true`), also set `ClrCallingConvention = ClrCallingConvention.In`
    - For regular `ref` parameters, set `ClrCallingConvention = ClrCallingConvention.Ref`
    - Apply intrinsic type substitution to the unwrapped element type
    - _Requirements: 2.1, 2.2, 3.2, 3.3_

  - [x] 4.2 Project CLR mutable `ref` returns as Mist `ref` returns
    - In the method projection logic, detect `ReturnType.IsByRef` on method symbols
    - Unwrap the by-ref return type via `GetElementType()` before projecting
    - Set `ReturnRefKind = RefKind.Ref` on the method symbol
    - Apply intrinsic type substitution to the unwrapped element type
    - _Requirements: 5.1, 5.2_

  - [x] 4.3 Write property tests for TypeProjector ref projection
    - **Property 3: Ref and in parameters projected as RefKind.Ref**
    - **Property 4: Intrinsic type substitution through ref unwrapping**
    - **Property 7: Ref returns projected as ReturnRefKind.Ref**
    - **Validates: Requirements 2.1, 2.2, 3.2, 3.3, 5.1, 5.2**
    - Create test class `ClrRefProjectionPropertyTests` in `Test/Mist.Tests/Backend/`
    - Use the test fixture assembly's types as input
    - Instantiate `TypeProjector` with standard configuration and assert properties hold

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Update CSharpEmitter to emit `in` for CLR `in` parameters
  - [x] 6.1 Modify ref argument emission to check `ClrCallingConvention`
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, locate the ref argument emission path (where `RefArgumentArgumentNode` is handled)
    - When emitting a `ref` argument for a call to a backend method, resolve the corresponding parameter symbol
    - If the parameter has `ClrCallingConvention == ClrCallingConvention.In`, emit `in {expr}` instead of `ref {expr}`
    - If `ClrCallingConvention == ClrCallingConvention.Ref` or `null`, emit `ref {expr}` (existing behavior)
    - _Requirements: 2.4, 3.4_

  - [x] 6.2 Write unit tests for emitter ref/in keyword emission
    - Test that emitted C# uses `in` keyword for CLR `in` parameters
    - Test that emitted C# uses `ref` keyword for CLR `ref` parameters
    - Test that emitted C# uses `ref` on call expressions for `ref` return methods
    - _Requirements: 2.4, 3.4, 5.4_

- [x] 7. Create sample application
  - [x] 7.1 Create `samples/ClrRefInterop/Program.mist` with proof code
    - Create the sample file with the exact proof code from Requirement 7
    - Demonstrate calling CLR methods with `ref` parameters (e.g., `Interlocked.Exchange`)
    - Demonstrate calling CLR methods with `in` parameters (projected as `ref`)
    - Demonstrate calling CLR methods with `ref` returns and binding to `ref` locals
    - Demonstrate that CLR methods with `out` parameters remain invisible
    - Each pattern prints labeled output to stdout
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.9_

  - [x] 7.2 Create `samples/ClrRefInterop/mist.toml` project file
    - Add standard project configuration for the sample
    - _Requirements: 7.1_

  - [x] 7.3 Integrate into AllFeatures orchestrator
    - Add `ClrRefInterop.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - _Requirements: 7.8_

- [x] 8. Update book documentation
  - [x] 8.1 Update `book/src/advanced/dotnet-interop.md`
    - Update the "What Gets Hidden" table to remove `ref` parameters from the filtered list
    - Add `ref readonly` returns to the filtered list with explanation
    - Add section explaining CLR `ref` parameters map to Mist `ref` parameters (require `ref` at call site)
    - Explain CLR `in` parameters are projected as Mist `ref` parameters
    - Explain CLR `ref` returns are supported and can be bound to `ref` locals
    - Explain CLR `ref readonly` returns remain filtered (emitted C# would require `ref readonly` binding)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_

  - [x] 8.2 Create book snippet for ref interop
    - Create `book/snippets/advanced/dotnet-interop/ref-interop.mist` demonstrating ref parameter usage
    - Create empty `book/snippets/advanced/dotnet-interop/ref-interop.cs` for SnippetEmitter
    - Run SnippetEmitter tool to generate the emitted C# output
    - Use `{{#include}}` in the book chapter to reference the snippet
    - _Requirements: 9.2_

- [x] 9. Final checkpoint - Ensure all tests pass and sample compiles
  - Ensure all tests pass, ask the user if questions arise.
  - Verify the sample application compiles and produces expected output.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- The design uses C# throughout — all implementation is in C#
- The test fixture assembly (`Mist.Tests.InteropFixtures`) already exists and contains related fixtures (`HasRefParam`, `HasInParam`, `HasOutParam`, `HasRefReturn`); the new fixture class extends this with additional scenarios needed for comprehensive property testing
- The `VisibilityFilter` and `TypeProjector` are tested in-process (no process spawning) per the test process spawning policy
- The sample application serves as the primary integration test (1 compile + 1 run)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "3.1"] },
    { "id": 1, "tasks": ["1.2", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3"] },
    { "id": 3, "tasks": ["4.1", "4.2"] },
    { "id": 4, "tasks": ["4.3", "6.1"] },
    { "id": 5, "tasks": ["6.2", "7.1", "7.2"] },
    { "id": 6, "tasks": ["7.3", "8.1"] },
    { "id": 7, "tasks": ["8.2"] }
  ]
}
```
