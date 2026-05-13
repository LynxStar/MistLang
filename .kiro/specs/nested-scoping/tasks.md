# Implementation Plan: Nested Scoping

## Overview

This plan implements nested scoping for the Mist language compiler across four layers: grammar/parser, semantic model builder, C# emitter, and pretty printer. Tasks are ordered so each builds on the previous, starting with grammar changes that unlock parsing, then semantic registration, then emission and formatting, and finally the sample app and integration.

## Tasks

- [x] 1. Extend grammar and parser for nested types and namespaces
  - [x] 1.1 Add `NestedTypeMember` alternative to `MemberDeclaration` rule in `Source/Mist/Grammar/Mist.storm`
    - Add `| nestedType:TypeDeclaration as NestedTypeMember` to the `MemberDeclaration` rule
    - Add `| namespaceDeclaration:NamespaceDeclaration as NamespaceNamespaceItem` to the `NamespaceBodyItem` rule
    - Rebuild the project to trigger the Storm source generator and produce the new AST node types (`NestedTypeMemberMemberDeclarationNode`, `NamespaceNamespaceItemNamespaceBodyItemNode`)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 2.4, 5.1, 5.3_

  - [x] 1.2 Write property test: Parser nesting universality (Property 1)
    - **Property 1: Parser nesting universality**
    - For any container type kind × nested type kind, verify the parser produces a `NestedTypeMember` node
    - Create `Test/Mist.Tests/Semantics/NestedScopingPropertyTests.cs` with a shared `CompilerDriver` instance
    - Create `Test/Mist.Tests/Generators/MistNestedScopingGenerator.cs` with `ArbTypeKind` and `ArbNestedTypeSource` generators
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**

  - [x] 1.3 Write property test: Nesting depth acceptance (Property 2)
    - **Property 2: Nesting depth acceptance**
    - For any depth 1–8 and any sequence of type kinds, verify the parser produces an AST with exactly that depth
    - Use in-process compilation via `CompilerDriver.Compile()`
    - **Validates: Requirements 1.6, 2.3, 5.3**

  - [x] 1.4 Write property test: Modifier preservation on nested types (Property 3)
    - **Property 3: Modifier preservation on nested types**
    - For any subset of {public, private, static}, verify the AST node's modifier list matches exactly
    - **Validates: Requirements 1.7**

