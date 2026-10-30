# Implementation Plan: Method Overloading

## Overview

This plan implements C#-style method and constructor overloading for the Mist compiler. The implementation proceeds bottom-up: first extending the scope/symbol infrastructure, then adding the overload resolver, updating the semantic model builder for duplicate detection and interface satisfaction, updating the type checker for call-site resolution, and finally verifying the C# emitter. Each task builds incrementally on the previous ones.

## Tasks

- [x] 1. Extend IScope with LookupAll and implement in Scope
  - [x] 1.1 Add `LookupAll(string name)` method to `IScope` interface
    - Add `IReadOnlyList<ISymbol> LookupAll(string name)` to `Source/Mist/Semantics/Contracts/IScope.cs`
    - Keep existing `Lookup(string name)` unchanged for backward compatibility
    - _Requirements: 8.1, 8.2_

  - [x] 1.2 Implement `LookupAll` in `Scope` class
    - Add `LookupAll` method to `Source/Mist/Semantics/Model/Scope.cs`
    - Implement `CollectAll` private helper that traverses the parent chain collecting all symbols with the given name
    - Return results from current scope and all ancestor scopes
    - _Requirements: 8.1, 8.3_

  - [x] 1.3 Write property test for multi-symbol scope lookup (Property 3)
    - **Property 3: Multi-symbol scope lookup returns all matches**
    - For any scope containing N symbols with the same name, `LookupAll(name)` returns exactly N symbols
    - **Validates: Requirements 1.3, 8.1**

  - [x] 1.4 Write property test for scope chain traversal (Property 4)
    - **Property 4: Scope chain traversal collects from ancestors**
    - For any nested scope chain where symbols with the same name exist at multiple levels, `LookupAll(name)` on the innermost scope returns symbols from all levels
    - **Validates: Requirements 8.3**

  - [x] 1.5 Write property test for backward-compatible single lookup (Property 5)
    - **Property 5: Backward-compatible single lookup**
    - For any scope where a name maps to exactly one symbol, `Lookup(name)` returns that symbol unchanged
    - **Validates: Requirements 8.2**

- [x] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Create ParameterSignature utility class
  - [x] 3.1 Create `ParameterSignature` static class
    - Create new file `Source/Mist/Semantics/Model/ParameterSignature.cs`
    - Implement `Extract(ISymbol symbol, ISemanticModel model)` that extracts the ordered list of parameter types from a method or constructor symbol's declaring AST node (MethodDeclarationNode or ConstructorDeclarationNode)
    - Implement `AreEqual(IReadOnlyList<IType> a, IReadOnlyList<IType> b)` that compares two parameter signatures by count and type DisplayName
    - Handle parameterless methods/constructors (return empty list)
    - Handle interface method declarations (InterfaceMethodDeclarationNode)
    - _Requirements: 1.1, 1.2, 2.1, 2.2_

  - [x] 3.2 Write unit tests for ParameterSignature
    - Test extraction from parameterless methods
    - Test extraction from methods with multiple parameters
    - Test `AreEqual` with identical signatures, different signatures, and different-length signatures
    - _Requirements: 1.1, 1.2_

- [x] 4. Create OverloadResolver static class
  - [x] 4.1 Create `OverloadResolver` with resolution result types
    - Create new file `Source/Mist/Analysis/Builtin/OverloadResolver.cs`
    - Define `OverloadResolutionStatus` enum: `Success`, `NoApplicableCandidate`, `AmbiguousCall`
    - Define `OverloadResolutionResult` readonly record struct with `Status`, `BestCandidate`, and `AmbiguousCandidates`
    - _Requirements: 3.1, 3.2, 3.3, 4.1_

  - [x] 4.2 Implement `Resolve` method in `OverloadResolver`
    - Implement the resolution algorithm: filter applicable candidates (parameter count match + type assignability), select exact match or most-specific candidate, detect ambiguity
    - Use `ParameterSignature.Extract` to get parameter types for each candidate
    - Use the existing `IsAssignable` logic from TypeCheckerAnalyzer for type compatibility checks
    - Implement `MoreSpecific(A, B)` comparison: A is more specific than B if all A param types are assignable to B param types, and at least one B param type is not assignable to A
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 4.3 Write property test for single applicable overload resolution (Property 6)
    - **Property 6: Single applicable overload resolves without error**
    - For any overload set and call site where exactly one candidate is applicable, the resolver selects that candidate
    - **Validates: Requirements 3.1, 3.2, 2.3**

  - [x] 4.4 Write property test for most-specific candidate wins (Property 7)
    - **Property 7: Most-specific candidate wins**
    - For any overload set where multiple candidates are applicable but one has strictly more specific parameter types, the resolver selects the most-specific candidate
    - **Validates: Requirements 3.3**

  - [x] 4.5 Write property test for no applicable candidate (Property 8)
    - **Property 8: No applicable candidate produces diagnostic**
    - For any overload set and call site where no candidate matches, the resolver returns `NoApplicableCandidate`
    - **Validates: Requirements 3.4, 3.5**

  - [x] 4.6 Write property test for ambiguous call (Property 9)
    - **Property 9: Ambiguous call produces diagnostic**
    - For any overload set and call site where two or more candidates are equally applicable, the resolver returns `AmbiguousCall`
    - **Validates: Requirements 4.1, 4.3**

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Add new diagnostic IDs
  - [x] 6.1 Add overloading diagnostic IDs to the codebase
    - Add `MIST_SM_0020 (historical)` (duplicate method signature) to SemanticModelBuilder usage
    - Add `MIST_SM_0021 (historical)` (duplicate constructor signature) to SemanticModelBuilder usage
    - Add `MIST_SM_0022 (historical)` (missing interface overload) to SemanticModelBuilder usage
    - Add `MIST_TC_0031 (historical)` (no applicable overload) to TypeCheckerAnalyzer usage
    - Add `MIST_TC_0032 (historical)` (ambiguous overload) to TypeCheckerAnalyzer usage
    - Note: `DiagnosticId` is a simple record struct wrapping a string, so no enum changes needed — just use the new string IDs in diagnostic creation
    - _Requirements: 1.2, 2.2, 3.4, 4.1, 6.3_

