# Implementation Plan: Optional Types

## Overview

This plan implements optional type syntax (`T?`), the `Optional<T>` type alias, and three operators (`?.`, `??`, `??=`) for the Mist programming language. The implementation proceeds bottom-up through the compiler pipeline: lexer tokens → grammar rules → semantic model resolution → type checker validation → C# emission → tooling (TextMate, book, sample). All property tests use in-process compilation via the shared `CompilerDriver` — no per-iteration process spawning.

## Tasks

- [x] 1. Lexer: Add new tokens for optional operators
  - [x] 1.1 Add `QUESTION_QUESTION_EQUAL`, `QUESTION_QUESTION`, `QUESTION_DOT`, and `QUESTION` token declarations to `Source/Mist/Grammar/Mist.storm`
    - Declare tokens in priority order: `??=` before `??` before `?.` before `?`
    - Place them in the "Operators and Punctuation" section before existing single-character tokens
    - _Requirements: 3.1, 4.1, 5.1, 6.1, 6.2, 6.3, 8.1, 8.2, 8.3_

  - [x] 1.2 Update `Source/Mist/Lexing/MistLexer.cs` to map the new Storm tokens to the Mist token kind enum
    - Add token kind entries for `QuestionQuestionEqual`, `QuestionQuestion`, `QuestionDot`, and `Question`
    - Ensure the lexer mapping table includes the new token kinds
    - _Requirements: 3.1, 4.1, 5.1_

  - [x] 1.3 Write property test for lexer token priority (Property 1 prerequisite)
    - **Property: Token maximal-munch priority**
    - Verify that `??=` lexes as one token (not `??` + `=`), `??` lexes as one token (not `?` + `?`), and `?.` lexes as one token (not `?` + `.`)
    - Place in `Test/Mist.Tests/Lexing/`
    - Use in-process lexer — no process spawning
    - **Validates: Requirements 8.4**

- [x] 2. Grammar: Add optional type suffix and operator rules
  - [x] 2.1 Add `optional:QUESTION?` field to `PrimitiveTypeRef`, `NamedTypeRef`, `PrimitiveSingleTypeRef`, and `NamedSingleTypeRef` alternatives in `Source/Mist/Grammar/Mist.storm`
    - The `optional` field appears after `arrayRank` to ensure `int[]?` parses as `(int[]) | void`
    - _Requirements: 1.1, 1.2, 1.4, 1.6_

  - [x] 2.2 Add `OptionalMemberAccessSuffix` and `OptionalMethodCallSuffix` alternatives to the `PostfixSuffix` rule
    - Declare `QUESTION_DOT` alternatives before `DOT` alternatives for correct priority
    - `OptionalMethodCallSuffix`: `QUESTION_DOT name:IDENTIFIER typeArgs:TypeArgumentList? LEFT_PARENTHESIS arguments:ArgumentList? RIGHT_PARENTHESIS`
    - `OptionalMemberAccessSuffix`: `QUESTION_DOT name:IDENTIFIER`
    - _Requirements: 3.5, 3.7, 3.8, 6.3_

  - [x] 2.3 Add `VoidCoalescing` precedence level and `VoidCoalescingAssignment` to the `Assignment` rule
    - Insert `VoidCoalescing` rule between `Assignment` and `LogicalOr`: `rule VoidCoalescing = left:LogicalOr (QUESTION_QUESTION right:LogicalOr)*;`
    - Modify `Assignment` to reference `VoidCoalescing` and add `VoidCoalescingAssignment` alternative: `target:VoidCoalescing QUESTION_QUESTION_EQUAL value:Assignment`
    - Right-associativity for `??` is achieved by the right operand recursing to `LogicalOr`
    - _Requirements: 4.6, 5.1, 6.1, 6.2, 6.4, 6.5, 6.6_

  - [x] 2.4 Write property tests for optional type suffix parsing (Properties 1, 3, 4, 5)
    - **Property 1: Optional suffix parsing equivalence** — For any valid type name T, `T?` parses to a type reference with the `optional` field set
    - **Property 3: Optional suffix in all type positions** — For any type position, `T?` parses without errors
    - **Property 4: Precedence — optional suffix binds tighter than union, looser than array** — `T[]?` has arrayRank set and optional set; `T? | U` parses as UnionTypeRef
    - **Property 5: Double-optional is an error** — `T??` produces a diagnostic error
    - Place in `Test/Mist.Tests/Parsing/OptionalTypeSuffixPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 1.1, 1.2, 1.4, 1.6, 1.7**

  - [x] 2.5 Write property tests for operator parsing (Properties 10, 13, 17)
    - **Property 10: Optional chaining parses at arbitrary depth** — For chain length N (2–10), N chained `?.` accesses parse without errors
    - **Property 13: Void-coalescing right-associativity** — `a ?? b ?? c` produces right-associative AST
    - **Property 17: Precedence — ?? between LogicalOr and Assignment** — `a || b ?? c` parses as `(a || b) ?? c`
    - Place in `Test/Mist.Tests/Parsing/OptionalOperatorParsingPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.5, 4.6, 6.1, 6.4, 6.6**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Semantic model: Resolve `T?` to `UnionType([T, void])`
  - [x] 4.1 Update the semantic model builder to handle the `optional:QUESTION?` field on type reference nodes
    - When `optional` is present: resolve base type T, then create `UnionType([T, VoidType])` with canonical ordering
    - If T already contains void (union flattening), return T unchanged
    - If T is void itself, flag for diagnostic warning (`MIST_TC_0046 (historical)`)
    - If T is already optional (double-`?`), flag for diagnostic error (`MIST_TC_0045 (historical)`)
    - _Requirements: 1.1, 1.3, 1.5, 1.7_

  - [x] 4.2 Write property test for void deduplication (Property 2)
    - **Property 2: Void deduplication in optional types**
    - For any union type U containing void, applying `?` produces the same constituent set
    - Place in `Test/Mist.Tests/Semantics/OptionalTypeDeduplicationPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 1.3**

- [x] 5. Standard library: Replace `Optional<T>` class with type alias
  - [x] 5.1 Replace the class-based `Optional<T>` in `Source/Standard/Optional.mist` with `type Optional<T> = T | void;`
    - Remove the existing class definition (HasValue, Value, etc.)
    - The type alias is transparent — the type checker sees through it to `UnionType([T, VoidType])`
    - _Requirements: 2.1, 2.2, 2.3, 2.5_

  - [x] 5.2 Write property tests for Optional<T> equivalence and member rejection (Properties 6, 7)
    - **Property 6: Optional<T>, T?, and T|void structural equivalence** — Cross-assignment between all three forms produces no errors
    - **Property 7: No member access on Optional<T> alias** — Accessing `.HasValue` or `.Value` on `Optional<T>` produces a diagnostic error
    - Place in `Test/Mist.Tests/Semantics/OptionalTypeAliasPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 2.2, 2.3, 2.4**

