# Implementation Plan: Property Initializers

## Overview

This plan implements property initializer blocks on `new` expressions across the full Mist compiler pipeline: grammar → parser → semantic model → type checker → C# emitter → pretty printer → syntax highlighting → samples → property-based tests. Tasks follow the compiler pipeline order so each stage builds on the previous one. The implementation language is C# (matching the existing codebase), with Storm grammar files for parser rules.

## Tasks

- [ ] 1. Grammar: Add property initializer rules to `Mist.storm`
  - [x] 1.1 Extend the existing `ObjectCreationExpression` alternative in the `Primary` rule to accept an optional `PropertyInitializerBlock`
    - In `Source/Mist/Grammar/Mist.storm`, change the existing `ObjectCreationExpression` alternative from:
      ```
      | NEW type:QualifiedIdentifier typeArgs:TypeArgumentList? LEFT_PARENTHESIS arguments:ArgumentList? RIGHT_PARENTHESIS as ObjectCreationExpression
      ```
      to:
      ```
      | NEW type:QualifiedIdentifier typeArgs:TypeArgumentList? LEFT_PARENTHESIS arguments:ArgumentList? RIGHT_PARENTHESIS initializer:PropertyInitializerBlock? as ObjectCreationExpression
      ```
    - _Requirements: 1.1, 1.4, 1.5, 1.7_
  - [x] 1.2 Add a new `ObjectCreationExpressionNoArgs` alternative to the `Primary` rule for the no-parentheses case
    - Add immediately after the modified `ObjectCreationExpression` alternative:
      ```
      | NEW type:QualifiedIdentifier typeArgs:TypeArgumentList? initializer:PropertyInitializerBlock as ObjectCreationExpressionNoArgs
      ```
    - This handles `new Type { ... }` which invokes the parameterless constructor
    - _Requirements: 1.6, 1.7_
  - [x] 1.3 Add `PropertyInitializerBlock`, `PropertyAssignmentList`, and `PropertyAssignment` rules to `Source/Mist/Grammar/Mist.storm`
    - Define `rule PropertyInitializerBlock = LEFT_BRACE assignments:PropertyAssignmentList? RIGHT_BRACE;`
    - Define `rule PropertyAssignmentList = PropertyAssignment (COMMA PropertyAssignment)* COMMA?;`
    - Define `rule PropertyAssignment = name:IDENTIFIER EQUAL value:Expression;`
    - Trailing comma support is consistent with `EnumMemberList` and `SwitchArmList`
    - _Requirements: 1.2, 1.3, 1.4, 1.5_

- [x] 2. Checkpoint — Verify grammar compiles
  - Rebuild the solution to ensure the Storm source generator produces the new AST node types (`ObjectCreationExpressionPrimaryNode` with optional `Initializer` field, `ObjectCreationExpressionNoArgsPrimaryNode`, `PropertyInitializerBlockNode`, `PropertyAssignmentListNode`, `PropertyAssignmentNode`) from the updated grammar. Ensure all tests pass, ask the user if questions arise.

- [ ] 3. Semantic Model: Extend expression type resolution for property initializers
  - [x] 3.1 Extend the `ObjectCreationExpressionPrimaryNode` case in `ResolveExpression` in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - After resolving the type and constructor arguments (existing logic), check if `objCreation.Initializer` is non-null
    - If present, iterate over each `PropertyAssignment` in the initializer block
    - For each assignment, call `ResolveExpression` on the value expression to compute and record its type in `expressionTypes`
    - The overall expression type remains the constructed type (unchanged)
    - _Requirements: 7.2, 7.3_
  - [x] 3.2 Add handling for the new `ObjectCreationExpressionNoArgsPrimaryNode` in `ResolveExpression`
    - Resolve the type name identically to the existing `ObjectCreationExpressionPrimaryNode` path
    - Handle type arguments if present
    - Iterate over each `PropertyAssignment` in the initializer block and resolve value expressions
    - Record the constructed type as the expression type
    - _Requirements: 7.2, 7.3_