- [x] 7. Update SemanticModelBuilder for overload support
  - [x] 7.1 Add duplicate signature detection pass
    - Add `Pass1_CheckDuplicateSignatures` method to `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Run after Pass 2 (type resolution) so parameter types are resolved
    - Group methods by name within each type scope; for groups with count > 1, check all pairs for identical parameter signatures
    - Emit `MIST_SM_0020 (historical)` for duplicate method signatures
    - Group constructors within each type scope; check all pairs for identical parameter signatures
    - Emit `MIST_SM_0021 (historical)` for duplicate constructor signatures
    - Wire the new pass into the `Build` method after Pass 2
    - _Requirements: 1.2, 1.4, 2.2_

  - [x] 7.2 Write property test for overload registration (Property 1)
    - **Property 1: Overload registration preserves all distinct signatures**
    - For any type with N method/constructor declarations sharing a name but with pairwise-distinct parameter signatures, the semantic model registers exactly N symbols
    - **Validates: Requirements 1.1, 2.1, 6.1**

  - [x] 7.3 Write property test for duplicate signature detection (Property 2)
    - **Property 2: Duplicate signature detection**
    - For any type containing two methods (or constructors) with the same name and identical parameter signatures, the builder emits a duplicate-signature diagnostic
    - **Validates: Requirements 1.2, 1.4, 2.2**

  - [x] 7.4 Update `Pass1_CheckMemberRedeclarations` for overloading
    - Modify `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` `Pass1_CheckMemberRedeclarations`
    - For methods: relax the check so that same name with a different parameter signature is a valid overload (not a redeclaration error)
    - Only emit `MIST_MEMBER_REDECLARATION (historical)` when a derived method has the same name AND identical parameter signature as a base method
    - Fields continue to use name-only matching (unchanged)
    - _Requirements: 5.1, 5.2_

  - [x] 7.5 Write property test for cross-hierarchy overloading (Property 10)
    - **Property 10: Cross-hierarchy overloading is not a redeclaration error**
    - For any class hierarchy where a derived class declares a method with the same name but different parameter signature as a base class method, no redeclaration diagnostic is emitted
    - **Validates: Requirements 5.1**

  - [x] 7.6 Write property test for cross-hierarchy same-signature redeclaration (Property 11)
    - **Property 11: Cross-hierarchy same-signature is a redeclaration error**
    - For any class hierarchy where a derived class declares a method with the same name and identical parameter signature as a base class method, a redeclaration diagnostic is emitted
    - **Validates: Requirements 5.2**

  - [x] 7.7 Update `Pass1_ValidateInterfaceSatisfaction` for overloaded interfaces
    - Modify `ValidateInterfaceSatisfaction` in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Update interface method matching to match by name AND parameter signature (not just name)
    - For each interface method, find all methods in the implementing type with the same name, then check if any has a matching parameter signature
    - Emit `MIST_SM_0022 (historical)` with the specific signature when no match is found
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 7.8 Write property test for interface overload satisfaction (Property 13)
    - **Property 13: Interface overload satisfaction**
    - For any interface with overloaded methods and an implementing class, the builder emits a diagnostic for each unmatched signature and no diagnostic when all signatures are matched
    - **Validates: Requirements 6.2, 6.3**

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Update TypeChecker for overload resolution at call sites
  - [x] 9.1 Update method call resolution in TypeCheckerAnalyzer
    - Modify `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Update `CheckMethodCallArguments` and `CheckInvocationArguments` to:
      1. Collect the full overload set via `LookupAll` on the target type's member scope, plus inherited methods from base types
      2. Collect argument types from the call site
      3. Call `OverloadResolver.Resolve(candidates, argTypes, model)`
      4. On `NoApplicableCandidate`: emit `MIST_TC_0031 (historical)` with method name and argument types
      5. On `AmbiguousCall`: emit `MIST_TC_0032 (historical)` with conflicting candidate signatures
      6. On `Success`: proceed with the resolved symbol for further type checking
    - Preserve existing single-method diagnostics (`MIST_TC_0002 (historical)`, `MIST_TC_0011 (historical)`) when only one candidate exists
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.2_

  - [x] 9.2 Update constructor call resolution in TypeCheckerAnalyzer
    - Update `CheckObjectCreation` to use `OverloadResolver.Resolve` for `new T(args)` expressions
    - Collect all constructors from the target type's member scope
    - Apply overload resolution and emit `MIST_TC_0031 (historical)` or `MIST_TC_0032 (historical)` as appropriate
    - _Requirements: 2.3, 4.3_

  - [x] 9.3 Update constructor chain resolution in TypeCheckerAnalyzer
    - Update `CheckConstructorChain` to use `OverloadResolver.Resolve` for `: this(args)` and `: base(args)` chains
    - For `: this(args)`, resolve against the same type's constructor overload set (excluding the current constructor)
    - For `: base(args)`, resolve against the base type's constructor overload set
    - _Requirements: 2.4, 2.5_

  - [x] 9.4 Write property test for inherited methods in overload resolution (Property 12)
    - **Property 12: Inherited methods included in overload resolution**
    - For any method call on a derived type where the matching overload is declared in a base class, the resolver finds and selects that inherited candidate
    - **Validates: Requirements 3.6, 5.3**

  - [x] 9.5 Write unit tests for TypeChecker overload diagnostics
    - Test `MIST_TC_0031 (historical)` diagnostic message content for no-match scenarios
    - Test `MIST_TC_0032 (historical)` diagnostic message content includes conflicting candidate signatures
    - Test ambiguous constructor call diagnostic (Requirement 4.3)
    - Test backward compatibility: single-method programs produce no new diagnostics
    - _Requirements: 3.4, 3.5, 4.1, 4.2, 4.3_