- [x] 2. Checkpoint - Ensure grammar changes compile and parser tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement semantic model registration for nested types
  - [x] 3.1 Handle `NestedTypeMemberMemberDeclarationNode` in `SemanticModelBuilder`
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a case in the member registration switch for `NestedTypeMemberMemberDeclarationNode`
    - Call `RegisterTypeDeclaration` with the parent type's scope as the parent scope
    - Nested types get `ContainingNamespace = null` (they inherit from the parent chain)
    - Type parameter inheritance is automatic via the scope chain (nested type's scope parent → enclosing type's scope)
    - _Requirements: 7.1, 7.2, 7.3, 9.1, 9.2_

  - [x] 3.2 Handle `NamespaceNamespaceItemNamespaceBodyItemNode` in `SemanticModelBuilder`
    - Add a case in the namespace body item processing for nested namespace declarations
    - Compute the nested namespace FQN by concatenating parent namespace + "." + declared name
    - Create or retrieve the namespace scope and recursively register types within
    - _Requirements: 5.2, 5.4_

  - [x] 3.3 Add namespace validation pass (`Pass1_ValidateNamespaceDeclarations`)
    - Detect duplicate file-scoped namespaces → report `MIST0301`
    - Detect mixed file-scoped and block namespaces → report `MIST0302`
    - Detect file-scoped namespace inside block namespace → report `MIST0305`
    - _Requirements: 3.1, 3.2, 3.3, 5.5, 14.1, 14.2_

  - [x] 3.4 Add duplicate nested type and type parameter conflict diagnostics
    - When registering a nested type, check if the name already exists in the parent type's scope → report `MIST0303`
    - Check if the nested type name conflicts with a type parameter on the parent → report `MIST0304`
    - _Requirements: 9.4, 9.5, 14.3_

  - [x] 3.5 Write property test: Namespace FQN concatenation (Property 5)
    - **Property 5: Namespace FQN concatenation**
    - For any chain of nested block namespaces, verify the FQN equals the dot-joined names
    - Create `ArbNamespaceChain` generator in `MistNestedScopingGenerator.cs`
    - **Validates: Requirements 5.2, 5.4**

  - [x] 3.6 Write property test: Type FQN construction (Property 6)
    - **Property 6: Type FQN construction**
    - For any type inside a namespace, verify FQN = namespace + "." + DisplayName
    - **Validates: Requirements 6.1, 6.2, 6.3, 8.1, 8.5**

  - [x] 3.7 Write property test: Closed generic FQN for nested types (Property 7)
    - **Property 7: Closed generic FQN for nested types**
    - For any nested type inside generic ancestors, verify FQN includes closed generic forms
    - **Validates: Requirements 8.2, 8.3, 8.4**

  - [x] 3.8 Write property test: Ancestor type parameter visibility (Property 8)
    - **Property 8: Ancestor type parameter visibility**
    - For any nested type at depth d, verify all ancestor type parameters are resolvable
    - **Validates: Requirements 7.1, 7.2, 7.3**

  - [x] 3.9 Write property test: Type parameter shadowing (Property 9)
    - **Property 9: Type parameter shadowing**
    - For any nested type that re-declares a parent's type parameter name, verify the nested one takes precedence
    - **Validates: Requirements 7.4**

  - [x] 3.10 Write property test: Same-name namespace scope sharing (Property 10)
    - **Property 10: Same-name namespace scope sharing**
    - For any file with multiple same-name block namespaces, verify types are cross-resolvable
    - **Validates: Requirements 4.1**

  - [x] 3.11 Write property test: Different-name namespace scope isolation (Property 11)
    - **Property 11: Different-name namespace scope isolation**
    - For any file with different-name block namespaces, verify types are NOT cross-resolvable without using
    - **Validates: Requirements 4.2**

- [x] 4. Checkpoint - Ensure semantic model tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement C# emission for nested types and namespaces
  - [x] 5.1 Handle `NestedTypeMemberMemberDeclarationNode` in `CSharpEmitter`
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, add a case in `EmitMemberDeclaration` for `NestedTypeMemberMemberDeclarationNode`
    - Call `EmitTypeDeclaration(nestedTypeMember.NestedType)` — existing indentation tracking handles nesting
    - Ensure nested types do NOT re-declare parent type parameters
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [x] 5.2 Handle `NamespaceNamespaceItemNamespaceBodyItemNode` in `CSharpEmitter`
    - Add a case in namespace body item emission for nested namespace declarations
    - Implement same-name namespace merging: collect all types from blocks with identical FQNs before emission
    - Omit empty namespace blocks from output
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

  - [x] 5.3 Write property test: C# emission preserves nesting structure (Property 12)
    - **Property 12: C# emission preserves nesting structure**
    - For any nested type declaration, verify the C# output contains the type inside the parent body and does not re-declare parent type parameters
    - **Validates: Requirements 10.1, 10.2**

- [x] 6. Implement pretty printer for nested types and namespaces
  - [x] 6.1 Handle `NestedTypeMemberMemberDeclarationNode` in `MistPrettyPrinter`
    - In `Source/Mist/Parsing/MistPrettyPrinter.cs`, add a case in member declaration emission for `NestedTypeMemberMemberDeclarationNode`
    - Call `EmitTypeDeclaration(nestedTypeMember.NestedType)` — existing indentation tracking handles nesting
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 6.2 Handle `NamespaceNamespaceItemNamespaceBodyItemNode` in `MistPrettyPrinter`
    - Add a case in namespace body item emission for nested namespace declarations
    - Call `EmitNamespaceDeclaration(nsItem.NamespaceDeclaration)` — preserves source structure without merging
    - _Requirements: 13.1, 13.2_

  - [x] 6.3 Write property test: Parse–print–parse round trip (Property 4)
    - **Property 4: Parse–print–parse round trip**
    - For any valid Mist source with nested types/namespaces, verify parse → print → parse produces structurally identical AST
    - **Validates: Requirements 1.8, 12.5, 13.3**

