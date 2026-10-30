# Implementation Plan: Inferred Namespaces

## Overview

This plan implements inferred namespaces for the Mist compiler, touching every stage of the compilation pipeline: project configuration, grammar, semantic model, C# emitter, pretty printer, and CLI wiring. Tasks are ordered so each builds on the previous, starting with data model changes and ending with integration wiring and a sample project.

## Tasks

- [x] 1. Extend ProjectConfiguration and CompilerOptions with NamespaceRoot
  - [x] 1.1 Add NamespaceRoot field to ProjectConfiguration record
    - In `Source/Mist/Driver/Contracts/ProjectConfiguration.cs`, change the record to `ProjectConfiguration(string Name, string NamespaceRoot)`
    - The `NamespaceRoot` field is always populated — either from `namespace_root` in `mist.toml` or defaulted to `Name`
    - _Requirements: 1.7_

  - [x] 1.2 Add NamespaceRoot field to CompilerOptions record
    - In `Source/Mist/Driver/Contracts/CompilerOptions.cs`, add `string NamespaceRoot` parameter to the record
    - Place it after `ProjectName` and before `EnabledAnalyzerIds`
    - _Requirements: 2.1_

  - [x] 1.3 Update ProjectFileParser to parse and validate namespace_root
    - In `Source/Mist/Driver/ProjectFileParser.cs`, after reading `name`:
      - Check for optional `namespace_root` field in the TOML model
      - If present and non-empty: validate characters using the same `ValidNamePattern()` regex, use as `NamespaceRoot`
      - If present but empty/whitespace: report `MIST0056` error and return null
      - If present but invalid characters: report `MIST0057` error listing invalid chars and return null
      - If absent: default `NamespaceRoot` to `name`
    - Return `new ProjectConfiguration(name, namespaceRoot)` with the resolved value
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [x] 1.4 Write property tests for NamespaceRoot resolution (Property 1)
    - **Property 1: NamespaceRoot Resolution**
    - Generate random valid `name` and optional `namespace_root` strings; verify resolved `NamespaceRoot` equals `namespace_root` when present, `name` when absent
    - Test file: `Test/Mist.Tests/Backend/NamespaceRootResolutionPropertyTests.cs`
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - **Validates: Requirements 1.2, 1.3, 12.1**

  - [x] 1.5 Write property tests for NamespaceRoot validation (Property 2)
    - **Property 2: NamespaceRoot Validation**
    - Generate random strings with valid/invalid characters; verify parsing succeeds iff value is non-empty, non-whitespace, and contains only `[a-zA-Z0-9\-_.]`
    - Test file: `Test/Mist.Tests/Backend/NamespaceRootValidationPropertyTests.cs`
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - **Validates: Requirements 1.4, 1.5, 1.6**

- [x] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Update Grammar to support multiple namespaces per file
  - [x] 3.1 Modify CompilationUnit rule and add TopLevelItem rule
    - In `Source/Mist/Grammar/Mist.storm`, change:
      ```
      rule CompilationUnit = usings:UsingDeclaration* namespace:NamespaceDeclaration? types:TypeDeclaration*;
      ```
      to:
      ```
      rule CompilationUnit = usings:UsingDeclaration* items:TopLevelItem*;

      rule TopLevelItem
          = namespaceDeclaration:NamespaceDeclaration  as NamespaceItem
          | typeDeclaration:TypeDeclaration            as TypeItem
          ;
      ```
    - The `NamespaceDeclaration` rule itself is unchanged (already supports `FileScopedNamespace` and `BlockNamespace`)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_

  - [x] 3.2 Write property tests for multi-namespace parsing (Property 5)
    - **Property 5: Multi-Namespace Parsing**
    - Generate syntactically valid Mist source files with 0–3 namespace declarations (file-scoped, block, or mixed) interleaved with type declarations; verify parser produces a valid AST without errors
    - Test file: `Test/Mist.Tests/Parsing/MultiNamespaceParsingPropertyTests.cs`
    - Add a generator in `Test/Mist.Tests/Generators/` for multi-namespace source files
    - **Validates: Requirements 4.1, 4.2, 4.5, 4.6**

