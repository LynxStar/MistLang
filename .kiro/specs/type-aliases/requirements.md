# Requirements Document

## Introduction

Type aliases introduce type-level expressions to the Mist programming language. A type alias declares a new named type that is equivalent to an existing type expression. This feature supports simple renaming, type unions, generic type parameters, and combinations thereof. The syntax is `type {identifier} = {type_expression};` and the feature must be supported through the full compiler pipeline: lexing, parsing, semantic analysis, and C# transpilation.

## Glossary

- **Mist_Compiler**: The Mist language compiler, encompassing lexing, parsing, semantic analysis, and code generation stages.
- **Lexer**: The Storm-generated lexical analysis component that tokenizes Mist source text.
- **Parser**: The Storm-generated parser that produces an AST from a token stream according to the Mist grammar.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model (symbol table, type information) from the parsed AST.
- **Type_Checker**: The type-checking analyzer that validates type correctness across the program.
- **CSharp_Transpiler**: The C# code generation backend that emits equivalent C# source from the Mist semantic model and AST.
- **Type_Alias_Declaration**: A top-level or namespace-level declaration of the form `type {identifier} = {type_expression};` that introduces a named alias for a type expression.
- **Type_Expression**: The right-hand side of a type alias declaration, which may be a named type reference, a union of type references, or a generic type reference.
- **Type_Parameter**: A generic placeholder declared in angle brackets (e.g., `<TValue>`) on a type alias that is substituted when the alias is used.
- **Union_Type**: A type formed by combining two or more types with the pipe operator (`|`), representing a value that may be any one of the constituent types.

## Requirements

### Requirement 1: Lexing the `type` Keyword

**User Story:** As a compiler developer, I want the lexer to recognize `type` as a keyword token, so that type alias declarations can be distinguished from identifiers during parsing.

#### Acceptance Criteria

1. WHEN the Lexer encounters the character sequence `type` followed by a non-identifier character, THE Lexer SHALL produce a TYPE keyword token.
2. WHEN the Lexer encounters an identifier that starts with `type` but continues with additional identifier characters (e.g., `typeAlias`), THE Lexer SHALL produce an IDENTIFIER token rather than a TYPE keyword token.
3. THE Lexer SHALL preserve the existing behavior for all other tokens when the TYPE keyword is added to the keyword list.

### Requirement 2: Parsing Type Alias Declarations

**User Story:** As a compiler developer, I want the parser to recognize type alias declarations, so that the AST correctly represents the developer's intent.

#### Acceptance Criteria

1. WHEN the Parser encounters `type IDENTIFIER = TypeReference ;` at the top level or within a namespace body, THE Parser SHALL produce a TypeAliasDeclaration AST node containing the alias name and the target type expression.
2. WHEN the Parser encounters `type IDENTIFIER TypeParameterList = TypeReference ;`, THE Parser SHALL produce a TypeAliasDeclaration AST node containing the alias name, the type parameter list, and the target type expression.
3. WHEN the Parser encounters a type alias declaration where the type expression is a union type (e.g., `int | string`), THE Parser SHALL produce a TypeAliasDeclaration AST node whose target type expression is a UnionTypeRef node.
4. WHEN the Parser encounters a type alias declaration missing the `=` token, THE Parser SHALL report a diagnostic error indicating the expected `=` token.
5. WHEN the Parser encounters a type alias declaration missing the terminating `;`, THE Parser SHALL report a diagnostic error indicating the expected `;` token.
6. WHEN the Parser encounters a type alias declaration with an empty type expression after `=`, THE Parser SHALL report a diagnostic error indicating an expected type expression.

### Requirement 3: Pretty-Printing Type Alias Declarations

**User Story:** As a compiler developer, I want the pretty printer to format type alias AST nodes back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a TypeAliasDeclaration node without type parameters, THE Pretty_Printer SHALL emit `type {name} = {type_expression};` with correct spacing and semicolon.
2. WHEN the Pretty_Printer encounters a TypeAliasDeclaration node with type parameters, THE Pretty_Printer SHALL emit `type {name}<{params}> = {type_expression};` with the type parameter list in angle brackets.
3. WHEN the Pretty_Printer encounters a TypeAliasDeclaration node whose target is a union type, THE Pretty_Printer SHALL emit the constituent types separated by ` | `.
4. FOR ALL valid TypeAliasDeclaration AST nodes, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 4: Semantic Model Registration

