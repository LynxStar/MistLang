# Implementation Plan: Type Argument Inference

## Overview

This plan implements type argument inference for generic method calls in the Mist compiler. The approach is incremental: first build the core inference engine in isolation, then integrate it into the type checker, extend overload resolution, update the emitter, and finally add documentation and the sample application. Each step builds on the previous, ensuring no orphaned code.

## Tasks

- [x] 1. Implement core inference engine
  - [x] 1.1 Create `InferenceResult` record and `InferenceContext` class
    - Create `Source/Mist/Analysis/Builtin/InferenceResult.cs` with the `InferenceResult` readonly record struct
    - Create `Source/Mist/Analysis/Builtin/InferenceContext.cs` with the mutable context class containing `TryBind`, `GetBinding`, `HasConflicts`, `Conflicts`, and `GetUnboundParameters`
    - `InferenceContext` constructor accepts `IReadOnlyList<IType>` type parameters and initializes bindings dictionary
    - `TryBind` returns true on new binding or consistent re-binding, false on conflict (recording both types)
    - _Requirements: 1.1, 1.2, 3.2, 3.3_

  - [x] 1.2 Create `TypeArgumentInferenceEngine` static class with `TryInfer` and `Unify`
    - Create `Source/Mist/Analysis/Builtin/TypeArgumentInferenceEngine.cs`
    - Implement `TryInfer` that initializes context, iterates parameter-argument pairs positionally, calls `Unify`, then checks context for completeness
    - Implement private `Unify(IType declaredParamType, IType argumentType, InferenceContext context)` with three cases: TypeParameterType binding, ConstructedType recursive descent, and skip-silently fallback
    - Skip arguments whose resolved type is `ErrorType` or null/`UnknownType`
    - Return `InferenceResult` with success/failure details
    - _Requirements: 1.1, 1.2, 1.4, 2.1, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4_

  - [x] 1.3 Write property test: Direct type parameter inference (Property 1)
    - **Property 1: Direct type parameter inference**
    - **Validates: Requirements 1.2, 1.6, 3.1**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceDirectPropertyTests.cs`
    - Generate random method signatures with TypeParameterType parameters and random concrete argument types
    - Assert that inference maps each type parameter to the concrete type at its corresponding position

  - [x] 1.4 Write property test: Constructed type recursive unification (Property 2)
    - **Property 2: Constructed type recursive unification**
    - **Validates: Requirements 2.1, 2.3, 2.4, 2.5**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceConstructedTypePropertyTests.cs`
    - Generate random ConstructedType parameters with type parameters at various nesting depths
    - Assert that inference recursively descends and binds each type parameter correctly

  - [x] 1.5 Write property test: Consistent multi-position bindings (Property 3)
    - **Property 3: Consistent multi-position bindings**
    - **Validates: Requirements 3.2, 3.4**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceConsistentBindingPropertyTests.cs`
    - Generate signatures where the same type parameter appears in multiple positions with the same concrete type
    - Assert that inference succeeds and records the single concrete type

  - [x] 1.6 Write property test: Conflicting bindings produce failure (Property 4)
    - **Property 4: Conflicting bindings produce diagnostic**
    - **Validates: Requirements 3.3, 4.2, 4.3**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceConflictPropertyTests.cs`
    - Generate signatures where the same type parameter appears in multiple positions with different concrete types
    - Assert that inference returns `Success = false` with the conflicting types recorded

  - [x] 1.7 Write property test: Uninferrable type parameters produce failure (Property 5)
    - **Property 5: Uninferrable type parameters produce diagnostic**
    - **Validates: Requirements 1.4, 4.1, 4.3**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceUninferrablePropertyTests.cs`
    - Generate signatures where at least one type parameter does not appear in any parameter position
    - Assert that inference returns `Success = false` with the uninferrable type parameter names listed

- [x] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Integrate inference engine into the semantic model
  - [x] 3.1 Extend `ISemanticModel` and `SemanticModel` with inferred type argument storage
    - Add `RecordInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode, IReadOnlyList<IType> inferredTypeArgs)` to `ISemanticModel`
    - Add `GetInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode)` returning `IReadOnlyList<IType>?` to `ISemanticModel`
    - Implement both methods in `SemanticModel` using a `Dictionary<Storm.Runtime.SyntaxNode, IReadOnlyList<IType>>` field
    - _Requirements: 1.3, 7.1, 8.1_

- [x] 4. Integrate inference engine into the type checker
  - [x] 4.1 Add inference invocation in `TypeCheckerAnalyzer.CheckMethodCallArguments`
    - When `methodCall.TypeArgs is null` and the resolved method symbol has type parameters, call `TypeArgumentInferenceEngine.TryInfer`
    - On success: set `methodTypeParams` and `methodTypeArgs` from the inference result, call `model.RecordInferredTypeArguments`, and proceed to constraint checking
    - On failure: call a new `EmitInferenceFailureDiagnostic` helper
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 4.1, 4.2, 4.3_

  - [x] 4.2 Implement `EmitInferenceFailureDiagnostic` helper method
    - Add diagnostic IDs `MIST_TC_0080` (uninferrable) and `MIST_TC_0081` (conflict)
    - Emit exactly one diagnostic per call site with the appropriate message format
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 4.3 Add constraint satisfaction checking for inferred type arguments
    - After inference succeeds, iterate type parameters and call `CheckConstraintSatisfaction` for each inferred type argument (same path as explicit type args)
    - Reuse existing constraint violation diagnostic IDs (`MIST_TC_0040` through `MIST_TC_0047`)
    - _Requirements: 4.4, 6.1, 6.2, 6.3, 6.4_

  - [x] 4.4 Extend `SubstituteMethodReturnType` to handle inferred type arguments
    - When `methodCall.TypeArgs is null`, query `model.GetInferredTypeArguments(methodCall)` and substitute if available
    - Preserve existing behavior for explicit type arguments
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [x] 4.5 Write property test: Explicit type arguments bypass inference (Property 6)
    - **Property 6: Explicit type arguments bypass inference**
    - **Validates: Requirements 1.5, 4.5, 7.5**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceBypassPropertyTests.cs`
    - Generate generic method calls with explicit type arguments provided
    - Assert that the compiler uses explicit type arguments directly and does not attempt inference

  - [x] 4.6 Write property test: Return type substitution after inference (Property 8)
    - **Property 8: Return type substitution after inference**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceReturnTypePropertyTests.cs`
    - Generate generic methods with return types mentioning type parameters, invoke with inferred args
    - Assert that the resolved return type has all type parameters substituted with inferred concrete types

  - [x] 4.7 Write property test: Constraint satisfaction uses same diagnostics as explicit (Property 9)
    - **Property 9: Constraint satisfaction uses same diagnostics as explicit**
    - **Validates: Requirements 4.4, 6.1, 6.2, 6.3, 6.4**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceConstraintPropertyTests.cs`
    - Generate constrained type parameters with inferred types that violate constraints
    - Assert that the same diagnostic IDs are emitted as when explicit type arguments violate the same constraints

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Integrate inference with overload resolution
  - [x] 6.1 Extend `OverloadResolver.Resolve` to filter candidates by inference success
    - When type arguments are omitted and multiple overload candidates exist, attempt inference for each candidate
    - Exclude candidates where inference fails from the applicable set
    - Record inferred types for candidates where inference succeeds
    - Proceed with normal specificity ranking on remaining candidates
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [x] 6.2 Write property test: Overload resolution filters by inference success (Property 10)
    - **Property 10: Overload resolution filters by inference success**
    - **Validates: Requirements 5.1, 5.2, 5.5**
    - Create `Test/Mist.Tests/Analysis/TypeArgumentInferenceOverloadPropertyTests.cs`
    - Generate overload sets where some candidates have inferrable type parameters and others don't
    - Assert that candidates with failed inference are excluded and selection proceeds on remaining candidates

