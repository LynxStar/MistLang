# Requirements Document

## Introduction

This document specifies the requirements for adding property support to the Mist programming language compiler. Mist is a C#-like language that transpiles to C#. The compiler pipeline consists of Lexer → Parser → Semantic Model → Type Checker → C# Transpiler. Currently, no property support exists. This feature adds auto-properties (`get; set;`), block-body accessors, the `field` keyword for backing field access, mixed accessor forms, accessor modifiers, `init`-only setters, static properties, expression-bodied properties and accessors (`=>`), computed properties (get-only with body, no backing field), interface property declarations, and indexers (`this[ParamType param]`).

## Glossary

- **Mist_Compiler**: The Mist language compiler, which lexes, parses, builds a semantic model, type-checks, and transpiles Mist source code to C#.
- **Parser**: The component of the Mist_Compiler that reads a token stream and produces an abstract syntax tree (AST) according to the Storm grammar.
- **Semantic_Model_Builder**: The component of the Mist_Compiler that walks ASTs to register types, resolve symbols, and build the semantic model.
- **Type_Checker**: The component of the Mist_Compiler that validates type correctness of expressions, statements, and member declarations.
- **Emitter**: The C# transpiler backend of the Mist_Compiler that walks the AST and semantic model to produce C# source code.
- **Property_Declaration**: A member of a class, struct, or interface that declares a named value with get and/or set accessors.
- **Auto_Property**: A property where the compiler generates a backing field automatically, declared with `get;` and/or `set;` (semicolon-only accessors).
- **Accessor**: A `get`, `set`, or `init` block within a property declaration that defines how the property value is read or written.
- **Backing_Field**: A compiler-generated private field that stores the value of an auto-property, accessible within accessor bodies via the `field` keyword.
- **Init_Accessor**: A setter-like accessor declared with `init` instead of `set`, allowing assignment only during object initialization.
- **Accessor_Modifier**: An access modifier (e.g., `private`) applied to an individual accessor to restrict its visibility relative to the property's declared visibility.
- **Expression_Body**: A shorthand accessor or property body using the `=>` (fat arrow) token followed by a single expression, instead of a block body (e.g., `get => expr;` or `Type Name => expr;`).
- **Computed_Property**: A property that has only a `get` accessor with a body (block or expression) and no `set`/`init` accessor. The Semantic_Model_Builder classifies such properties as computed and does not generate a backing field for them.
- **Indexer**: A member declared with `this[ParamType param]` that allows instances to be accessed with indexing syntax (`obj[key]`). Indexers have get and/or set accessors like properties but accept parameters.
- **Interface_Property_Declaration**: A property signature declared within an interface body, consisting of a type, name, and accessor list containing `get;` and/or `set;` without bodies.
- **Storm_Grammar**: The grammar definition format used by the Mist compiler, defined in `Source/Mist/Grammar/Mist.storm`.
- **Pretty_Printer**: The component of the Mist_Compiler that formats AST nodes back into Mist source text.
- **Syntax_Highlighting_Extension**: The VS Code extension at `Editor/mist-syntax-highlighting/` that provides TextMate grammar-based syntax highlighting for Mist source files. The TextMate grammar is defined in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.
- **Sample_Application**: A self-contained Mist program in the `samples/` directory that demonstrates language features and serves as an integration test. Each sample has its own subdirectory containing one or more `.mist` files including a `Program.mist` entry point.

## Requirements

### Requirement 1: Grammar Keywords and Tokens for Properties

**User Story:** As a compiler developer, I want the Storm grammar to define property-related keyword tokens, so that the lexer can recognize property syntax.

#### Acceptance Criteria

1. THE Storm_Grammar SHALL define keyword tokens for `get`, `set`, and `init`.
2. THE Storm_Grammar SHALL define the `field` keyword token for backing field access within accessor bodies.
3. THE Storm_Grammar SHALL define the `=>` (fat arrow) token for expression-bodied properties and accessors.

### Requirement 2: Auto-Property Parsing

**User Story:** As a Mist developer, I want to declare auto-properties with `get;` and `set;`, so that I can define simple data-holding properties without writing accessor bodies.

#### Acceptance Criteria

1. WHEN a property declaration contains `get;` and `set;` accessors, THE Parser SHALL produce an AST node representing an auto-property with both accessors.
2. WHEN a property declaration contains only `get;`, THE Parser SHALL produce an AST node representing a read-only auto-property.
3. WHEN a property declaration contains only `set;`, THE Parser SHALL produce an AST node representing a write-only auto-property.
4. WHEN a property declaration contains an initializer (e.g., `= defaultValue;`), THE Parser SHALL capture the initializer expression in the AST node.

### Requirement 3: Block-Body Accessor Parsing

**User Story:** As a Mist developer, I want to write get and set accessors with block bodies, so that I can implement custom logic for reading and writing property values.

