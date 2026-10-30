# Implementation Plan: Extension Methods (Extension Blocks)

## Overview

This plan implements extension blocks for the Mist programming language, following the compiler pipeline order: grammar → AST generation → pretty-printer → semantic model → type checker → C# emitter → syntax highlighting → standard library → sample application → documentation. Each task builds incrementally on the previous, ensuring no orphaned code.

## Tasks

- [x] 1. Grammar changes — Add EXTENSION token and extension block rules
  - [x] 1.1 Add `EXTENSION` token to `keywords.storm`
    - Add `token EXTENSION = "extension";` to `Source/Mist/Grammar/keywords.storm`
    - _Requirements: 1.1, 1.2_

  - [x] 1.2 Add `ExtensionBlockDeclaration` and `ExtensionMemberDeclaration` rules to `Mist.storm`
    - Add `ExtensionBlockDeclaration` rule with optional type parameters, receiver type, receiver name, and member list
    - Add `ExtensionMemberDeclaration` rule with method and property alternatives
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 4.1, 4.2, 5.1, 5.2, 5.3, 5.4, 6.1, 6.2, 6.3, 6.4_

  - [x] 1.3 Extend `TopLevelItem` and `NamespaceDeclaration` rules to include extension blocks
    - Add `ExtensionItem` alternative to `TopLevelItem`
    - Add `NamespaceBodyItem` rule with type and extension alternatives
    - Update `NamespaceDeclaration` to use `NamespaceBodyItem*`
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 2. Rebuild and verify AST generation
  - [x] 2.1 Run the Storm source generator and verify generated AST nodes compile
    - Build the solution to trigger source generation
    - Verify `ExtensionBlockDeclarationNode`, `ExtensionMemberDeclarationNode`, and `ExtensionItemTopLevelItemNode` are generated
    - Fix any compilation errors in generated code
    - Ensure zero warnings
    - _Requirements: 2.2, 4.2_

- [x] 3. Pretty-printer support for extension blocks
  - [x] 3.1 Add `EmitExtensionBlockDeclaration` and `EmitExtensionMemberDeclaration` to `MistPrettyPrinter`
    - Handle type parameter list emission
    - Handle receiver type and receiver name emission
    - Handle member list emission (methods and properties)
    - Handle static modifier preservation
    - _Requirements: 1.3, 2.6, 3.4, 4.3, 5.5, 6.6, 7.4_

  - [x] 3.2 Update `EmitCompilationUnit` and `EmitNamespaceDeclaration` to handle extension items
    - Add case for `ExtensionItemTopLevelItemNode` in top-level item emission
    - Add case for `NamespaceExtensionItem` in namespace body emission
    - _Requirements: 7.1, 7.2, 7.4_

  - [x] 3.3 Write property test for extension block round-trip (Property 1)
    - **Property 1: Extension Block Round-Trip**
    - Generate random valid extension block ASTs (generic, non-generic, mixed static/instance, various receiver types, multiple blocks)
    - Pretty-print → parse → pretty-print and assert identical output
    - Use FsCheck with MaxTest = 100, batched compilation pattern (1 compile, 1 run, N output lines)
    - Use shared CompilerDriver for in-process compilation
    - **Validates: Requirements 2.7, 4.4, 5.6, 6.7**

