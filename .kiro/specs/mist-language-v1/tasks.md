# Implementation Plan: Mist Language v1

## Overview

This plan implements the complete Mist v1 language as specified in the design document. The implementation builds on the existing compiler architecture (Storm source generator → lexer → parser → semantic model → analyzers → backend) by filling in every architectural stub with concrete implementations. Work proceeds bottom-up through the pipeline: grammar first, then semantic model types, then the semantic model builder, then the type checker, then the C# emitter, then the pretty printer, and finally the sample application. Each phase wires into the existing pipeline contracts so there is no orphaned code.

## Tasks

- [x] 1. Implement the complete Mist Storm grammar
  - [x] 1.1 Create the `keywords.storm` file at `Source/Mist/Grammar/keywords.storm`
    - Define all keyword tokens: `namespace`, `using`, `class`, `struct`, `public`, `private`, `static`, `void`, `new`, `return`, `if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `var`, `true`, `false`, `this`
    - Define all primitive type keyword tokens: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`
    - Keywords must be declared before `IDENTIFIER` so declaration-order priority ensures keywords win over identifiers
    - _Requirements: 1.2, 1.3, 1.11_

  - [x] 1.2 Rewrite `Source/Mist/Grammar/Mist.storm` with the complete v1 grammar
    - Add `import "keywords";` directive (Storm imports use the file name without the `.storm` extension)
    - Define hidden tokens: `WHITESPACE`, `LINE_COMMENT`
    - Define all operator and punctuation tokens (multi-character operators before single-character counterparts for maximal-munch)
    - Define fragment rules: `HEX_DIGIT`, `INT_SUFFIX`, `FLOAT_SUFFIX`, `ESCAPE_SEQUENCE`
    - Define literal tokens: `HEX_LITERAL`, `INTEGER_LITERAL`, `FLOAT_LITERAL`, `CHAR_LITERAL`, `STRING_LITERAL`, `INTERPOLATED_STRING_START`, `INTERPOLATED_STRING_TEXT`, `INTERPOLATED_STRING_END`
    - Define `IDENTIFIER` token after all keywords
    - Define all parser rules: `CompilationUnit`, `UsingDeclaration`, `QualifiedIdentifier`, `NamespaceDeclaration`, `TypeDeclaration`, `ClassDeclaration`, `StructDeclaration`, `Modifier`, `MemberDeclaration`, `FieldDeclaration`, `ConstructorDeclaration`, `ConstructorChain`, `MethodDeclaration`, `ParameterList`, `Parameter`, `ArgumentList`, `TypeReference`, `PrimitiveType`, `Block`, all statement rules, and all expression rules with precedence climbing
    - _Requirements: 1.1, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 2.1–2.11, 3.1–3.9, 4.1–4.8_

  - [x] 1.3 Verify the Storm source generator processes the updated grammar
    - Build the solution and confirm the Storm source generator produces the lexer, parser, and AST node types in the `Mist.Generated` namespace
    - Fix any grammar issues reported by the Storm source generator validation passes (ambiguity, left recursion, name resolution, reachability)
    - _Requirements: 1.1–1.11, 2.1–2.11_

- [x] 2. Checkpoint — Grammar compiles and generates lexer/parser
  - Ensure the solution builds cleanly with the new grammar. Ask the user if questions arise.

