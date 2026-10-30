# Requirements Document

## Introduction

This document specifies the requirements for adding traits to the Mist programming language. Traits are a structural typing mechanism that allows a type to satisfy a trait contract based on the shape of its public members, without requiring an explicit declaration linking the two. Unlike Mist's existing interfaces (which use nominal typing and require `: IFoo` on implementing types), traits use late-bound structural matching: any type whose public members match the trait's declared members automatically satisfies the trait.

When a type's public members match a trait by name and type signature, the type is considered a structural match. When the member names differ but the types are compatible, an explicit `with` block can map the type's members to the trait's members. Traits are a compile-time concept — they do not exist at runtime and must be lowered to C# constructs (likely generated interfaces with explicit implementations) during emission.

Key characteristics:
- **Structural typing**: A type satisfies a trait if its public members match the trait's members by name and type signature, with no explicit declaration required.
- **`with` blocks**: Explicit member mapping for types whose members have different names but compatible types.
- **Visibility constraint**: Traits can only bind against publicly visible members. All trait members are implicitly public.
- **Compile-time erasure**: Traits are resolved and lowered during compilation; the emitted C# uses interfaces and explicit implementations.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Trait_Declaration**: The syntax for declaring a trait: `trait Name { members... }`.
- **TraitType**: The semantic model representation of a user-defined trait type, including its member scope and the set of member signatures that constitute the trait contract.
- **Trait_Member**: A member declared inside a Trait_Declaration. Trait members can be field signatures, method signatures, or property signatures. All trait members are implicitly public.
- **Structural_Match**: A type satisfies a trait when every Trait_Member has a corresponding public member on the type with the same name and compatible type signature, without any explicit declaration linking the two.
- **With_Block**: A top-level declaration of the form `with TypeName : TraitName { mappings... }` that explicitly maps members from a type to a trait when the names differ.
- **Member_Mapping**: A single entry inside a With_Block of the form `sourceMember as traitMember;` that maps a public member of the type to a member of the trait.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors, properties) of a type.
- **Method_Signature**: The combination of a method's name, return type, and parameter types that uniquely identifies a method for trait satisfaction checking.
- **ClassType**: The semantic model representation of a user-defined class type.
- **StructType**: The semantic model representation of a user-defined struct type.
- **InterfaceType**: The semantic model representation of a user-defined interface type (nominal typing, existing feature).

## Requirements

### Requirement 1: Trait Keyword and Declaration Grammar

