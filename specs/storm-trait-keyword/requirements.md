# Requirements Document

## Introduction

Add a `trait` keyword to the Storm grammar definition language. A trait declares a structural contract (a set of named, typed fields) that grammar rules can bind to. At source-generator time, Storm validates that binding rules satisfy the trait's field shape and emits a C# interface for each trait. Nodes that bind the trait implement the interface, enabling polymorphic code over heterogeneous AST nodes that share a common field shape.

Traits have no parsing semantics — they do not alter the grammar, the parser, or the parse result. They are purely a post-parse structural assertion that affects only the emitted C# AST node types.

### Motivation

In the Mist compiler's ProgramModel construction, `MethodBuilder` and `ConstructorBuilder` have nearly identical logic for resolving modifiers, parameters, and body validation. But they cannot share code because `MethodDeclarationNode` and `ConstructorDeclarationNode` have no shared type — even though both have `.Modifiers`, `.Parameters`, and `.Body` fields. This pattern repeats throughout the compiler: similar logic duplicated across consumers because the AST doesn't express structural commonality.

With traits, a grammar author can declare:

```storm
trait CallableDeclaration =
    attributes:AttributeList*
    modifiers:Modifier*
    parameters:ParameterList?
    withClauses:WithClause*
    constraints:WhereClause*
    body:MethodBody;

rule ConstructorDeclaration : CallableDeclaration = ...;
rule MethodDeclaration : CallableDeclaration = ...;
```

Storm then emits `ICallableDeclarationNode` and both nodes implement it, allowing a single shared resolution method to accept either node type.

## Glossary

- **Storm_Source_Generator**: The Roslyn incremental source generator that lexes, parses, lowers, validates, and emits C# code from `.storm` grammar files. Located at `Source/Storm.SourceGenerator/`.
- **Trait_Declaration**: A top-level declaration in a `.storm` file introduced by the `trait` keyword. Declares a set of named, typed fields that constitute a structural contract.
- **Trait_Field**: A single named field within a Trait_Declaration, consisting of a field name, a type reference (rule name or token name), and an optional cardinality suffix (`?`, `*`, `+`).
- **Trait_Binding**: The explicit opt-in by a rule to satisfy one or more traits, expressed via colon syntax after the rule name (e.g., `rule Foo : TraitA, TraitB = ...`).
- **Trait_Interface**: The C# interface emitted by the Storm_Source_Generator for each Trait_Declaration (e.g., `ICallableDeclarationNode`).
- **Rule_Node**: The C# AST node class emitted for a grammar rule. Either a sealed class (single-alternative) or an abstract base class (multi-alternative).
- **Inline_Block**: An existing Storm construct (`inline PrefixRule: { ... }`) that factors out a shared prefix among alternatives.
- **StormLexer**: The hand-written lexer component of the Storm_Source_Generator that tokenizes `.storm` source text.
- **StormParser**: The hand-written recursive-descent parser component of the Storm_Source_Generator that produces a syntax AST from tokens.
- **GrammarIRLowering**: The pass that lowers the parser AST into the intermediate representation (GrammarIR) used by validation and emission.
- **AstEmitter**: The component that generates C# AST node source files from the GrammarIR.

## Requirements

### Requirement 1: Trait Declaration Lexing

**User Story:** As a grammar author, I want to use the `trait` keyword in `.storm` files, so that the lexer recognizes it as a distinct keyword token.

#### Acceptance Criteria

1. WHEN the StormLexer encounters the character sequence `trait` bounded by non-identifier characters, THE StormLexer SHALL produce a token of kind `TraitKeyword` with lexeme `"trait"`.
2. WHEN the StormLexer encounters an identifier that starts with `trait` but contains additional identifier characters (e.g., `traits`, `traitName`), THE StormLexer SHALL produce a token of kind `Identifier`.
3. WHEN the StormLexer encounters an identifier that differs from `trait` only by casing (e.g., `Trait`, `TRAIT`), THE StormLexer SHALL produce a token of kind `Identifier`.

