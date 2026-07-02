# Requirements Document

## Introduction

This feature unifies all type declaration grammar rules in the Mist Storm grammar into a single `TypeDeclaration` rule. Currently, class, struct, and abstract declarations are three separate grammar rules (`ClassDeclaration`, `StructDeclaration`, `AbstractDeclaration`) with nearly identical structure that differ only in their keyword and member rule. Enums, newtypes, and type aliases are also separate top-level items. The goal is to merge these into a single `TypeDeclaration` alternation rule with a unified member rule, moving the distinction between valid member kinds from the parser to semantic analysis. This eliminates duplicate grammar rules across `TopLevelItem`, `NamespaceBodyItem`, and `MemberDeclaration`, and simplifies all downstream consumers that currently switch on multiple AST node variants.

## Glossary

- **Grammar**: The Mist PEG grammar defined in `Mist.storm`, which produces AST node types via a source generator.
- **TypeDeclaration**: The unified alternation rule that encompasses all type-introducing declarations (class, struct, abstract, enum, newtype, type alias).
- **TypeKeyword**: The variant token that distinguishes the kind of type being declared (CLASS, STRUCT, ABSTRACT).
- **MemberDeclaration**: The unified rule listing all syntactically valid member kinds within a type body.
- **AbstractMemberDeclaration**: The current (pre-unification) rule listing members valid only in abstract types. After unification, these members fold into `MemberDeclaration`.
- **SemanticPass**: The compiler phases (TypeBuilder, ConcreteTypeBuilder, AbstractTypeBuilder, analyzers) that validate which members and modifiers are valid for a given type kind.
- **TopLevelItem**: The grammar rule for items that appear at file scope.
- **NamespaceBodyItem**: The grammar rule for items inside namespace blocks.
- **AST**: The Abstract Syntax Tree produced by the Storm parser from the grammar rules.
- **PrettyPrinter**: The Mist pretty-printer that formats AST nodes back to source text.
- **CSharpEmitter**: The backend that transpiles the Mist AST/semantic model to C# source.

## Requirements

### Requirement 1: Unified Type Declaration Body Rule

**User Story:** As a compiler developer, I want class, struct, and abstract declarations to share a single grammar structure with a keyword variant, so that there is one code path to parse and process type declarations.

#### Acceptance Criteria

1. THE Grammar SHALL define a single body rule (e.g., `TypeBody`) that accepts a type keyword variant (CLASS, STRUCT, or ABSTRACT), followed by optional attributes, modifiers, name, type parameters, base clause, where constraints, and a brace-delimited member list.
2. THE TypeDeclaration rule SHALL include the unified body rule as one of its alternations (alongside enum, newtype, and type alias variants).
3. WHEN a source file contains a `class`, `struct`, or `abstract` declaration, THE Grammar SHALL parse all three using the same unified body rule, differing only in the matched keyword token.
4. THE unified body rule SHALL accept `MemberDeclaration*` (the unified member list) for all three keyword variants, with no syntactic restriction based on keyword.

### Requirement 2: Unified Member Declaration Rule

**User Story:** As a compiler developer, I want all member kinds (concrete and abstract) to be syntactically valid in any type body, so that the parser does not enforce type-kind restrictions.

#### Acceptance Criteria

1. THE MemberDeclaration rule SHALL include all current concrete member kinds: constructor, method, operator, cast operator, property, indexer, default block, field, nested type, member type alias, associated type, and unconstrained associated type.
2. THE MemberDeclaration rule SHALL additionally include abstract method declarations (methods without a body) and abstract property declarations as syntactically valid alternatives.
3. WHEN a method declaration has no body (semicolon terminator), THE Grammar SHALL parse it as a valid member in any type body.
4. THE Grammar SHALL remove the separate `AbstractMemberDeclaration` rule entirely after folding its members into `MemberDeclaration`.

### Requirement 3: TypeDeclaration Encompasses All Named-Type Introductions

**User Story:** As a compiler developer, I want enums, newtypes, and type aliases to be alternations within `TypeDeclaration`, so that one rule reference covers all type-introducing declarations.

#### Acceptance Criteria

1. THE TypeDeclaration rule SHALL include `EnumDeclaration` as an alternation variant.
2. THE TypeDeclaration rule SHALL include `NewtypeDeclaration` as an alternation variant.
3. THE TypeDeclaration rule SHALL include `TypeAliasDeclaration` as an alternation variant.
4. WHEN a newtype or type alias appears at top level, in a namespace body, or as a nested member, THE Grammar SHALL parse it through the `TypeDeclaration` rule.

