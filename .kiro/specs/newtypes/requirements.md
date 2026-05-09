# Requirements Document

## Introduction

Newtypes introduce nominally distinct type cloning to the Mist programming language. Unlike type aliases (which create transparent synonyms), a newtype creates a distinct type that is a structural clone of its source type — equivalent to copy-pasting the source type's definition and renaming it. The syntax reuses the existing `type` keyword: `type Name = new SourceType;`. The clone includes all member categories: fields, properties, methods, constructors, operators, cast operators, and indexers. No filtering or transformation is performed — the newtype is a complete copy with a new identity.

## Glossary

- **Mist_Compiler**: The Mist language compiler, encompassing lexing, parsing, semantic analysis, and code generation stages.
- **Lexer**: The Storm-generated lexical analysis component that tokenizes Mist source text.
- **Parser**: The Storm-generated parser that produces an AST from a token stream according to the Mist grammar.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model (symbol table, type information) from the parsed AST.
- **Type_Checker**: The type-checking analyzer that validates type correctness across the program.
- **CSharp_Transpiler**: The C# code generation backend that emits equivalent C# source from the Mist semantic model and AST.
- **Newtype_Declaration**: A top-level or namespace-level declaration of the form `type Name = new SourceType;` that introduces a new distinct type cloned from a source type.
- **Source_Type**: The type on the right-hand side of `new` in a newtype declaration, from which all members are cloned.

## Requirements

### Requirement 1: Newtype Declaration Syntax

**User Story:** As a Mist developer, I want to declare newtypes using the `type Name = new SourceType;` syntax, so that I can create distinct types that clone the structure of existing types.

#### Acceptance Criteria

1. WHEN the Parser encounters `type IDENTIFIER = new TypeReference ;` at the top level or within a namespace body, THE Parser SHALL produce a NewtypeDeclaration AST node containing the newtype name and the source type reference.
2. WHEN the Parser encounters a newtype declaration missing the `new` keyword after `=`, THE Parser SHALL parse the declaration as a regular TypeAliasDeclaration (existing behavior).
3. WHEN the Parser encounters a newtype declaration missing the terminating `;`, THE Parser SHALL report a diagnostic error indicating the expected `;` token and the line and column position where `;` was expected.
4. IF the Parser encounters `type IDENTIFIER = new` followed by a token that is not a valid start of a TypeReference, THEN THE Parser SHALL report a diagnostic error indicating that a source type is expected after `new`.

### Requirement 2: Newtype as Distinct Type

**User Story:** As a Mist developer, I want newtypes to be distinct from their source types, so that the type system prevents accidental substitution between the newtype and its source.

#### Acceptance Criteria

1. WHEN the Type_Checker encounters a newtype, THE Type_Checker SHALL treat the newtype as a distinct nominal type that is not assignable to or from its source type without a cast expression.
2. WHEN the Type_Checker resolves member access on a newtype instance, THE Type_Checker SHALL resolve against the cloned member set, not the original source type members.
3. IF a value of the source type is used where the newtype is expected (variable initialization, method argument, or return statement) without a cast expression, THEN THE Type_Checker SHALL report a type mismatch diagnostic error.
4. IF a value of the newtype is used where the source type is expected (variable initialization, method argument, or return statement) without a cast expression, THEN THE Type_Checker SHALL report a type mismatch diagnostic error.
5. IF a value of one newtype is assigned to a variable of a different newtype that shares the same source type, THEN THE Type_Checker SHALL report a type mismatch diagnostic error.

### Requirement 3: Semantic Model Registration