- [x] 3. Implement semantic model types and enums
  - [x] 3.1 Update `SymbolKind` enum and `ISymbol` interface in `Source/Mist/Semantics/Contracts/ISymbol.cs`
    - Replace existing `SymbolKind` members (`Variable`, `Function`, `Type`, `Parameter`, `Namespace`) with: `Namespace`, `Class`, `Struct`, `Field`, `Constructor`, `Method`, `Parameter`, `LocalVariable`
    - Add `SymbolOrigin` enum with `UserCode` and `Backend` members
    - Add `Origin` property to `ISymbol` interface
    - _Requirements: 5.1, 7.1, 7.4_

  - [x] 3.2 Update `TypeKind` enum and `IType` interface in `Source/Mist/Semantics/Contracts/IType.cs`
    - Replace existing `TypeKind` members (`Primitive`, `Function`, `Record`, `Union`, `Unknown`) with exactly two members: `Class` and `Struct`
    - _Requirements: 5.2_

  - [x] 3.3 Create concrete type classes in `Source/Mist/Semantics/Model/Types/`
    - Create `PrimitiveType` with singleton instances for all 13 primitives (`Int`, `Uint`, `Long`, `Ulong`, `Short`, `Ushort`, `Byte`, `Sbyte`, `Float`, `Double`, `Decimal`, `Bool`, `Char`), all with `TypeKind.Struct`
    - Create `StringType` singleton with `TypeKind.Class`
    - Create `ArrayType` with `ElementType` property and `TypeKind.Class`
    - Create `VoidType` singleton with `TypeKind.Struct`
    - Create `ClassType` with `DisplayName`, `MemberScope`, `ContainingNamespace`
    - Create `StructType` with `DisplayName`, `MemberScope`, `ContainingNamespace`
    - Create `UnknownType` singleton whose `Kind` property throws `InvalidOperationException`
    - _Requirements: 5.11, 5.12, 5.13, 5.14, 5.15_

  - [x] 3.4 Update the `Symbol` implementation in `Source/Mist/Semantics/Model/` to support `SymbolOrigin`
    - Add `Origin` property to the concrete `Symbol` class
    - Ensure `DeclarationSpan` is `null` for `SymbolOrigin.Backend` symbols
    - _Requirements: 7.1, 7.4_

  - [x] 3.5 Fix all compilation errors in existing code caused by the enum and interface changes
    - Update `SemanticModelBuilder`, `TypeCheckerAnalyzer`, `CSharpEmitter`, and any other code referencing the old `SymbolKind` or `TypeKind` members
    - _Requirements: 5.1, 5.2_

- [x] 4. Implement the ambient type resolver interface and C# backend resolver
  - [x] 4.1 Create `IAmbientTypeResolver` interface in `Source/Mist/Semantics/Contracts/`
    - Define `HasNamespace(string namespaceName)`, `ResolveType(string namespaceName, string typeName)`, `GetMembers(IType type)`, `GetTypeNamesInNamespace(string namespaceName)` methods
    - _Requirements: 7.1_

  - [x] 4.2 Update `ISemanticModelBuilder` to accept an optional `IAmbientTypeResolver` parameter
    - Add `IAmbientTypeResolver? ambientTypeResolver` parameter to the `Build` method
    - _Requirements: 7.1_

  - [x] 4.3 Update `IBackend` interface to provide an ambient type resolver
    - Add `IAmbientTypeResolver? GetAmbientTypeResolver()` method to `IBackend`
    - Update `CSharpTranspilerBackend` to implement the new method
    - _Requirements: 7.1, 7.3_

  - [x] 4.4 Implement `CSharpAmbientTypeResolver` in `Source/Mist/Backends/CSharpTranspiler/`
    - Implement the lazy query-based resolver with factory functions and caching
    - Register known .NET types: `System.Console` (WriteLine, ReadLine, Write), `System.String` (Length, Substring, Contains), `System.Math` (Abs, Max, Min), `System.Array` (Length), `System.Convert` (ToInt32, ToString)
    - All resolved symbols tagged with `SymbolOrigin.Backend`
    - _Requirements: 7.3_

  - [x] 4.5 Write unit tests for `CSharpAmbientTypeResolver`
    - Test `HasNamespace` returns true for `System` and false for unknown namespaces
    - Test `ResolveType` returns correct `IType` for known types and null for unknown types
    - Test `GetMembers` returns expected member symbols for resolved types
    - Test `GetTypeNamesInNamespace` returns expected type names
    - Test caching: calling `ResolveType` twice returns the same instance (reference equality)
    - Test lazy materialization: unreferenced types are not in the cache
    - _Requirements: 7.3_

  - [x] 4.6 Update `CompilerDriver` to pass the ambient type resolver through the pipeline
    - Obtain `IAmbientTypeResolver` from the selected backend via `GetAmbientTypeResolver()`
    - Pass it to `ISemanticModelBuilder.Build()`
    - _Requirements: 7.1_

