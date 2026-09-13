# Requirements Document

## Introduction

Newtypes introduce nominally distinct type cloning to the Mist programming language. Unlike type aliases (which create transparent synonyms), a newtype creates a distinct type that is a structural clone of its source type — equivalent to copy-pasting the source type's definition and renaming it. The syntax reuses the existing `type` keyword: `type Name = new SourceType;`. The clone includes all member categories: fields, properties, methods, constructors, operators, cast operators, and indexers. No filtering or transformation is performed — the newtype is a complete copy with a new identity.

A newtype shares its source's implementation and none of its identity: `type Jaguar = new Tiger;` has Tiger's base, interfaces and members, and no nominal relationship to Tiger, so a Jaguar is never a Tiger. A value leaves a newtype for its source wherever a copy cannot be told from the original, and enters it only where the program states the intent -- a cast, or a literal written into a slot whose type is in sight.

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
- **Renamed**: A type, signature or member body taken from the Source_Type's declaration with every reference to the Source_Type replaced by the newtype.
- **Renamed_Clone**: The newtype as built from the Source_Type's own declaration, Renamed (Requirement 3.2).
- **Copyable_Source**: A Source_Type whose copy cannot be told from the original: a struct, an intrinsic Standard type, or a class declared `readonly`.
- **Newtype_Literal**: A literal that may enter a newtype without a cast (Requirement 9.1).

## Requirements

### Requirement 1: Newtype Declaration Syntax

**User Story:** As a Mist developer, I want to declare newtypes using the `type Name = new SourceType;` syntax, so that I can create distinct types that clone the structure of existing types.

#### Acceptance Criteria

1. WHEN the Parser encounters `type IDENTIFIER = new TypeReference ;` at the top level or within a namespace body, THE Parser SHALL produce a NewtypeDeclaration AST node containing the newtype name and the source type reference.
2. WHEN the Parser encounters a newtype declaration missing the `new` keyword after `=`, THE Parser SHALL parse the declaration as a regular TypeAliasDeclaration (existing behavior).
3. WHEN the Parser encounters a newtype declaration missing the terminating `;`, THE Parser SHALL report a diagnostic error indicating the expected `;` token and the line and column position where `;` was expected.
4. IF the Parser encounters `type IDENTIFIER = new` followed by a token that is not a valid start of a TypeReference, THEN THE Parser SHALL report a diagnostic error indicating that a source type is expected after `new`.

### Requirement 2: Newtype Identity and Conversions

**User Story:** As a Mist developer, I want a newtype to be a type of its own that shares its source type's implementation, so that a value enters the newtype only where the program states the intent, and leaves it wherever a copy of it is as good as the value.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat a newtype as a distinct nominal type with no nominal relationship to its Source_Type: a newtype value is not a value of the Source_Type, and a type test of a newtype value against the Source_Type (`is`, `as`, a type pattern) SHALL fail, statically and at runtime.
2. THE Type_Checker SHALL treat the newtype as having the Source_Type's base type and abstract bases, each Renamed, so that a newtype value is assignable to each of them: with `class Tiger : Cat, IStriped` and `type Jaguar = new Tiger;`, `Cat c = jaguar;` is valid and `jaguar is Cat` holds, while `jaguar is Tiger` does not.
3. WHEN the Type_Checker resolves member access on a newtype value, THE Type_Checker SHALL resolve against the newtype's Renamed members, so that a member the Source_Type declares as taking or answering the Source_Type takes or answers the newtype: `age + age` is an `Age`, and `jaguar.Twin()` is a `Jaguar`.
4. WHERE the Source_Type is a Copyable_Source, THE Type_Checker SHALL admit an implicit conversion from the newtype to the Source_Type, whose value is a copy of the newtype's value. It is a Lossless_Conversion (cast-operators spec glossary), so it never counts as the one user-declared conversion a value may pass through: `int x = age;` and `long l = age;` are valid, and `age + 1` is `int + int`.
5. WHERE the Source_Type is a Copyable_Source, a cast from any value to the newtype SHALL call the newtype's conversion function, whose parameter is the Source_Type and whose result is the newtype itself, never an optional of it. The operand is matched as an argument (cast-operators spec Requirement 8.10): `(Meters)reading` is valid where `type Meters = new double;` and `Reading` declares an implicit conversion to `double`, and `(Meters)feet` is valid where `type Feet = new double;`, a `Feet` reaching `double` by 2.4.
6. IF a value of any type other than the newtype is used where the newtype is expected without a cast expression, THEN THE Type_Checker SHALL report a type mismatch, except for a Newtype_Literal in a position Requirement 9.2 names. This includes a value of the Source_Type and a value of another newtype over the same Source_Type: `Meters m = feet;` is refused.
7. WHERE the Source_Type is not a Copyable_Source, THE Type_Checker SHALL admit no implicit conversion between the newtype and the Source_Type in either direction, and an explicit conversion only as Requirement 10 states.

