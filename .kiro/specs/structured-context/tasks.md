# Implementation Plan: Structured Context

## Overview

This plan implements Racket-style `parameterize` semantics for the Mist language, spanning grammar additions, semantic model extensions, a new context checker analyzer, C# emitter extensions, pretty printer support, TextMate grammar updates, and a sample application. Tasks are ordered so that each step builds on the previous, with no orphaned code.

## Tasks

- [x] 1. Grammar additions and parser generation
  - [x] 1.1 Add context declaration and block statement rules to Mist.storm
    - Add `WithVarDeclarationStatement`, `WithTypedDeclarationStatement`, `WithDiscardDeclarationStatement`, `WithPushStatement`, and `WithBlockStatement` rules to the `Statement` alternative list
    - Add the `WithClause` rule (`WITH TypeReference IDENTIFIER`)
    - Add `withClauses:WithClause*` to `MethodDeclaration`, `ConstructorDeclaration`, `InterfaceMethodDeclaration`, and `TraitMethodDeclaration` rules (after parameter list, before constraints/body)
    - Ensure disambiguation: context statements use `WITH VAR`, `WITH TypeRef IDENTIFIER EQUAL`, `WITH TypeRef IDENTIFIER(text="_") EQUAL`, `WITH LEFT_PARENTHESIS`, or `WITH Expression SEMICOLON` inside method bodies; existing `WithDeclaration` remains at top level with `WITH IDENTIFIER COLON`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5, 13.1, 13.3_

  - [x] 1.2 Verify parser generation and write parse-level smoke tests
    - Rebuild the Storm-generated parser from the updated grammar
    - Write unit tests that parse each new statement variant and verify the correct AST node types are produced
    - Verify that existing trait-with blocks (`with TypeName : TraitName { ... }`) continue to parse correctly
    - _Requirements: 1.5, 1.6, 1.7, 1.8, 2.3, 3.3, 13.1, 13.3_

- [x] 2. Checkpoint - Ensure grammar compiles and parse tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Semantic model builder extensions
  - [x] 3.1 Add ContextClauseInfo data model and extend method symbols
    - Create `ContextClauseInfo` class (ContextType, Identifier, Span) in `Source/Mist/Semantics/Model/`
    - Add a `WithClauses` property (list of `ContextClauseInfo`) to the method symbol representation
    - Ensure the property is populated for methods, constructors, interface methods, and trait methods
    - _Requirements: 3.1, 3.2, 3.5, 7.2_

  - [x] 3.2 Implement Pass 1 with-clause extraction
    - When visiting method-like declarations in Pass 1, extract `WithClause` AST nodes and store resolved type + identifier on the method symbol's `WithClauses` property
    - Handle type resolution for with-clause type references (reuse existing type resolution infrastructure)
    - _Requirements: 3.1, 3.2, 3.5, 7.3_

  - [x] 3.3 Implement Pass 2 context declaration statement processing
    - For `WithVarDeclarationStatement`: infer type from initializer, register `SymbolKind.LocalVariable`
    - For `WithTypedDeclarationStatement`: resolve type reference, register `SymbolKind.LocalVariable`
    - For `WithDiscardDeclarationStatement`: resolve type reference, no local variable registration
    - For `WithPushStatement`: infer type from expression, no local variable registration
    - For `WithBlockStatement`: infer type from expression, process block body
    - For method `WithClause`: register a `SymbolKind.LocalVariable` in the method body scope
    - Apply context type keying rules: aliases resolve through, unions distinct, generics with different args distinct
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 4.7, 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8, 6.1, 7.1, 7.2_

  - [x] 3.4 Write property test for context type keying exactness
    - **Property 3: Context type keying exactness**
    - Generate programs using type aliases, generic types with varying type arguments, union types, and refinement types as context keys
    - Compile in-process using shared `CompilerDriver` instance
    - Assert the checker correctly identifies matches and mismatches based on resolved types
    - Standard-pipeline evidence (2026-10-03): `StructuredContextDecisionTests` exercises optional-reference identity through nested tuple/callable types and normalized aliases/refinements; context-key spelling tests use hand-built types. `StructuredContextKeyEmissionTests` covers direct key spelling and lowering. Runtime evidence is in `StructuredContextRuntimeBoundaryTests` and the existing StructuredContext sample, rather than regenerating source programs for each decision.
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 5.8**