- [x] 5. Checkpoint — Ambient type resolver compiles and unit tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement the semantic model builder (AST walking and name resolution)
  - [x] 6.1 Implement Pass 1 in `SemanticModelBuilder` — register user-defined types
    - Walk each AST top-down; for each `ClassDeclarationNode` or `StructDeclarationNode`, create a type symbol with `SymbolOrigin.UserCode` and register it in the enclosing namespace scope
    - For each `FieldDeclarationNode`, `ConstructorDeclarationNode`, and `MethodDeclarationNode`, create member symbols with `SymbolOrigin.UserCode` in the type's scope
    - Build the scope hierarchy: global → namespace → type → method/constructor → block
    - _Requirements: 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 6.1_

  - [x] 6.2 Implement Pass 2 in `SemanticModelBuilder` — resolve types and build expression types
    - Resolve `TypeReference` nodes to `IType` by looking up names in the scope chain
    - Integrate ambient type resolver: when a type is not found in user-defined scopes, query `IAmbientTypeResolver` (HasNamespace, ResolveType, GetMembers, GetTypeNamesInNamespace)
    - Register resolved ambient types in the scope hierarchy with `SymbolOrigin.Backend`
    - Resolve `using` declarations by verifying namespace existence (user-defined or ambient)
    - Compute and record expression types for all expression nodes
    - Infer types for `var` declarations from initializer expressions
    - Resolve `this` to the enclosing type within instance methods and constructors
    - Resolve member access expressions by looking up member names in the target type's scope
    - Register parameters as `SymbolKind.Parameter` and local variables as `SymbolKind.LocalVariable`
    - Emit diagnostics for undefined identifiers (assign `UnknownType`) and duplicate declarations
    - _Requirements: 5.9, 5.10, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 7.2, 7.4, 8.9_

  - [x] 6.3 Write property test for declaration-to-SymbolKind mapping
    - **Property 4: Declaration-to-SymbolKind mapping**
    - **Validates: Requirements 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**

  - [x] 6.4 Write property test for name resolution scope chain
    - **Property 7: Name resolution follows scope chain**
    - **Validates: Requirements 6.1, 6.2, 6.3**

  - [x] 6.5 Write property test for undefined identifier diagnostics
    - **Property 8: Undefined identifiers produce diagnostics**
    - **Validates: Requirements 6.4**

  - [x] 6.6 Write property test for ambient type on-demand resolution
    - **Property 12: Ambient types are resolved on-demand in user code**
    - **Validates: Requirements 7.1, 7.2, 7.4**

  - [x] 6.7 Write unit tests for semantic model builder
    - Test scope hierarchy construction (global → namespace → type → method → block)
    - Test `using` declaration imports types from referenced namespace
    - Test `this` resolution within instance methods and constructors
    - Test member access resolution on user-defined and ambient types
    - Test duplicate declaration diagnostics
    - Test `SymbolOrigin` tagging: user-defined symbols get `UserCode`, backend symbols get `Backend`
    - _Requirements: 6.1, 6.2, 6.3, 6.5, 6.6, 6.7_

- [x] 7. Checkpoint — Semantic model builder compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement the type checker
  - [x] 8.1 Implement type checking rules in `TypeCheckerAnalyzer`
    - Implement assignment type checking: verify RHS is assignable to LHS declared type
    - Implement method invocation type checking: verify argument types match parameter types
    - Implement arithmetic operator checking: `+`, `-`, `*`, `/`, `%` only on numeric primitives (plus `string` concatenation for `+`)
    - Implement comparison operator checking: `<`, `>`, `<=`, `>=` only on numeric primitives
    - Implement equality operator checking: `==`, `!=` on compatible types
    - Implement logical operator checking: `&&`, `||`, `!` only on `bool`
    - Implement condition checking: `if`, `while`, `for` conditions must be `bool`
    - Implement return type checking: return value must match method return type, void methods return no value
    - Implement `var` inference validation: `var` without initializer emits diagnostic
    - Implement array operation checking: integer index, array target, integer size for `new T[n]`
    - Implement constructor validation: `new T(args)` must match a constructor, `: this(args)` must match
    - Use `UnknownType` for error suppression to prevent cascading diagnostics
    - Emit diagnostics with IDs `MIST_TC_0001` through `MIST_TC_0013`
    - _Requirements: 8.1–8.14_

  - [x] 8.2 Implement access modifier enforcement in `TypeCheckerAnalyzer`
    - Verify `private` members (and members with no modifier, which default to `private`) are accessed only within the declaring type
    - Verify `public` members are accessible from any scope
    - Verify `static` members are accessed via type name, not instance
    - Verify instance members are accessed via instance, not type name
    - Emit diagnostics `MIST_TC_0014`, `MIST_TC_0015`, `MIST_TC_0016`
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

  - [x] 8.3 Implement entry point validation in `TypeCheckerAnalyzer`
    - Verify exactly one class contains `static void Main()` or `static void Main(string[] args)`
    - Emit `MIST_TC_0017` for no entry point, `MIST_TC_0018` for multiple entry points
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 8.4 Write property test for well-typed programs
    - **Property 5: Well-typed programs produce no type errors**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 8.11, 8.12, 8.13, 8.14, 10.1**

  - [x] 8.5 Write property test for type-incompatible operations
    - **Property 6: Type-incompatible operations produce diagnostics**
    - **Validates: Requirements 8.1, 8.3, 8.4, 8.6, 8.7**

  - [x] 8.6 Write property test for access modifier enforcement
    - **Property 9: Access modifier enforcement**
    - **Validates: Requirements 9.1, 9.2, 9.3**

  - [x] 8.7 Write property test for static vs instance access enforcement
    - **Property 10: Static vs instance access enforcement**
    - **Validates: Requirements 9.4, 9.5**

  - [x] 8.8 Write property test for entry point validation
    - **Property 11: Entry point validation**
    - **Validates: Requirements 10.1, 10.2, 10.3**

  - [x] 8.9 Write unit tests for type checker edge cases
    - Test specific diagnostic messages match expected patterns (MIST_TC_0001 through MIST_TC_0018)
    - Test `var` without initializer produces `MIST_TC_0009`
    - Test `UnknownType` suppresses cascading errors
    - Test numeric widening conversions in assignability
    - Test string concatenation with `+` operator
    - _Requirements: 8.1–8.14, 9.1–9.5, 10.1–10.3_