- [x] 7. Checkpoint - Ensure emitter and pretty printer tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement sample application and integration
  - [x] 8.1 Create `samples/NestedScoping/Program.mist` and `samples/NestedScoping/mist.toml`
    - Demonstrate: class in class, struct in class, interface in class, trait in class, enum in class
    - Demonstrate: class in struct, class in interface, class in trait
    - Demonstrate: deeply nested types (3+ levels)
    - Demonstrate: nested type inside generic parent accessing parent's type parameters
    - Demonstrate: multiple sibling nested types in same parent
    - Demonstrate: block namespaces nested inside block namespaces
    - Demonstrate: repeated block namespaces with same name
    - Demonstrate: file-scoped namespace with nested types inside type declarations
    - Demonstrate: referencing nested types by qualified name from outside the parent
    - Each permutation prints labeled output to stdout
    - Include `public static void RunDemo()` entry point
    - _Requirements: 16.1, 16.2, 16.3, 16.5_

  - [x] 8.2 Update `samples/AllFeatures/Program.mist` to call `NestedScoping.Program.RunDemo()`
    - Add the call to the AllFeatures orchestrator
    - _Requirements: 16.4_

- [x] 9. Update TextMate grammar for nested type highlighting
  - [x] 9.1 Verify and update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Ensure type declaration keywords (class, struct, interface, trait, enum) receive `keyword.declaration.mist` scope regardless of nesting depth
    - Ensure identifiers following nested type keywords receive correct `entity.name.type.*.mist` scope
    - Ensure nested braces match correctly up to 5 levels deep
    - Ensure keywords inside strings/comments are NOT highlighted
    - Ensure type parameter lists on nested types are highlighted correctly
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5_

- [x] 10. Write example-based unit tests for edge cases and diagnostics
  - [x] 10.1 Write unit tests for diagnostic error conditions
    - Test: duplicate file-scoped namespace produces `MIST0301`
    - Test: mixed namespace styles produce `MIST0302`
    - Test: file-scoped namespace inside block namespace produces `MIST0305`
    - Test: duplicate nested type name produces `MIST0303`
    - Test: nested type name conflicts with type parameter produces `MIST0304`
    - Test: unresolved type parameter reference produces diagnostic
    - _Requirements: 3.1, 3.2, 3.3, 5.5, 9.4, 9.5, 7.5, 14.1, 14.2, 14.3_

  - [x] 10.2 Write unit tests for nested type member acceptance and qualified name resolution
    - Test: nested type body accepts all member kinds (fields, methods, properties, constructors, operators, indexers, default blocks, further nested types)
    - Test: code outside parent requires qualified name to reference nested type
    - Test: C# emission for nested enum with underlying type and explicit values
    - Test: C# emission at depth > 4 produces valid output
    - Test: namespace merging for same-name blocks in C# output
    - Test: empty namespace omitted from C# output
    - _Requirements: 2.1, 9.3, 10.4, 10.5, 11.3, 11.4_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using FsCheck + xUnit
- Unit tests validate specific examples and edge cases
- All property tests use in-process compilation via `CompilerDriver.Compile()` (no process spawning per iteration)
- The shared `CompilerDriver` instance pattern from existing tests should be reused
- The Storm source generator automatically produces new AST node types when the grammar is modified — a rebuild is sufficient

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "1.4"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "3.4"] },
    { "id": 3, "tasks": ["3.5", "3.6", "3.7", "3.8", "3.9", "3.10", "3.11"] },
    { "id": 4, "tasks": ["5.1", "5.2", "6.1", "6.2"] },
    { "id": 5, "tasks": ["5.3", "6.3"] },
    { "id": 6, "tasks": ["8.1", "9.1", "10.1", "10.2"] },
    { "id": 7, "tasks": ["8.2"] }
  ]
}
```