- [x] 4. Checkpoint — Verify semantic model builds correctly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Type Checker: Add property initializer validation and diagnostics
  - [x] 5.1 Add diagnostic IDs MIST_PI_0001 through MIST_PI_0006 to the diagnostics infrastructure
    - MIST_PI_0001 (Error): "Type '{typeName}' does not contain a property or field named '{memberName}'"
    - MIST_PI_0002 (Error): "Cannot assign '{valueType}' to '{memberName}' of type '{memberType}'"
    - MIST_PI_0003 (Error): "Property '{memberName}' has no accessible 'set' or 'init' accessor and cannot be assigned in an initializer"
    - MIST_PI_0004 (Error): "Field '{memberName}' is readonly and cannot be assigned in an initializer"
    - MIST_PI_0005 (Error): "'{memberName}' is inaccessible due to its protection level"
    - MIST_PI_0006 (Error): "Member '{memberName}' is assigned more than once in the initializer"
    - _Requirements: 3.2, 4.2, 5.2, 5.4, 5.6, 6.2_
  - [x] 5.2 Add `CheckPropertyInitializerBlock` method to `TypeCheckerAnalyzer` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Create a private method `CheckPropertyInitializerBlock(PropertyInitializerBlockNode initializerBlock, IType constructedType, SyntaxNode parentNode, ISemanticModel model, DiagnosticBag diagnostics)`
    - For each `PropertyAssignment`, perform five validation checks in order:
      1. **Member existence** — Look up the name in the constructed type's `MemberScope`; emit MIST_PI_0001 if not found as property or field
      2. **Type compatibility** — Get member's `DeclaredType` and value expression type; call `IsAssignable`; emit MIST_PI_0002 if not assignable
      3. **Writability (property)** — Check `PropertyMetadata.HasSet || PropertyMetadata.HasInit`; emit MIST_PI_0003 if neither
      4. **Writability (field)** — Check `symbol.ReadonlyStatus`; emit MIST_PI_0004 if readonly
      5. **Accessibility** — Check access modifier; emit MIST_PI_0005 if private and call site is outside declaring type
    - Track seen names in `HashSet<string>` for duplicate detection; emit MIST_PI_0006 on second occurrence
    - _Requirements: 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 6.1, 6.2_
  - [x] 5.3 Integrate `CheckPropertyInitializerBlock` into the existing `CheckObjectCreation` flow
    - In the `ObjectCreationExpressionPrimaryNode` handling, after existing constructor validation, call `CheckPropertyInitializerBlock` if `Initializer` is non-null
    - Add a new case for `ObjectCreationExpressionNoArgsPrimaryNode` that resolves the type, verifies a parameterless constructor exists, then calls `CheckPropertyInitializerBlock`
    - _Requirements: 1.6, 3.1, 4.1, 5.1, 6.1_