- [x] 6. Type checker: Validate `?.`, `??`, and `??=` operators
  - [x] 6.1 Implement type checking for `OptionalMemberAccessSuffix` and `OptionalMethodCallSuffix`
    - Verify receiver type is a union containing void; emit `MIST_TC_0040 (historical)` if not
    - Compute projected type T by removing void from union constituents
    - Resolve member/method on T; result type is `R | void` (flattened if R already contains void)
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 6.2 Implement type checking for `VoidCoalescing` expressions
    - Verify left operand type contains void; emit `MIST_TC_0041 (historical)` if not
    - Compute non-void portion T; verify right operand is assignable to T; emit `MIST_TC_0044 (historical)` if not
    - Result type is T (void removed)
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 6.3 Implement type checking for `VoidCoalescingAssignment` with flow narrowing
    - Verify left operand is a mutable variable; emit `MIST_TC_0043 (historical)` if not
    - Verify left operand type contains void; emit `MIST_TC_0042 (historical)` if not
    - Verify right operand assignable to non-void portion T; emit `MIST_TC_0044 (historical)` if not
    - Narrow variable type to T for all subsequent statements in same scope via `MethodFlowState`
    - Verify `??=` is used as a statement, not a sub-expression; emit `MIST_TC_0047 (historical)` if misused
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_

  - [x] 6.4 Write property tests for `?.` type checking (Properties 8, 9)
    - **Property 8: Optional chaining projects receiver and wraps result type** — `?.` on `T | void` calling a method returning R infers `R | void`
    - **Property 9: Optional chaining requires optional type** — `?.` on a non-optional type produces `MIST_TC_0040 (historical)`
    - Place in `Test/Mist.Tests/Analysis/OptionalChainingPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 3.1, 3.3, 3.4, 3.7, 3.8**

  - [x] 6.5 Write property tests for `??` type checking (Properties 11, 12)
    - **Property 11: Void-coalescing requires optional left operand** — `??` on non-optional type produces `MIST_TC_0041 (historical)`
    - **Property 12: Void-coalescing result type inference** — `T | void ?? T` infers result type T
    - Place in `Test/Mist.Tests/Analysis/VoidCoalescingPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 4.1, 4.2, 4.4**

  - [x] 6.6 Write property tests for `??=` type checking (Properties 14, 15, 16)
    - **Property 14: Void-coalescing assignment requires mutable optional variable** — `??=` on readonly/non-variable/non-optional produces errors
    - **Property 15: Void-coalescing assignment flow narrowing** — After `x ??= value`, x has type T (not T | void)
    - **Property 16: Void-coalescing assignment is statement-only** — Using `??=` as sub-expression produces `MIST_TC_0047 (historical)`
    - Place in `Test/Mist.Tests/Analysis/VoidCoalescingAssignmentPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 5.1, 5.4, 5.6, 5.7, 5.8**

- [x] 7. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. C# emitter: Generate code for optional operators
  - [x] 8.1 Implement emission for `?.` (optional chaining)
    - Generate ternary: `(receiver.IsVoid ? (Union_R_Void)MistVoid.Instance : (Union_R_Void)receiver.AsT.Member)`
    - For complex receiver expressions, introduce a temporary variable to avoid double-evaluation
    - For chained `?.`, generate nested ternaries that short-circuit the entire chain
    - _Requirements: 7.2, 7.6, 3.6_

  - [x] 8.2 Implement emission for `??` (void-coalescing)
    - Generate ternary: `(left.IsVoid ? right : left.AsT)`
    - Short-circuit semantics are naturally achieved by C# ternary evaluation
    - _Requirements: 7.3, 7.5, 4.5_

  - [x] 8.3 Implement emission for `??=` (void-coalescing assignment)
    - Generate: `if (x.IsVoid) { x = value; }`
    - After this point, emit subsequent references to x using the narrowed type T
    - _Requirements: 7.4, 5.2, 5.3_

  - [x] 8.4 Write property test for T? emission equivalence (Property 18)
    - **Property 18: T? emits identically to T|void**
    - For any type T, the emitted C# for a program using `T?` is character-for-character identical to the same program using `T | void`
    - Place in `Test/Mist.Tests/Backend/OptionalTypeEmissionPropertyTests.cs`
    - Use in-process compilation via shared `CompilerDriver`
    - **Validates: Requirements 7.1**

- [x] 9. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. TextMate grammar: Add operator highlighting
  - [x] 10.1 Add patterns for `??=`, `??`, and `?.` to `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add in order: `??=` first, then `??`, then `?.` (longest match first)
    - Scopes: `keyword.operator.void-coalescing-assignment.mist`, `keyword.operator.void-coalescing.mist`, `keyword.operator.void-conditional.mist`
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 11. Sample application
  - [x] 11.1 Create `samples/OptionalTypes/Program.mist` using the proof code from the requirements verbatim
    - Include the `Inner` class, `Outer` class, and `Program` class with `RunDemo()` exactly as specified
    - Use C#-style (Allman) braces throughout
    - The sample must exercise: `T?` on primitives/named types, `Optional<T>` equivalence, `?.` for method calls and chaining, `??` for fallback and chaining, `??=` for assignment and flow narrowing, combined `?.` with `??`
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [x] 11.2 Create `samples/OptionalTypes/mist.toml` project file
    - Configure the project with appropriate name and references
    - _Requirements: 10.1_

  - [x] 11.3 Register the sample in `samples/AllFeatures/Program.mist` and `samples/AllFeatures/mist.toml`
    - Add `OptionalTypes.Program.RunDemo()` call to AllFeatures
    - Add project reference to OptionalTypes in AllFeatures mist.toml
    - _Requirements: 10.4_

