# Implementation Plan: Dependent Type Parameters

## Overview

This plan implements dependent type parameters across the full Mist compiler pipeline: Grammar → Parser → Pretty-Printer → Semantic Model → Type Checker → C# Emitter. Tasks follow the pipeline order so each stage can be tested independently before moving downstream. Property-based tests use FsCheck with batched compilation to comply with the process-spawning policy.

## Tasks

- [x] 1. Extend the Storm grammar with dependent type parameter and type argument rules
  - [x] 1.1 Update `TypeParameter` rule in `Source/Mist/Grammar/Mist.storm` to add `DependentTypeParameter` alternative with `COLON PrimitiveType` annotation
    - The `DependentTypeParameter` alternative must come before `RegularTypeParameter` for greedy matching
    - _Requirements: 1.1, 1.2, 1.3, 1.6_
  - [x] 1.2 Replace `TypeArgumentList` rule with new `TypeArgument`-based rule supporting literal values, negated integers, booleans, chars, hex/binary literals, const references, and type references
    - Add `TypeArgument` rule with alternatives: `NegatedIntegerTypeArgument`, `IntegerLiteralTypeArgument`, `HexLiteralTypeArgument`, `BinaryLiteralTypeArgument`, `TrueLiteralTypeArgument`, `FalseLiteralTypeArgument`, `CharLiteralTypeArgument`, `ConstReferenceTypeArgument`, `TypeReferenceTypeArgument`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_
  - [x] 1.3 Rebuild the project to verify Storm source generator produces the new AST node types without errors
    - Confirm `DependentTypeParameterTypeParameterNode` and all `TypeArgument` node variants are generated
    - _Requirements: 1.4, 1.5, 2.5_

- [x] 2. Update the Pretty-Printer to handle dependent type parameters and type arguments
  - [x] 2.1 Update `MistPrettyPrinter` to emit `Name:Type` for `DependentTypeParameterTypeParameterNode` in type parameter lists
    - No spaces around the colon
    - _Requirements: 3.1, 3.2_
  - [x] 2.2 Update `MistPrettyPrinter` to emit literal values for all `TypeArgument` node variants in type argument lists
    - Handle negated integers with minus sign, hex/binary literals verbatim, `true`/`false`, char literals, const identifiers, and type references
    - _Requirements: 3.3, 3.4, 3.5_
  - [x] 2.3 Write property test for parse–print round trip (Property 1)
    - **Property 1: Parse–Print Round Trip**
    - Create `Test/Mist.Tests/Parsing/DependentTypeRoundTripPropertyTests.cs`
    - Use FsCheck generator producing random dependent type parameter declarations and type arguments
    - Verify `print(parse(print(ast))) == print(ast)` using string equality
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**