**User Story:** As a compiler developer, I want type aliases to be registered in the semantic model, so that other parts of the compiler can resolve alias names to their underlying types.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a TypeAliasDeclaration node, THE Semantic_Model_Builder SHALL register the alias name in the symbol table of the enclosing scope.
2. WHEN the Semantic_Model_Builder encounters a generic TypeAliasDeclaration node, THE Semantic_Model_Builder SHALL record the type parameters alongside the alias definition so they can be substituted during type resolution.
3. IF a TypeAliasDeclaration uses a name that is already defined in the same scope, THEN THE Semantic_Model_Builder SHALL report a duplicate symbol diagnostic error.
4. IF a TypeAliasDeclaration references an undefined type in its type expression, THEN THE Semantic_Model_Builder SHALL report an unresolved type diagnostic error.

### Requirement 5: Type Resolution Through Aliases

**User Story:** As a compiler developer, I want type aliases to be transparent during type checking, so that using an alias is equivalent to using the underlying type.

#### Acceptance Criteria

1. WHEN the Type_Checker resolves a type reference that names a type alias, THE Type_Checker SHALL resolve the reference to the underlying type expression of the alias.
2. WHEN the Type_Checker resolves a generic type alias with type arguments (e.g., `IntDict<string>`), THE Type_Checker SHALL substitute the provided type arguments into the alias's type parameters and resolve to the resulting constructed type.
3. IF a generic type alias is used with an incorrect number of type arguments, THEN THE Type_Checker SHALL report a type argument count mismatch diagnostic error.
4. IF a type alias forms a cycle (e.g., `type A = B;` and `type B = A;`), THEN THE Type_Checker SHALL report a cyclic type alias diagnostic error rather than entering infinite resolution.

### Requirement 6: C# Transpilation of Type Aliases

**User Story:** As a compiler developer, I want type aliases to be correctly transpiled to C#, so that the generated code compiles and behaves equivalently.

#### Acceptance Criteria

1. WHEN the CSharp_Transpiler encounters a usage of a simple type alias (non-generic, non-union), THE CSharp_Transpiler SHALL emit a C# `using` alias directive (e.g., `using IntAlias = System.Int32;`) or inline the resolved type at usage sites.
2. WHEN the CSharp_Transpiler encounters a usage of a union type alias, THE CSharp_Transpiler SHALL emit the union type using the existing union helper infrastructure (the `UnionHelperGenerator`).
3. WHEN the CSharp_Transpiler encounters a usage of a generic type alias with concrete type arguments, THE CSharp_Transpiler SHALL emit the fully substituted type at usage sites.
4. THE CSharp_Transpiler SHALL produce C# code that compiles without errors for all valid type alias usages.

### Requirement 7: Grammar Integration

**User Story:** As a compiler developer, I want the Storm grammar to define the type alias declaration rule, so that the source generator produces the correct AST node types.

#### Acceptance Criteria

1. THE Mist_Compiler grammar SHALL define a `TypeAliasDeclaration` rule matching the pattern: `TYPE name:IDENTIFIER typeParams:TypeParameterList? EQUAL type:TypeReference SEMICOLON`.
2. THE Mist_Compiler grammar SHALL include `TypeAliasDeclaration` as a variant in the `TopLevelItem` rule.
3. THE Mist_Compiler grammar SHALL include `TypeAliasDeclaration` as a variant in the `NamespaceBodyItem` rule.
4. THE Mist_Compiler grammar SHALL define `TYPE` as a keyword token with the literal value `"type"`.

### Requirement 8: Editor Syntax Highlighting

**User Story:** As a Mist developer, I want type alias declarations to be syntax-highlighted in my editor, so that the code is readable and the `type` keyword is visually distinct.

#### Acceptance Criteria

1. WHEN the editor encounters the `type` keyword in a type alias declaration, THE syntax highlighting grammar SHALL classify `type` as a keyword token.
2. WHEN the editor encounters the alias name following the `type` keyword, THE syntax highlighting grammar SHALL classify the alias name as a type entity.