#### Acceptance Criteria

1. WHEN a `get` accessor contains a block body, THE Parser SHALL produce an AST node containing the block's statements.
2. WHEN a `set` accessor contains a block body, THE Parser SHALL produce an AST node containing the block's statements, with an implicit `value` parameter available.
3. WHEN a property mixes an auto-accessor (e.g., `get;`) with a block-body accessor (e.g., `set { ... }`), THE Parser SHALL produce an AST node representing the mixed form.

### Requirement 4: Init-Only Accessor Parsing

**User Story:** As a Mist developer, I want to declare `init` accessors on properties, so that property values can be set during object initialization but remain immutable afterward.

#### Acceptance Criteria

1. WHEN a property declaration contains an `init;` accessor, THE Parser SHALL produce an AST node representing an init-only setter.
2. WHEN an `init` accessor contains a block body, THE Parser SHALL produce an AST node containing the block's statements.

### Requirement 5: Accessor Modifier Parsing

**User Story:** As a Mist developer, I want to apply access modifiers to individual accessors (e.g., `private set;`), so that I can restrict write access while keeping read access public.

#### Acceptance Criteria

1. WHEN an accessor has an access modifier (e.g., `private set;`), THE Parser SHALL capture the modifier on the accessor's AST node.
2. THE Type_Checker SHALL verify that the accessor modifier is more restrictive than the property's declared access level.
3. IF an accessor modifier is less restrictive than or equal to the property's access level, THEN THE Type_Checker SHALL report a diagnostic error.

### Requirement 6: Field Keyword in Accessor Bodies

**User Story:** As a Mist developer, I want to use the `field` keyword inside accessor bodies, so that I can access the compiler-generated backing field directly.

#### Acceptance Criteria

1. WHEN the `field` keyword appears inside a `get`, `set`, or `init` accessor body, THE Parser SHALL produce an AST node representing a backing field reference.
2. THE Semantic_Model_Builder SHALL resolve the `field` keyword to the auto-generated backing field of the enclosing property.
3. IF the `field` keyword appears outside of a property accessor body, THEN THE Type_Checker SHALL report a diagnostic error.

### Requirement 7: Expression-Bodied Property and Accessor Parsing

**User Story:** As a Mist developer, I want to use expression-bodied syntax (`=>`) for properties and accessors, so that I can write concise single-expression property logic.

#### Acceptance Criteria

1. WHEN a property declaration uses top-level expression-body syntax (e.g., `Type Name => expr;`), THE Parser SHALL produce an AST node representing an expression-bodied property with an implicit get-only accessor.
2. WHEN a `get` accessor uses expression-body syntax (e.g., `get => expr;`), THE Parser SHALL produce an AST node containing the expression as the accessor body.
3. WHEN a `set` or `init` accessor uses expression-body syntax (e.g., `set => expr;`), THE Parser SHALL produce an AST node containing the expression as the accessor body, with an implicit `value` parameter available.
4. WHEN a property mixes an expression-bodied accessor with an auto-accessor or block-body accessor, THE Parser SHALL produce an AST node representing the mixed form.

### Requirement 8: Static Property Parsing

**User Story:** As a Mist developer, I want to declare static properties on classes and structs, so that I can expose type-level computed or stored values through property syntax.

#### Acceptance Criteria

1. WHEN a property declaration includes the `static` modifier, THE Parser SHALL capture the `static` modifier on the property's AST node.
2. THE Semantic_Model_Builder SHALL register static properties as type-level symbols rather than instance-level symbols.
3. WHEN a static property is accessed, THE Type_Checker SHALL verify that the access occurs through the type name rather than through an instance reference.
4. IF a static property accessor body references `this`, THEN THE Type_Checker SHALL report a diagnostic error.

### Requirement 9: Computed Property Classification

**User Story:** As a compiler developer, I want the semantic model to classify get-only properties with bodies as computed properties, so that the compiler avoids generating unnecessary backing fields.

#### Acceptance Criteria

1. WHEN a property has only a `get` accessor with a body (block or expression) and no `set` or `init` accessor, THE Semantic_Model_Builder SHALL classify the property as a Computed_Property.
2. THE Semantic_Model_Builder SHALL NOT generate a backing field for a Computed_Property.
3. IF a Computed_Property accessor body references the `field` keyword, THEN THE Type_Checker SHALL report a diagnostic error indicating that computed properties have no backing field.

### Requirement 10: Interface Property Declaration Parsing

**User Story:** As a Mist developer, I want to declare property signatures in interfaces, so that implementing types are required to provide the specified accessors.

#### Acceptance Criteria