- [x] 4. Checkpoint — Grammar and pretty-printer complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Semantic model — ExtensionRegistry and registration
  - [x] 5.1 Create `ExtensionBlockInfo` and `ExtensionRegistry` classes
    - Create `Source/Mist/Semantics/Model/ExtensionBlockInfo.cs`
    - Create `Source/Mist/Semantics/Model/ExtensionRegistry.cs`
    - Implement `Register`, `GetApplicableExtensions`, `GetUniversalExtensions`, `GetExactExtensions` methods
    - Add `ExtensionRegistry` property to `ISemanticModel` interface and implementation
    - _Requirements: 8.1, 8.2, 8.3, 9.1_

  - [x] 5.2 Add extension block processing to `SemanticModelBuilder` — Pass 1 (type registration)
    - Scan for `ExtensionBlockDeclarationNode` in top-level items and namespace bodies
    - Create placeholder scope for each extension block
    - Register type parameters (if generic) in the extension block's scope
    - _Requirements: 8.5_

  - [x] 5.3 Add extension block processing to `SemanticModelBuilder` — Pass 2 (reference resolution)
    - Resolve receiver type reference to an `IType`
    - Register receiver parameter symbol in the block's scope
    - Walk member declarations: register methods as `SymbolKind.Method`, properties as `SymbolKind.Property`
    - Reject `set` accessors with `MIST_SB_0071 (historical)`
    - Reject invalid members (fields) with `MIST_SB_0072 (historical)`
    - Reject `void` receiver type with `MIST_SB_0074 (historical)`
    - Register `ExtensionBlockInfo` in the `ExtensionRegistry`
    - _Requirements: 8.1, 8.2, 8.4, 8.6, 8.7, 8.8, 9.1, 9.4, 10.1, 10.2, 10.3, 20.1, 20.2, 20.6_

  - [x] 5.4 Add duplicate signature detection across extension blocks
    - Check for duplicate member signatures across all extension blocks for the same receiver type
    - Emit `MIST_SB_0073 (historical)` on the second occurrence
    - _Requirements: 8.9, 20.4, 20.7_

  - [x] 5.5 Add receiver parameter shadowing warning
    - If a member declaration inside an extension block shadows the receiver parameter name, emit diagnostic warning
    - _Requirements: 10.4, 20.5_

  - [x] 5.6 Write property test for extension registration completeness (Property 2)
    - **Property 2: Extension Registration Completeness**
    - Generate N extension blocks on receiver type T with distinct member signatures
    - Assert ExtensionRegistry contains exactly N entries for T with all declared members
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.6, 8.7, 9.1, 20.7**

  - [x] 5.7 Write property test for set accessor rejection (Property 11)
    - **Property 11: Set Accessor Rejection**
    - Generate extension properties with `set` or `init` accessors
    - Assert compiler emits `MIST_SB_0071 (historical)` diagnostic
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 6.5, 20.3**

  - [x] 5.8 Write property test for duplicate signature detection (Property 10)
    - **Property 10: Duplicate Signature Detection**
    - Generate two extension blocks on the same receiver type with identical member signatures
    - Assert compiler emits `MIST_SB_0073 (historical)` diagnostic
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 8.9, 20.4**

