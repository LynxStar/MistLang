# Requirements Document

## Introduction

This document specifies the requirements for adding property initializers on `new` expressions to the Mist programming language. Property initializers allow developers to set property values inline when constructing an object, using a brace-delimited block after the constructor argument list. This is analogous to C#'s object initializer syntax:

```
var p = new Point(0, 0) { X = 3, Y = 4 };
var config = new Config() { Timeout = 30, Verbose = true };
```

The feature extends the existing `ObjectCreationExpression` grammar rule to accept an optional initializer block. Each assignment within the block targets a named property or public field on the newly constructed object. The compiler validates that each name exists on the target type as either a settable property or an accessible field, that the assigned value is type-compatible, and that the member is writable. The C# emitter produces standard C# object initializer syntax, which is a direct 1:1 mapping.

This feature touches the grammar (`Mist.storm`), the parser (via Storm code generation), the pretty printer (`MistPrettyPrinter`), the semantic model builder (`SemanticModelBuilder`), the type checker (`TypeCheckerAnalyzer`), and the C# emitter (`CSharpEmitter`).

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Property_Initializer_Block**: A brace-delimited block appearing after the constructor argument list in a `new` expression, containing zero or more Property_Assignments separated by commas.
- **Property_Assignment**: A single `Name = Expression` pair within a Property_Initializer_Block that assigns a value to a named property or public field on the newly created object.
- **Object_Creation_Expression**: The existing `new Type(args)` expression that constructs an instance of a class or struct.
- **Settable_Property**: A property that has a `set` or `init` accessor, making it eligible for assignment in a Property_Initializer_Block.
- **Assignable_Member**: A property with a `set` or `init` accessor, or a public (non-readonly) field. Both are valid targets in a Property_Initializer_Block.
- **TextMate_Grammar**: The TextMate grammar definition at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` that provides syntax highlighting for Mist source files in VS Code.
- **Sample_Application**: A self-contained Mist program in the `samples/` directory that demonstrates language features and serves as an integration test.

## Requirements

### Requirement 1: Property Initializer Grammar

**User Story:** As a Mist developer, I want to write property initializers after `new` expressions using brace syntax, so that I can set property values inline at construction time.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `ObjectCreationExpression` rule to accept an optional Property_Initializer_Block after the constructor argument list.
2. THE Grammar SHALL define the Property_Initializer_Block as an opening brace, a comma-separated list of Property_Assignments, and a closing brace.
3. THE Grammar SHALL define a Property_Assignment as an identifier (the property name), the `=` token, and an expression (the value).
4. THE Grammar SHALL allow trailing commas after the last Property_Assignment in the block.
5. THE Grammar SHALL allow an empty Property_Initializer_Block (`new Type() { }`).
6. THE Grammar SHALL allow the constructor argument list to be omitted when a Property_Initializer_Block is present (e.g., `new Point { X = 3, Y = 4 }` invokes the parameterless constructor).
7. WHEN a `new` expression with a Property_Initializer_Block is parsed, THE Parser SHALL produce an AST node that captures the type, constructor arguments, and the ordered list of Property_Assignments (each containing the property name identifier and value expression).

### Requirement 2: Property Initializer Precedence and Usage Contexts

**User Story:** As a Mist developer, I want property initializers to work naturally in all expression contexts, so that I can use them in assignments, arguments, and other compound expressions without ambiguity.

#### Acceptance Criteria

1. THE Grammar SHALL allow a `new` expression with a Property_Initializer_Block to appear as the right-hand side of a variable declaration (e.g., `var p = new Point { X = 3 };`).
2. THE Grammar SHALL allow a `new` expression with a Property_Initializer_Block to appear as a method argument (e.g., `Process(new Config { Verbose = true })`).
3. THE Grammar SHALL allow a `new` expression with a Property_Initializer_Block to appear as the right-hand side of an assignment (e.g., `p = new Point { X = 1 };`).
4. THE Grammar SHALL allow a `new` expression with a Property_Initializer_Block to appear inside interpolated string expressions (e.g., `$"{new Wrapper { Value = 42 }.Value}"`).
5. WHEN a `new` expression with a Property_Initializer_Block is used in a context requiring a statement terminator, THE Parser SHALL require a semicolon after the closing brace of the initializer block.

### Requirement 3: Property Initializer Type Checking — Member Existence

**User Story:** As a Mist developer, I want the compiler to verify that each name in an initializer block exists on the target type as a property or field, so that I get a compile-time error for typos or invalid member names.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that each name in a Property_Initializer_Block corresponds to a declared property or field on the type being constructed.
2. IF a name in a Property_Initializer_Block does not correspond to any declared property or field on the target type, THEN THE Type_Checker SHALL report a diagnostic error identifying the invalid member name and the target type.
3. THE Type_Checker SHALL resolve names against the constructed type's own declared properties/fields and inherited properties/fields.

### Requirement 4: Property Initializer Type Checking — Type Compatibility

**User Story:** As a Mist developer, I want the compiler to verify that each value expression in an initializer block is type-compatible with the target property or field, so that I get a compile-time error for type mismatches.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that the type of each value expression in a Property_Assignment is assignable to the declared type of the corresponding property or field.
2. IF the value expression type is not assignable to the member type, THEN THE Type_Checker SHALL report a diagnostic error indicating the type mismatch, naming the member, the expected type, and the actual type.
3. THE Type_Checker SHALL apply the same implicit conversion rules used for regular property/field assignment when checking type compatibility in Property_Assignments.

### Requirement 5: Property Initializer Type Checking — Writability and Accessibility

**User Story:** As a Mist developer, I want the compiler to verify that each member in an initializer block is writable and accessible, so that I get a compile-time error when trying to initialize a read-only property or inaccessible member.

#### Acceptance Criteria

1. WHEN the target member is a property, THE Type_Checker SHALL verify it has a `set` or `init` accessor.
2. IF a property referenced in a Property_Initializer_Block has no `set` or `init` accessor (i.e., it is read-only), THEN THE Type_Checker SHALL report a diagnostic error indicating the property cannot be assigned in an initializer.
3. WHEN the target member is a field, THE Type_Checker SHALL verify it is not `readonly`.
4. IF a field referenced in a Property_Initializer_Block is `readonly`, THEN THE Type_Checker SHALL report a diagnostic error indicating the field cannot be assigned in an initializer.
5. THE Type_Checker SHALL verify that the target member (property setter/init or field) is accessible from the call site (respecting `private` and other access modifiers).
6. IF the member is not accessible from the call site, THEN THE Type_Checker SHALL report a diagnostic error indicating the member is inaccessible.

### Requirement 6: Property Initializer Type Checking — Duplicate Members

**User Story:** As a Mist developer, I want the compiler to reject duplicate member assignments in an initializer block, so that I don't accidentally set the same property or field twice.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that no member name appears more than once in a single Property_Initializer_Block.
2. IF a member name appears more than once in a Property_Initializer_Block, THEN THE Type_Checker SHALL report a diagnostic error indicating the duplicate assignment.

### Requirement 7: Property Initializer Semantic Model

**User Story:** As a compiler developer, I want the semantic model to represent property initializer blocks with their assignments and resolved types, so that downstream passes can analyze and emit them.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL resolve each Property_Assignment's name to the corresponding property or field symbol on the target type.
2. THE Semantic_Model_Builder SHALL resolve the type of each value expression in a Property_Assignment.
3. THE Semantic_Model_Builder SHALL record the expression type of the overall `new` expression with a Property_Initializer_Block as the constructed type (the initializer does not change the expression's type).

### Requirement 8: C# Code Emission for Property Initializers

**User Story:** As a compiler developer, I want the C# emitter to produce valid C# object initializer syntax from Mist property initializer blocks, so that the transpiled code compiles and behaves correctly.

#### Acceptance Criteria

1. THE Emitter SHALL emit a Mist `new` expression with a Property_Initializer_Block as a C# object creation expression with an object initializer (e.g., `new Point(0, 0) { X = 3, Y = 4 }`).
2. THE Emitter SHALL emit each Property_Assignment as `PropertyName = expression` within the C# object initializer braces.
3. THE Emitter SHALL separate Property_Assignments with commas in the emitted C# code.
4. WHEN the constructor argument list is omitted in the Mist source, THE Emitter SHALL emit an explicit empty argument list in C# (e.g., `new Point() { X = 3 }`).
5. THE Emitter SHALL emit the Property_Initializer_Block on the same line as the constructor call for short initializers, consistent with existing emission style.

### Requirement 9: Property Initializer Pretty Printing

**User Story:** As a compiler developer, I want the pretty printer to format property initializer blocks back into Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a `new` expression with a Property_Initializer_Block as `new Type(args) { Name1 = expr1, Name2 = expr2 }`.
2. THE Pretty_Printer SHALL format the Property_Initializer_Block with assignments separated by commas.
3. WHEN the constructor argument list is absent in the AST, THE Pretty_Printer SHALL omit the argument list (e.g., `new Point { X = 3, Y = 4 }`).
4. FOR ALL valid `new` expressions with Property_Initializer_Blocks, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 10: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want property initializer syntax to be highlighted correctly, so that property names and values are visually distinct in the editor.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL highlight property names within a Property_Initializer_Block using an appropriate scope (e.g., `variable.other.property.mist`).
2. THE TextMate_Grammar SHALL highlight the `=` operator within a Property_Initializer_Block using an operator scope.
3. WHEN property initializer syntax appears inside string literals or comments, THE TextMate_Grammar SHALL NOT apply special highlighting to those occurrences.

### Requirement 11: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating property initializers on `new` expressions, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/PropertyInitializers/Program.mist`.
2. The sample SHALL demonstrate a `new` expression with constructor arguments and a property initializer block (e.g., `new Point(0, 0) { X = 3, Y = 4 }`).
3. The sample SHALL demonstrate a `new` expression with a property initializer block but no constructor arguments (e.g., `new Config { Timeout = 30 }`).
4. The sample SHALL demonstrate a `new` expression with an empty property initializer block (e.g., `new Config() { }`).
5. The sample SHALL demonstrate initializing properties that have `init` accessors.
6. The sample SHALL demonstrate initializing properties that have `set` accessors.
7. The sample SHALL demonstrate initializing multiple properties in a single block.
8. The sample SHALL demonstrate using a `new` expression with property initializers as a method argument.
9. The sample SHALL demonstrate using a `new` expression with property initializers in a variable declaration with type inference (`var`).
10. The sample SHALL demonstrate using a `new` expression with property initializers on a generic type (e.g., `new Box<int> { Value = 42 }`).
11. The sample SHALL demonstrate using a `new` expression with property initializers on a struct type.
12. The sample SHALL demonstrate initializing a public field (not a property) in an initializer block.
13. The sample SHALL demonstrate using a `new` expression with property initializers where the value expression is a complex expression (e.g., method call, arithmetic).
14. The sample SHALL include a `public static void RunDemo()` entry point that the AllFeatures orchestrator can call.
15. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
16. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 12: AllFeatures Sample Integration

**User Story:** As a Mist developer, I want the AllFeatures sample to include property initializer usage alongside existing features, so that I can see how property initializers integrate with the rest of the language.

#### Acceptance Criteria

1. `samples/AllFeatures/Program.mist` SHALL call `PropertyInitializers.Program.RunDemo()` to include the feature in the full-suite sample run.
2. THE AllFeatures Sample_Application SHALL continue to compile and produce correct output for all previously demonstrated features.