1. WHEN an interface body contains a property declaration with `get;` and/or `set;` accessors, THE Parser SHALL produce an AST node representing an interface property signature.
2. THE Parser SHALL NOT accept accessor bodies (block or expression) on interface property declarations.
3. THE Semantic_Model_Builder SHALL register interface property declarations as abstract property symbols on the interface type.
4. WHEN a class or struct implements an interface with property declarations, THE Type_Checker SHALL verify that the implementing type provides matching property accessors.
5. IF an implementing type is missing a required property accessor declared by an interface, THEN THE Type_Checker SHALL report a diagnostic error.

### Requirement 11: Indexer Declaration Parsing

**User Story:** As a Mist developer, I want to declare indexers with `this[ParamType param]`, so that instances of my types can be accessed with indexing syntax.

#### Acceptance Criteria

1. WHEN a member declaration uses `this` followed by a bracketed parameter list (e.g., `Type this[ParamType param]`), THE Parser SHALL produce an AST node representing an Indexer declaration.
2. THE Parser SHALL capture the indexer's parameter list, return type, and accessor list (get and/or set with auto, block, or expression bodies).
3. THE Semantic_Model_Builder SHALL register each indexer declaration as a symbol with kind `Indexer` on the enclosing type.
4. WHEN an instance is accessed with indexing syntax (e.g., `obj[key]`), THE Type_Checker SHALL resolve the access to the indexer's get accessor and verify argument type compatibility.
5. WHEN an indexed access is assigned a value (e.g., `obj[key] = val;`), THE Type_Checker SHALL resolve the access to the indexer's set accessor and verify both argument and value type compatibility.
6. IF a type declares more than one indexer with the same parameter types, THEN THE Type_Checker SHALL report a diagnostic error for duplicate indexer declarations.

### Requirement 12: Property Semantic Model Registration

**User Story:** As a compiler developer, I want the semantic model to register property symbols, so that type checking and code emission can reference property metadata.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL register each property declaration as a symbol with kind `Property`.
2. THE Semantic_Model_Builder SHALL resolve the property's declared type from the type reference in the declaration.
3. THE Semantic_Model_Builder SHALL record accessor information (get, set, init, their modifiers, whether they are auto, block-body, or expression-body) on the property symbol.
4. WHEN a property is an auto-property or uses the `field` keyword, THE Semantic_Model_Builder SHALL generate a backing field symbol associated with the property.
5. THE Semantic_Model_Builder SHALL record whether a property is static on the property symbol.

### Requirement 13: Property Type Checking

**User Story:** As a compiler developer, I want the type checker to validate property declarations and usage, so that type errors are caught at compile time.

#### Acceptance Criteria

1. WHEN a property is accessed for reading, THE Type_Checker SHALL verify that the property has a `get` accessor accessible from the call site.
2. WHEN a property is assigned a value, THE Type_Checker SHALL verify that the property has a `set` or `init` accessor accessible from the call site.
3. THE Type_Checker SHALL verify that the assigned value's type is compatible with the property's declared type.
4. IF a `set` or `init` accessor body references `value`, THE Type_Checker SHALL resolve `value` to the property's declared type.
5. WHEN an `init` accessor is used, THE Type_Checker SHALL verify that the assignment occurs within an object initializer or constructor context.
6. WHEN an expression-bodied `get` accessor or expression-bodied property is used, THE Type_Checker SHALL verify that the expression's type is compatible with the property's declared type.
7. WHEN an expression-bodied `set` or `init` accessor is used, THE Type_Checker SHALL verify that the expression is a valid statement expression.

### Requirement 14: Property C# Emission

**User Story:** As a compiler developer, I want the C# emitter to produce valid C# property syntax, so that transpiled code compiles correctly.

#### Acceptance Criteria

1. THE Emitter SHALL emit auto-properties with `get;` and/or `set;` as C# auto-properties.
2. THE Emitter SHALL emit block-body accessors as C# accessor blocks with their statements.
3. THE Emitter SHALL emit `init` accessors as C# `init` accessors.
4. THE Emitter SHALL emit accessor modifiers (e.g., `private set;`) on the generated C# accessors.
5. THE Emitter SHALL emit property initializers (e.g., `= defaultValue;`) on the generated C# properties.
6. WHEN a property uses the `field` keyword, THE Emitter SHALL emit the backing field as a private field and replace `field` references with the generated field name in the C# output.
7. THE Emitter SHALL emit expression-bodied properties (e.g., `Type Name => expr;`) as C# expression-bodied properties.
8. THE Emitter SHALL emit expression-bodied accessors (e.g., `get => expr;`) as C# expression-bodied accessors.
9. THE Emitter SHALL emit the `static` modifier on static property declarations in the C# output.
10. THE Emitter SHALL emit interface property declarations as C# interface property signatures with `get;` and/or `set;`.
11. THE Emitter SHALL emit indexer declarations as C# indexers using `this[ParamType param]` syntax with their accessor bodies.

### Requirement 15: Property Pretty Printing

