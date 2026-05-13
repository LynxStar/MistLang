# Requirements Document

## Introduction

This document specifies the requirements for adding nested scoping support to the Mist programming language. Currently, Mist supports namespaces (file-scoped and block-scoped) and type declarations (class, struct, interface, trait, enum), but types cannot be declared inside other types, and namespaces cannot be nested inside other namespaces or types.

This feature introduces the ability to nest type declarations within other type declarations and to nest namespaces within namespaces. Type declarations remain body-less from a nesting perspective — a type can be nested inside another type's member list, but the nested type does not introduce a new block for further nesting inside itself (types still use their existing member block for fields, methods, etc.). When a type is nested inside a generic parent, the parent's type parameters are accessible to the nested type.

The fully qualified name (FQN) of a nested type uses the closed generic form of its parent (e.g., `Quack.Foo<int>.Bar`).

## Glossary

- **Parser**: The Storm-generated recursive descent parser that produces the Mist AST from a token stream.
- **Semantic_Model_Builder**: The pass that constructs the semantic model (symbol table, scopes, type information) from the parsed AST.
- **Type_Checker**: The semantic analysis pass that validates types, infers types for expressions, and reports type errors.
- **CSharp_Emitter**: The backend that transpiles the Mist AST into equivalent C# source code.
- **Pretty_Printer**: The component that formats AST nodes back into Mist source text.
- **Nested_Type**: A type declaration (class, struct, interface, trait, or enum) that appears within the member list of another type declaration.
- **File_Scoped_Namespace**: A namespace declared with the semicolon syntax (`namespace Foo;`) that applies to all subsequent declarations in the file.
- **Block_Namespace**: A namespace declared with braces (`namespace Foo { ... }`) that scopes only the declarations within its block.
- **FQN**: Fully Qualified Name — the dot-separated path from the root namespace to a type, using closed generic forms for generic parents.
- **Closed_Generic**: A generic type with all type parameters substituted with concrete type arguments (e.g., `Foo<int>`).
- **Open_Generic**: A generic type with unsubstituted type parameters (e.g., `Foo<TValue>`).
- **Parent_Type**: The enclosing type declaration that contains a Nested_Type.
- **TextMate_Grammar**: The JSON-based grammar used for syntax highlighting in the VS Code extension.

## Requirements

### Requirement 1: Nest Type Declarations Within Type Declarations

**User Story:** As a Mist developer, I want to declare types inside other types, so that I can logically group related types together and express ownership relationships.

#### Acceptance Criteria

1. WHEN the Parser encounters a TypeDeclaration (class, struct, interface, trait, or enum) within the member list of a ClassDeclaration, THE Parser SHALL produce a nested TypeDeclaration AST node as a member of the enclosing class.
2. WHEN the Parser encounters a TypeDeclaration within the member list of a StructDeclaration, THE Parser SHALL produce a nested TypeDeclaration AST node as a member of the enclosing struct.
3. WHEN the Parser encounters a TypeDeclaration within the member list of an InterfaceDeclaration, THE Parser SHALL produce a nested TypeDeclaration AST node as a member of the enclosing interface.
4. WHEN the Parser encounters a TypeDeclaration within the member list of a TraitDeclaration, THE Parser SHALL produce a nested TypeDeclaration AST node as a member of the enclosing trait.
5. WHEN the Parser encounters a TypeDeclaration within the member list of an EnumDeclaration, THE Parser SHALL produce a nested TypeDeclaration AST node as a member of the enclosing enum.
6. THE Parser SHALL accept nested types at any nesting depth up to at least 8 levels (e.g., a class inside a class inside a class), producing a correctly-structured AST where each nested TypeDeclaration is a member of its immediate enclosing type.
7. THE Parser SHALL accept the modifiers public, private, and static on nested type declarations, preserving them in the AST node's modifier list.
8. WHEN the Parser produces a nested TypeDeclaration AST node, THE Parser SHALL preserve all syntactic elements of the nested type (type parameters, base list, where-clauses, and member declarations) identically to how they are parsed for a top-level TypeDeclaration.

### Requirement 2: Nested Types Do Not Introduce Additional Nesting Blocks