- [x] 6. Checkpoint — Semantic model registration complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Type checker — Member lookup fallback and specificity
  - [x] 7.1 Implement extension member lookup fallback in `TypeCheckerAnalyzer`
    - When own member lookup fails, query `ExtensionRegistry.GetApplicableExtensions(receiverType)`
    - Filter by namespace visibility (only extensions whose `ContainingNamespace` is imported or null)
    - Collect candidate extension members matching the name
    - Apply overload resolution if multiple candidates
    - Emit `MIST_UNDEFINED_MEMBER (historical)` if no candidates found
    - _Requirements: 11.1, 11.2, 11.3, 11.6, 21.1_

  - [x] 7.2 Implement specificity ordering for extension candidates
    - Own members > extension members (already handled by fallback structure)
    - More-specific receiver type > less-specific (e.g., `string` > `unknown`)
    - Exact type match > base type match > universal
    - Emit `MIST_TC_0082 (historical)` on ambiguity
    - _Requirements: 11.4, 11.5, 21.2_

  - [x] 7.3 Implement generic extension type inference
    - Unify extension's receiver type pattern with actual receiver type
    - Extract type argument bindings (e.g., T → int)
    - Substitute bindings into member signatures (return types, parameter types)
    - Silently exclude from candidates if unification fails
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 21.3, 25.1, 25.2, 25.3, 25.4_

  - [x] 7.4 Implement static extension member lookup
    - When static member access fails on type's own static scope, query `ExtensionRegistry.GetExactExtensions(type)`
    - Filter for members marked `static`
    - Apply same overload resolution and specificity rules
    - _Requirements: 13.1, 13.2, 13.3_

  - [x] 7.5 Implement static receiver access validation
    - If a `static` extension member body references the receiver parameter → emit `MIST_TC_0080 (historical)`
    - If a method parameter name conflicts with receiver parameter name → emit `MIST_TC_0081 (historical)`
    - _Requirements: 3.3, 13.4, 20.5_

  - [x] 7.6 Implement trait/interface exclusion
    - `CheckTraitSatisfaction`: Do NOT query `ExtensionRegistry` — only own members count
    - Member access on trait-typed or interface-typed variables: Do NOT fall back to extension lookup
    - _Requirements: 14.1, 14.2, 14.3, 15.1, 15.2_

  - [x] 7.7 Implement universal extension lookup for `unknown`-typed values
    - When member lookup is performed on a value of type `unknown`, search only universal extensions
    - Do NOT search type-specific extensions when static type is `unknown`
    - _Requirements: 24.1, 24.2_

  - [x] 7.8 Implement union type extension lookup
    - When member lookup is performed on a union type `A | B`, find extension methods applicable to both A and B
    - Exclude extensions applicable to only some constituents
    - Universal extensions always applicable to union-typed values
    - _Requirements: 26.1, 26.2, 26.3_

  - [x] 7.9 Write property test for extension member lookup fallback (Property 3)
    - **Property 3: Extension Member Lookup Fallback**
    - Generate types without member M, and extension blocks on those types declaring M
    - Assert calling `instance.M(args)` resolves to the extension member and type-checks
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 11.1, 11.2, 13.1, 13.2**

  - [x] 7.10 Write property test for own members shadow extensions (Property 4)
    - **Property 4: Own Members Shadow Extensions**
    - Generate types declaring member M, and extension blocks also declaring M
    - Assert calling `instance.M()` resolves to the type's own member
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 11.4, 13.3**

  - [x] 7.11 Write property test for receiver specificity ordering (Property 5)
    - **Property 5: Receiver Specificity Ordering**
    - Generate extension method M on both a specific type T and on `unknown`
    - Assert calling `instance.M()` on T resolves to the T-specific extension
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 11.5**

  - [x] 7.12 Write property test for generic extension type inference (Property 6)
    - **Property 6: Generic Extension Type Inference**
    - Generate generic extensions `extension<T>(G<T> self)` and concrete types `G<X>`
    - Assert type checker infers T = X and substitutes throughout member signatures
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 12.1, 12.4, 25.1, 25.2, 25.3**

  - [x] 7.13 Write property test for trait/interface exclusion (Property 7)
    - **Property 7: Trait/Interface Exclusion**
    - Generate types with extension methods matching trait/interface signatures
    - Assert type does NOT satisfy trait via extension members
    - Assert extension members NOT discoverable on trait-typed variables
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 14.1, 14.2, 14.3, 15.1, 15.2**

  - [x] 7.14 Write property test for universal extension applicability (Property 8)
    - **Property 8: Universal Extension Applicability**
    - Generate universal extension method M and various types (concrete, unknown, union)
    - Assert calling `value.M()` resolves to the universal extension on all types
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 9.2, 24.1, 26.3**

  - [x] 7.15 Write property test for static receiver access rejection (Property 9)
    - **Property 9: Static Receiver Access Rejection**
    - Generate static extension members whose bodies reference the receiver parameter
    - Assert type checker emits `MIST_TC_0080 (historical)`
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 3.3, 13.4**

  - [x] 7.16 Write property test for namespace visibility (Property 12)
    - **Property 12: Namespace Visibility**
    - Generate extension blocks in namespace N, call sites with and without `using N`
    - Assert extensions only discoverable when namespace is imported
    - Assert top-level extensions visible from all files
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 27.1, 27.2**

  - [x] 7.17 Write property test for union extension applicability (Property 13)
    - **Property 13: Union Extension Applicability**
    - Generate union types `A | B` and extension methods applicable to both/one/neither
    - Assert M discoverable on union only if applicable to all constituents
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 26.1, 26.2**

  - [x] 7.18 Write property test for unknown type-specific exclusion (Property 14)
    - **Property 14: Unknown Type-Specific Exclusion**
    - Generate type-specific extension methods and values with static type `unknown`
    - Assert calling `value.M()` does NOT resolve to type-specific extensions
    - Use FsCheck with MaxTest = 100, in-process compilation via shared CompilerDriver
    - **Validates: Requirements 24.2**