- [x] 4. Update SemanticModelBuilder for inferred namespaces and multi-namespace support
  - [x] 4.1 Update ISemanticModelBuilder interface and Build method signature
    - In `Source/Mist/Semantics/Contracts/ISemanticModelBuilder.cs`, add `string namespaceRoot` and `string projectFolder` parameters to the `Build` method
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, update the `Build` method signature to match
    - _Requirements: 2.4_

  - [x] 4.2 Add ComputeInferredNamespace static method
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add:
      ```csharp
      internal static string ComputeInferredNamespace(
          string namespaceRoot, string projectFolder, string filePath)
      ```
    - Compute relative path from `projectFolder` to the file's directory
    - If relative path is `"."` or empty, return `namespaceRoot`
    - Otherwise replace path separators with dots and return `"{namespaceRoot}.{segments}"`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 4.3 Write property tests for inferred namespace computation (Property 3)
    - **Property 3: Inferred Namespace Computation**
    - Generate random valid namespace root strings and relative file paths; verify the computed namespace follows `{root}.{folder1}.{folder2}...` formula, and equals root for project-root files
    - Test file: `Test/Mist.Tests/Semantics/InferredNamespaceComputationPropertyTests.cs`
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

  - [x] 4.4 Rewrite Pass1_RegisterTypes for new grammar structure and inferred namespaces
    - Update `Pass1_RegisterTypes` to:
      1. Accept `namespaceRoot` and `projectFolder` parameters
      2. Compute the inferred namespace for the file using `ComputeInferredNamespace`
      3. Iterate over `TopLevelItem` nodes (instead of `namespace? types*`)
      4. Track a "current namespace" — initially the inferred namespace
      5. For `NamespaceItem` with file-scoped: update current namespace to declared name; register subsequent types under it
      6. For `NamespaceItem` with block: register only enclosed types under declared namespace; current namespace unchanged after block
      7. For `TypeItem`: register type under current namespace
      8. Use `FindExistingNamespaceScope` to reuse namespace scopes across files
    - Update all callers of `Pass1_RegisterTypes` in the `Build` method to pass the new parameters
    - _Requirements: 3.6, 3.7, 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 5.5, 9.1, 9.2, 9.3_

  - [x] 4.5 Write property tests for type registration under inferred namespace (Property 4)
    - **Property 4: Type Registration Under Inferred Namespace**
    - Generate Mist source files without namespaces at various paths; verify all types have `ContainingNamespace` set to the computed inferred namespace
    - Test file: `Test/Mist.Tests/Semantics/InferredNamespaceRegistrationPropertyTests.cs`
    - **Validates: Requirements 3.6, 3.7**

  - [x] 4.6 Write property tests for file-scoped namespace association (Property 6)
    - **Property 6: File-Scoped Namespace Type Association**
    - Generate files with file-scoped namespaces and subsequent types; verify each type is registered under the correct namespace until the next declaration or EOF
    - Test file: `Test/Mist.Tests/Semantics/FileScopedNamespaceAssociationPropertyTests.cs`
    - **Validates: Requirements 4.3, 5.2**

  - [x] 4.7 Write property tests for block namespace association (Property 7)
    - **Property 7: Block Namespace Type Association**
    - Generate files with block namespaces and types inside/outside; verify only enclosed types are registered under the block namespace
    - Test file: `Test/Mist.Tests/Semantics/BlockNamespaceAssociationPropertyTests.cs`
    - **Validates: Requirements 4.4, 5.3**

  - [x] 4.8 Write property tests for top-level types defaulting to inferred namespace (Property 8)
    - **Property 8: Top-Level Types Default to Inferred Namespace**
    - Generate files with explicit namespaces and top-level types outside any namespace; verify top-level types are registered under the inferred namespace
    - Test file: `Test/Mist.Tests/Semantics/TopLevelTypeInferredNamespacePropertyTests.cs`
    - **Validates: Requirements 5.4, 9.1, 9.2**

  - [x] 4.9 Write property tests for cross-file namespace scope merging (Property 9)
    - **Property 9: Cross-File Namespace Scope Merging**
    - Generate multiple files declaring the same namespace (explicitly or via inference); verify all types are registered in the same namespace scope
    - Test file: `Test/Mist.Tests/Semantics/NamespaceMergingPropertyTests.cs`
    - **Validates: Requirements 5.1, 5.5**

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement same-namespace scoping and using declaration resolution
  - [x] 6.1 Update Pass2_ResolveTypes for same-namespace resolution
    - In `Pass2_ResolveTypes`, update type resolution to:
      1. For each file, determine which namespace scopes are active (inferred + explicit)
      2. Set file-level scope parent to the primary namespace scope so `Lookup` searches: local → file → namespace → global
      3. For `using` declarations, add imported namespace scopes to the file scope
      4. When resolving a type name, after checking the scope chain, also check imported namespaces
      5. If multiple imported namespaces contain the same type name, report `MIST_AMBIGUOUS_TYPE (historical)` error
      6. If a `using` references a non-existent namespace, report a diagnostic
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 7.4_

  - [x] 6.2 Write property tests for same-namespace type resolution (Property 10)
    - **Property 10: Same-Namespace Type Resolution**
    - Generate type pairs in same/different namespaces with cross-references; verify same-namespace resolves without `using`, different-namespace fails without `using`
    - Test file: `Test/Mist.Tests/Semantics/SameNamespaceResolutionPropertyTests.cs`
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

  - [x] 6.3 Write property tests for using declaration resolution (Property 11)
    - **Property 11: Using Declaration Enables Cross-Namespace Resolution**
    - Generate files with `using` declarations and cross-namespace type references; verify types resolve by simple name
    - Test file: `Test/Mist.Tests/Semantics/UsingDeclarationResolutionPropertyTests.cs`
    - **Validates: Requirements 7.1**

  - [x] 6.4 Write property tests for non-existent namespace using diagnostic (Property 12)
    - **Property 12: Non-Existent Namespace Using Diagnostic**
    - Generate `using` declarations for random non-existent namespace names; verify a diagnostic is produced
    - Test file: `Test/Mist.Tests/Semantics/UndefinedNamespaceDiagnosticPropertyTests.cs`
    - **Validates: Requirements 7.2**

  - [x] 6.5 Write property tests for ambiguous type reference diagnostic (Property 13)
    - **Property 13: Ambiguous Type Reference Diagnostic**
    - Generate two namespaces with same-named types, import both via `using`; verify ambiguity error when the name is referenced
    - Test file: `Test/Mist.Tests/Semantics/AmbiguousTypeReferencePropertyTests.cs`
    - **Validates: Requirements 7.3**

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Update CompilerDriver and CLI to wire NamespaceRoot through the pipeline
  - [x] 8.1 Update CompilerDriver to pass NamespaceRoot and ProjectFolder to SemanticModelBuilder
    - In `Source/Mist/Driver/CompilerDriver.cs`, update the `semanticModelBuilder.Build()` call to pass `options.NamespaceRoot` and `options.ProjectFolder`
    - _Requirements: 2.2, 2.3_

  - [x] 8.2 Update CLI to pass NamespaceRoot from ProjectConfiguration to CompilerOptions
    - In `Source/Mist.CLI/Program.cs`, update the `CompilerOptions` construction in `CompileProject` to include `NamespaceRoot: config.NamespaceRoot`
    - _Requirements: 2.1_

  - [x] 8.3 Fix all existing test call sites for CompilerOptions and Build
    - Update all test files that construct `CompilerOptions` to include the new `NamespaceRoot` parameter (default to `ProjectName` for backward compatibility)
    - Update all test files that call `semanticModelBuilder.Build()` to pass `namespaceRoot` and `projectFolder` parameters
    - Affected test files include those in `Test/Mist.Tests/Integration/`, `Test/Mist.Tests/Backend/`, `Test/Mist.Tests/PipelineBackendPropertyTests.cs`, and any others constructing `CompilerOptions` or calling `Build`
    - _Requirements: 12.1, 12.2_