- [x] 9. Checkpoint — Type checker compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement the C# transpiler emitter
  - [x] 10.1 Implement `CSharpTypeMap` in `Source/Mist/Backends/CSharpTranspiler/`
    - Map all Mist primitive types to C# keyword strings
    - Map `StringType`, `VoidType`, `ArrayType`, `ClassType`, `StructType` to C# source representations
    - _Requirements: 11.4, 11.5_

  - [x] 10.2 Rewrite `CSharpEmitter` to walk the semantic model and emit complete C# source
    - Emit one `.cs` file per Mist source file
    - Emit `using` directives for all referenced namespaces (including `SymbolOrigin.Backend` namespaces)
    - Emit namespace declarations mirroring Mist source
    - Emit class/struct declarations with modifiers, fields, constructors, methods
    - Emit method bodies with all statements and expressions translated to valid C#
    - Preserve `var` as `var` in generated C#
    - Emit constructor chaining `: this(args)` syntax
    - Emit array declarations, creation, and indexing
    - Emit string interpolation as C# `$"...{expr}..."`
    - Only emit symbols with `SymbolOrigin.UserCode`; skip `SymbolOrigin.Backend` symbols
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9_

  - [x] 10.3 Update `.csproj` emission to target .NET 10 with correct settings
    - Emit `<OutputType>Exe</OutputType>`, `<TargetFramework>net10.0</TargetFramework>`, `<Deterministic>true</Deterministic>`
    - _Requirements: 11.10_

  - [x] 10.4 Implement `dotnet build` invocation and error mapping
    - After emitting all C# source files, invoke `dotnet build` on the emitted project
    - Parse MSBuild diagnostic output via `RoslynDiagnosticMapper` and surface errors as Mist diagnostics with `PipelineStage.Backend`
    - Report the resulting assembly path on success
    - _Requirements: 11.11, 11.12_

  - [x] 10.5 Write property test for emitter origin filtering
    - **Property 13: C# emitter emits only UserCode symbols**
    - **Validates: Requirements 7.5, 11.1, 11.2**

  - [x] 10.6 Write property test for emitter file-count invariant
    - **Property 14: C# emitter file-count invariant**
    - **Validates: Requirements 11.1, 11.10**

  - [x] 10.7 Write unit tests for C# emitter output
    - Test specific emission patterns: namespace declarations, class/struct with modifiers, method bodies
    - Test `using` directives include ambient type namespaces
    - Test `.csproj` content matches expected settings
    - Test `var` preservation, constructor chaining, array syntax, string interpolation
    - _Requirements: 11.1–11.12_