- [x] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Verify C# Emitter and add emission tests
  - [x] 11.1 Verify CSharpEmitter handles overloaded methods and constructors
    - Review `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` to confirm it emits all method/constructor declarations from the AST without deduplication
    - The emitter walks `classDecl.Members` and emits each method/constructor individually via `EmitMemberDeclaration`, so it should naturally emit all overloads
    - Add a guard comment or assertion if needed to ensure no same-name deduplication occurs
    - _Requirements: 7.1, 7.2_

  - [x] 11.2 Write property test for overload emission completeness (Property 14)
    - **Property 14: Overload emission completeness**
    - For any type with N overloaded methods (or constructors) sharing a name, the C# emitter produces exactly N separate C# method (or constructor) declarations
    - **Validates: Requirements 7.1, 7.2**

  - [x] 11.3 Write property test for round-trip compilation (Property 15)
    - **Property 15: Round-trip compilation of overloaded programs**
    - For any valid Mist program containing overloaded methods and/or constructors, the full pipeline (parse → semantic model → type check → emit → Roslyn compile) produces a successful C# compilation
    - **Validates: Requirements 7.3**

- [x] 12. Add FsCheck generators for overloading tests
  - [x] 12.1 Create overloading-specific FsCheck generators
    - Add generators to `Test/Mist.Tests/Generators/` (new file `MistOverloadGenerator.cs`)
    - `GenOverloadedMethods(name, n)` — generates N method declarations sharing a name with distinct parameter signatures
    - `GenOverloadedConstructors(n)` — generates N constructor declarations with distinct parameter signatures
    - `GenDuplicateSignatureMethods(name)` — generates two methods with the same name and same parameter signature
    - `GenOverloadCallSite(overloadSet, matchIndex)` — generates a call site whose arguments match the overload at `matchIndex`
    - `GenAmbiguousCallSite(overloadSet)` — generates a call site where multiple overloads are equally applicable
    - `GenClassHierarchyWithOverloads()` — generates base/derived class pairs with overloaded methods
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1_

- [x] 13. Add integration tests for end-to-end overloading
  - [x] 13.1 Write integration tests for overloaded method programs
    - Add tests to `Test/Mist.Tests/Integration/` (new file `OverloadIntegrationTests.cs`)
    - Test: compile a Mist program with overloaded methods through the full pipeline, verify successful Roslyn compilation
    - Test: compile a Mist program with overloaded constructors, verify successful Roslyn compilation
    - Test: compile a Mist program with overloaded interface methods and implementing class, verify successful Roslyn compilation
    - Test: compile a Mist program with cross-hierarchy overloads (base + derived), verify successful Roslyn compilation
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document using FsCheck 3.1.0 with xUnit
- Unit tests validate specific examples and edge cases
- The design uses C# throughout, so all implementation is in C#
- The existing `Scope.AddMember` already supports multiple same-named symbols (it appends to a list), so no storage changes are needed — only the query method `LookupAll` is new
- The emitter already walks AST members individually, so overloaded methods are naturally emitted without structural changes
