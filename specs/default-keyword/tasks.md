# Implementation Plan: `default` Keyword

> **Superseded** by [`defaultability`](../defaultability/requirements.md) (2026-09-26), which states the rules as they now stand and governs wherever the two differ. This document is kept so references to it by requirement number still resolve; the new spec's appendix maps each requirement here to where it now lives.

## Overview

This plan implements the `default` keyword feature across the Mist compiler pipeline. Tasks follow the pipeline order: grammar → pretty-printer → semantic model → type checker → emitter → diagnostics → tooling. Each task builds on previous tasks, and property-based tests are placed close to the implementation they validate. All tests use in-process compilation via `CompilerDriver` with a shared driver instance — no per-iteration process spawning.

## Tasks

- [x] 1. Grammar and keyword changes
  - [x] 1.1 Add `DEFAULT` keyword token to `Source/Mist/Grammar/keywords.storm`
    - Add `token DEFAULT = "default";` after the existing cast operator keywords section
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Add `DefaultBlockDeclaration`, `DefaultFieldInitializer`, and `DefaultNestedOverride` rules to `Source/Mist/Grammar/Mist.storm`
    - Add `rule DefaultNestedOverride = fieldName:IDENTIFIER EQUAL value:Expression;`
    - Add `rule DefaultFieldInitializer` with two variants: `DefaultFieldOverride` (`fieldName:IDENTIFIER EQUAL value:Expression`) and `DefaultTypeBinding` (`fieldName:IDENTIFIER COLON typeName:TypeReference` with optional `LEFT_BRACE nestedEntries:DefaultNestedOverride* RIGHT_BRACE`)
    - Add `rule DefaultBlockDeclaration = DEFAULT LEFT_BRACE entries:DefaultFieldInitializer* RIGHT_BRACE;`
    - Add `| defaultBlock:DefaultBlockDeclaration as DefaultBlockMember` as a new variant in the `MemberDeclaration` rule — place it before the `field:FieldDeclaration as FieldMember` variant so the parser prefers the `default {` match over treating `default` as a type reference in a field declaration
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 1.3 Add `DefaultExpression` and `DefaultLiteralExpression` variants to the `Primary` rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| DEFAULT LEFT_PARENTHESIS type:TypeReference RIGHT_PARENTHESIS as DefaultExpression` before the existing `IdentifierExpression` variant
    - Add `| DEFAULT as DefaultLiteralExpression` after `DefaultExpression` but before `IdentifierExpression`
    - `DefaultExpression` must precede `DefaultLiteralExpression` so ordered-choice prefers the longer match when `(` follows `default`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 1.4 Build the solution and verify Storm generates the new AST node types without errors or warnings
    - Run `dotnet build Source/Mist/Mist.csproj` and confirm zero errors and zero warnings
    - The Storm source generator will auto-generate: `DefaultBlockDeclarationNode`, `DefaultFieldOverrideDefaultFieldInitializerNode`, `DefaultTypeBindingDefaultFieldInitializerNode`, `DefaultNestedOverrideNode`, `DefaultBlockMemberMemberDeclarationNode`, `DefaultExpressionPrimaryNode`, `DefaultLiteralExpressionPrimaryNode`
    - _Requirements: 2.4, 2.5, 2.6, 2.7_

- [x] 2. Checkpoint — Grammar compiles cleanly
  - Ensure the solution builds with zero errors and zero warnings. Ask the user if questions arise.

- [x] 3. Pretty-printer support
  - [x] 3.1 Add default block, default expression, and default literal emission to `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - In `EmitMemberDeclaration`, add a case for `DefaultBlockMemberMemberDeclarationNode` that calls a new `EmitDefaultBlockDeclaration` method
    - `EmitDefaultBlockDeclaration`: empty block → `default { }` on one line; non-empty block → `default {` + newline, each entry indented on its own line, closing `}`
    - For `DefaultFieldOverrideDefaultFieldInitializerNode` entries → `fieldName = <expression>`
    - For `DefaultTypeBindingDefaultFieldInitializerNode` entries → `fieldName: <TypeReference>` optionally followed by `{ field = value, ... }` if nested overrides exist
    - In the expression emission path (wherever `PrimaryNode` variants are handled), add cases for `DefaultExpressionPrimaryNode` → `default(<TypeReference>)` and `DefaultLiteralExpressionPrimaryNode` → `default`
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 4. Diagnostic infrastructure changes
  - [x] 4.1 Add `Info` severity to `Source/Mist/Common/Diagnostics/DiagnosticSeverity.cs`
    - Add `Info` as the first enum value (before `Warning`) so severity ordering is Info < Warning < Error
    - Verify `DiagnosticBag.HasErrors` only checks for `Error` severity (it already does)
    - Update `Source/Mist/Common/Diagnostics/DiagnosticFormatter.cs` to format `Info` diagnostics with an `info:` prefix
    - _Requirements: 11.2, 13.10_

  - [x] 4.2 Add default keyword diagnostic IDs to `Source/Mist/Common/Diagnostics/DiagnosticId.cs`
    - Add all 12 diagnostic IDs from the design: `DefaultNotSupported`, `DefaultDuplicateBlock`, `DefaultFieldNotVisible`, `DefaultNotConstant`, `DefaultUndefinedType`, `DefaultOnInterface`, `DefaultOnTrait`, `DefaultOnTypeParameter`, `DefaultCannotInferType`, `DefaultRedundantBlock`, `DefaultRequiresTypeBinding`, `DefaultBaseNotDefaultable`
    - Follow the existing naming pattern with `MIST_DEFAULT_* (historical scheme)` string values
    - _Requirements: 13.1–13.12_

- [x] 5. Semantic model changes
  - [x] 5.1 Add `DefaultabilityStatus` enum and `DefaultFieldValue` class to `Source/Mist/Semantics/Model/`
    - Create `Source/Mist/Semantics/Model/DefaultabilityStatus.cs` with enum values: `Unknown`, `Inferred`, `Explicit`, `NotDefaultable`
    - Create `Source/Mist/Semantics/Model/DefaultFieldValue.cs` with properties: `FieldName`, `FieldType`, `ValueKind`, `ConstantValue`, `ConcreteType`, `NestedOverrides`
    - Create `Source/Mist/Semantics/Model/DefaultValueKind.cs` with enum values: `ZeroInitialized`, `FieldInitializer`, `ExplicitOverride`, `TypeBinding`, `InheritedDefault`, `NestedTypeDefault`
    - _Requirements: 5.5, 6.1–6.6_

  - [x] 5.2 Add defaultability properties to `ClassType` and `StructType` in `Source/Mist/Semantics/Model/Types/`
    - Add `Defaultability` property (type `DefaultabilityStatus`, default `Unknown`) to both `ClassType` and `StructType`
    - Add `DefaultValues` property (type `IReadOnlyDictionary<string, DefaultFieldValue>?`, default `null`) to both types
    - Add internal `DefaultBlockNode` property (type `DefaultBlockDeclarationNode?`, default `null`) to both types for storing the AST node during Pass 1
    - _Requirements: 5.1, 5.2, 5.5_

  - [x] 5.3 Register default blocks in Pass 1 of `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - In `RegisterMemberDeclaration`, add a case for `DefaultBlockMemberMemberDeclarationNode`
    - Store the `DefaultBlockDeclarationNode` on the containing `ClassType` or `StructType` via the new `DefaultBlockNode` property
    - If a default block is already stored on the type, emit `MIST_DEFAULT_DUPLICATE (historical)` diagnostic
    - _Requirements: 5.2, 5.4_

  - [x] 5.4 Implement defaultability analysis pass in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Add a new pass (after Pass 2 type resolution, since field types must be resolved) that computes defaultability for all `ClassType` and `StructType` symbols
    - Build a dependency graph based on field types and process types in topological order
    - For each type: if it has an explicit default block → `Explicit`; if all fields have defaultable types → `Inferred`; otherwise → `NotDefaultable` + emit `MIST_DEFAULT_REQUIRES_BINDING (historical)` diagnostic naming the problematic field
    - Treat all primitive types and `string` as intrinsically defaultable
    - If a derived type's base class is not defaultable, mark the derived type as `NotDefaultable` and emit `MIST_DEFAULT_BASE_NOT_DEFAULTABLE (historical)`
    - Compute `DefaultValues` dictionary for defaultable types using field initializers, zero-initialization, type bindings, nested overrides, and inherited base defaults
    - Wire the new pass into the `Build` method
    - _Requirements: 5.1, 5.2, 5.3, 5.6, 6.1–6.10, 8.6, 10.1–10.4_

  - [x] 5.5 Resolve default expressions in Pass 2 of `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - In `ResolvePrimary`, add a case for `DefaultExpressionPrimaryNode`: resolve the type reference and record the expression type in the expression type map
    - Add a case for `DefaultLiteralExpressionPrimaryNode`: record as needing type inference (use a sentinel or context-based resolution)
    - _Requirements: 3.4, 3.5, 7.1, 7.8_

- [x] 6. Checkpoint — Semantic model builds cleanly
  - Ensure the solution builds with zero errors and zero warnings. Ask the user if questions arise.

- [x] 7. Type checker changes
  - [x] 7.1 Add default expression validation to `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - In `CheckPrimaryNode`, add a case for `DefaultExpressionPrimaryNode`:
      - Resolve the type reference; if unresolved → emit `MIST_DEFAULT_UNDEFINED_TYPE (historical)`
      - If `InterfaceType` → emit `MIST_DEFAULT_ON_INTERFACE (historical)`
      - If `TraitType` → emit `MIST_DEFAULT_ON_TRAIT (historical)`
      - If `TypeParameterType` → emit `MIST_DEFAULT_ON_TYPE_PARAMETER (historical)`
      - If `ClassType`/`StructType` with `NotDefaultable` → emit `MIST_DEFAULT_NOT_SUPPORTED (historical)` with explanation
      - If defaultable → valid, assign type
    - Add a case for `DefaultLiteralExpressionPrimaryNode`:
      - Look up expected type from context (assignment target, return type, parameter type)
      - If context provides a type → validate as `default(InferredType)`
      - If in a `var` declaration → emit `MIST_DEFAULT_CANNOT_INFER (historical)`
      - If no context type → emit `MIST_DEFAULT_CANNOT_INFER (historical)`
    - _Requirements: 7.1–7.10_

  - [x] 7.2 Add default block validation to `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Add a validation pass in `Analyze` that walks all type symbols with explicit default blocks
    - Validate each field override references a field that exists in the type's member scope (including inherited fields)
    - Validate visibility: base type fields must be public, not private → emit `MIST_DEFAULT_FIELD_NOT_VISIBLE (historical)` on violation
    - Validate type bindings: concrete type must be a defaultable `ClassType` or `StructType`
    - Validate compile-time constant expressions (v1: only literal expressions) → emit `MIST_DEFAULT_NOT_CONSTANT (historical)` on violation
    - Check for redundancy: if explicit default block produces same values as inferred default → emit `MIST_DEFAULT_REDUNDANT (historical)` with `Info` severity
    - _Requirements: 9.1–9.3, 6.9, 11.1, 11.2, 13.3, 13.4, 13.10_

- [x] 8. Checkpoint — Type checker builds cleanly
  - Ensure the solution builds with zero errors and zero warnings. Ask the user if questions arise.

- [x] 9. C# emitter changes
  - [x] 9.1 Emit `__CreateDefault` factory method for defaultable types in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - In `EmitClassDeclaration`, after emitting existing members, emit a `public static ClassName __CreateDefault()` method for defaultable classes
    - The factory method body creates a new instance (using a private parameterless constructor), sets each field to its computed default value, and returns the instance
    - For fields whose type is a defaultable class or struct with reference-type fields, recursively call `NestedType.__CreateDefault()` to ensure no shared references
    - For interface/trait fields with type bindings, call `ConcreteType.__CreateDefault()` and apply any nested overrides via temp variables
    - For primitive fields, emit the literal default value directly
    - In `EmitStructDeclaration`, emit the same `__CreateDefault()` pattern for defaultable structs
    - _Requirements: 12.1, 12.2, 12.4, 12.5, 12.7, 12.8, 12.9_

  - [x] 9.2 Emit private parameterless constructor for defaultable classes in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - In `EmitClassDeclaration`, if the class is defaultable and does not already have a parameterless constructor, emit `private ClassName() { }`
    - Structs do not need this — C# allows `new MyStruct()` without an explicit parameterless constructor
    - _Requirements: 12.6_

  - [x] 9.3 Emit default expressions in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - In `EmitPrimary`, add a case for `DefaultExpressionPrimaryNode`: emit `TypeName.__CreateDefault()` for class/struct types; for primitive types, emit the C# default literal directly (e.g., `default(int)`) except for `string` which emits `""`
    - Add a case for `DefaultLiteralExpressionPrimaryNode`: look up the inferred type from the semantic model's expression type map and emit the same pattern as `DefaultExpressionPrimaryNode`
    - _Requirements: 12.3_

  - [x] 9.4 Skip default block member emission in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - In `EmitMemberDeclaration`, add a case for `DefaultBlockMemberMemberDeclarationNode` that emits nothing (the default block is consumed by the semantic model, not emitted as a C# member)
    - _Requirements: 12.1_

- [x] 10. Checkpoint — Emitter builds cleanly
  - Ensure the solution builds with zero errors and zero warnings. Ask the user if questions arise.

- [x] 11. Syntax highlighting and documentation
  - [x] 11.1 Add `default` to keyword patterns in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add `default` to the `keyword.other.mist` pattern: change `\\b(new|var|this|get|set|init|field|operator|with|as)\\b` to `\\b(new|var|this|get|set|init|field|operator|with|as|default)\\b`
    - _Requirements: 14.1, 14.2_

  - [x] 11.2 Update `CSHARP_FEATURES.md` feature tracking
    - In the `Expressions — Literals` table, change `default` / `default(T)` from 🔲 to ✅ with a note: "Mist divergence: value-based defaults, inferred defaults, type bindings, bare `default` literal"
    - Update the Summary table row for `Expressions — Literals` to reflect the new implementation count
    - _Requirements: 15.1, 15.2_

- [x] 12. Property-based tests and unit tests
  - [x] 12.1 Write property test for pretty-printer round-trip (Property 1)
    - Create `Test/Mist.Tests/Parsing/DefaultPrettyPrinterPropertyTests.cs`
    - **Property 1: Pretty-printer round-trip for default constructs**
    - **Validates: Requirements 4.5**
    - Use FsCheck to generate random Mist source fragments containing default blocks (0–5 entries, mix of field overrides and type bindings), `default(TypeName)` expressions, and bare `default` expressions
    - Parse → pretty-print → re-parse → pretty-print and assert the two printed outputs are identical
    - Use in-process parsing via `MistParser` and `MistPrettyPrinter` — no process spawning needed

  - [x] 12.2 Write property test for default block parsing structure (Property 2)
    - Create `Test/Mist.Tests/Parsing/DefaultBlockParsingPropertyTests.cs`
    - **Property 2: Default block parsing preserves structure**
    - **Validates: Requirements 2.1, 2.2, 2.4, 2.5**
    - Use FsCheck to generate class/struct declarations with default blocks containing N entries (0–5)
    - Parse and verify the AST node's default block contains exactly N entries with correct field names and entry kinds

  - [x] 12.3 Write property test for default expression parsing (Property 3)
    - Add to `Test/Mist.Tests/Parsing/DefaultBlockParsingPropertyTests.cs`
    - **Property 3: Default expression parsing captures type reference**
    - **Validates: Requirements 3.1, 3.4**
    - Use FsCheck to generate valid type names, parse `default(TypeName)`, verify the AST node is `DefaultExpressionPrimaryNode` with the correct type reference

  - [x] 12.4 Write property tests for defaultability classification (Properties 4, 5)
    - Create `Test/Mist.Tests/Semantics/DefaultabilityPropertyTests.cs`
    - **Property 4: Defaultability classification correctness**
    - **Validates: Requirements 5.1, 5.2**
    - **Property 5: Non-defaultable type detection**
    - **Validates: Requirements 5.3, 13.11**
    - Use FsCheck to generate type declarations with varying field types (all-defaultable, some interface fields, with/without default blocks)
    - Compile in-process via shared `CompilerDriver` and inspect the semantic model's defaultability status
    - Verify correct classification and diagnostic emission for non-defaultable types

  - [x] 12.5 Write property tests for default value computation (Properties 6, 7, 8)
    - Create `Test/Mist.Tests/Semantics/DefaultValueComputationPropertyTests.cs`
    - **Property 6: Default value computation from field initializers and zero-initialization**
    - **Validates: Requirements 6.1, 6.2**
    - **Property 7: Explicit override replaces inferred default**
    - **Validates: Requirements 6.4**
    - **Property 8: Type binding resolution**
    - **Validates: Requirements 6.5, 6.6**
    - Use FsCheck to generate types with varying field initializers, zero-init fields, explicit overrides, and type bindings
    - Compile in-process and inspect computed `DefaultValues` on the semantic model types

  - [x] 12.6 Write property test for inheritance of default values (Property 9)
    - Add to `Test/Mist.Tests/Semantics/DefaultValueComputationPropertyTests.cs`
    - **Property 9: Inheritance of default values**
    - **Validates: Requirements 6.7, 6.8, 10.1, 10.2, 10.3**
    - Use FsCheck to generate base/derived class pairs with varying default blocks
    - Compile in-process and verify inherited field defaults match base class defaults unless overridden

  - [x] 12.7 Write property tests for type checker validation (Properties 10, 11, 12)
    - Create `Test/Mist.Tests/Analysis/DefaultTypeCheckerPropertyTests.cs`
    - **Property 10: Default expression type assignment**
    - **Validates: Requirements 7.1, 7.2**
    - **Property 11: Bare default type inference**
    - **Validates: Requirements 7.8**
    - **Property 12: Visibility enforcement in default blocks**
    - **Validates: Requirements 9.1, 9.2, 9.3**
    - Compile in-process and verify expression types are correctly assigned, bare `default` infers from context, and visibility violations produce diagnostics

  - [x] 12.8 Write property test for redundant default block detection (Property 13)
    - Add to `Test/Mist.Tests/Analysis/DefaultTypeCheckerPropertyTests.cs`
    - **Property 13: Redundant default block detection**
    - **Validates: Requirements 11.1, 11.2**
    - Use FsCheck to generate types with explicit default blocks that match inferred defaults
    - Compile in-process and verify `MIST_DEFAULT_REDUNDANT (historical)` info diagnostic is emitted

  - [x] 12.9 Write property tests for C# emission (Properties 14, 15)
    - Create `Test/Mist.Tests/Backend/DefaultEmitterPropertyTests.cs`
    - **Property 14: C# emission of default value accessors**
    - **Validates: Requirements 12.1**
    - **Property 15: C# emission of default expressions**
    - **Validates: Requirements 12.3**
    - Compile in-process and inspect the emitted C# source for `__CreateDefault` factory methods and `__CreateDefault()` call sites

  - [x] 12.10 Write batched end-to-end test for default instance independence (Property 16)
    - Create `Test/Mist.Tests/Integration/DefaultInstanceIndependenceTests.cs`
    - **Property 16: Default instance independence for types with reference fields**
    - **Validates: Requirements 12.4, 12.9**
    - Generate a single Mist program with multiple default expressions for classes and structs (including structs with reference-type fields), compile once via shared `CompilerDriver`, run the compiled output once, verify output values and that reference-type fields in independently-created default instances are distinct
    - Uses batching pattern: 1 compile, 1 process spawn, N output lines compared
    - _Process spawning: 1 `dotnet` invocation per test run (~1.5s)_

- [x] 13. Final checkpoint — All tests pass
  - Ensure the solution builds with zero errors and zero warnings and all tests pass. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at each pipeline stage
- Property tests validate universal correctness properties from the design document
- All property-based tests use in-process compilation via a shared `CompilerDriver` instance — no per-iteration process spawning
- The batched end-to-end test (12.10) spawns `dotnet` exactly once per test run
- The Storm source generator auto-generates AST node types from grammar rules, so no manual AST node code is needed
- Default blocks are structurally restricted to classes and structs by the grammar (interfaces use `InterfaceMemberDeclaration`, traits use `TraitMemberDeclaration`), so no additional parser rejection logic is needed for Requirements 2.6 and 2.7