**User Story:** As a Mist developer, I want to declare traits using the `trait` keyword, so that I can define structural contracts that types can satisfy implicitly.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TRAIT` keyword token with the value `"trait"`.
2. THE Grammar SHALL define a `TraitDeclaration` rule that consists of the `TRAIT` keyword, a name identifier, a left brace, zero or more Trait_Member declarations, and a right brace.
3. THE Grammar SHALL define a `TraitFieldDeclaration` rule that consists of a type reference, a name identifier, and a semicolon.
4. THE Grammar SHALL define a `TraitMethodDeclaration` rule that consists of a return type reference, a name identifier, a left parenthesis, an optional parameter list, a right parenthesis, and a semicolon.
5. THE Grammar SHALL define a `TraitPropertyDeclaration` rule that consists of a type reference, a name identifier, a left brace, one or more accessor signatures (get, set, or init followed by a semicolon), and a right brace.
6. THE Grammar SHALL include `TraitDeclaration` as a variant in the `TypeDeclaration` rule alongside `ClassDeclaration`, `StructDeclaration`, and `InterfaceDeclaration`.
7. WHEN a trait declaration is parsed, THE Parser SHALL produce an AST node that captures the trait name and the list of Trait_Member declarations.
8. WHEN a trait declaration contains no members, THE Parser SHALL produce an AST node with an empty member list.

### Requirement 2: With Block Grammar

**User Story:** As a Mist developer, I want to write `with` blocks that map a type's members to a trait's members, so that I can satisfy a trait even when my type's member names differ from the trait's.

#### Acceptance Criteria

1. THE Grammar SHALL define a `WITH` keyword token with the value `"with"`.
2. THE Grammar SHALL define a `WithDeclaration` rule that consists of the `WITH` keyword, a type name identifier, a colon, a trait name identifier, a left brace, one or more Member_Mapping declarations, and a right brace.
3. THE Grammar SHALL define a `MemberMapping` rule that consists of a source member name identifier, the `AS` keyword token (with value `"as"`), a target trait member name identifier, and a semicolon.
4. THE Grammar SHALL include `WithDeclaration` as a variant in the `TopLevelItem` rule, so that `with` blocks can appear at the top level of a compilation unit alongside namespace and type declarations.
5. WHEN a `with` block is parsed, THE Parser SHALL produce an AST node that captures the type name, the trait name, and the list of Member_Mapping entries.
6. WHEN a `with` block contains a mapping where the source and target names are the same, THE Parser SHALL accept the mapping without error (semantic validation handles redundancy).

### Requirement 3: Pretty-Printer Support for Traits and With Blocks

**User Story:** As a Mist developer, I want the pretty-printer to correctly format trait declarations and `with` blocks, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a trait declaration as `trait Name {` followed by its member signatures and a closing `}`.
2. THE Pretty_Printer SHALL format each trait field member as `type name;` with proper indentation inside the trait body.
3. THE Pretty_Printer SHALL format each trait method member as `returnType name(parameters);` with proper indentation inside the trait body.
4. THE Pretty_Printer SHALL format each trait property member as `type name { get; set; }` (or the applicable accessor combination) with proper indentation inside the trait body.
5. THE Pretty_Printer SHALL format a `with` block as `with TypeName : TraitName {` followed by its member mappings and a closing `}`.
6. THE Pretty_Printer SHALL format each member mapping as `sourceName as targetName;` with proper indentation inside the `with` block body.
7. FOR ALL valid Mist ASTs containing trait declarations and `with` blocks, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 4: TraitType Semantic Model

**User Story:** As a Mist developer, I want the compiler to represent traits in the semantic model, so that structural matching and `with` block resolution can be validated during compilation.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL create a `TraitType` for each Trait_Declaration, storing the trait name and a Member_Scope containing its Trait_Member declarations.
2. THE Semantic_Model_Builder SHALL register a symbol with `SymbolKind.Trait` for each Trait_Declaration in the parent scope.
3. THE Semantic_Model_Builder SHALL register a symbol for each Trait_Member in the TraitType's Member_Scope: `SymbolKind.Field` for field members, `SymbolKind.Method` for method members, and `SymbolKind.Property` for property members.
4. THE `TypeKind` enum SHALL include a `Trait` variant to classify TraitType instances.
5. THE `SymbolKind` enum SHALL include a `Trait` variant to classify trait symbols.

### Requirement 5: Structural Match Resolution

**User Story:** As a Mist developer, I want the compiler to automatically determine whether a type structurally satisfies a trait based on its public members, so that I do not need to explicitly declare trait conformance.

#### Acceptance Criteria

1. WHEN a type is checked for structural satisfaction of a trait, THE Semantic_Model_Builder SHALL compare each Trait_Member against the type's public members by name and type signature.
2. WHEN every Trait_Member has a corresponding public member on the type with the same name and a compatible type signature, THE Semantic_Model_Builder SHALL consider the type a Structural_Match for the trait.
3. WHEN a trait field member is matched, THE Semantic_Model_Builder SHALL accept a public field or a public property with a compatible type on the candidate type as satisfying the trait field.
4. WHEN a trait method member is matched, THE Semantic_Model_Builder SHALL require the candidate type to have a public method with the same name, same return type, and same parameter types (count and types).
5. WHEN a trait property member is matched, THE Semantic_Model_Builder SHALL require the candidate type to have a public property with the same name, compatible type, and at least the accessors declared in the trait.
6. THE Semantic_Model_Builder SHALL only consider publicly visible members of the candidate type when checking structural satisfaction; private members SHALL be excluded from matching.
7. WHEN a candidate type has a matching member by name but with an incompatible type signature, THE Semantic_Model_Builder SHALL treat the trait as not satisfied by that type.

### Requirement 6: With Block Resolution and Validation

**User Story:** As a Mist developer, I want the compiler to validate `with` blocks and use them to establish trait satisfaction when structural matching by name alone is insufficient.

#### Acceptance Criteria

1. WHEN a `with` block is encountered, THE Semantic_Model_Builder SHALL resolve the type name to a ClassType or StructType and the trait name to a TraitType.
2. WHEN the type name in a `with` block does not resolve to a declared class or struct, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the type is undefined.
3. WHEN the trait name in a `with` block does not resolve to a declared trait, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the trait is undefined.
4. WHEN a Member_Mapping references a source member name that does not exist as a public member on the type, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the source member is undefined or not public.
5. WHEN a Member_Mapping references a target trait member name that does not exist in the trait, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the target trait member is undefined.
6. WHEN a Member_Mapping maps a source member to a target trait member with an incompatible type signature, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the type mismatch.
7. WHEN a `with` block provides valid mappings for all unmapped trait members (those not already structurally matched by name), THE Semantic_Model_Builder SHALL consider the type as satisfying the trait through the combined structural match and explicit mappings.
8. WHEN a `with` block does not provide mappings for all trait members that are not structurally matched, THE Semantic_Model_Builder SHALL emit a diagnostic error listing the unsatisfied trait members.
9. WHEN multiple `with` blocks exist for the same type-trait pair, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating a duplicate `with` block.

### Requirement 7: Trait Assignability in the Type Checker

**User Story:** As a Mist developer, I want to assign a value of any type that satisfies a trait (structurally or via a `with` block) to a trait-typed variable, so that I can write generic code that accepts any structurally compatible type.

#### Acceptance Criteria

1. WHEN the target type of an assignment is a TraitType and the source type structurally satisfies the trait, THE Type_Checker SHALL treat the assignment as valid.
2. WHEN the target type of an assignment is a TraitType and the source type satisfies the trait through a `with` block, THE Type_Checker SHALL treat the assignment as valid.
3. WHEN the target type of an assignment is a TraitType and the source type does not satisfy the trait (neither structurally nor via a `with` block), THE Type_Checker SHALL emit a diagnostic error indicating the type does not satisfy the trait.
4. WHEN a method parameter is typed as a TraitType, THE Type_Checker SHALL accept an argument of any type that satisfies the trait (structurally or via a `with` block).
5. WHEN a method return type is a TraitType, THE Type_Checker SHALL accept a return statement returning a value of any type that satisfies the trait (structurally or via a `with` block).

### Requirement 8: Trait Member Access

**User Story:** As a Mist developer, I want to access members on a trait-typed variable using the trait's member names, so that I can call methods and read fields/properties through the trait contract.

#### Acceptance Criteria

1. WHEN a member access expression targets a variable typed as a TraitType, THE Semantic_Model_Builder SHALL resolve the member name against the TraitType's Member_Scope.
2. WHEN the member name is found in the TraitType's Member_Scope, THE Semantic_Model_Builder SHALL return the member's declared type.
3. WHEN the member name is not found in the TraitType's Member_Scope, THE Semantic_Model_Builder SHALL emit a `MIST_UNDEFINED_MEMBER (historical)` diagnostic error.
4. WHEN a method call targets a trait-typed variable, THE Type_Checker SHALL validate the arguments against the trait method's parameter types.

### Requirement 9: C# Code Emission for Traits

**User Story:** As a Mist developer, I want the transpiled C# code to correctly implement trait semantics using C# interfaces and explicit implementations, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. THE Emitter SHALL emit each Trait_Declaration as a C# interface declaration with the trait's member signatures translated to C# interface members.
2. THE Emitter SHALL emit trait field members as C# interface properties with a getter (since C# interfaces cannot have fields).
3. THE Emitter SHALL emit trait method members as C# interface method signatures.
4. THE Emitter SHALL emit trait property members as C# interface property signatures with the corresponding accessors.
5. WHEN a type structurally satisfies a trait, THE Emitter SHALL emit the C# type declaration with the generated trait interface in its inheritance list (e.g., `class Foo : ITraitName`).
6. WHEN a type satisfies a trait through a `with` block, THE Emitter SHALL emit explicit interface implementations on the type that delegate to the mapped members (e.g., `int ITraitName.x => this.a;`).
7. WHEN a type satisfies a trait both structurally (some members match by name) and via a `with` block (remaining members are mapped), THE Emitter SHALL emit the trait interface in the inheritance list and explicit interface implementations only for the mapped members.

### Requirement 10: Trait Diagnostic Messages

**User Story:** As a Mist developer, I want clear and specific error messages when trait satisfaction fails, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN a type is assigned to a trait-typed variable and the type does not satisfy the trait, THE Compiler SHALL emit a diagnostic that names the trait, the type, and lists each unsatisfied trait member.
2. WHEN a `with` block maps a member with an incompatible type, THE Compiler SHALL emit a diagnostic that names the source member, the target trait member, the expected type, and the actual type.
3. WHEN a `with` block references an undefined source member, THE Compiler SHALL emit a diagnostic that names the member and the type being mapped.
4. WHEN a `with` block references an undefined target trait member, THE Compiler SHALL emit a diagnostic that names the member and the trait.
5. WHEN a type has a member with the correct name but wrong visibility (private instead of public), THE Compiler SHALL emit a diagnostic indicating the member exists but is not publicly accessible for trait matching.

### Requirement 11: Trait and Interface Coexistence

**User Story:** As a Mist developer, I want traits and interfaces to coexist without conflict, so that I can use nominal typing (interfaces) and structural typing (traits) in the same program.

#### Acceptance Criteria

1. THE Compiler SHALL allow a type to implement interfaces (nominal) and satisfy traits (structural) simultaneously without conflict.
2. THE Compiler SHALL keep trait satisfaction and interface implementation as independent checks; satisfying a trait SHALL NOT imply implementing an interface, and implementing an interface SHALL NOT imply satisfying a trait.
3. WHEN a trait and an interface have the same member signatures, THE Compiler SHALL treat them as independent contracts; a type must satisfy each independently.
4. THE Compiler SHALL allow a trait-typed variable and an interface-typed variable to coexist in the same scope without ambiguity.

### Requirement 12: Syntax Highlighting for Trait and With Keywords

**User Story:** As a Mist developer using an editor, I want the `trait` and `with` keywords and trait declaration names to be syntax-highlighted, so that trait code is visually consistent with other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `trait` in the `keyword.declaration.mist` pattern alongside `class`, `struct`, and `interface`.
2. THE TextMate grammar SHALL include a `declaration-names` pattern that matches `trait` followed by an identifier and assigns the identifier the scope `entity.name.type.trait.mist`.
3. THE TextMate grammar SHALL include `with` and `as` in the keyword patterns so they are highlighted as keywords.
4. WHEN a Mist source file contains a `trait` keyword, THE editor SHALL highlight it with the same style as `class`, `struct`, and `interface` keywords.

### Requirement 13: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented trait features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the trait feature is implemented, THE Compiler team SHALL update the `Type Declarations` section of CSHARP_FEATURES.md to include a `Traits (structural typing)` entry marked as ✅ Implemented.
2. WHEN the trait feature is implemented, THE Compiler team SHALL add a `Structural Typing` section to CSHARP_FEATURES.md documenting trait declarations, structural matching, and `with` blocks.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 14: Trait Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates traits in action, so that I can see how trait declaration, structural matching, `with` blocks, and trait-typed parameters work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `samples/Traits/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate declaring a trait with field members.
3. THE sample application SHALL demonstrate a type that structurally satisfies a trait without any explicit declaration.
4. THE sample application SHALL demonstrate a `with` block that maps a type's members to a trait when the names differ.
5. THE sample application SHALL demonstrate passing both structurally matched and `with`-mapped types to a function that accepts a trait-typed parameter.
6. THE sample application SHALL demonstrate a compile-time error case (commented out) showing a type that does not satisfy the trait.
7. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
8. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated trait features and prints observable output.