- [x] 4. Checkpoint - Ensure semantic model builds correctly
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Context checker analyzer
  - [x] 5.1 Create ContextCheckerAnalyzer skeleton and register it
    - Create `ContextCheckerAnalyzer` implementing `IAnalyzer` in `Source/Mist/Analysis/Builtin/`
    - Define diagnostic IDs: `MIST_CTX_0001` through `MIST_CTX_0006`
    - Register the analyzer in the `CompilerDriver` pipeline (after `TypeCheckerAnalyzer`)
    - _Requirements: 9.1, 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x] 5.2 Implement context availability checking (call-graph walk)
    - Collect all method symbols with `WithClauses`
    - For each call site invoking such a method, walk up enclosing scopes and call chain looking for a context declaration whose resolved type matches
    - A context declaration satisfies the requirement if it appears before the call site in the same block, or in any enclosing block/method that transitively calls the current method
    - Handle cycles with a visited set per walk path
    - Emit `MIST_CTX_0001` when no provider is found
    - Treat methods without context declarations/bindings as transparent
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.8, 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 5.3 Implement interface/trait contract enforcement
    - For each interface method with `with` clauses: verify implementing methods declare the exact same clauses (matched by resolved type, no variance)
    - Emit `MIST_CTX_0006` for omitted or mismatched clauses
    - For trait methods with `with` clauses: during structural matching, only consider a type's method a match if it also declares the same `with` clauses
    - When dispatching through interface/trait type, use the interface/trait method's `with` clauses for context checking
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [x] 5.4 Implement remaining context checker diagnostics
    - `MIST_CTX_0002`: duplicate context binding (same type in multiple with-clauses on one method)
    - `MIST_CTX_0003`: context declaration outside method body
    - `MIST_CTX_0004`: cannot infer context type from expression
    - `MIST_CTX_0005`: assignment to context-bound variable
    - Verify constructor with-clauses are checked at `new` expression call sites
    - _Requirements: 3.6, 7.5, 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x] 5.5 Write property test for context checker soundness and completeness
    - **Property 2: Context checker soundness and completeness**
    - Generate random call graphs with varying context declaration placements
    - Compile in-process using shared `CompilerDriver` instance
    - Assert diagnostics are emitted if and only if a context requirement is unsatisfied
    - Include interface/trait dispatch scenarios
    - **Validates: Requirements 1.9, 2.4, 3.4, 3.6, 3.7, 8.6, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 13.2, 13.3**

  - [x] 5.6 Write property test for interface and trait contract enforcement
    - **Property 5: Interface and trait with-clause contract enforcement**
    - Generate programs with interfaces/traits declaring with-clauses and implementing types with varying clause correctness
    - Compile in-process using shared `CompilerDriver` instance
    - Assert diagnostics emitted if and only if implementing method omits or mismatches a required with-clause
    - For traits, assert structural matching fails when with-clauses differ
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

