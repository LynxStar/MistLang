# Implementation Plan: Dependent Type Refinement Constraints

## Overview

This plan implements a 6-step workaround removal strategy for the Mist compiler, extending the grammar to accept refinement type identifiers as dependent type parameter constraints, adding parser completeness checking, resolving identifier constraints semantically, and progressively removing synthetic workarounds. Each step leaves tests passing. The implementation uses C# for compiler changes and Mist for stdlib/sample files.

## Tasks

- [x] 1. Extend TypeParameter grammar rule
  - [x] 1.1 Add `NamedConstraintDependentTypeParameter` alternative to `Mist.storm`
    - Add the new alternative `| name:IDENTIFIER COLON constraintName:IDENTIFIER as NamedConstraintDependentTypeParameter` between the existing `DependentTypeParameter` and `RegularTypeParameter` alternatives in the `TypeParameter` rule
    - Verify the Storm source generator produces `NamedConstraintDependentTypeParameterTypeParameterNode` with `Name` and `ConstraintName` token fields
    - _Requirements: 1.1, 1.4_

  - [x] 1.2 Update `TypeParameterNodeExtensions.cs` for the new node type
    - Add a case for `NamedConstraintDependentTypeParameterTypeParameterNode` in the `GetName()` extension method
    - _Requirements: 1.1_

  - [x] 1.3 Update `MistPrettyPrinter.cs` for the new node type
    - Add pretty-print handling: `$"{named.Name.Lexeme}:{named.ConstraintName.Lexeme}"`
    - _Requirements: 9.1_

- [x] 2. Parser completeness check
  - [x] 2.1 Expose `LastPosition` from the Storm-generated parser
    - Modify `ParserEmitter.cs` in the Storm source generator to emit `public static int LastPosition => _pos;` on the generated parser class
    - _Requirements: 3.1_

  - [x] 2.2 Implement completeness check in `MistParser.cs`
    - After `ParseCompilationUnit` returns a non-null root, compare `Mist.Generated.MistParser.LastPosition` to `defaultTokens.Length`
    - If `lastPos < defaultTokens.Length`, emit `MIST0123` diagnostic (severity Error, stage Parsing) with the line/column of the first unconsumed token
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 2.3 Write property test for parser completeness (Property 5)
    - **Property 5: Parser Completeness Invariant**
    - Generate source strings that are valid prefixes with trailing unparseable content, assert `MIST0123` is emitted with correct position
    - **Validates: Requirements 3.1, 3.3, 3.4**

  - [x] 2.4 Write property test for completeness silence (Property 6)
    - **Property 6: Completeness Silence on Full Consumption**
    - Generate fully valid source files, assert no `MIST0123` diagnostic is emitted
    - **Validates: Requirements 3.2**

- [x] 3. Semantic resolution of identifier constraints
  - [x] 3.1 Implement identifier constraint resolution in `SemanticModelBuilder`
    - In `ExtractTypeParameters`, add a branch for `NamedConstraintDependentTypeParameterTypeParameterNode`
    - Look up the constraint name in scope, verify it resolves to a `RefinementType`, extract the base type
    - Validate the base type is in `AllowedDependentTypeAnnotations`
    - Create `DependentTypeParameterType` with the resolved base type
    - Emit appropriate diagnostics for unresolved, non-refinement, or disallowed-base-type cases
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.2 Write property test for identifier constraint resolution (Property 2)
    - **Property 2: Identifier Constraint Resolution Correctness**
    - Generate scenarios with refinement types whose base types are in the allowed set, assert correct `DependentTypeParameterType.ValueType`
    - **Validates: Requirements 2.1**

  - [x] 3.3 Write property test for disallowed base type rejection (Property 3)
    - **Property 3: Disallowed Base Type Rejection**
    - Generate scenarios with refinement types whose base types are NOT in the allowed set, assert `MIST0407` is emitted
    - **Validates: Requirements 2.2**

  - [x] 3.4 Write property test for unresolved constraint rejection (Property 4)
    - **Property 4: Unresolved Constraint Rejection**
    - Generate scenarios with identifier constraints that don't resolve to any known type, assert an error diagnostic is emitted
    - **Validates: Requirements 2.3**

- [x] 4. Checkpoint — Verify stdlib full parse
  - Ensure `Array.mist` and `Span.mist` parse completely with zero `MIST0123` diagnostics
  - Verify all type declarations are registered in the Standard namespace scope
  - Ensure all existing tests pass, ask the user if questions arise.
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 5. Remove `EnsureSpanAliasesRegistered` workaround
  - [x] 5.1 Remove `EnsureSpanAliasesRegistered` and `RegisterSyntheticSpanAlias` from `SemanticModelBuilder.cs`
    - Delete the `EnsureSpanAliasesRegistered` method body and all call sites
    - Delete the `RegisterSyntheticSpanAlias` helper method
    - Verify arity-1 `Span<T>` and `ReadOnlySpan<T>` aliases are registered from the parsed `type Span<T> = Span<T, unknown>;` declarations in `Span.mist`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 6. Remove SpanType constructor special case
  - [x] 6.1 Add intrinsic constructor declarations to `Span.mist`
    - Add `intrinsic public Span(Array<T, unknown> source);` to the `Span` struct body
    - Add `intrinsic public ReadOnlySpan(Array<T, unknown> source);` to the `ReadOnlySpan` struct body
    - _Requirements: 6.1_

  - [x] 6.2 Implement cross-reference bridge for constructor lookup
    - In `CollectConstructorOverloads`, when `createdType` is `SpanType`, look up the corresponding registered `StructType` in the Standard namespace scope and pull constructors from its member scope
    - Add `FindRegisteredStructType` helper method (with caching)
    - _Requirements: 6.2_

  - [x] 6.3 Remove the `if (createdType is SpanType)` constructor validation branch
    - Delete the dedicated SpanType constructor validation block in `TypeCheckerAnalyzer.cs` (~line 13785)
    - Constructor resolution now falls through to standard `CollectConstructorOverloads` path
    - _Requirements: 6.2, 6.3, 6.4, 6.5_