**User Story:** As a compiler developer, I want the pretty printer to format property AST nodes back into Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format property declarations including modifiers, type, name, and accessor list.
2. THE Pretty_Printer SHALL format accessor bodies (block bodies and expression bodies) with correct indentation.
3. THE Pretty_Printer SHALL format accessor modifiers (e.g., `private set;`) in the output.
4. THE Pretty_Printer SHALL format expression-bodied properties (e.g., `Type Name => expr;`) in the output.
5. THE Pretty_Printer SHALL format static property declarations with the `static` modifier in the output.
6. THE Pretty_Printer SHALL format interface property declarations with their accessor signatures.
7. THE Pretty_Printer SHALL format indexer declarations with `this[ParamType param]` syntax and their accessor list.
8. FOR ALL valid property, interface property, and indexer AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 16: Syntax Highlighting for Property Keywords

**User Story:** As a Mist developer using VS Code, I want property-related keywords and operators to be syntax-highlighted, so that property declarations are visually distinct and readable in the editor.

#### Acceptance Criteria

1. THE Syntax_Highlighting_Extension SHALL highlight the `get`, `set`, and `init` keywords using a keyword scope (e.g., `keyword.other.mist` or a property-specific scope).
2. THE Syntax_Highlighting_Extension SHALL highlight the `field` keyword using the `keyword.other.mist` scope.
3. THE Syntax_Highlighting_Extension SHALL highlight the `=>` fat arrow operator using an operator scope (e.g., `keyword.operator.arrow.mist`).
4. WHEN `get`, `set`, `init`, or `field` appear inside string literals or comments, THE Syntax_Highlighting_Extension SHALL NOT apply keyword highlighting to those occurrences.
5. THE Syntax_Highlighting_Extension SHALL continue to highlight all previously supported keywords (`if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `return`, `namespace`, `using`, `class`, `struct`, `interface`, `public`, `private`, `static`, `new`, `var`, `this`) without change.

### Requirement 17: Properties Sample Application

**User Story:** As a Mist developer, I want a dedicated sample application that demonstrates all property features, so that I can learn property syntax and verify that the compiler handles each property form correctly.

#### Acceptance Criteria

1. THE Sample_Application SHALL be located at `samples/Properties/` and contain a `Program.mist` entry point file.
2. THE Sample_Application SHALL include at least one `.mist` file demonstrating auto-properties with `get;` and `set;` accessors.
3. THE Sample_Application SHALL include at least one `.mist` file demonstrating block-body accessors with custom logic.
4. THE Sample_Application SHALL include at least one `.mist` file demonstrating expression-bodied properties and expression-bodied accessors using `=>`.
5. THE Sample_Application SHALL include at least one `.mist` file demonstrating the `field` keyword inside accessor bodies.
6. THE Sample_Application SHALL include at least one `.mist` file demonstrating mixed accessor forms (e.g., auto-accessor combined with block-body or expression-body accessor).
7. THE Sample_Application SHALL include at least one `.mist` file demonstrating accessor modifiers (e.g., `private set;`).
8. THE Sample_Application SHALL include at least one `.mist` file demonstrating `init` accessors.
9. THE Sample_Application SHALL include at least one `.mist` file demonstrating static properties.
10. THE Sample_Application SHALL include at least one `.mist` file demonstrating computed properties (get-only with body, no backing field).
11. THE Sample_Application SHALL include at least one `.mist` file demonstrating interface property declarations and a class implementing those properties.
12. THE Sample_Application SHALL include at least one `.mist` file demonstrating indexer declarations with `this[ParamType param]` syntax.
13. THE Sample_Application SHALL follow the same directory and namespace conventions as existing samples (e.g., `samples/Interfaces/`).

### Requirement 18: AllFeatures Sample Update for Properties

**User Story:** As a Mist developer, I want the AllFeatures sample to include property usage alongside existing features, so that I can see how properties integrate with fields, methods, constructors, interfaces, and inheritance.

#### Acceptance Criteria

1. WHEN the AllFeatures Sample_Application is compiled, THE Mist_Compiler SHALL compile property declarations alongside existing class members (fields, methods, constructors).
2. THE AllFeatures Sample_Application SHALL include at least one class that uses auto-properties in combination with existing constructor and method patterns.
3. THE AllFeatures Sample_Application SHALL include at least one class that uses computed properties to expose derived values from existing fields or other properties.
4. THE AllFeatures Sample_Application SHALL include at least one interface that declares property signatures, and at least one implementing class that provides matching property accessors.
5. THE AllFeatures Sample_Application SHALL demonstrate property access (reading and writing) in the `Program.mist` entry point alongside existing feature demonstrations.
6. THE AllFeatures Sample_Application SHALL continue to compile and produce correct output for all previously demonstrated features (primitives, strings, control flow, arrays, classes, structs, interfaces, inheritance, static members).