- [x] 9. Update CSharpEmitter for namespace output
  - [x] 9.1 Update CSharpSourceWriter.EmitCompilationUnit for new grammar structure
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, update `EmitCompilationUnit` to:
      1. Iterate over `TopLevelItem` nodes instead of `namespace? types*`
      2. For types belonging to the inferred namespace (no explicit namespace in source), emit a file-scoped `namespace X.Y.Z;` declaration before the types
      3. For explicit file-scoped namespaces, emit `namespace X.Y.Z;` as before
      4. For explicit block namespaces, emit `namespace X.Y.Z { ... }` as before
      5. When a file has multiple namespaces, emit each namespace section with its associated types
    - Determine a type's namespace by looking up the symbol's `ContainingNamespace` from the semantic model
    - Emit `using` directives for each `UsingDeclaration` in the Mist source
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [x] 9.2 Write property tests for C# emitter namespace output (Property 14)
    - **Property 14: C# Emitter Namespace Output**
    - Generate Mist source files with various namespace configurations; verify emitted C# contains corresponding namespace declarations
    - Test file: `Test/Mist.Tests/Backend/NamespaceEmissionPropertyTests.cs`
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

  - [x] 9.3 Write property tests for C# emitter using directive output (Property 15)
    - **Property 15: C# Emitter Using Directive Output**
    - Generate Mist source files with `using` declarations; verify emitted C# contains corresponding `using` directives
    - Test file: `Test/Mist.Tests/Backend/UsingDirectiveEmissionPropertyTests.cs`
    - **Validates: Requirements 8.6**