- [x] 6. Checkpoint - Ensure context checker passes all tests
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. C# emitter extensions
  - [x] 7.1 Implement MistContext.g.cs conditional generation
    - Generate `MistContext.g.cs` (static `Dictionary<Type, Stack<object>>` with Push/Pop/Peek) when any compilation unit uses structured context
    - Add detection logic: scan semantic model for any context declarations or with-clauses
    - Include the file in the emitted output dictionary
    - _Requirements: 12.1, 12.9_

  - [x] 7.2 Implement context declaration statement emission
    - `with var x = expr;` → `var x = expr; MistContext.Push<T>(x); try { ... } finally { MistContext.Pop<T>(); }`
    - `with TypeReference x = expr;` → same pattern with explicit type
    - `with TypeReference _ = expr;` → `MistContext.Push<T>(expr); try { ... } finally { MistContext.Pop<T>(); }`
    - `with expr;` → `MistContext.Push<InferredType>(expr); try { ... } finally { MistContext.Pop<InferredType>(); }`
    - Multiple declarations in same scope nest try/finally blocks (reverse pop order)
    - _Requirements: 12.2, 12.3, 12.4, 12.5, 12.6_

  - [x] 7.3 Implement context block and method with-clause emission
    - `with(expr) { ... }` → `MistContext.Push<T>(expr); try { ... } finally { MistContext.Pop<T>(); }`
    - Method `with TypeReference identifier` binds the value from `MistContext.Peek<Key>()` at method entry, casting to the ordinary emitted value type. `Key` normalizes aliases/refinements and preserves fully known optional-reference identity with `MistContext.Optional<T>`, recursively through supported nested tuple/callable constructions; optional-value keys retain CLR `Nullable<T>` identity, and pure function keys retain their marker.
    - Standard-pipeline entry evidence (2026-10-03): extension methods, abstract default bodies and concrete restated defaults all use the same context-aware entry contract as ordinary methods, including unused clauses. `ExtensionCarrierEmissionTests`, `RestatedDefaultEntryEmissionTests` and `StructuredContextRefusalTests` cover the supported entry routes and missing-context controls; the existing sample runs both default views.
    - _Requirements: 12.7, 12.8_

  - [x] 7.4 Write property test for runtime context propagation and scoping
    - **Property 4: Runtime context propagation and scoping**
    - Generate programs with context declarations at various call-graph depths, shadowing, and restoration
    - Use batched approach: generate N scenarios, build single program printing N labeled results, compile once, run once, compare output line-by-line
    - Assert consumers receive expected values, shadowing works, restoration works, different types don't interfere
    - Standard-pipeline evidence (2026-10-03): key-only optional-reference markers leave ordinary value emission unchanged and optional-value keys retain CLR `Nullable<T>` identity; runtime coverage checks independent required/optional stacks, closed generics, normalized aliases/refinements, nested keys, and restoration/removal after exceptions. The existing sample's sections 11, 17 and 18 expose generic/optional/nested identity and extension/default entry in the sole AllFeaturesV2 baseline.
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.8, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 7.1, 7.4, 10.1, 10.2, 10.3, 10.4, 10.5, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**

- [x] 8. Checkpoint - Ensure emitter produces correct C# output
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Pretty printer support
  - [x] 9.1 Implement pretty printer Emit methods for context constructs
    - `EmitWithVarDeclarationStatement` — formats `with var {name} = {expr};`
    - `EmitWithTypedDeclarationStatement` — formats `with {type} {name} = {expr};`
    - `EmitWithDiscardDeclarationStatement` — formats `with {type} _ = {expr};`
    - `EmitWithPushStatement` — formats `with {expr};`
    - `EmitWithBlockStatement` — formats `with ({expr}) { ... }`
    - Method declarations emit `with {type} {name}` clauses after the parameter list (for methods, constructors, interface methods, trait methods)
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [x] 9.2 Write property test for pretty-printer round-trip idempotence
    - **Property 1: Pretty-printer round-trip idempotence**
    - Generate random valid Mist source programs containing all context construct variants
    - Include with-clauses on constructors, interface methods, and trait methods
    - Parse, print, parse again, print again; assert the two printed outputs are identical
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.5, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 13.1**

- [x] 10. TextMate grammar updates
  - [x] 10.1 Update mist.tmLanguage.json for structured context syntax
    - Add patterns for context declaration statements: highlight `with` as `keyword.other.mist`, type references with appropriate type scope, identifiers as `variable.other.mist`
    - Add pattern for context block: `with` + parenthesized expression
    - Add pattern for method `with` clauses: type as type scope, identifier as `variable.parameter.mist`
    - Add pattern for discard `_` as `variable.language.mist`
    - Verify existing `with` keyword scope (`keyword.other.mist`) is preserved for trait-with blocks
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7_