### Requirement 2: Trait Declaration Parsing

**User Story:** As a grammar author, I want to declare traits with named, typed fields, so that I can define structural contracts for rules.

#### Acceptance Criteria

1. WHEN the StormParser encounters a `TraitKeyword` token followed by an identifier, an equals sign, a space-separated list of one or more trait fields, and a semicolon, THE StormParser SHALL produce a `StormTraitDecl` syntax node containing the trait name and an ordered list of fields.
2. THE StormParser SHALL parse each trait field as a field name identifier, a colon, a type name (a single identifier referencing a rule or token), and an optional cardinality suffix (`?`, `*`, or `+`), where the absence of a suffix indicates exactly-one cardinality.
3. WHEN a trait declaration contains multiple fields, THE StormParser SHALL preserve the declared field order in the resulting `StormTraitDecl` node such that iterating the field list yields fields in source-declaration order.
4. IF a trait declaration is missing the trailing semicolon, THEN THE StormParser SHALL report a diagnostic error at the current token position and recover by synchronizing to the next top-level declaration keyword or end-of-file.
5. IF a trait declaration contains zero fields (the equals sign is immediately followed by a semicolon with no field definitions), THEN THE StormParser SHALL report a diagnostic error indicating that a trait must declare at least one field.
6. IF a trait field is missing the colon between the field name and the type name, THEN THE StormParser SHALL report a diagnostic error at the current token position and attempt to continue parsing subsequent fields.
7. WHEN the StormParser encounters a `TraitKeyword` token, THE StormParser SHALL recognize `trait` as a valid declaration start in the same contexts where `rule`, `token`, and `fragment` declarations are accepted.
8. THE StormParser SHALL accept trait field type names that are plain identifiers only (not qualified references); a qualified reference in a trait field type position SHALL produce a diagnostic error.
9. ALL trait fields MUST be named and typed (i.e., use `name:Type` syntax). There is no concept of an unlabeled or untyped trait field because traits describe the interface shape of emitted node properties.

### Requirement 3: Trait Binding Syntax Parsing

**User Story:** As a grammar author, I want to bind rules to traits using colon syntax, so that I can declare which structural contracts a rule satisfies.

#### Acceptance Criteria

1. WHEN the StormParser encounters a rule declaration with a colon followed by one or more comma-separated identifiers between the rule name and the equals sign (e.g., `rule Foo : Bar, Baz = ...;`), THE StormParser SHALL store those identifiers as trait bindings in declaration order on the `StormRuleDecl` syntax node.
2. WHEN a rule declaration has no colon between the name and the equals sign, THE StormParser SHALL produce a `StormRuleDecl` with an empty trait binding list, preserving backward compatibility with existing grammar files.
3. WHEN a rule declaration is annotated with `@memo` and also includes trait bindings (e.g., `@memo rule Foo : Bar = ...;`), THE StormParser SHALL store the trait bindings on the resulting `StormRuleDecl` with `IsMemo` set to true.
4. IF the StormParser encounters a colon after the rule name but the token following the colon is not a valid identifier, THEN THE StormParser SHALL report a diagnostic error indicating an expected trait name and attempt to recover parsing at the equals sign.
5. Trait bindings SHALL only be permitted on rules without named alternatives (single-alternative rules). IF a multi-alternative rule declares trait bindings, THEN THE Storm_Source_Generator SHALL report a diagnostic error indicating that traits can only be bound to rules without alternatives.
6. WHEN a trait declaration includes a colon followed by one or more comma-separated trait names before the equals sign (e.g., `trait B : A = extraField:Foo;`), THE StormParser SHALL treat this as trait composition — trait B includes all fields from trait A plus its own declared fields.

### Requirement 4: Trait IR Lowering

**User Story:** As a source generator developer, I want trait declarations and bindings lowered into the intermediate representation, so that downstream passes can validate and emit code from them.

#### Acceptance Criteria

