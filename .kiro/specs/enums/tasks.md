# Implementation Plan: Enum Declarations

## Overview

This plan implements enum declarations for the Mist programming language across four compiler stages: grammar/parser, semantic model, type checker, and C# emitter. Each task builds incrementally, wiring new functionality into the existing pipeline before moving to the next stage. Property-based tests validate correctness properties from the design document using FsCheck with the batched/in-process compilation approach.

## Tasks

- [ ] 1. Add `enum` keyword and grammar rules
  - [ ] 1.1 Register the `enum` keyword token in `Source/Mist/Grammar/keywords.storm`
    - Add `token ENUM = "enum";` to the keywords file
    - _Requirements: 11.1_

  - [ ] 1.2 Add enum grammar rules in `Source/Mist/Grammar/Mist.storm`
    - Add `EnumDeclaration`, `EnumMemberList`, and `EnumMember` rules
    - Integrate `EnumDeclaration` as a new alternative in `TypeDeclaration`
    - Ensure modifiers, optional type annotation (`: TypeReference`), comma-separated members with optional trailing comma, and optional member values (`: Expression`) are all supported
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [ ] 1.3 Update `MistPrettyPrinter` to emit enum declarations
    - Add `EmitEnumDeclaration` method in `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - Handle access modifiers, optional type annotation, comma-separated member list with optional trailing comma, and member value expressions
    - _Requirements: 12.1_

  - [ ]* 1.4 Write property test for enum declaration round-trip (Property 1)
    - **Property 1: Enum declaration round-trip**
    - Create `Test/Mist.Tests/Parsing/EnumRoundTripPropertyTests.cs`
    - Generate random valid enum ASTs via FsCheck (varying modifiers, type annotations, valued/name-only members, trailing commas), print to source, parse, print again, compare strings
    - Create `Test/Mist.Tests/Generators/MistEnumGenerator.cs` for enum source generation
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**

  - [ ]* 1.5 Write unit tests for enum keyword registration
    - Create `Test/Mist.Tests/Lexing/EnumKeywordTests.cs`
    - Verify `enum` lexes as a keyword token
    - Verify `enum` in identifier position produces a syntax error
    - _Requirements: 11.1, 11.2_

  - [ ] 1.6 Update TextMate grammar for `enum` syntax highlighting
    - Add `enum` to the `keyword.declaration.mist` match pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add a `declaration-names` pattern to highlight the identifier after `enum` as `entity.name.type.enum.mist`
    - _Requirements: 13.1, 13.2, 13.3_

- [ ] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 3. Implement enum semantic model
  - [ ] 3.1 Add `TypeKind.Enum` to the `TypeKind` enumeration
    - Add `Enum` value to `Source/Mist/Semantics/Contracts/IType.cs`
    - _Requirements: 2.3_

  - [ ] 3.2 Create `EnumType` class implementing `IType`
    - Create `Source/Mist/Semantics/Model/Types/EnumType.cs`
    - Include `DisplayName`, `Kind` (always `TypeKind.Enum`), `UnderlyingType`, `Members` list, `MemberScope`, and `ContainingNamespace`
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 3.3 Create `EnumMemberInfo` class
    - Create or co-locate with `EnumType.cs`
    - Include `Name`, `ResolvedValue`, and `ValueType` properties
    - _Requirements: 2.2_

  - [ ] 3.4 Register enums in `SemanticModelBuilder` Pass 1
    - Modify `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Register enum as a type symbol with a new `SymbolKind.Enum` value
    - Create `EnumType` instance and register member symbols in its `MemberScope`
    - _Requirements: 2.1, 5.1, 5.3_

  - [ ] 3.5 Resolve underlying type and member values in Pass 2
    - Modify `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - If explicit annotation: resolve the `TypeReference` node
    - If members have values: infer from member value types (homogeneous → that type, heterogeneous → union type)
    - If name-only: set underlying type to `byte`
    - Assign sequential values (0, 1, 2, ...) for name-only members, continuing after explicit values
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4_

  - [ ] 3.6 Add diagnostic IDs for enum errors
    - Register `MIST_SB_0080` through `MIST_SB_0084` in the diagnostics infrastructure
    - Report errors for: member value not assignable to explicit type, member value not assignable to inferred type, undefined member access, reverse conversion, duplicate enum name
    - _Requirements: 3.5, 3.6, 3.7, 5.2_

  - [ ]* 3.7 Write property test for enum type construction invariants (Property 2)
    - **Property 2: Enum type construction invariants**
    - Create `Test/Mist.Tests/Semantics/EnumTypeConstructionPropertyTests.cs`
    - Generate valid enum sources, compile in-process using shared `CompilerDriver`, inspect `EnumType` in semantic model
    - Verify `Kind == TypeKind.Enum`, non-null `UnderlyingType`, and `Members.Count` equals declared member count
    - **Validates: Requirements 2.1, 2.2, 2.3**

  - [ ]* 3.8 Write property test for underlying type inference (Property 3)
    - **Property 3: Underlying type inference correctness**
    - Create `Test/Mist.Tests/Semantics/EnumTypeInferencePropertyTests.cs`
    - Generate enums with known member types, compile in-process, verify underlying type matches expected
    - **Validates: Requirements 3.1, 3.2, 3.3**

  - [ ]* 3.9 Write property test for sequential value assignment (Property 4)
    - **Property 4: Sequential value assignment**
    - Create `Test/Mist.Tests/Semantics/EnumSequentialValuePropertyTests.cs`
    - Generate name-only and mixed enums, compile in-process, verify member values follow sequencing rules
    - **Validates: Requirements 4.1, 4.2, 4.3**

- [ ] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement type checker support for enums
  - [ ] 5.1 Add enum member access resolution in type checker
    - Modify `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Resolve `EnumName.MemberName` as static member access on the enum type
    - Report `MIST_SB_0082` for undefined member access
    - _Requirements: 5.1, 5.2, 5.3_

  - [ ] 5.2 Add enum as valid type reference
    - Ensure enum names are accepted in variable declarations, parameter types, return types, and field types
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [ ] 5.3 Implement implicit conversion from enum to underlying type
    - Add conversion rule: enum → underlying type is accepted without error
    - Reject reverse conversion (underlying type → enum) with `MIST_SB_0083`
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ] 5.4 Implement nominal type distinction
    - Ensure two `EnumType` instances with different names are never assignable to each other, even with identical underlying types
    - _Requirements: 2.4_

  - [ ] 5.5 Implement pattern matching support for enum values
    - Handle `is EnumName.MemberName` patterns as value equality checks against the member's resolved value
    - _Requirements: 8.1, 8.2, 8.3_

  - [ ]* 5.6 Write property test for enum member access resolution (Property 5)
    - **Property 5: Enum member access resolution**
    - Create `Test/Mist.Tests/Analysis/EnumMemberAccessPropertyTests.cs`
    - Generate enums, compile programs that access members, verify no diagnostics and correct type
    - **Validates: Requirements 5.1, 5.3**

  - [ ]* 5.7 Write property test for invalid member access (Property 6)
    - **Property 6: Invalid member access produces error**
    - Create `Test/Mist.Tests/Analysis/EnumInvalidMemberAccessPropertyTests.cs`
    - Generate enums, compile programs accessing non-existent members, verify `MIST_SB_0082` diagnostic
    - **Validates: Requirements 5.2**

  - [ ]* 5.8 Write property test for enum as type reference (Property 7)
    - **Property 7: Enum usable as type reference**
    - Create `Test/Mist.Tests/Analysis/EnumTypeReferencePropertyTests.cs`
    - Generate enums used in all type positions (variable, parameter, return, field), compile, verify no diagnostics
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

  - [ ]* 5.9 Write property test for implicit conversion (Property 8)
    - **Property 8: Implicit conversion to underlying type**
    - Create `Test/Mist.Tests/Analysis/EnumImplicitConversionPropertyTests.cs`
    - Generate enums with assignments to underlying type, compile, verify no diagnostics
    - **Validates: Requirements 7.1, 7.2**

  - [ ]* 5.10 Write property test for reverse conversion rejection (Property 9)
    - **Property 9: Reverse conversion rejected**
    - Create `Test/Mist.Tests/Analysis/EnumReverseConversionPropertyTests.cs`
    - Generate enums with reverse assignments, compile, verify type error diagnostic
    - **Validates: Requirements 7.3**

  - [ ]* 5.11 Write property test for nominal type distinction (Property 12)
    - **Property 12: Nominal type distinction**
    - Create `Test/Mist.Tests/Analysis/EnumNominalDistinctionPropertyTests.cs`
    - Generate pairs of enums with same underlying type, compile cross-assignments, verify errors
    - **Validates: Requirements 2.4**