- [x] 6. Checkpoint — Verify type checker diagnostics
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. C# Emitter: Emit property initializer blocks as C# object initializer syntax
  - [x] 7.1 Extend the `ObjectCreationExpressionPrimaryNode` case in `EmitPrimary` in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - After emitting `new Type(args)`, check if `objCreation.Initializer` is non-null
    - If present, emit `{ Name1 = expr1, Name2 = expr2, ..., NameN = exprN }` after the constructor call
    - Each assignment emits `assignment.Name.Lexeme = EmitExpression(assignment.Value)`
    - Assignments are comma-separated, on the same line (consistent with existing emission style)
    - _Requirements: 8.1, 8.2, 8.3, 8.5_
  - [x] 7.2 Add a new case for `ObjectCreationExpressionNoArgsPrimaryNode` in `EmitPrimary`
    - Emit `new Type()` with explicit empty argument list (C# requires parens even when Mist omits them)
    - Emit the initializer block `{ Name1 = expr1, ... }` after the empty argument list
    - _Requirements: 8.4, 8.1, 8.2, 8.3, 8.5_

- [ ] 8. Pretty Printer: Format property initializer blocks for round-trip fidelity
  - [x] 8.1 Extend the `ObjectCreationExpressionPrimaryNode` case in `EmitPrimary` in `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - After formatting `new Type(args)`, check if `objCreation.Initializer` is non-null
    - If present, format as `new Type(args) { Name1 = expr1, Name2 = expr2 }`
    - _Requirements: 9.1, 9.2_
  - [x] 8.2 Add a new case for `ObjectCreationExpressionNoArgsPrimaryNode` in `EmitPrimary`
    - Format as `new Type { Name1 = expr1, Name2 = expr2 }` (no parentheses, matching Mist source)
    - _Requirements: 9.3_
  - [x] 8.3 Add `EmitPropertyAssignmentList` helper method to `MistPrettyPrinter`
    - Iterate over assignments, format each as `Name = expr`, join with `, `
    - Handle null/empty assignment list (empty block → empty string between braces)
    - _Requirements: 9.1, 9.2, 9.4_

- [ ] 9. Syntax Highlighting: Update TextMate grammar for property initializer patterns
  - [x] 9.1 Add a pattern to highlight property names within initializer blocks in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add a pattern matching `IDENTIFIER\s*=` after `{` or `,` within a `new` expression context
    - Scope the identifier as `variable.other.property.mist`
    - Scope the `=` as an operator (existing operator scope)
    - _Requirements: 10.1, 10.2_
  - [x] 9.2 Verify string/comment exclusion for property initializer patterns
    - Confirm the existing TextMate grammar matches strings and comments before the new pattern, so property initializer syntax inside strings or comments does not receive special highlighting
    - _Requirements: 10.3_

- [x] 10. Checkpoint — Verify all compiler pipeline stages work together
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Sample Application: Create PropertyInitializers sample and update AllFeatures
  - [x] 11.1 Create `samples/PropertyInitializers/Program.mist` with `public static void RunDemo()` entry point
    - Create the `samples/PropertyInitializers/` directory with a `Program.mist` file
    - Use `namespace PropertyInitializers;`
    - Define helper types: a class with settable properties, init-only properties, public fields, a struct, a generic class
    - _Requirements: 11.1, 11.14, 11.16_
  - [x] 11.2 Demonstrate `new` expression with constructor arguments and property initializer block
    - Example: `new Point(0, 0) { X = 3, Y = 4 }`
    - Print labeled output showing the initialized values
    - _Requirements: 11.2_
  - [x] 11.3 Demonstrate `new` expression with property initializer block but no constructor arguments
    - Example: `new Config { Timeout = 30, Verbose = true }`
    - Print labeled output
    - _Requirements: 11.3_
  - [x] 11.4 Demonstrate `new` expression with empty property initializer block
    - Example: `new Config() { }`
    - Print labeled output
    - _Requirements: 11.4_
  - [x] 11.5 Demonstrate initializing properties with `init` accessors and `set` accessors
    - Show both `init`-only and `set` properties being assigned in initializer blocks
    - Print labeled output
    - _Requirements: 11.5, 11.6_
  - [x] 11.6 Demonstrate initializing multiple properties in a single block
    - Example: `new Config { Timeout = 30, Verbose = true, Name = "test" }`
    - Print labeled output
    - _Requirements: 11.7_
  - [x] 11.7 Demonstrate property initializers as method argument and in variable declaration with type inference
    - Example: `Process(new Config { Verbose = true })` and `var c = new Config { Timeout = 10 }`
    - Print labeled output
    - _Requirements: 11.8, 11.9_
  - [x] 11.8 Demonstrate property initializers on generic type and struct type
    - Example: `new Box<int> { Value = 42 }` and `new MyStruct { X = 1, Y = 2 }`
    - Print labeled output
    - _Requirements: 11.10, 11.11_
  - [x] 11.9 Demonstrate initializing a public field and using complex value expressions
    - Example: `new Data { Count = GetCount() + 1, Label = $"item-{id}" }`
    - Print labeled output
    - _Requirements: 11.12, 11.13_
  - [x] 11.10 Demonstrate all permutations print labeled output and verify compilation
    - Each permutation prints labeled output to stdout so correctness is observable
    - _Requirements: 11.15, 11.16_
  - [x] 11.11 Update `samples/AllFeatures/Program.mist` to call `PropertyInitializers.Program.RunDemo()`
    - Add the call alongside existing feature demonstrations
    - Ensure all previously demonstrated features still work
    - _Requirements: 12.1, 12.2_

- [x] 12. Checkpoint — Compile and run sample applications
  - Compile `samples/PropertyInitializers/` and `samples/AllFeatures/` end-to-end. Verify correct output. Ensure all tests pass, ask the user if questions arise.

- [ ] 13. Property-Based Tests
  - [x] 13.1 Write PBT for pretty-printer round trip (Property 1)
    - Create `Test/Mist.Tests/Parsing/PropertyInitializerRoundTripPropertyTests.cs`
    - Generate random valid `new` expressions with property initializer blocks (with/without constructor args, varying assignment counts 0–5, trailing commas, nested expressions)
    - For any valid Mist source with property initializers, parse → pretty-print → parse → pretty-print produces identical text
    - Uses shared `CompilerDriver` instance, in-process, no spawning
    - **Property 1: Pretty-printer round trip**
    - **Validates: Requirements 9.4**
  - [x] 13.2 Write PBT for emitter produces valid C# initializer syntax (Property 2)
    - Create `Test/Mist.Tests/Backend/PropertyInitializerEmissionPropertyTests.cs`
    - Generate random valid `new` expressions with N assignments (1–5), verify emitted C# contains `{ Name1 = expr1, ..., NameN = exprN }` after the constructor call
    - Batched: generate all scenarios upfront, compile once, run once, compare output lines
    - **Property 2: Emitter produces valid C# initializer syntax**
    - **Validates: Requirements 8.1, 8.2, 8.3**
  - [x] 13.3 Write PBT for non-existent member produces diagnostic (Property 3)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerMemberExistencePropertyTests.cs`
    - Generate random type declarations and initializer blocks with invalid member names
    - Verify exactly one MIST_PI_0001 diagnostic is produced per invalid name
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 3: Non-existent member produces diagnostic**
    - **Validates: Requirements 3.1, 3.2**
  - [x] 13.4 Write PBT for type-incompatible assignment produces diagnostic (Property 4)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerTypeCompatibilityPropertyTests.cs`
    - Generate random type declarations with settable properties and assign type-incompatible values
    - Verify exactly one MIST_PI_0002 diagnostic is produced per incompatible assignment
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 4: Type-incompatible assignment produces diagnostic**
    - **Validates: Requirements 4.1, 4.2**
  - [x] 13.5 Write PBT for read-only property produces diagnostic (Property 5)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerWritabilityPropertyTests.cs`
    - Generate random type declarations with get-only properties
    - Verify exactly one MIST_PI_0003 diagnostic is produced per read-only property assignment
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 5: Read-only property produces diagnostic**
    - **Validates: Requirements 5.1, 5.2**
  - [x] 13.6 Write PBT for readonly field produces diagnostic (Property 6)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerReadonlyFieldPropertyTests.cs`
    - Generate random type declarations with readonly fields
    - Verify exactly one MIST_PI_0004 diagnostic is produced per readonly field assignment
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 6: Readonly field produces diagnostic**
    - **Validates: Requirements 5.3, 5.4**
  - [x] 13.7 Write PBT for duplicate member produces diagnostic (Property 7)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerDuplicatePropertyTests.cs`
    - Generate random initializer blocks with duplicate member names
    - Verify exactly one MIST_PI_0006 diagnostic is produced for the duplicate occurrence
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 7: Duplicate member produces diagnostic**
    - **Validates: Requirements 6.1, 6.2**
  - [x] 13.8 Write PBT for valid initializer produces no diagnostics (Property 8)
    - Create `Test/Mist.Tests/Analysis/PropertyInitializerValidPropertyTests.cs`
    - Generate random type declarations with N settable properties/fields and initializer blocks assigning distinct, type-compatible values to subsets
    - Verify zero property-initializer diagnostics (MIST_PI_*) are produced
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 8: Valid initializer produces no diagnostics**
    - **Validates: Requirements 3.1, 4.1, 5.1, 6.1, 7.3**
  - [x] 13.9 Write PBT for omitted argument list emits empty parens in C# (Property 9)
    - Create `Test/Mist.Tests/Backend/PropertyInitializerNoArgsEmissionPropertyTests.cs`
    - Generate random `new Type { ... }` expressions (no-args variant)
    - Verify emitted C# always includes explicit `()` before the initializer block
    - In-process via shared `CompilerDriver`, no spawning
    - **Property 9: Omitted argument list emits empty parens in C#**
    - **Validates: Requirements 8.4**

- [x] 14. Final checkpoint — Ensure all tests pass
  - Run the full test suite (`dotnet test`). Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each pipeline stage
- Property tests validate universal correctness properties from the design document
- The grammar changes (Task 1) must be completed first as the Storm source generator produces AST node types that all subsequent stages depend on
- The `ObjectCreationExpression` alternative ordering matters: the variant with parentheses must come before the no-args variant to avoid ambiguity (the parser tries alternatives in order)
- All PBT tests are in-process except emission correctness (Property 2) which uses batched compilation (1 compile + 1 run)
- Six new diagnostic IDs (MIST_PI_0001–MIST_PI_0006) are introduced, all at Error severity