1. WHEN the GrammarIRLowering pass encounters a `StormTraitDecl`, THE GrammarIRLowering pass SHALL produce a `TraitIR` node containing the trait name and a list of field specifications where each field preserves its declared name, its type reference resolved to the corresponding `TokenIR` or `RuleIR`, and its cardinality (none, `?`, `*`, or `+`).
2. WHEN the GrammarIRLowering pass encounters a `StormRuleDecl` with trait bindings, THE GrammarIRLowering pass SHALL resolve each binding name to its corresponding `TraitIR` and store resolved references on the `RuleIR`.
3. IF a trait binding name does not match any declared trait, THEN THE GrammarIRLowering pass SHALL report a diagnostic error identifying the unresolved trait name and the declaring rule name.
4. THE GrammarIR SHALL include a collection of all `TraitIR` nodes in declaration order, accessible to downstream passes.
5. IF a trait field's type name does not resolve to any declared token or rule, THEN THE GrammarIRLowering pass SHALL report a diagnostic error identifying the unresolved type name, the field name, and the enclosing trait name.
6. IF two `StormTraitDecl` nodes declare the same trait name, THEN THE GrammarIRLowering pass SHALL report a diagnostic error identifying the duplicate trait name.
7. Trait field type resolution SHALL follow the same token/rule name lookup mechanism used for field labels in rule expressions (i.e., an identifier matches a token if it appears in the token list, otherwise it is treated as a rule reference).
8. Forward references SHALL be supported: a rule may bind a trait that is declared later in the file, and a trait may reference rules or tokens declared after it.

### Requirement 5: Trait Structural Validation

**User Story:** As a grammar author, I want the source generator to validate that binding rules satisfy their declared traits, so that structural mismatches are caught at compile time.

#### Acceptance Criteria

1. WHEN a rule binds a trait, THE Storm_Source_Generator SHALL verify that the rule's field labels include an entry matching each trait field by exact name, same type reference name, and same cardinality suffix.
2. IF a rule binds a trait but has no field label whose name matches a trait field name, THEN THE Storm_Source_Generator SHALL report a diagnostic error identifying the missing field name and the trait that requires it.
3. IF a rule binds a trait and a field label matches a trait field by name but differs in type reference name or cardinality suffix, THEN THE Storm_Source_Generator SHALL report a diagnostic error identifying the field name, the expected type and cardinality from the trait, and the actual type and cardinality found on the rule.
4. WHEN a rule binds a trait that composes other traits (via `trait B : A = ...`), THE Storm_Source_Generator SHALL validate the rule against all fields from the composed hierarchy (B's own fields plus A's fields).
5. IF a rule binds multiple traits that declare fields with the same name, THEN THE Storm_Source_Generator SHALL require the rule's field to satisfy both trait field specifications independently (same name, same type, same cardinality required from both).

### Requirement 6: Trait Interface Emission

**User Story:** As a consumer of the generated AST, I want the source generator to emit C# interfaces for traits, so that I can write polymorphic code over nodes that share a structural contract.

#### Acceptance Criteria

1. FOR EACH Trait_Declaration in the grammar, THE AstEmitter SHALL emit a C# interface named `I{TraitName}Node` in the consumer namespace.
2. THE emitted Trait_Interface SHALL contain a get-only property for each trait field, with the property name being the PascalCase form of the field name and the type determined by the field's type reference and cardinality.
3. WHEN a trait field has cardinality `*` or `+`, THE emitted property type SHALL be `ImmutableArray<{TypeName}Node>`.
4. WHEN a trait field has cardinality `?`, THE emitted property type SHALL be `{TypeName}Node?`.
5. WHEN a trait field has no cardinality suffix, THE emitted property type SHALL be `{TypeName}Node`.
6. WHEN a trait field references a token type and has no cardinality suffix, THE emitted property type SHALL be `Token`.
7. WHEN a trait field references a token type and has cardinality `?`, THE emitted property type SHALL be `Token?`.
8. WHEN a trait field references a token type and has cardinality `*` or `+`, THE emitted property type SHALL be `ImmutableArray<Token>`.
9. WHEN a trait composes other traits (e.g., `trait B : A = ...`), THE emitted interface `IBNode` SHALL extend `IANode` — the composed trait's interface is a base interface of the composing trait's interface.