### Requirement 3: Semantic Model Registration

**User Story:** As a compiler developer, I want newtypes to be registered in the semantic model with their cloned member sets, so that downstream compiler phases can use the type information.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a NewtypeDeclaration node, THE Semantic_Model_Builder SHALL register the newtype name as a distinct type symbol (SymbolKind distinguishable from TypeAlias, Class, and Struct) in the symbol table of the enclosing scope.
2. WHEN the Semantic_Model_Builder encounters a NewtypeDeclaration node, THE Semantic_Model_Builder SHALL build the newtype as the Renamed_Clone of the Source_Type's declaration: its base type, abstract bases, constraints and all members (fields, properties, methods, constructors, operators, cast operators, indexers), with the bodies of those members bound again, every reference to the Source_Type by name resolving to the newtype.
3. IF a NewtypeDeclaration uses a name that is already defined in the same scope, THEN THE Semantic_Model_Builder SHALL report a duplicate symbol diagnostic error and skip registration of the duplicate newtype (the existing symbol remains unchanged).
4. IF a NewtypeDeclaration references an undefined source type, THEN THE Semantic_Model_Builder SHALL report an unresolved type diagnostic error and assign an error type to the newtype symbol so that downstream phases do not produce cascading errors.
5. THE Renamed_Clone SHALL contain only what the Source_Type's own declaration contains. A retroactive conformance (`with Tiger : INamed { ... }`) or an extension block declared for the Source_Type anywhere else SHALL NOT apply to the newtype. A type nested in the Source_Type is not cloned: the Renamed_Clone names the same nested type.
6. IF a member of the Renamed_Clone does not bind -- a `Tiger` method passing `this` to a parameter of type `Tiger` passes a `Jaguar` in the clone -- THEN THE Semantic_Model_Builder SHALL report a diagnostic at the newtype declaration naming the Source_Type's member, and SHALL NOT report a second time a diagnostic the Source_Type's own declaration already reports.
7. THE Semantic_Model_Builder SHALL accept as a Source_Type a class or struct declared in Mist source, including a Standard type, intrinsic or not, and a constructed instance of a generic one, whose Renamed_Clone binds the type parameters to the instance's arguments (`type IntBox = new Box<int>;`). A tuple Source_Type, variadic or not, is accepted as the variadics spec's Requirement 22.3 states. A sealed Source_Type is refused as the full-accessibility-model spec's Requirement 12.7 states. IF the Source_Type is any other type -- an array, enum, union, function type, refinement, type parameter or CLR type -- THEN THE Semantic_Model_Builder SHALL report that a newtype over that kind of type is not supported.
8. WHERE the Source_Type is an intrinsic Standard type, THE newtype SHALL expose a public readonly field `Value` of the Source_Type holding the backing value, beside its Renamed members.

### Requirement 4: Pretty-Printing Newtype Declarations

**User Story:** As a compiler developer, I want the pretty printer to format newtype AST nodes back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a NewtypeDeclaration node, THE Pretty_Printer SHALL emit `type {name} = new {source_type};` on a single line.
2. FOR ALL valid NewtypeDeclaration AST nodes, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 5: C# Transpilation of Newtypes

**User Story:** As a compiler developer, I want newtypes to be correctly transpiled to C#, so that the generated code compiles and preserves the structural clone semantics.

#### Acceptance Criteria