- [x] 3. Checkpoint — Grammar and Pretty-Printer
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement semantic model support for dependent type parameters
  - [x] 4.1 Create `DependentTypeParameterType` class implementing `IType` in `Source/Mist/Semantics/Model/`
    - Fields: `Name`, `DisplayName`, `Kind` (TypeKind.Struct), `Ordinal`, `ValueType`
    - _Requirements: 6.1, 6.4_
  - [x] 4.2 Create `DependentValueArgument` class in `Source/Mist/Semantics/Model/`
    - Fields: `Value` (object), `ValueType` (IType), `DisplayValue` (string)
    - _Requirements: 6.1_
  - [x] 4.3 Update `SemanticModelBuilder` Pass 1 to detect `DependentTypeParameterTypeParameterNode` and create `DependentTypeParameterType` instances
    - Validate the value type annotation is in the allowed set {int, uint, long, ulong, short, ushort, byte, sbyte, bool, char}
    - Emit `MIST_DT_0001` for disallowed types
    - Store dependent parameters alongside regular parameters preserving declared order
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 6.1, 6.4_
  - [x] 4.4 Update `SemanticModelBuilder` Pass 2 to generate implicit dependent properties and register dependent parameter names in scope
    - For each `DependentTypeParameterType` on a type, generate a public static get-only property symbol
    - Register the parameter name as a compile-time constant in the type's scope
    - For method-level dependent parameters, register in the method's lexical scope
    - Emit `MIST_DT_0005` for name collisions with members, `MIST_DT_0006` for duplicate parameter names
    - _Requirements: 6.2, 6.3, 6.5, 6.6, 7.1, 7.5_
  - [x] 4.5 Extend `ConstructedType` to support `DependentArguments` parallel list
    - Add `IReadOnlyList<DependentValueArgument?>? DependentArguments` property
    - Update type equality to compare dependent value arguments using `object.Equals`
    - _Requirements: 8.1, 8.2_
  - [x] 4.6 Implement disambiguation of `ConstReferenceTypeArgument` during type argument resolution
    - Look up identifier in scope: if const → dependent value argument; if type → regular type argument; if unresolved → emit diagnostic
    - _Requirements: 2.4, 5.2_
  - [x] 4.7 Write property test for disallowed dependent type annotations (Property 2)
    - **Property 2: Disallowed Dependent Type Annotations Produce Diagnostics**
    - Create `Test/Mist.Tests/Semantics/DependentTypeAnnotationValidationPropertyTests.cs`
    - Generate random type annotations not in the allowed set and verify `MIST_DT_0001` is emitted
    - **Validates: Requirements 4.2, 4.3, 4.4, 4.5, 4.6, 15.4**
  - [x] 4.8 Write property test for dependent property generation (Property 8)
    - **Property 8: Dependent Property Generation**
    - Create `Test/Mist.Tests/Semantics/DependentTypePropertyGenerationPropertyTests.cs`
    - For types with N dependent parameters, verify exactly N implicit properties are generated with correct names and types
    - **Validates: Requirements 7.1, 7.2, 7.5**

- [x] 5. Checkpoint — Semantic Model
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement type checker validation for dependent types
  - [x] 6.1 Add compile-time constant validation in `TypeCheckerAnalyzer`
    - Accept: integer/boolean/char literals, negated integer literals, hex/binary literals, const references
    - Reject: variables, method calls, property accesses, expressions → emit `MIST_DT_0002`
    - Validate type match between argument and parameter annotation → emit `MIST_DT_0003` on mismatch
    - _Requirements: 5.1, 5.2, 5.3, 15.1, 15.2, 15.6_
  - [x] 6.2 Implement dependent type equality checking
    - Two `ConstructedType` instances are equal iff same base type AND all dependent values are identical
    - `FixedBuffer<5>` ≠ `FixedBuffer<6>`, assignment between them produces type mismatch error
    - _Requirements: 8.1, 8.2_
  - [x] 6.3 Implement dependent property access resolution
    - Resolve `TypeName<Value>.ParamName` to the generated dependent property
    - Treat as read-only; reject assignment attempts
    - _Requirements: 7.2, 7.3, 7.4_
  - [x] 6.4 Implement scoping rules for dependent type parameters
    - Type-level: allow usage in field types, property types, method signatures, constructor parameters
    - Method-level: allow usage in parameter types, local variable types, return type; disallow in enclosing type fields/properties
    - _Requirements: 8.3, 8.4, 8.5, 8.6, 8.7, 11.2, 11.3, 11.5_
  - [x] 6.5 Implement pattern matching validation for dependent types
    - Allow `is` patterns against closed dependent types
    - Reject patterns against open dependent types → emit `MIST_DT_0007`
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 15.7_
  - [x] 6.6 Implement inheritance and forwarding validation
    - Validate widening: `int` → `long` OK, `long` → `int` rejected → emit `MIST_DT_0004`
    - Propagate dependent parameters from derived to base types correctly
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_
  - [x] 6.7 Implement interface and trait dependent parameter validation
    - Require concrete dependent value when implementing an interface with dependent parameters
    - Allow forwarding from class dependent parameter to interface dependent parameter
    - _Requirements: 10.3, 10.4, 10.5_
  - [x] 6.8 Add missing dependent arguments diagnostic
    - When a type with dependent parameters is used without providing arguments → emit `MIST_DT_0006`
    - _Requirements: 15.5_
  - [x] 6.9 Write property test for non-constant dependent arguments (Property 3)
    - **Property 3: Non-Constant Dependent Arguments Produce Diagnostics**
    - Create `Test/Mist.Tests/Analysis/DependentTypeConstantValidationPropertyTests.cs`
    - Generate random non-constant expressions in dependent argument positions and verify `MIST_DT_0002` is emitted
    - **Validates: Requirements 5.1, 5.3, 15.1, 15.6**
  - [x] 6.10 Write property test for dependent type equality (Property 4)
    - **Property 4: Dependent Type Equality**
    - Create `Test/Mist.Tests/Analysis/DependentTypeEqualityPropertyTests.cs`
    - Generate pairs of constructed dependent types and verify equality iff all dependent values are identical
    - **Validates: Requirements 8.1, 8.2**
  - [x] 6.11 Write property test for open dependent type pattern rejection (Property 5)
    - **Property 5: Open Dependent Type Pattern Rejection**
    - Create `Test/Mist.Tests/Analysis/DependentTypePatternMatchPropertyTests.cs`
    - Generate pattern match expressions against open dependent types and verify `MIST_DT_0007` is emitted
    - **Validates: Requirements 9.3, 9.4, 15.7**

