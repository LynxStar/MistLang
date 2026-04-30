# Implementation Plan: Void Unit Type

## Overview

Promote Mist's existing `VoidType` from a return-type-only marker to a fully powered unit type. The implementation spans four compiler layers: semantic model builder, type checker, C# type map, and C# emitter. The `void` keyword remains the canonical alias; the identifier `Void` (capital V) resolves to the same type. `Void.Void` is the sole mechanism for explicitly obtaining a void value.

## Tasks

- [ ] 1. Register the new diagnostic ID
  - [x] 1.1 Add `MIST_TC_0034` to `DiagnosticId.cs`
    - Add `VoidArrayNotPermitted` field: `public static readonly DiagnosticId VoidArrayNotPermitted = new("MIST_TC_0034");`
    - Place it in a new "Void type diagnostics" section
    - _Requirements: 8.1, 8.2_

- [ ] 2. Semantic model builder changes
  - [x] 2.1 Add `"Void"` mapping to `TryResolveWellKnownType` in `SemanticModelBuilder.cs`
    - Add case `"Void" => VoidType.Instance` alongside the existing `"void"` case
    - This enables `Void` as an identifier to resolve to `VoidType.Instance` through the existing `ResolveIdentifierAsType` → `TryResolveWellKnownType` path
    - _Requirements: 1.4, 1.5_

  - [x] 2.2 Handle `Void.Void` member access in `ResolvePostfixSuffix` in `SemanticModelBuilder.cs`
    - In the `MemberAccessSuffixPostfixSuffixNode` case, add a check before the general `LookupMemberType` call
    - When `targetType is VoidType` and `memberName == "Void"`, set `expressionTypes[suffix] = VoidType.Instance` and return `VoidType.Instance`
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 2.3 Write property test for void type reference resolution (Property 1)
    - **Property 1: Void type reference resolution**
    - For any Mist program containing a type reference to `void` or `Void` in any valid position, the semantic model builder resolves to `VoidType.Instance`
    - Create `Test/Mist.Tests/Semantics/VoidTypeResolutionPropertyTests.cs`
    - Use FsCheck generators to produce programs with void type references in return types, variable types, parameter types, and generic type arguments
    - **Validates: Requirements 1.4, 1.5, 3.1, 4.1, 5.1, 6.1**

  - [x] 2.4 Write property test for Void.Void expression type (Property 2)
    - **Property 2: Void.Void expression type**
    - For any Mist program containing the expression `Void.Void`, the semantic model assigns type `VoidType`
    - Create `Test/Mist.Tests/Semantics/VoidInstanceExpressionPropertyTests.cs`
    - Use FsCheck generators to produce programs with `Void.Void` in various expression contexts
    - **Validates: Requirements 2.1, 2.2**

  - [x] 2.5 Write unit tests for void type identity and var inference
    - Create `Test/Mist.Tests/Semantics/VoidTypeIdentityTests.cs`
    - Test `VoidType.Instance.Kind == TypeKind.Struct` (Req 1.1)
    - Test `VoidType.Instance` is singleton (Req 1.2)
    - Test `VoidType.Instance.DisplayName == "void"` (Req 1.3)
    - Test `var x = Void.Void;` infers `VoidType` (Req 4.3)
    - Test `void x = Void.Void;` accepted (Req 4.2)
    - _Requirements: 1.1, 1.2, 1.3, 4.2, 4.3_

- [x] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 4. Type checker changes
  - [x] 4.1 Add void array restriction in `CheckArrayCreation` in `TypeCheckerAnalyzer.cs`
    - After checking the size expression, check if the element type is `VoidType`
    - If `elementType is ArrayType at && at.ElementType is VoidType`, emit `MIST_TC_0034` diagnostic
    - _Requirements: 8.2_

  - [x] 4.2 Add void array restriction in `CheckVariableDeclarationStatement` in `TypeCheckerAnalyzer.cs`
    - When the resolved variable type is `ArrayType` with `VoidType` element, emit `MIST_TC_0034` diagnostic
    - Covers `void[] x = ...` declarations
    - _Requirements: 8.1_

  - [x] 4.3 Accept `return Void.Void;` in void methods in `CheckReturnStatement` in `TypeCheckerAnalyzer.cs`
    - Modify the void-method branch: add `&& valueType is not VoidType` to the condition that rejects return values
    - Change: `if (valueType is not null && !IsUnknown(valueType))` → `if (valueType is not null && !IsUnknown(valueType) && valueType is not VoidType)`
    - _Requirements: 3.4, 3.5_

  - [x] 4.4 Write property test for void methods accepting return values (Property 3)
    - **Property 3: Void methods accept missing return values**
    - For any void method, the type checker accepts `return;`, `return Void.Void;`, and no return statement
    - Create `Test/Mist.Tests/Analysis/VoidReturnPropertyTests.cs`
    - **Validates: Requirements 3.2, 3.3, 3.5**

  - [x] 4.5 Write property test for non-void to void type mismatch (Property 4)
    - **Property 4: Non-void to void type mismatch**
    - For any non-void type T, providing a value of type T where VoidType is expected produces a diagnostic
    - Create `Test/Mist.Tests/Analysis/VoidTypeMismatchPropertyTests.cs`
    - **Validates: Requirements 3.4, 4.4, 6.3**

  - [x] 4.6 Write property test for void array restriction (Property 5)
    - **Property 5: Void array restriction**
    - For any program with `void[]` declaration or `new void[n]`, the type checker emits `MIST_TC_0034`
    - Create `Test/Mist.Tests/Analysis/VoidArrayRestrictionPropertyTests.cs`
    - **Validates: Requirements 8.1, 8.2**

  - [x] 4.7 Write property test for void/non-void equality mismatch (Property 6)
    - **Property 6: Void/non-void equality type mismatch**
    - Comparing a VoidType expression with a non-void expression using `==` or `!=` produces a diagnostic
    - Create `Test/Mist.Tests/Analysis/VoidEqualityMismatchPropertyTests.cs`
    - **Validates: Requirements 9.3**

  - [x] 4.8 Write unit tests for type checker void scenarios
    - Create `Test/Mist.Tests/Analysis/VoidTypeCheckerTests.cs`
    - Test `Void.Void == Void.Void` accepted (Req 9.1)
    - Test `Void.Void != Void.Void` accepted (Req 9.2)
    - Test `method(Void.Void)` with void param accepted (Req 6.2)
    - Test `Result<void>` accepted (Req 5.2)
    - Test `return Void.Void;` in void method accepted (Req 3.5)
    - Test `void` field in class accepted (Req 7.1)
    - Test `Void` field in struct accepted (Req 7.2)
    - Test `void` field assigned `Void.Void` accepted (Req 7.3)
    - _Requirements: 3.5, 5.2, 6.2, 7.1, 7.2, 7.3, 9.1, 9.2_