### Requirement 7: Node Interface Implementation Emission

**User Story:** As a consumer of the generated AST, I want AST node classes to implement trait interfaces, so that I can treat structurally compatible nodes polymorphically.

#### Acceptance Criteria

1. WHEN a single-alternative rule binds one or more traits, THE AstEmitter SHALL emit the sealed node class with all bound Trait_Interfaces in its implements list, ordered by their declaration order in the trait binding syntax.
2. THE emitted node class SHALL satisfy the Trait_Interface contract solely through its existing public properties, without emitting explicit interface implementation members (the node's properties already match the interface property signatures in name and type as guaranteed by Requirement 5 validation).
3. WHEN a trait composes another trait (e.g., `trait B : A = ...`), a rule binding B SHALL cause the emitted node to implement both `IBNode` and `IANode` (the composed trait's interface is included transitively).

### Requirement 8: Inline Block Trait Propagation

**User Story:** As a grammar author, I want inline block target rules to participate in trait bindings placed on the enclosing rule, so that factored alternatives still get trait interfaces.

#### Acceptance Criteria

1. WHEN a rule with trait bindings contains an inline block, THE Storm_Source_Generator SHALL validate that each inline arm's target declaration node satisfies the trait by checking that the target rule's own field labels include entries matching each trait field by name, type, and cardinality.
2. WHEN an inline block arm's target rule satisfies a trait bound on the enclosing rule, THE AstEmitter SHALL emit the Trait_Interface on the target rule's emitted node class (not on the arm wrapper node).
3. IF an inline block arm's target rule does not satisfy a trait bound on the enclosing rule, THEN THE Storm_Source_Generator SHALL report a diagnostic error identifying the arm, the target rule, and the unsatisfied trait.
4. IF an inline block arm's target rule already has its own trait bindings, THEN propagated traits from the enclosing rule SHALL be additive — the target rule implements both its own traits and the propagated traits.
5. IF the same trait would be added to a target rule from multiple sources (own binding + propagation, or multiple enclosing rules), THE AstEmitter SHALL deduplicate it — the interface appears once in the implements list.

### Requirement 9: Multiple Trait Bindings

**User Story:** As a grammar author, I want a single rule to bind multiple traits, so that a node can participate in multiple polymorphic groupings.

#### Acceptance Criteria

1. WHEN a rule binds multiple traits, THE AstEmitter SHALL include all corresponding Trait_Interfaces in the node class's implements list in declaration order.
2. WHEN a rule binds multiple traits and a single property on the node satisfies a field requirement from more than one trait, THE AstEmitter SHALL emit that property once; the node class still satisfies all trait interfaces through that single property.
3. IF a rule binds multiple traits that declare fields with the same name but incompatible type or cardinality, THEN THE Storm_Source_Generator SHALL report a diagnostic error at the rule's trait binding site identifying the conflicting trait field definitions.

### Requirement 10: Trait Declaration Pretty Printing

**User Story:** As a developer working on the Storm source generator, I want the pretty printer to roundtrip trait declarations and trait bindings, so that diagnostic output and tooling can reproduce the grammar accurately.

#### Acceptance Criteria

1. WHEN the StormPrettyPrinter formats a `StormTraitDecl`, THE StormPrettyPrinter SHALL produce output in the form `trait Name = field1:Type1 field2:Type2?;` where each field is rendered as `fieldName:TypeName` immediately followed by the cardinality suffix (`?`, `*`, or `+`) if present, fields are separated by a single space, and field order matches the declaration order in the syntax node.
2. WHEN the StormPrettyPrinter formats a `StormRuleDecl` that has a non-empty trait binding list, THE StormPrettyPrinter SHALL produce output in the form `rule Name : Trait1, Trait2 = <body>;` where trait names are separated by `, ` (comma-space) in declaration order, and the body is printed using the existing expression formatting rules.
3. WHEN the StormPrettyPrinter formats a `StormRuleDecl` that has an empty trait binding list, THE StormPrettyPrinter SHALL produce output in the existing form `rule Name = <body>;` with no colon or trait names.
4. FOR ALL valid trait declarations, parsing the pretty-printed output SHALL produce a `StormTraitDecl` node whose trait name, field names, field type names, and field cardinality suffixes are identical to the original node (structural equality of semantic content).

### Requirement 11: Storm TextMate Grammar Update

**User Story:** As a grammar author using VS Code, I want the `trait` keyword to be syntax-highlighted correctly in `.storm` files, so that I get visual cues consistent with other Storm declaration keywords.

#### Acceptance Criteria

1. THE Storm TextMate grammar (`Editor/mist-syntax-highlighting/syntaxes/storm.tmLanguage.json`) SHALL recognize `trait` as a declaration keyword with scope `keyword.declaration.storm`.
2. THE Storm TextMate grammar SHALL highlight the identifier following `trait` as a type name with scope `entity.name.type.trait.storm`.
3. THE Storm TextMate grammar SHALL highlight the colon and trait names in a rule binding (e.g., `rule Foo : Bar, Baz`) with scope `entity.other.inherited-class.storm` for the trait names.

### Requirement 12: CallableDeclaration Trait in Mist Grammar

**User Story:** As a Mist compiler developer, I want a `CallableDeclaration` trait declared in `Mist.storm` and bound to `ConstructorDeclaration` and `MethodDeclaration`, so that the ProgramModel builders can share callable-resolution logic through a single interface.

#### Acceptance Criteria

1. THE Mist grammar (`Source/Mist/Grammar/Mist.storm`) SHALL declare a trait named `CallableDeclaration` with fields: `attributes:AttributeList*`, `modifiers:Modifier*`, `parameters:ParameterList?`, `withClauses:WithClause*`, `constraints:WhereClause*`, `body:MethodBody`.
2. THE `ConstructorDeclaration` rule SHALL bind the `CallableDeclaration` trait.
3. THE `MethodDeclaration` rule SHALL bind the `CallableDeclaration` trait.
4. THE generated `ICallableDeclarationNode` interface SHALL be emitted in the `Mist.Generated` namespace and implemented by both `ConstructorDeclarationNode` and `MethodDeclarationNode`.

### Requirement 13: Inline Block Trait Validation (ModifiedTypeHead)

**User Story:** As a Mist compiler developer, I want to verify that inline blocks correctly propagate traits to their arm target nodes, using the existing `ModifiedTypeHead` inline in `MemberDeclaration` as a real-world validation case.

#### Acceptance Criteria

1. WHEN the `MemberDeclaration` rule's inline block for `ModifiedTypeHead` is processed, THE Storm_Source_Generator SHALL emit an `IModifiedTypeHeadNode` interface (or equivalent based on the inline's prefix rule name) and apply it to `MemberTypeAliasDeclarationNode` and `ModifiedAssociatedTypeSlotDeclarationNode`.
2. This behavior SHALL emerge naturally from Requirement 8 (Inline Block Trait Propagation) without any special-casing for this particular inline block.

### Requirement 14: Zero Downstream Impact

**User Story:** As a Mist compiler developer, I want the trait feature to be purely additive — only Storm's source generator changes implementation, while Mist and Mist.CLI remain untouched.

#### Acceptance Criteria

1. THE `Source/Mist/` project SHALL NOT require any code changes to compile after the trait feature is implemented. New interfaces appear on existing node classes, but no existing code needs updating.
2. THE `Source/Mist.CLI/` project SHALL NOT require any code changes.
3. ALL existing tests in `Test/Mist.Tests/` and `Test/Mist.CLI.Tests/` SHALL continue to pass without modification.
4. Only `Source/Storm.SourceGenerator/`, `Test/Storm.SourceGenerator.Tests/`, `Source/Mist/Grammar/Mist.storm`, and `Editor/mist-syntax-highlighting/` SHALL contain implementation changes.