- [x] 7. Checkpoint — Type Checker
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement C# emitter support for dependent types
  - [x] 8.1 Implement const-carrier interface generation in the emitter
    - Emit `IConstInt`, `IConstBool`, `IConstChar`, etc. into `DependentTypeHelpers.g.cs`
    - Only emit interfaces for primitive types actually used as dependent parameters in the program
    - _Requirements: 13.1, 13.2_
  - [x] 8.2 Implement carrier struct generation and registry
    - For each unique (type, value) pair, emit a carrier struct (e.g., `Const_Int_5 : IConstInt`)
    - Naming convention: `Const_{Type}_{Value}` with special handling for negatives, booleans, chars
    - Maintain a registry to deduplicate carrier structs across the program
    - _Requirements: 13.3, 13.6_
  - [x] 8.3 Implement dependent type parameter emission as constrained generic parameters
    - Prefix dependent parameter names with `T` in C# output (e.g., `Size` → `TSize`)
    - Add `where TSize : IConstInt` constraint
    - Handle mixed parameter lists with both regular and carrier-constrained parameters
    - _Requirements: 13.4, 13.7, 13.8_
  - [x] 8.4 Implement dependent property emission
    - Emit `public static int Size => TSize.Value;` for each dependent parameter
    - _Requirements: 13.5_
  - [x] 8.5 Implement object construction and type reference emission with carrier structs
    - `new FixedBuffer<5>()` → `new FixedBuffer<Const_Int_5>()`
    - `FixedBuffer<5>.Size` → `FixedBuffer<Const_Int_5>.Size`
    - _Requirements: 13.6, 13.9_
  - [x] 8.6 Implement `DependentTypeAttribute` and `DependentTypeValueAttribute` emission
    - Emit `[DependentType("Size", typeof(int))]` on types with dependent parameters
    - Emit `[DependentTypeValue("Size", 5)]` on generated properties for constructed types
    - _Requirements: 14.1, 14.2, 14.3, 14.4_
  - [x] 8.7 Implement pattern matching emission for closed dependent types
    - `if (x is FixedBuffer<5>)` → `if (x is FixedBuffer<Const_Int_5>)`
    - _Requirements: 9.1, 9.2, 13.9_
    - Standard-pipeline sized-sequence correction (2026-10-03): `Array<T, N>`, `Span<T, N>` and `ReadOnlySpan<T, N>` erase a concrete `N` from their C# spelling, so every runtime test states it: `ErasedShape` emits `int[] { Length: 3 } three` for a type test, keeps distinct sized switch arms distinct with an unsized arm after them reachable, merges the clause into a typed property prefix, and carries it through tuple type tests, positional elements and nested tuples (`{ Item1: { Length: 3 } }`). Zero lengths, aliases and member subpatterns follow the same rule. A sequence whose elements carry a length is refused as `MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST`. See emitter orders 11, 90, 103 and 139 for verification evidence (ErasedShapeTests, SizedSequencePatternEmissionTests, SizedSequenceRuntimeBoundaryTests, samples/SizedSequencePatterns). _Requirements: 9.1, 9.2, 9.5._
  - [x] 8.8 Implement inheritance emission with dependent types
    - Fixed value: `class SmallBuffer : FixedBuffer<10>` → `class SmallBuffer : FixedBuffer<Const_Int_10>`
    - Forwarded parameter: `class ResizableBuffer<N:int> : FixedBuffer<N>` → `class ResizableBuffer<TN> : FixedBuffer<TN> where TN : IConstInt`
    - _Requirements: 12.1, 12.2, 13.9_
  - [x] 8.9 Write property test for emitter producing compilable C# (Property 6)
    - **Property 6: Emitter Produces Compilable C#**
    - Create `Test/Mist.Tests/Backend/DependentTypeEmitterPropertyTests.cs`
    - Use batched approach: generate 100 valid Mist programs with dependent types, compile in-process via `CompilerDriver`, build emitted C# once with `dotnet build`, verify exit code 0
    - **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8, 13.9**
  - [x] 8.10 Write property test for carrier struct uniqueness (Property 7)
    - **Property 7: Carrier Struct Uniqueness**
    - Create `Test/Mist.Tests/Backend/DependentTypeCarrierStructPropertyTests.cs`
    - Generate programs with repeated dependent value usages and verify exactly one carrier struct per unique (type, value) pair
    - **Validates: Requirements 13.3, 13.6**