- [x] 7. Remove Array/Span foreach fast-path
  - [x] 7.1 Bridge `GetInterfaceList` for dedicated types
    - Extend `GetInterfaceList` to handle `ArrayType` and `SpanType` by looking up the registered `ClassType`/`StructType` declarations and returning their interfaces
    - Add `FindRegisteredClassType` helper method (with caching)
    - Construct type parameter → type argument mapping for substitution (element type + unknown length)
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 7.2 Remove the `if (type is ArrayType)` and `if (type is SpanType)` fast-path branches in `FindIEnumerableImplementation`
    - Delete both dedicated branches
    - Foreach element-type resolution now goes through standard interface lookup path
    - _Requirements: 7.3, 7.4, 7.5_

- [x] 8. Checkpoint — Ensure all tests pass
  - Run full test suite (4,257+ tests) and verify zero regressions
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 8.1, 8.2, 8.3_

- [x] 9. Property tests and round-trip tests
  - [x] 9.1 Write property test for TypeParameter round-trip (Property 1)
    - **Property 1: TypeParameter Round-Trip (Grammar)**
    - Generate random valid type parameter nodes (all three variants: `Name:PrimitiveKeyword`, `Name:Identifier`, `Name`), pretty-print them, re-parse, assert structural equivalence
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 9.1, 9.2, 9.3**

  - [x] 9.2 Write property test for Span constructor resolution equivalence (Property 9)
    - **Property 9: Constructor Resolution Equivalence**
    - Generate `new Span<T>(expr)` constructions with valid and invalid arguments, verify accept/reject behavior matches previous hardcoded branch
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**

  - [x] 9.3 Write property test for foreach element-type resolution equivalence (Property 10)
    - **Property 10: Foreach Element-Type Resolution Equivalence**
    - Generate foreach iterations over `ArrayType` and `SpanType` values, verify element type from interface lookup equals the type's `ElementType` property
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

- [x] 10. Sample application
  - [x] 10.1 Create `samples/DependentTypeRefinementConstraints/Program.mist` with proof code
    - Include the exact proof code from Requirement 10 verbatim
    - Demonstrate: refinement type declaration, refinement as dependent constraint on a class, refinement as dependent constraint on a struct, construction with satisfying literal, dependent parameter value access, labeled output
    - _Requirements: 10.1, 10.2, 10.3, 10.5_

  - [x] 10.2 Create `samples/DependentTypeRefinementConstraints/mist.toml` project file
    - Configure the sample project to reference the Standard library
    - _Requirements: 10.5_

  - [x] 10.3 Integrate into AllFeatures orchestrator
    - Add `DependentTypeRefinementConstraints.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add project reference to `samples/AllFeatures/mist.toml`
    - _Requirements: 10.4_

- [x] 11. Book documentation
  - [x] 11.1 Add refinement type constraints section to dependent types chapter
    - Update `book/src/type-system/dependent-types.md` with a section explaining that refinement types whose base type is an allowed primitive can be used as dependent parameter annotations
    - Include a compiled snippet demonstrating the feature
    - _Requirements: 11.1, 11.2, 11.3_

  - [x] 11.2 Create book snippet for refinement type constraints
    - Add `.mist` file under `book/snippets/type-system/` demonstrating a refinement type used as a dependent parameter constraint
    - _Requirements: 11.3_

- [x] 12. Update TODO.md
  - [x] 12.1 Remove resolved TODO items
    - Remove the "PEG parser silently drops unparseable content" section (resolved by MIST0123)
    - Remove the "TypeParameter constraint limited to PrimitiveType" section (resolved by this feature)
    - _Requirements: 3.1, 1.1_

- [x] 13. Final checkpoint — Ensure all tests pass
  - Run full test suite and verify zero regressions
  - Verify sample application compiles and produces expected output
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation — each step in the workaround removal leaves tests green
- Property tests validate universal correctness properties from the design document
- The workaround removal ordering (steps 5–7) is critical: each depends on stdlib parsing fully (step 4)
- The dedicated `ArrayType`/`SpanType` IType representations remain unchanged — only their registration and member resolution wiring changes
- Process spawning tests must follow the batching policy (1 compile, 1 run per test class)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "2.2"] },
    { "id": 2, "tasks": ["2.3", "2.4", "3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3", "3.4"] },
    { "id": 4, "tasks": ["5.1"] },
    { "id": 5, "tasks": ["6.1"] },
    { "id": 6, "tasks": ["6.2"] },
    { "id": 7, "tasks": ["6.3", "7.1"] },
    { "id": 8, "tasks": ["7.2"] },
    { "id": 9, "tasks": ["9.1", "9.2", "9.3"] },
    { "id": 10, "tasks": ["10.1", "10.2", "11.1", "11.2", "12.1"] },
    { "id": 11, "tasks": ["10.3"] }
  ]
}
```