- [x] 12. Book chapter
  - [x] 12.1 Create snippet files in `book/snippets/type-system/optional-types/`
    - Create `.mist` + empty `.cs` pairs for: `optional-suffix.mist`, `optional-alias.mist`, `optional-chaining.mist`, `void-coalescing.mist`, `void-coalescing-assignment.mist`
    - Each snippet is a complete, compilable Mist program demonstrating one concept
    - _Requirements: 9.4, 9.6_

  - [x] 12.2 Run SnippetEmitter to generate `.cs` files for all snippets
    - Execute `dotnet run --project tools/SnippetEmitter` to populate the `.cs` files
    - Verify all snippets compile successfully
    - _Requirements: 9.4_

  - [x] 12.3 Create the book chapter at `book/src/type-system/optional-types.md`
    - Cover: `T?` suffix syntax, `Optional<T>` type alias, `?.` optional chaining, `??` void-coalescing, `??=` void-coalescing assignment with flow narrowing
    - Use the tabbed `{{#include}}` pattern for all code examples (Mist tab + Emitted C# tab)
    - _Requirements: 9.1, 9.3, 9.5_

  - [x] 12.4 Register the chapter in `book/src/SUMMARY.md` under the Type System section (after "Type Unions")
    - _Requirements: 9.2_

- [x] 13. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Rewrite C# emission: Use Nullable/null instead of Union helper structs for `T | void`
  - [x] 14.1 Update type emission to detect `T | void` and emit `T?` (value types) or `T` (reference types)
    - In `CSharpEmitter.GetTypeName` / type reference emission, when the type is a `UnionType` with exactly two constituents where one is void: emit `Nullable<T>` for value types, plain `T` for reference types
    - Skip `Union_T_Void` helper struct generation for these two-constituent void unions
    - Multi-constituent unions containing void (e.g., `int | string | void`) continue using the existing union helper approach
    - _Requirements: 7.1, 7.8_

  - [x] 14.2 Update void literal emission to generate `null` for optional type contexts
    - When `Void.Void` or a void-typed expression is assigned to a `T | void` variable, emit `null` instead of `MistVoid.Instance`
    - _Requirements: 7.7_

  - [x] 14.3 Rewrite `?.` emission to use C#'s native null-conditional operator
    - Replace the ternary-based `(receiver.IsVoid ? ... : ...)` pattern with direct `receiver?.Member` / `receiver?.Method(args)` emission
    - Remove the `EnsureSimpleReceiverForOptionalChain` temp variable logic (C#'s `?.` handles this natively)
    - _Requirements: 7.2, 7.6_

  - [x] 14.4 Rewrite `??` emission to use C#'s native null-coalescing operator
    - Replace the ternary-based `(left.IsVoid ? right : left.AsT)` pattern with direct `left ?? right` emission
    - _Requirements: 7.3, 7.5_

  - [x] 14.5 Rewrite `??=` emission to use C#'s native null-coalescing assignment
    - Replace the `if (x.IsVoid) { x = value; }` pattern with direct `x ??= value;` emission
    - _Requirements: 7.4_

  - [x] 14.6 Update Property 18 test to verify Nullable/null emission
    - Rewrite `OptionalTypeEmissionPropertyTests` to verify: value types emit as `T?`, reference types emit as plain `T`, void literals emit as `null`, operators emit as native C# `?.`/`??`/`??=`
    - _Requirements: 7.1_

  - [x] 14.7 Fix existing tests broken by the emission strategy change
    - Update `UnionEmitterPropertyTests` expectations for property counts (void no longer generates union helper properties for two-constituent unions)
    - Update any integration tests that assert on emitted C# output containing `Union_T_Void` patterns
    - Verify all 42 previously-failing tests now pass
    - _Requirements: 7.1_

- [x] 15. Final verification checkpoint
  - Run full test suite, ensure all tests pass including the previously-failing 42 tests

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- All property tests use in-process compilation via the shared `CompilerDriver` — zero per-iteration process spawns
- The sample app is verified through the existing AllFeatures integration test (no separate process-spawning test needed)
- The SnippetEmitter run in task 12.2 is a one-time tool invocation, not a per-iteration test

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "2.1", "2.2", "2.3"] },
    { "id": 2, "tasks": ["1.3", "2.4", "2.5"] },
    { "id": 3, "tasks": ["4.1", "5.1"] },
    { "id": 4, "tasks": ["4.2", "5.2", "6.1", "6.2", "6.3"] },
    { "id": 5, "tasks": ["6.4", "6.5", "6.6"] },
    { "id": 6, "tasks": ["8.1", "8.2", "8.3"] },
    { "id": 7, "tasks": ["8.4", "10.1"] },
    { "id": 8, "tasks": ["11.1", "11.2", "12.1"] },
    { "id": 9, "tasks": ["11.3", "12.2"] },
    { "id": 10, "tasks": ["12.3", "12.4"] },
    { "id": 11, "tasks": ["14.1", "14.2"] },
    { "id": 12, "tasks": ["14.3", "14.4", "14.5"] },
    { "id": 13, "tasks": ["14.6", "14.7"] }
  ]
}
```