**User Story:** As a Mist developer, I want nested types to use the same member block syntax as top-level types, so that the language remains consistent and I understand that nesting is one-directional (a type can be placed inside another, but the nested type's body is still just its own members).

#### Acceptance Criteria

1. THE Parser SHALL parse the body of a nested type declaration using the same `MemberDeclaration*` grammar production as a top-level type declaration, permitting fields, methods, properties, constructors, operators, indexers, default blocks, and further nested types as members.
2. THE Parser SHALL accept a `TypeDeclaration` as a valid alternative within the `MemberDeclaration` rule, so that nested types appear alongside other members without requiring a separate grammar production.
3. THE Parser SHALL accept further nested type declarations inside a nested type's member list to a minimum depth of 3 levels (e.g., class inside class inside class) without parser error.
4. THE Parser SHALL NOT introduce any new grammar rule or block syntax specific to nested type bodies — the `MemberDeclaration*` production used for top-level type bodies SHALL be the sole production governing nested type bodies.

### Requirement 3: File-Scoped Namespace Uniqueness

**User Story:** As a Mist developer, I want the compiler to reject duplicate file-scoped namespace declarations in the same file, so that namespace intent is unambiguous.

#### Acceptance Criteria

1. WHEN a file contains more than one file-scoped namespace declaration (e.g., two `namespace Foo;` statements), THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the second declaration indicating that only one file-scoped namespace is permitted per file.
2. WHEN a file contains a file-scoped namespace declaration followed by a block namespace declaration, THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the block namespace declaration indicating that file-scoped and block namespaces cannot be mixed in the same file.
3. WHEN a file contains a block namespace declaration followed by a file-scoped namespace declaration, THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the file-scoped namespace declaration indicating that file-scoped and block namespaces cannot be mixed in the same file.

### Requirement 4: Block Namespace Repetition

**User Story:** As a Mist developer, I want to use the same block namespace name multiple times in a file, so that I can organize code into logical sections while contributing to the same namespace.

#### Acceptance Criteria

1. WHEN a file contains multiple block namespace declarations with the same qualified name, THE Semantic_Model_Builder SHALL register all type declarations from each block into a single shared namespace scope, such that types from any of the blocks are resolvable by simple name from within any other block of the same namespace in that file.
2. WHEN a file contains multiple block namespace declarations with different qualified names, THE Semantic_Model_Builder SHALL register each block's type declarations in its own distinct namespace scope, such that types in one block are not resolvable by simple name from another block with a different namespace without a Using_Declaration.
3. THE Parser SHALL accept any number of block namespace declarations in a single file without reporting a diagnostic.
4. IF two block namespace declarations with the same qualified name in a single file each declare a type with the same name, THEN THE Semantic_Model_Builder SHALL report a duplicate-type diagnostic for the second declaration.

### Requirement 5: Nest Namespaces Within Namespaces

**User Story:** As a Mist developer, I want to nest namespace blocks inside other namespace blocks, so that I can express hierarchical namespace structures using indentation rather than dot-separated names.

#### Acceptance Criteria

1. WHEN the Parser encounters a block namespace declaration inside another block namespace declaration, THE Parser SHALL produce a NamespaceDeclaration AST node as a child within the parent namespace's body item list.
2. THE Semantic_Model_Builder SHALL compute the FQN of a nested namespace by concatenating the enclosing namespace's fully-qualified name, a dot separator, and the nested namespace's declared name, applied recursively at each nesting level (e.g., block `A` containing block `B` containing block `C` yields FQNs `A`, `A.B`, and `A.B.C`).
3. THE Parser SHALL accept namespace nesting up to at least 8 levels deep.
4. WHEN a block namespace is nested inside a file-scoped namespace, THE Semantic_Model_Builder SHALL compute the FQN by prepending the file-scoped namespace's fully-qualified name (e.g., file-scoped `A` containing block `B` containing block `C` yields `A.B` and `A.B.C`).
5. IF a file-scoped namespace declaration appears inside a block namespace body, THEN THE Parser SHALL report a diagnostic error and not produce a FileScopedNamespace AST node at that position.

### Requirement 6: Nest Types Within Namespaces

**User Story:** As a Mist developer, I want to declare types inside namespace blocks (including nested namespace blocks), so that types are scoped to their containing namespace.

#### Acceptance Criteria

1. WHEN a TypeDeclaration appears inside a block namespace with a single-segment name, THE Semantic_Model_Builder SHALL set the type's ContainingNamespace to that segment and register the type in the corresponding namespace scope such that the type's fully qualified name equals the namespace name followed by a dot and the type's DisplayName (e.g., namespace `Foo` containing class `Bar` yields `Foo.Bar`).
2. WHEN a TypeDeclaration appears inside a block namespace with a multi-segment qualified name, THE Semantic_Model_Builder SHALL set the type's ContainingNamespace to the full dotted namespace path and register the type in the corresponding namespace scope such that the type's fully qualified name equals the full namespace path followed by a dot and the type's DisplayName (e.g., namespace `Foo.Bar.Baz` containing class `Qux` yields `Foo.Bar.Baz.Qux`).
3. WHEN a TypeDeclaration appears after a file-scoped namespace declaration, THE Semantic_Model_Builder SHALL set the type's ContainingNamespace to the file-scoped namespace name and register the type in the corresponding namespace scope such that the type's fully qualified name equals the file-scoped namespace name followed by a dot and the type's DisplayName.
4. WHEN a TypeDeclaration appears at the top level without any enclosing or preceding namespace declaration, THE Semantic_Model_Builder SHALL set the type's ContainingNamespace to the inferred namespace derived from the file's path relative to the project folder and the project's namespace root.

### Requirement 7: Generic Type Parameter Inheritance for Nested Types

**User Story:** As a Mist developer, I want nested types to have access to the enclosing type's generic type parameters, so that I can define helper types that operate on the parent's generic arguments without re-declaring them.

#### Acceptance Criteria

1. WHEN a type declaration appears as a member inside the body of a generic parent type (class, struct, or interface), THE Semantic_Model_Builder SHALL register the parent's type parameters as symbols in the nested type's scope so that name lookup resolves them without re-declaration.
2. WHEN a nested type uses a type parameter name from its enclosing parent in a type reference position (field type, method return type, method parameter type, or base type), THE Type_Checker SHALL resolve that name to the parent's type parameter and treat it identically to a locally declared type parameter of the same name.
3. WHEN a nested type is nested 2 or more levels deep inside generic parent types, THE Semantic_Model_Builder SHALL make all ancestor type parameters visible in the innermost nested type's scope, with the innermost declaration shadowing any outer declaration that shares the same name.
4. WHEN a nested type declares its own type parameter with the same name as an ancestor's type parameter, THE Semantic_Model_Builder SHALL treat the nested type's parameter as shadowing the ancestor's parameter within the nested type's scope, and THE Type_Checker SHALL resolve all references to that name within the nested type to the nested type's own parameter.
5. IF a nested type references a type parameter name that is not declared by any enclosing ancestor type or by the nested type itself, THEN THE Type_Checker SHALL emit a diagnostic indicating an unresolved type parameter reference.

### Requirement 8: Fully Qualified Name Construction for Nested Types

**User Story:** As a Mist developer, I want nested types to have fully qualified names that include the closed generic form of their parent, so that I can unambiguously reference specific instantiations of nested types.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL construct the FQN of a nested type by joining the parent type's name (with type arguments if generic) and the nested type's name with a dot separator.
2. WHEN the parent type is generic, THE FQN SHALL use the closed generic form (e.g., `Foo<int>.Bar`, not `Foo<TValue>.Bar`).
3. WHEN multiple levels of generic parents exist, THE FQN SHALL include closed generics at each level (e.g., `Outer<string>.Middle<int>.Inner`).
4. WHEN the nested type itself is generic, THE FQN SHALL include the nested type's own type arguments (e.g., `Foo<int>.Bar<string>`).
5. WHEN a namespace precedes the nested type chain, THE FQN SHALL include the namespace prefix (e.g., `Quack.Foo<int>.Bar`).

### Requirement 9: Semantic Model Registration of Nested Types

**User Story:** As a Mist developer, I want the compiler to correctly register nested types in the symbol table, so that they can be referenced from other code using their FQN or relative name.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL register each nested type in the symbol table under its fully qualified name, where the FQN is constructed by joining the namespace prefix, the parent type chain (using closed generic forms for generic parents), and the nested type's simple name with dot separators (e.g., `Quack.Foo<int>.Bar`).
2. WHEN code within the parent type references the nested type by its simple name (the unqualified type identifier), THE Type_Checker SHALL resolve the reference to the nested type registered in the parent type's scope.
3. WHEN code outside the parent type references the nested type, THE Type_Checker SHALL require either a dot-qualified name that includes the parent type (e.g., `Parent.Nested` or `Namespace.Parent.Nested`) or a using declaration that brings the nested type's FQN into scope.
4. IF a nested type has the same simple name as another type already registered in the same parent type's member scope (including other nested types, but not members of a different kind such as fields or methods), THEN THE Semantic_Model_Builder SHALL report a diagnostic error indicating a duplicate type name, referencing the location of both the original and duplicate declarations.
5. IF a nested type has the same simple name as a type parameter declared on the parent type, THEN THE Semantic_Model_Builder SHALL report a diagnostic error indicating a name conflict between the nested type and the type parameter.

### Requirement 10: C# Emission for Nested Types

**User Story:** As a Mist developer, I want nested types to emit valid C# nested type declarations, so that the transpiled output compiles correctly on .NET.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters a nested type declaration (class, struct, interface, or enum declared as a member of another type), THE CSharp_Emitter SHALL emit the type declaration inside the parent type's C# class/struct/interface body, preserving the access modifier of the nested type.
2. WHEN the CSharp_Emitter encounters a nested type inside a generic parent, THE CSharp_Emitter SHALL emit the nested type without re-declaring the parent's type parameters, and SHALL emit only the nested type's own type parameters (if any) on its declaration.
3. WHEN the CSharp_Emitter encounters nested types at a depth of 2 or more levels, THE CSharp_Emitter SHALL emit each level of nesting indented one additional indent level (4 spaces) relative to its parent type body, with opening and closing braces correctly scoped to their respective type.
4. WHEN the CSharp_Emitter encounters a nested enum inside a class or struct, THE CSharp_Emitter SHALL emit a valid C# nested enum declaration including its underlying type annotation (if specified) and all enum members with their explicit values (if specified).
5. IF the CSharp_Emitter encounters a nested type declaration at a nesting depth exceeding 4 levels, THEN THE CSharp_Emitter SHALL still emit valid C# output (no maximum nesting depth restriction is imposed by the emitter).

### Requirement 11: C# Emission for Nested Namespaces

**User Story:** As a Mist developer, I want nested namespace blocks to emit valid C# namespace declarations, so that the transpiled output compiles correctly.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters a single block namespace with a qualified name (e.g., `A.B.C`), THE CSharp_Emitter SHALL emit either a single C# file-scoped namespace declaration with the fully qualified name (for C# 10+) or nested C# block namespace declarations (one per segment) for earlier C# versions.
2. WHEN the CSharp_Emitter encounters multiple block namespace declarations in the same compilation unit that share a common prefix (e.g., `namespace A { ... }` and `namespace A.B { ... }`), THE CSharp_Emitter SHALL emit each as a separate C# namespace declaration preserving the fully qualified name of each block.
3. WHEN the CSharp_Emitter encounters two or more block namespace declarations with identical fully qualified names in the same compilation unit, THE CSharp_Emitter SHALL merge their type declarations, extension blocks, and newtype declarations into a single C# namespace block in the output.
4. IF a block namespace declaration contains no type declarations, extension blocks, or newtype declarations after emission, THEN THE CSharp_Emitter SHALL omit the empty namespace block from the output.
5. WHEN the CSharp_Emitter emits nested C# block namespace declarations, THE CSharp_Emitter SHALL indent the contents of each nested block by one additional indentation level relative to its parent namespace block.

### Requirement 12: Pretty-Print Nested Types

**User Story:** As a Mist developer, I want the pretty printer to faithfully reproduce nested type syntax, so that round-trip formatting preserves my code structure.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a nested type declaration (class, struct, interface, trait, or enum) within a parent type's member list, THE Pretty_Printer SHALL emit the nested type declaration indented one additional level (4 spaces) relative to the parent type's body.
2. WHEN the Pretty_Printer encounters nested types at multiple levels of depth, THE Pretty_Printer SHALL increase indentation by one indent level (4 spaces) per nesting level, applying the same indentation rules recursively at each depth.
3. WHEN the Pretty_Printer encounters a nested type declaration with access modifiers (public, private), THE Pretty_Printer SHALL emit the access modifier before the type keyword on the same line, consistent with existing modifier formatting for other member declarations.
4. WHEN the Pretty_Printer encounters multiple sibling nested type declarations within the same parent type, THE Pretty_Printer SHALL separate each nested type from adjacent members with a blank line, consistent with existing member separation behavior.
5. FOR ALL valid Mist source files containing nested types, parsing the source then pretty-printing the resulting AST then parsing the pretty-printed output SHALL produce an AST that is structurally identical to the original (same node types, same token lexemes, same tree shape), disregarding source position spans.

### Requirement 13: Pretty-Print Nested Namespaces

**User Story:** As a Mist developer, I want the pretty printer to faithfully reproduce nested namespace syntax, so that round-trip formatting preserves my code structure.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a nested block namespace, THE Pretty_Printer SHALL emit the namespace declaration indented one additional level (4 spaces) relative to the parent namespace's body.
2. WHEN the Pretty_Printer encounters repeated block namespaces with the same name, THE Pretty_Printer SHALL emit each occurrence separately (preserving the source structure, not merging them).
3. FOR ALL valid Mist source files containing nested namespaces, parsing the source then pretty-printing the resulting AST then parsing the pretty-printed output SHALL produce an AST that is structurally identical to the original (same node types, same token lexemes, same tree shape), disregarding source position spans.

### Requirement 14: Error Diagnostics for Invalid Nesting

**User Story:** As a Mist developer, I want clear error messages when I misuse nesting, so that I can quickly fix my code.

#### Acceptance Criteria

1. IF a file-scoped namespace is declared more than once in the same file, THEN THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the duplicate (second) declaration with a message indicating only one file-scoped namespace is allowed per file.
2. IF a file mixes file-scoped and block namespace declarations, THEN THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the declaration that creates the conflict (the second style encountered) indicating the two styles cannot be combined in the same file.
3. IF a nested type has the same name as a sibling nested type within the same parent (regardless of type kind), THEN THE Semantic_Model_Builder SHALL report a diagnostic error at the location of the duplicate declaration, with a message indicating a duplicate type name and naming the conflicting type.

### Requirement 15: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want nested type declarations to be syntax-highlighted correctly, so that I get the same visual cues as top-level type declarations regardless of nesting depth.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL highlight type declaration keywords (class, struct, interface, trait, enum) with scope `keyword.declaration.mist` regardless of whether they appear at top level or nested within another type.
2. THE TextMate_Grammar SHALL highlight the identifier following a nested type declaration keyword with the type-specific scope matching the keyword used (`entity.name.type.class.mist` for class, `entity.name.type.struct.mist` for struct, `entity.name.type.interface.mist` for interface, `entity.name.type.trait.mist` for trait, `entity.name.type.enum.mist` for enum).
3. THE TextMate_Grammar SHALL correctly match nested braces so that highlighting does not break when types are nested up to 5 levels deep.
4. IF a type declaration keyword appears inside a string literal or comment, THEN THE TextMate_Grammar SHALL NOT highlight it as `keyword.declaration.mist`.
5. WHEN a nested type declaration includes a type parameter list (e.g., `class Inner<T>`), THE TextMate_Grammar SHALL highlight the keyword, type name, and type parameters with the same scopes used for top-level generic type declarations.

### Requirement 16: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating nested scoping, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/NestedScoping/Program.mist`.
2. The sample SHALL demonstrate the following patterns: a class nested inside a class, a struct nested inside a class, an interface nested inside a class, a trait nested inside a class, an enum nested inside a class, a class nested inside a struct, a class nested inside an interface, a class nested inside a trait, deeply nested types (three or more levels), a nested type inside a generic parent accessing the parent's type parameters, multiple nested types as siblings within the same parent, block namespaces nested inside block namespaces, repeated block namespaces with the same name, a file-scoped namespace with nested types inside type declarations, and referencing nested types by qualified name from outside the parent.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `NestedScoping.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.