1. WHEN the CSharp_Transpiler encounters a newtype whose source is a class or a struct, THE CSharp_Transpiler SHALL emit a C# class or struct declaration named after the newtype carrying the Renamed_Clone: its base type, interfaces and members as Requirement 3.2 builds them.
2. WHEN the CSharp_Transpiler encounters a newtype whose source is an intrinsic Standard type, THE CSharp_Transpiler SHALL emit a C# type distinct from the backing type that holds one backing value and supplies every Renamed member of the Source_Type, an intrinsic declaration being a contract the backend fulfils.
3. THE emitted program SHALL keep a newtype value's runtime type the newtype, so that the type test of Requirement 2.1 fails at runtime as it does statically. A newtype is therefore never lowered to its Source_Type.
4. THE CSharp_Transpiler SHALL emit every conversion Requirements 2.4, 2.5 and 10 admit, behaving as they state: a conversion from or to a Copyable_Source copies the value, field by field for a struct or a readonly class.
5. THE CSharp_Transpiler SHALL produce C# code that compiles without errors for all valid newtype declarations and every conversion they admit. How a conversion reaches private state is the CSharp_Transpiler's choice, accessibility having been enforced before emission.

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

### Requirement 9: Literals Entering a Newtype

**User Story:** As a Mist developer, I want to write `Age a = 30;` without a cast, so that a literal I place into a slot whose type I can see is accepted, while a literal passed where the newtype requirement sits out of sight -- a call argument -- still needs a cast to state the intent.

#### Acceptance Criteria

1. A Newtype_Literal is an integer, real, string, character or boolean literal, or a numeric literal preceded by a unary minus, that could initialize a variable of the Source_Type directly: `int x = 30;` is valid, so `30` may enter `type Age = new int;`, and `byte b = 300;` is not, so `300` may not enter `type Small = new byte;`.
2. THE Type_Checker SHALL admit a Newtype_Literal as a value of the newtype, without a cast, when it is written directly as:
   1. the initializer of a local variable declared with the newtype (`Age a = 30;`);
   2. the right-hand side of a simple assignment to a variable, field, property or element whose type is the newtype (`a = 31;`), including a member assignment in an object initializer;
   3. the initializer of a field or property declared with the newtype;
   4. the default value of a parameter declared with the newtype;
   5. the value of a `return` from a method, property getter or operator whose declared return type is the newtype;
   6. an element of an array literal written directly in one of the positions above whose type is an array of the newtype (`Age[] ages = [30, 40];`).
3. THE Type_Checker SHALL NOT admit a Newtype_Literal without a cast in any other position: not as a method, constructor or indexer argument (with parameters `Age` and `Height`, `CreatePerson(30, 180)` is refused), an operand of an operator, a branch of a conditional or `switch` expression, the value of a compound assignment, or the result of a lambda.

### Requirement 10: Conversions of a Mutable Class Source

**User Story:** As a Mist developer, I want to convert between a newtype and its class source when the class defines how it is copied, so that the conversion copies by the class author's own rules and never changes an object's type.

#### Acceptance Criteria

1. WHERE the Source_Type is a class that is not a Copyable_Source and implements `IClonable<Source_Type>`, a cast from the Source_Type to the newtype SHALL be admitted, and its value SHALL be what the newtype's Renamed `Clone` body returns when run with `this` as the operand. A cast from the newtype to the Source_Type SHALL be admitted likewise, its value being what the Source_Type's `Clone` body returns when run with `this` as the operand.
2. THE body of such a conversion SHALL reach every member of either type that the `Clone` body reads, whatever its accessibility, the body being the Source_Type's own code.
3. IF the Source_Type is a class that is not a Copyable_Source and does not implement `IClonable<Source_Type>` -- including when it inherits `IClonable` of a base type only -- THEN a cast between the newtype and the Source_Type in either direction SHALL be refused with a diagnostic saying that the Source_Type defines no copy.
4. IF the body of a conversion does not bind with `this` as the operand -- a field declared `Tiger mate` is `Jaguar mate` in the Renamed_Clone, so the body passes a `Tiger` where a `Jaguar` is required -- THEN the cast SHALL be refused with a diagnostic naming the member that blocks it. A field is never converted recursively.
5. Neither conversion SHALL be implicit.