- [x] 5. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. C# type map context-sensitive emission
  - [x] 6.1 Add `isValueContext` parameter to `GetTypeName` in `CSharpTypeMap.cs`
    - Change signature: `public static string GetTypeName(IType type, bool isValueContext = false)`
    - Change `VoidType` case: `VoidType => isValueContext ? "MistVoid" : "void"`
    - Default `false` preserves backward compatibility for all existing callers
    - _Requirements: 10.5_

- [ ] 7. C# emitter changes
  - [x] 7.1 Add `MistVoid` struct emission to `CSharpEmitter.cs`
    - Emit the `MistVoid` readonly struct with `IEquatable<MistVoid>`, `Instance` field, equality operators, and `ToString`
    - Emit once per compilation unit that uses void as a value type
    - Track `_needsMistVoidStruct` flag to conditionally emit
    - _Requirements: 10.1, 10.2_

  - [x] 7.2 Implement void method return type detection (value-returning analysis) in `CSharpEmitter.cs`
    - Before emitting method declarations, scan the semantic model for call sites of each void method
    - Build `_valueReturningVoidMethods` (`HashSet<string>`) of method names whose return values are consumed
    - In `EmitMethodDeclaration`, check this set to decide between `void` and `MistVoid` return type
    - _Requirements: 10.3, 10.4_

  - [x] 7.3 Emit `Void.Void` expressions as `MistVoid.Instance` in `CSharpEmitter.cs`
    - In `EmitPostfixSuffix` for `MemberAccessSuffixPostfixSuffixNode`, detect `Void.Void` pattern
    - When target is `"Void"` and member name is `"Void"`, emit `MistVoid.Instance`
    - Set `_needsMistVoidStruct = true`
    - _Requirements: 10.1_

  - [x] 7.4 Emit void variables and parameters using `MistVoid` in `CSharpEmitter.cs`
    - In `EmitTypeReference`, when in a value context (variable declaration, parameter, generic type argument), emit `MistVoid` instead of `void`
    - Track `_inValueContext` state in the emitter
    - Set `_needsMistVoidStruct = true` when void is used in value context
    - _Requirements: 10.2_

  - [x] 7.5 Emit return statements in value-returning void methods in `CSharpEmitter.cs`
    - For void methods in `_valueReturningVoidMethods`: `return;` → `return MistVoid.Instance;`
    - Implicit end-of-method → append `return MistVoid.Instance;`
    - `return Void.Void;` → `return MistVoid.Instance;`
    - _Requirements: 10.3_

  - [x] 7.6 Write property test for context-sensitive void C# emission (Property 7)
    - **Property 7: Context-sensitive void C# emission**
    - For void used as a value type, the emitter produces `MistVoid`; for fire-and-forget void methods, it produces C# `void`
    - Create `Test/Mist.Tests/Backend/VoidEmissionPropertyTests.cs`
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**

  - [x] 7.7 Write unit tests for emitter void scenarios
    - Create `Test/Mist.Tests/Backend/VoidEmitterTests.cs`
    - Test `Void.Void` emits `MistVoid.Instance`
    - Test void variable emits `MistVoid` type
    - Test void parameter emits `MistVoid` type
    - Test fire-and-forget void method emits C# `void`
    - Test value-returning void method emits `MistVoid` return type
    - Test `MistVoid` struct is emitted when needed
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 8. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Integration tests
  - [x] 9.1 Write end-to-end compilation integration tests
    - Create `Test/Mist.Tests/Integration/VoidUnitTypeIntegrationTests.cs`
    - Test end-to-end compilation of program using `Result<void>` (Req 5.3)
    - Test generated C# compiles and runs with `MistVoid` struct (Req 10.1–10.5)
    - Test fire-and-forget void method emits C# `void` (Req 10.4)
    - Test value-returning void method emits `MistVoid` (Req 10.3)
    - _Requirements: 5.3, 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each compiler layer
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# throughout — no language selection needed
- No grammar changes are required; `Void` is already parseable as an `IDENTIFIER` and `void` as a `VOID` keyword token
- Existing void equality (`AreEqualityCompatible`) and void parameter/generic handling already work via singleton semantics — only tests are needed to verify