- [x] 8. Checkpoint — Type checker complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. C# emitter — Static class generation and call-site rewriting
  - [x] 9.1 Implement `EmitExtensionBlock` in `CSharpSourceWriter`
    - Generate static class with deterministic name: `{ReceiverTypeName}Extensions_{FileName}_{LineNumber}`
    - Emit instance methods with `this`-annotated receiver as first parameter
    - Emit static methods without `this` annotation
    - Handle generic extension blocks: hoist type parameters to individual methods
    - _Requirements: 16.1, 16.2, 16.3, 16.5, 19.1, 19.3_

  - [x] 9.2 Implement extension property emission
    - Emit expression-bodied properties as `get_PropertyName` static methods with `this` receiver
    - Emit block-bodied properties as `get_PropertyName` static methods with accessor body
    - _Requirements: 17.1, 17.2_

  - [x] 9.3 Implement call-site rewriting for extension methods and properties
    - Standard pipeline evidence (2026-10-03): `StaticExtensionCarrierCallTests` exercises resolved static extension calls at Tier 2, ordinary and promoted, with closed generic carrier arguments. Static calls name the assigned carrier and pass only declared arguments; primitive type-literal lookup and the recorded generic static binding gap remain separate limitations.
    - Rewrite `instance.Method(args)` to use C# extension method syntax (dot-notation)
    - Rewrite `instance.Property` to `ClassName.get_PropertyName(instance)` static call
    - Rewrite `TypeName.StaticMethod(args)` to `ClassName.StaticMethod(args)` static call
    - _Requirements: 16.4, 17.3, 19.2_

  - [x] 9.4 Implement universal extension emission
    - Map receiver type `unknown` to C# `object`
    - Value types boxed automatically by CLR when passed as `object`
    - _Requirements: 18.1, 18.2, 18.3, 24.3_

- [x] 10. Checkpoint — C# emitter complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Syntax highlighting — TextMate grammar update
  - [x] 11.1 Add `extension` keyword to declaration keyword pattern in `mist.tmLanguage.json`
    - Add `extension` to the keyword declaration match pattern
    - _Requirements: 22.1_

  - [x] 11.2 Add extension block structure pattern to `mist.tmLanguage.json`
    - Match `extension<TypeParams>(ReceiverType receiverName)` with appropriate scopes
    - Highlight type parameters, receiver type, and receiver parameter name
    - _Requirements: 22.2, 22.3, 22.4, 22.5_

- [x] 12. Standard library — Universal extension methods
  - [x] 12.1 Create universal extension block file in `Source/Standard/`
    - Create `Source/Standard/Object.mist` (or add to existing file)
    - Declare `extension(unknown self)` with `string ToString()`, `int GetHashCode()`, `Type GetType()`
    - _Requirements: 23.1, 23.2, 23.3_

  - [x] 12.2 Wire universal extensions into the standard library loader
    - Update `StdlibLoader` to include the new universal extension file
    - Verify `ToString()`, `GetHashCode()`, `GetType()` resolve on all types
    - _Requirements: 23.4, 23.5_

- [x] 13. Checkpoint — Full feature implementation complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Sample application demonstrating extension methods
  - [x] 14.1 Create a sample Mist program showcasing extension blocks
    - Demonstrate instance extension methods on a concrete type
    - Demonstrate generic extension methods on `List<T>`
    - Demonstrate universal extension via `extension(unknown self)`
    - Demonstrate static extension members
    - Demonstrate extension properties
    - Demonstrate namespace-scoped extensions with `using`
    - Place in a `Samples/` or `Examples/` directory
    - _Requirements: 2.1, 4.1, 5.1, 6.1, 9.1, 13.1_

- [x] 15. Documentation update
  - [x] 15.1 Update `README.md` or language documentation with extension block syntax and usage
    - Document the `extension(Type name) { ... }` syntax
    - Document generic extensions, universal extensions, static members
    - Document extension property limitations (read-only)
    - Document namespace scoping rules
    - Document shadowing/specificity rules
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 9.1, 11.4, 11.5, 27.1_

- [x] 16. Final checkpoint — All tasks complete
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck with MaxTest = 100 minimum per workspace policy
- All property tests use batched compilation (1 compile, 1 run, N output lines) or in-process compilation via shared CompilerDriver per workspace test-process-spawning policy
- Checkpoints ensure incremental validation after major milestones
- The zero-warnings policy applies to all implementation tasks