- [x] 11. Sample application
  - [x] 11.1 Create samples/StructuredContext/Program.mist
    - Demonstrate all context declaration variants: `with var x = expr`, `with Type x = expr`, `with Type _ = expr`, `with expr`, `with(expr) { ... }`
    - Demonstrate context propagation through transparent intermediate methods
    - Demonstrate shadowing and restoration
    - Demonstrate multiple with-clauses on a single method
    - Demonstrate explicit type annotation for variance control
    - Demonstrate multiple same-type pushes in a single scope
    - Demonstrate generic types as independent context keys
    - Demonstrate constructor with-clause
    - Demonstrate class, struct, trait-satisfying, and interface-implementing types as context values
    - Demonstrate type alias, union type, refinement type, and dependent type as context keys
    - Demonstrate interface method dispatch with with-clause
    - Demonstrate trait structural dispatch with with-clause
    - Each permutation prints labeled output to stdout
    - Standard-pipeline extension (2026-10-03): section 11 reads actual `Holder<int>` and `Holder<string>` contexts; section 17 reads required and optional-reference/value contexts plus nested tuple/callable keys, showing independent shadowing and restoration; section 18 calls a context-bearing extension and a default body through abstract and concrete views.
    - Include `public static void RunDemo()` entry point
    - _Requirements: 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.7, 16.8, 16.9, 16.10, 16.11, 16.12, 16.13, 16.14, 16.15, 16.16, 16.17, 16.18, 16.19, 16.20, 16.21_

  - [x] 11.2 Integrate into AllFeatures sample
    - `samples/AllFeaturesV2/Program.mist` calls `StructuredContext.Program.RunDemo()` and `samples/__baselines__/AllFeaturesV2.out` holds its labeled output; classic AllFeatures is retired.
    - _Requirements: 16.20_

- [x] 12. Final checkpoint - Ensure all tests pass and sample compiles
  - Standard-pipeline verification (2026-10-03): 185 focused tests, 325 integrated context/default/extension/async tests (ledger `20261003-213346`) and all 9,766 full-solution tests (ledger `20261003-213511`) pass, including the sole AllFeaturesV2 baseline. Default entry retains the published async completion behavior and forwards the concrete owner. Build and formatter checks report zero warnings/errors, diff checks pass, second scoped snippet generation is unchanged, and the book builds. The broader erased generic argument limit remains explicitly open below.
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# throughout, so all implementation is in C#
- The `ContextCheckerAnalyzer` is a new `IAnalyzer` registered alongside `TypeCheckerAnalyzer`
- The `WITH` keyword token already exists in `keywords.storm`
- Property-based tests use FsCheck with shared `CompilerDriver` instance (no per-iteration process spawns)
- Runtime propagation tests (Property 4) use the batched approach: 1 compile, 1 `dotnet exec`, N output lines compared
- The `WithBlockRegistry` for trait-with blocks remains unchanged; structured context uses a separate code path

### Remaining language decisions

Aliases and refinements retain their settled underlying-key normalization. Proving a refined clause from the nearest supplied value remains unspecified. Lambdas remain judged where written; capture, escape refusal and callable requirement policies remain unspecified. Concurrent-flow storage and async signatures/adaptation are outside this closeout.

A separate substantial runtime limit remains: `static T Get<T>() with T value`, called as `Get<string?>()` under optional-string context, peeks the erased CLR string key. Fully known optional/nested key syntax encodes correctly, and optional-value keys retain CLR `Nullable<T>` identity. Preserving optional-reference arguments erased behind generic keys needs key metadata through calls/receivers or broader specialization while preserving public APIs and ordinary value representation; this is outside packet 10's three established runtime sites and is tracked in analysis order 16 and emitter order 176.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3"] },
    { "id": 4, "tasks": ["3.4", "5.1"] },
    { "id": 5, "tasks": ["5.2", "5.3"] },
    { "id": 6, "tasks": ["5.4", "5.5", "5.6"] },
    { "id": 7, "tasks": ["7.1", "9.1", "10.1"] },
    { "id": 8, "tasks": ["7.2"] },
    { "id": 9, "tasks": ["7.3"] },
    { "id": 10, "tasks": ["7.4", "9.2"] },
    { "id": 11, "tasks": ["11.1"] },
    { "id": 12, "tasks": ["11.2"] }
  ]
}
```