- [x] 10. Update MistPrettyPrinter for new grammar structure
  - [x] 10.1 Update EmitCompilationUnit for TopLevelItem iteration
    - In `Source/Mist/Parsing/MistPrettyPrinter.cs`, update `EmitCompilationUnit` to:
      1. Emit `using` declarations (unchanged)
      2. Iterate over `TopLevelItem` nodes instead of `namespace? types*`
      3. For `NamespaceItem`: emit the namespace declaration (file-scoped or block) using existing `EmitNamespaceDeclaration`
      4. For `TypeItem`: emit the type declaration using existing `EmitTypeDeclaration`
    - _Requirements: 11.1, 11.2, 11.3_

  - [x] 10.2 Write property tests for pretty printer namespace round-trip (Property 16)
    - **Property 16: Pretty Printer Namespace Round-Trip**
    - Generate valid Mist source with namespace declarations; verify parse → print → parse produces an equivalent AST
    - Test file: `Test/Mist.Tests/Parsing/NamespaceRoundTripPropertyTests.cs`
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4**

- [x] 11. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Create sample project demonstrating namespaces
  - [x] 12.1 Create samples/Namespaces project structure and mist.toml
    - Create `samples/Namespaces/mist.toml` with `name = "Namespaces"` and `namespace_root = "Namespaces"`
    - Create subdirectories: `samples/Namespaces/Models/`, `samples/Namespaces/Models/Game/`
    - _Requirements: 10.1_

  - [x] 12.2 Create sample source files demonstrating all namespace features
    - Create a file at project root (e.g., `Program.mist`) demonstrating inferred namespace matching `NamespaceRoot`
    - Create files in `Models/` subdirectory demonstrating inferred namespace `Namespaces.Models`
    - Create a file in `Models/Game/` demonstrating nested inferred namespace `Namespaces.Models.Game`
    - Create a file demonstrating file-scoped namespace declaration
    - Create a file demonstrating block namespace declaration
    - Create a file demonstrating multiple namespace declarations (mixing file-scoped and block)
    - Create a file demonstrating `using` declarations to reference types from other namespaces
    - Create a file demonstrating same-namespace scoping (referencing a type without `using`)
    - _Requirements: 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_

  - [x] 12.3 Write integration test for sample project compilation
    - Add a test in `Test/Mist.Tests/Integration/EndToEndCompilationTests.cs` that compiles `samples/Namespaces` end-to-end and verifies it succeeds
    - _Requirements: 10.9_

- [x] 13. Backward compatibility verification
  - [x] 13.1 Write integration tests for backward compatibility
    - Add tests verifying existing sample projects (`samples/HelloMist`, `samples/AllFeatures`, etc.) still compile successfully
    - Verify that files without explicit namespaces now have types under the inferred namespace (equal to project name)
    - Verify that files with existing single namespace declarations continue to work identically
    - _Requirements: 12.1, 12.2, 12.3_

- [x] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document using FsCheck.Xunit with minimum 100 iterations
- Unit tests validate specific examples and edge cases
- The implementation language is C# throughout, matching the existing codebase
- The grammar change (task 3.1) will cause compilation errors in all downstream consumers (emitter, pretty printer, semantic model builder) — tasks 4, 9, and 10 resolve these