- [x] 7. Update the C# emitter for inferred type arguments
  - [x] 7.1 Implement `EmitInferredTypeArguments` helper in `CSharpEmitter`
    - Query `model.GetInferredTypeArguments(methodCall)` for the call-site node
    - Format inferred types as a C# type argument list string (e.g., `<int, string>`)
    - Use C# mapped type names for primitives (e.g., `int` not `Int32`)
    - Handle constructed generic types recursively
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 7.2 Modify `EmitPostfixSuffix` to use inferred type arguments when AST has none
    - In the `MethodCallSuffixPostfixSuffixNode` case, check for inferred type arguments when `methodCall.TypeArgs is null`
    - Call `EmitInferredTypeArguments` to produce the type argument string
    - Preserve existing behavior when explicit type arguments are present in the AST
    - _Requirements: 7.1, 7.5_

  - [x] 7.3 Write property test: Inferred calls emit explicit type arguments in C# (Property 7)
    - **Property 7: Inferred calls emit explicit type arguments in C#**
    - **Validates: Requirements 1.3, 7.1, 7.2, 7.4**
    - Create `Test/Mist.Tests/Backend/TypeArgumentInferenceEmissionPropertyTests.cs`
    - Use batched approach: generate N scenarios, build one Mist program, compile once in-process via `CompilerDriver`, compare emitted C# output
    - Assert that emitted C# contains explicit type arguments matching the inferred types in declaration order

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Update book documentation
  - [x] 9.1 Update generics chapter and create inference snippet
    - Update `book/src/type-system/generics.md`: remove "Mist does not yet support type argument inference" statement, add "Type Argument Inference" section
    - Create `book/snippets/type-system/generics/inference.mist` demonstrating: generic method called without type arguments, same method with explicit type arguments, inference from constructed type argument
    - Create `book/snippets/type-system/generics/inference.cs` (generated by running `dotnet run --project tools/SnippetEmitter` after the `.mist` file compiles)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [x] 10. Create sample application
  - [x] 10.1 Create the TypeArgumentInference sample project
    - Create `samples/TypeArgumentInference/mist.toml` project file
    - Create `samples/TypeArgumentInference/Program.mist` containing the Proof Code verbatim from Requirement 10
    - Include all demonstrated permutations: single type parameter inference, multiple type parameter inference, inference from constructed types, instance method inference, explicit type arguments still working
    - Expose `public static void RunDemo()` entry point
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 10.2 Integrate sample into AllFeatures orchestrator
    - Add `TypeArgumentInference.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - _Requirements: 10.4_

- [x] 11. Final checkpoint - Ensure all tests pass and sample produces expected output
  - Ensure all tests pass, ask the user if questions arise.
  - Verify the sample application compiles and produces the expected output exactly matching the Proof Code specification.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The inference engine (tasks 1.1–1.2) is a pure static class testable in isolation without process spawning
- Property tests for emission (task 7.3) use the batched approach: 1 compile, 1 process spawn, N output lines compared
- All other property tests (tasks 1.3–1.7, 4.5–4.7, 6.2) operate in-process on the engine/type checker directly

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["1.3", "1.4", "1.5", "1.6", "1.7"] },
    { "id": 3, "tasks": ["3.1"] },
    { "id": 4, "tasks": ["4.1", "4.2"] },
    { "id": 5, "tasks": ["4.3", "4.4"] },
    { "id": 6, "tasks": ["4.5", "4.6", "4.7", "6.1"] },
    { "id": 7, "tasks": ["6.2", "7.1"] },
    { "id": 8, "tasks": ["7.2"] },
    { "id": 9, "tasks": ["7.3"] },
    { "id": 10, "tasks": ["9.1"] },
    { "id": 11, "tasks": ["10.1"] },
    { "id": 12, "tasks": ["10.2"] }
  ]
}
```