- [x] 9. Checkpoint — C# Emitter
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Update TextMate grammar for dependent type syntax highlighting
  - [x] 10.1 Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` to highlight dependent type parameter annotations
    - Highlight the colon and type in `Size:int` with appropriate scoping
    - Highlight the parameter name with scope `entity.name.type.parameter.mist`
    - Highlight constant literals in type argument positions with `constant.numeric.mist` or `constant.language.mist`
    - _Requirements: 16.1, 16.2, 16.3_

- [x] 11. Create sample application demonstrating all dependent type features
  - [x] 11.1 Create `samples/DependentTypes/Program.mist` with comprehensive demonstrations
    - Class with single dependent parameter (e.g., `class FixedBuffer<Size:int>`)
    - Class with multiple dependent parameters (e.g., `class Matrix<Rows:int, Cols:int>`)
    - Mixed parameter list (e.g., `class TypedBuffer<T, Size:int>`)
    - Construction with literal values and const references
    - Dependent property access (e.g., `FixedBuffer<5>.Size`)
    - Dependent types on interfaces, traits, type aliases, and methods
    - Generic function accepting any dependent value
    - Inheritance: fixing and forwarding dependent values
    - Pattern matching against closed dependent types
    - All allowed value types: int, uint, long, ulong, short, ushort, byte, sbyte, bool, char
    - Each permutation prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - Do NOT use `Array` as a type name
    - _Requirements: 17.1–17.20_
  - [x] 11.2 Update `samples/AllFeatures/Program.mist` to call `DependentTypes.Program.RunDemo()`
    - _Requirements: 17.18_

- [x] 12. Update CSHARP_FEATURES.md documentation
  - [x] 12.1 Add `Dependent type parameters` entry marked ✅ Implemented to the Generics section
    - Update the Summary table counts
    - _Requirements: 18.1, 18.2_

- [x] 13. Final checkpoint — Full integration
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each pipeline stage
- Property tests validate universal correctness properties from the design document
- All property tests use FsCheck with batched compilation (shared `CompilerDriver`, max 1 process spawn per test class)
- The design uses C# throughout — no language selection needed
- Zero warnings policy applies to all code changes