**User Story:** As a compiler developer, I want newtypes to be registered in the semantic model with their cloned member sets, so that downstream compiler phases can use the type information.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a NewtypeDeclaration node, THE Semantic_Model_Builder SHALL register the newtype name as a distinct type symbol (SymbolKind distinguishable from TypeAlias, Class, and Struct) in the symbol table of the enclosing scope.
2. WHEN the Semantic_Model_Builder encounters a NewtypeDeclaration node, THE Semantic_Model_Builder SHALL clone all members (fields, properties, methods, constructors, operators, cast operators, indexers) from the source type and populate the newtype's member scope with the cloned set.
3. IF a NewtypeDeclaration uses a name that is already defined in the same scope, THEN THE Semantic_Model_Builder SHALL report a duplicate symbol diagnostic error and skip registration of the duplicate newtype (the existing symbol remains unchanged).
4. IF a NewtypeDeclaration references an undefined source type, THEN THE Semantic_Model_Builder SHALL report an unresolved type diagnostic error and assign an error type to the newtype symbol so that downstream phases do not produce cascading errors.

### Requirement 4: Pretty-Printing Newtype Declarations

**User Story:** As a compiler developer, I want the pretty printer to format newtype AST nodes back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a NewtypeDeclaration node, THE Pretty_Printer SHALL emit `type {name} = new {source_type};` on a single line.
2. FOR ALL valid NewtypeDeclaration AST nodes, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 5: C# Transpilation of Newtypes

**User Story:** As a compiler developer, I want newtypes to be correctly transpiled to C#, so that the generated code compiles and preserves the structural clone semantics.

#### Acceptance Criteria

1. WHEN the CSharp_Transpiler encounters a newtype whose source is a struct, THE CSharp_Transpiler SHALL emit a C# struct declaration named after the newtype containing the cloned member set.
2. WHEN the CSharp_Transpiler encounters a newtype whose source is a class, THE CSharp_Transpiler SHALL emit a C# class declaration named after the newtype containing the cloned member set.
3. WHEN the CSharp_Transpiler encounters a newtype whose source is a primitive type, THE CSharp_Transpiler SHALL emit a C# readonly struct wrapping the primitive value with an explicit conversion operator from the primitive to the newtype and an explicit conversion operator from the newtype to the primitive.
4. THE CSharp_Transpiler SHALL produce C# code that compiles without errors for all valid newtype declarations.

### Requirement 6: Grammar Integration

**User Story:** As a compiler developer, I want the Storm grammar to define the newtype declaration rule, so that the source generator produces the correct AST node type.

#### Acceptance Criteria

1. THE Mist_Compiler grammar SHALL define a `NewtypeDeclaration` rule matching the pattern: `TYPE name:IDENTIFIER EQUAL NEW sourceType:TypeReference SEMICOLON`.
2. THE Mist_Compiler grammar SHALL include `NewtypeDeclaration` as a variant in the `TopLevelItem` rule with field name `newtypeDeclaration` and label `as NewtypeItem`.
3. THE Mist_Compiler grammar SHALL include `NewtypeDeclaration` as a variant in the `NamespaceBodyItem` rule with field name `newtypeDeclaration` and label `as NamespaceNewtypeItem`.

### Requirement 7: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want newtype declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `new` following `=` in a type declaration context as part of the newtype syntax (scope `keyword.operator.new.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `type` in a newtype declaration as a type name (scope `entity.name.type.newtype.mist`).

### Requirement 8: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating newtypes, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Newtypes/Program.mist` with an accompanying `mist.toml` project file following the same structure as other sample projects.
2. The sample SHALL demonstrate: simple newtype from primitive (int, string, bool), newtype from struct, newtype from class, member access on newtype instances, and explicit casting between newtype and source type.
3. WHEN the sample is executed, THE System SHALL print output to stdout where each demonstrated pattern is preceded by a section header line in the format `--- <Pattern Name> ---` and each result value is printed in `key=value` format, so that output is deterministic and diff-comparable against a stored baseline.
4. A baseline file SHALL exist at `samples/__baselines__/Newtypes.txt` containing the expected stdout output, enabling automated correctness verification by comparing actual output to the baseline.
5. `samples/AllFeatures/Program.mist` SHALL call `Newtypes.Program.RunDemo()` to include the feature in the full-suite sample run.
6. WHEN the sample is compiled and run using the Mist compiler, THE System SHALL produce a zero exit code and emit no diagnostic errors or warnings.