- [ ] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Implement C# emitter for enums
  - [ ] 7.1 Emit enum as `record class` in `CSharpEmitter`
    - Modify `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - Generate `record class` with primary constructor parameter `Value` of the underlying type
    - Generate `public static readonly` field for each enum member
    - Generate `implicit operator` converting to the underlying type
    - Apply correct access modifier (public/private/internal default)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3_

  - [ ] 7.2 Handle union underlying type in emission
    - Use existing `UnionHelperGenerator.GetHelperTypeName` for the `Value` type when underlying type is a union
    - _Requirements: 9.5_

  - [ ] 7.3 Ensure enum members are always publicly accessible in emitted code
    - Emit member fields as `public static readonly` regardless of enum visibility modifier
    - _Requirements: 10.4_

  - [ ] 7.4 Update `CSharpTypeMap` for enum type mapping
    - Ensure `EnumType` maps to the generated record class name in type references
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [ ]* 7.5 Write property test for C# emission structure (Property 11)
    - **Property 11: C# emission structure**
    - Create `Test/Mist.Tests/Backend/EnumEmissionPropertyTests.cs`
    - Generate valid enums, emit C#, verify output contains `record class`, correct access modifier, `Value` constructor parameter, `public static readonly` fields, and `implicit operator`
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3**

  - [ ]* 7.6 Write unit tests for enum emission edge cases
    - Create `Test/Mist.Tests/Backend/EnumEmissionTests.cs`
    - Test union underlying type uses helper struct name (Requirement 9.5)
    - Test default visibility emits as `internal` (Requirement 10.3)
    - Test enum members always publicly accessible (Requirement 10.4)
    - _Requirements: 9.5, 10.3, 10.4_

- [ ] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Implement pattern matching emission and integration test
  - [ ] 9.1 Emit `is EnumName.MemberName` as value equality check in C#
    - Update pattern emission in `CSharpEmitter` to handle enum member patterns
    - Generate equality comparison against the static readonly field
    - _Requirements: 8.1, 8.2, 8.3_

  - [ ]* 9.2 Write property test for pattern matching (Property 10)
    - **Property 10: Pattern matching value equality**
    - Create `Test/Mist.Tests/Integration/EnumPatternMatchingPropertyTests.cs`
    - Batched test: generate enum + is-expression programs, compile once with shared `CompilerDriver`, run once, verify output lines match expected true/false results
    - **Validates: Requirements 8.1, 8.2, 8.3**

- [ ] 10. Create sample application
  - [ ] 10.1 Create `samples/Enums/Program.mist`
    - Demonstrate every syntax variant: explicit type annotation, inferred type, name-only enum with auto-assigned values, mixed explicit/implicit values, union underlying type, member access via dot notation, enum as variable/parameter/return/field type, implicit conversion to underlying type, pattern matching with `is` expressions, public/private/default visibility, trailing comma usage
    - Each permutation prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - _Requirements: 14.1, 14.2, 14.3, 14.5_

  - [ ] 10.2 Integrate into `samples/AllFeatures/Program.mist`
    - Add call to `Enums.Program.RunDemo()`
    - _Requirements: 14.4_

  - [ ]* 10.3 Write integration test for sample compilation
    - Verify `samples/Enums/Program.mist` compiles and runs without errors via the existing `SamplesCompileAndMatchBaselineTest` infrastructure
    - _Requirements: 14.5_

- [ ] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All property tests use the shared `CompilerDriver` instance for in-process compilation per the test-process-spawning policy
- Property 10 (pattern matching) uses the batched approach: generate all scenarios, compile once, run once, compare output lines