- [x] 11. Checkpoint — C# emitter compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Implement the pretty printer
  - [x] 12.1 Create `MistPrettyPrinter` in `Source/Mist/Parsing/`
    - Implement AST visitor that pattern-matches on all concrete node types
    - Emit syntactically valid Mist source text with consistent 4-space indentation
    - Handle all node types: compilation unit, using declarations, namespace declarations, class/struct declarations, field/constructor/method declarations, all statement types, all expression types
    - _Requirements: 12.1, 12.2, 12.3_

  - [x] 12.2 Write property test for pretty printer idempotence
    - **Property 2: Pretty printer idempotence**
    - **Validates: Requirements 12.2, 12.4**

  - [x] 12.3 Write property test for pretty printer semantic preservation
    - **Property 3: Pretty printer semantic preservation**
    - **Validates: Requirements 12.3**

- [x] 13. Implement lexer property test and unit tests
  - [x] 13.1 Write property test for lexer round-trip
    - **Property 1: Lexer round-trip (token concatenation reproduces source)**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10**

  - [x] 13.2 Write unit tests for lexer token kinds
    - Test each keyword produces the correct token kind (not `IDENTIFIER`)
    - Test multi-character operators are recognized correctly (`&&`, `||`, `==`, `!=`, `<=`, `>=`)
    - Test literal tokens: integer, hex, float, string, char, interpolated string
    - Test hidden tokens: whitespace and line comments
    - _Requirements: 1.2, 1.3, 1.4, 1.6, 1.7, 1.8, 1.9, 1.10, 1.11_

  - [x] 13.3 Write unit tests for parser rules
    - Test operator precedence: `1 + 2 * 3` parses as `1 + (2 * 3)`
    - Test each statement form parses correctly
    - Test edge cases: empty programs, programs with only comments
    - _Requirements: 2.1–2.11, 3.1–3.9, 4.1–4.8_

- [x] 14. Create the sample application
  - [x] 14.1 Create `samples/HelloMist/Point.mist`
    - Define `Point` class with private fields, constructors (including chaining with `: this()`), methods with return values, and string interpolation
    - _Requirements: 13.2, 13.3, 13.4, 13.5, 13.11_

  - [x] 14.2 Create `samples/HelloMist/Color.mist`
    - Define `Color` struct with public fields, constructor, and method
    - _Requirements: 13.2, 13.3, 13.5_

  - [x] 14.3 Create `samples/HelloMist/Program.mist`
    - Define `Program` class with `static void Main(string[] args)` entry point
    - Exercise all control flow: `if`/`else`, `while`, `for`, `foreach`, `return`, `break`
    - Exercise all operators: arithmetic, comparison, logical
    - Exercise `var` and explicit type declarations
    - Exercise array creation, indexing, and foreach iteration
    - Exercise object instantiation with `new`
    - Exercise all literal types: integer, hex, float, string, interpolated string, char, boolean
    - Exercise `Console.WriteLine` via ambient types
    - _Requirements: 13.1–13.14_

- [x] 15. Wire everything together and run end-to-end integration
  - [-] 15.1 Verify the full pipeline compiles the sample application
    - Run the `mist` CLI on the `samples/HelloMist/` directory
    - Verify the compiler produces a working .NET executable
    - Verify the executable runs without errors
    - _Requirements: 13.14_

  - [x] 15.2 Write integration tests for end-to-end compilation
    - Test compiling the sample application through the full pipeline
    - Test multi-file compilation correctly resolves cross-file type references within the same namespace
    - Test the produced .NET executable runs without errors
    - _Requirements: 13.14_

- [x] 16. Create FsCheck generators for property-based tests
  - [x] 16.1 Create `MistAstGenerator`, `MistProgramGenerator`, and `MistTypeGenerator` in `Test/Mist.Tests/Generators/`
    - Implement FsCheck `Arbitrary<T>` generators for Mist AST nodes, type references, expressions, and complete programs
    - Generators must produce structurally valid ASTs that respect grammar rules
    - Include generators for well-typed programs (for Property 5) and ill-typed programs (for Property 6)
    - _Requirements: supports all property tests_

- [x] 17. Final checkpoint — All tests pass and sample application compiles
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major pipeline stage
- Property tests validate universal correctness properties from the design document (Properties 1–14)
- Unit tests validate specific examples, edge cases, and error conditions
- The implementation language is C# throughout, matching the existing codebase
- FsCheck with xUnit is already configured in the test project (`FsCheck.Xunit` 3.1.0)
- Task 16 (FsCheck generators) can be implemented incrementally alongside the property test tasks, but is listed separately to keep generator code organized