### Requirement 4: Eliminate Duplicate Type References Across Scopes

**User Story:** As a compiler developer, I want `TopLevelItem`, `NamespaceBodyItem`, and `MemberDeclaration` to each reference `TypeDeclaration` once instead of listing type variants separately, so that adding a new type kind requires only one grammar change.

#### Acceptance Criteria

1. THE TopLevelItem rule SHALL reference `TypeDeclaration` as a single alternation that covers class, struct, abstract, enum, newtype, and type alias items.
2. THE TopLevelItem rule SHALL NOT have separate alternations for `NewtypeDeclaration` or `TypeAliasDeclaration` in addition to `TypeDeclaration`.
3. THE NamespaceBodyItem rule SHALL reference `TypeDeclaration` as a single alternation that covers all type-introducing declarations.
4. THE NamespaceBodyItem rule SHALL NOT have separate alternations for `NewtypeDeclaration` or `TypeAliasDeclaration` in addition to `TypeDeclaration`.
5. THE MemberDeclaration rule SHALL reference `TypeDeclaration` for nested types (covering nested enums, newtypes, and type aliases in addition to nested classes, structs, and abstracts).

### Requirement 5: Semantic Validation of Member-Kind Restrictions

**User Story:** As a compiler developer, I want the semantic pass to reject members that are invalid for a given type kind, so that language rules are still enforced despite the relaxed grammar.

#### Acceptance Criteria

1. WHEN a class or struct body contains an abstract method (method without body), THE SemanticPass SHALL report a diagnostic error indicating that bodyless methods require the `abstract` type kind.
2. WHEN an enum body contains a field, method, or other non-enum-member declaration, THE SemanticPass SHALL report a diagnostic error.
3. WHEN a struct body contains a default constructor, THE SemanticPass SHALL report a diagnostic error if Mist's language rules prohibit default constructors on structs.
4. THE SemanticPass SHALL preserve all existing language rules about which members are valid in which type kind; the grammar relaxation does not change the language semantics.

### Requirement 6: Downstream Consumer Simplification

**User Story:** As a compiler developer, I want downstream consumers (TypeBuilder, ConcreteTypeBuilder, AbstractTypeBuilder, PrettyPrinter, CSharpEmitter) to operate on the unified AST nodes rather than switching across separate class/struct/abstract node types.

#### Acceptance Criteria

1. WHEN the TypeBuilder encounters a unified type declaration node, THE TypeBuilder SHALL determine the type kind from the keyword variant and create the appropriate `TypeDefinition` subclass (ClassDefinition, StructDefinition, AbstractType).
2. WHEN the ConcreteTypeBuilder and AbstractTypeBuilder process members, they SHALL operate on the unified `MemberDeclaration` node type.
3. THE PrettyPrinter SHALL reproduce the original keyword (class, struct, or abstract) when printing a unified type declaration.
4. THE CSharpEmitter SHALL emit the correct C# construct based on the keyword variant of the unified type declaration.

### Requirement 7: Round-Trip Preservation

**User Story:** As a compiler developer, I want parsing a Mist source file and pretty-printing the resulting AST to produce equivalent source text, so that the grammar refactoring does not break the parser-printer round trip.

#### Acceptance Criteria

1. FOR ALL valid Mist source files containing type declarations, parsing then pretty-printing SHALL produce output that, when parsed again, yields an equivalent AST (round-trip property).
2. THE PrettyPrinter SHALL emit the correct keyword token (class, struct, abstract, enum) matching the original source for each type declaration.
3. THE PrettyPrinter SHALL emit member declarations in their original order and with correct formatting regardless of the unified member rule.

### Requirement 8: Zero Regressions in Existing Tests

**User Story:** As a compiler developer, I want the grammar refactoring to pass all existing parser, semantic, and end-to-end tests, so that the change is purely structural with no behavioral differences.

#### Acceptance Criteria

1. WHEN the refactored grammar is used, THE existing test suite SHALL pass with zero failures (excluding tests that explicitly test the old AST node type names, which must be updated).
2. THE codebase SHALL build with zero warnings after the refactoring is complete.
3. WHEN an existing sample program is compiled with the refactored grammar, THE compiled output SHALL be identical to output produced by the pre-refactoring grammar.
