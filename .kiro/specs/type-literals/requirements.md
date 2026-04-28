# Requirements Document

## Introduction

Mist diverges from C# by treating types as first-class literal expressions. In C#, obtaining a runtime `System.Type` object requires the `typeof()` operator. In Mist, every type name (primitive, user-defined, array) is a valid expression that evaluates to the `Type` representing that type. For example, `var foo = int;` declares `foo` with inferred type `Type` and value equivalent to C#'s `typeof(int)`. The C# transpiler backend translates these type literal expressions into `typeof(...)` invocations.

## Glossary

- **Type_Literal**: A type name (primitive keyword, user-defined class/struct name, or array type) used in expression position, evaluating to a value of type `Type`.
- **Type_Type**: The built-in Mist type whose instances represent types at runtime. Maps to `System.Type` in the C# backend. Named `TypeType` in code, following the convention of `StringType`, `VoidType`, etc.
- **Parser**: The Mist parser (Storm-generated from `Mist.storm`) that produces the AST from a token stream.
- **Pretty_Printer**: The `MistPrettyPrinter` that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` that walks ASTs to build symbols, scopes, types, and expression type mappings.
- **Type_Checker**: The `TypeCheckerAnalyzer` that validates type correctness across the program.
- **CSharp_Emitter**: The `CSharpSourceWriter` that walks the AST and emits C# source files.
- **CSharp_Type_Map**: The `CSharpTypeMap` that maps Mist semantic types to C# source representations.
- **Grammar**: The Storm grammar definition (`Mist.storm`) that defines the Mist language syntax.
- **Expression_Position**: Any syntactic location where an expression is expected (variable initializers, arguments, return values, assignments, etc.).
- **Type_Annotation_Position**: A syntactic location where a type reference is expected (variable type declarations, parameter types, return types, field types).

## Requirements

### Requirement 1: Grammar — Type Literal Expression

**User Story:** As a Mist developer, I want to use type names as expressions, so that I can obtain type values without a special operator.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TypeLiteralExpression` alternative in the `Primary` rule that accepts a `TypeReference` in expression position.
2. WHEN the Parser encounters a primitive type keyword (e.g., `int`, `bool`, `string`) in expression position, THE Parser SHALL produce a `TypeLiteralExpression` AST node containing the parsed `TypeReference`.
3. WHEN the Parser encounters a user-defined type name (e.g., `MyClass`) in expression position, THE Parser SHALL produce a `TypeLiteralExpression` AST node containing the parsed `TypeReference`.
4. WHEN the Parser encounters an array type (e.g., `int[]`) in expression position, THE Parser SHALL produce a `TypeLiteralExpression` AST node containing the parsed `TypeReference` with the array rank.
5. WHEN a type name appears in type annotation position (parameter types, field types, return types, typed variable declarations), THE Parser SHALL continue to parse the type name as a `TypeReference` and not as a `TypeLiteralExpression`.

### Requirement 2: Parser Disambiguation

**User Story:** As a Mist developer, I want the parser to correctly distinguish between type annotations and type literal expressions, so that my code compiles without ambiguity errors.

#### Acceptance Criteria

1. WHEN a typed variable declaration is written as `int x = 5;`, THE Parser SHALL parse `int` as a type annotation and `5` as the initializer expression.
2. WHEN an inferred variable declaration is written as `var x = int;`, THE Parser SHALL parse `int` as a `TypeLiteralExpression` initializer.
3. WHEN a type literal is used as a method argument (e.g., `foo(int)`), THE Parser SHALL parse `int` as a `TypeLiteralExpression` within the argument list.
4. WHEN a type literal is used as a return value (e.g., `return int;`), THE Parser SHALL parse `int` as a `TypeLiteralExpression` within the return statement.
5. WHEN a type literal is used in an assignment (e.g., `x = int;`), THE Parser SHALL parse `int` as a `TypeLiteralExpression` on the right-hand side of the assignment.
6. WHEN a user-defined type name appears in expression position and a local variable with the same name exists in scope, THE Semantic_Model_Builder SHALL resolve the name as the local variable reference, not as a type literal.

### Requirement 3: Type_Type in the Type System

**User Story:** As a Mist developer, I want type literal expressions to have a well-defined type, so that the type checker can validate my code.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL recognize a built-in `Type_Type` type that represents the metatype of all types.
2. WHEN the Semantic_Model_Builder encounters a `TypeLiteralExpression` AST node, THE Semantic_Model_Builder SHALL record the expression type of that node as `Type_Type`.
3. WHEN a variable is declared with `var foo = int;`, THE Semantic_Model_Builder SHALL infer the type of `foo` as `Type_Type`.
4. WHEN a variable is declared with explicit type `Type foo = int;`, THE Semantic_Model_Builder SHALL verify that `Type_Type` is assignable to the declared type annotation.
5. THE Type_Type SHALL have `TypeKind.Class` because the runtime representation (`System.Type`) is a reference type.

### Requirement 4: Semantic Validation of Type Literals

**User Story:** As a Mist developer, I want clear error messages when I use type literals incorrectly, so that I can fix my code.

#### Acceptance Criteria

1. IF a `TypeLiteralExpression` references a type name that cannot be resolved in the current scope, THEN THE Semantic_Model_Builder SHALL report a diagnostic error indicating the type is undefined.
2. WHEN a `TypeLiteralExpression` references a primitive type keyword, THE Semantic_Model_Builder SHALL resolve the type literal without requiring any imports.
3. WHEN a `TypeLiteralExpression` references a user-defined type, THE Semantic_Model_Builder SHALL resolve the type using the same scope and import rules as type annotations.
4. IF a `TypeLiteralExpression` is used where a non-`Type_Type` value is expected (e.g., `int x = int;`), THEN THE Type_Checker SHALL report a type mismatch diagnostic.

### Requirement 5: C# Backend — typeof() Emission

**User Story:** As a Mist developer, I want type literals to compile to valid C# code, so that my Mist programs run correctly on .NET.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter encounters a `TypeLiteralExpression` containing a primitive type, THE CSharp_Emitter SHALL emit `typeof(<primitive_keyword>)` (e.g., `typeof(int)`).
2. WHEN the CSharp_Emitter encounters a `TypeLiteralExpression` containing a user-defined type, THE CSharp_Emitter SHALL emit `typeof(<TypeName>)` (e.g., `typeof(MyClass)`).
3. WHEN the CSharp_Emitter encounters a `TypeLiteralExpression` containing an array type, THE CSharp_Emitter SHALL emit `typeof(<ElementType>[])` (e.g., `typeof(int[])`).
4. WHEN a variable is inferred from a type literal (e.g., `var foo = int;`), THE CSharp_Emitter SHALL emit `var foo = typeof(int);` so that C# infers the variable type as `System.Type`.
5. THE CSharp_Type_Map SHALL map `Type_Type` to the C# type name `System.Type`.

### Requirement 6: Pretty Printer — Type Literal Round-Trip

**User Story:** As a Mist tooling developer, I want the pretty printer to faithfully reproduce type literal expressions, so that parse-print-parse round-trips are idempotent.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a `TypeLiteralExpression` containing a primitive type, THE Pretty_Printer SHALL emit the primitive type keyword (e.g., `int`).
2. WHEN the Pretty_Printer encounters a `TypeLiteralExpression` containing a user-defined type, THE Pretty_Printer SHALL emit the qualified type name (e.g., `MyClass`).
3. WHEN the Pretty_Printer encounters a `TypeLiteralExpression` containing an array type, THE Pretty_Printer SHALL emit the element type followed by `[]` (e.g., `int[]`).
4. FOR ALL valid Mist source texts containing type literal expressions, parsing then printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 7: Syntax Highlighting — Type Keyword

**User Story:** As a Mist developer using an editor, I want the `Type` keyword to be highlighted as a type, so that it is visually consistent with other built-in types.

#### Acceptance Criteria

1. THE syntax highlighting grammar SHALL recognize `Type` as a built-in type keyword and apply the `storage.type.metatype.mist` scope.
2. Primitive type keywords (`int`, `bool`, `string`, etc.) SHALL continue to highlight correctly when used in expression position (no changes needed — existing `storage.type.primitive.mist` scope applies regardless of position).

### Requirement 8: Sample Application — Type Literals

**User Story:** As a Mist developer, I want a sample application that demonstrates type literal usage, so that I can learn the feature by example.

#### Acceptance Criteria

1. A sample Mist program SHALL exist at `samples/TypeLiterals/Program.mist` that demonstrates primitive type literals, array type literals, explicit `Type` annotation, void type literal, and passing type literals as method arguments.
2. THE sample program SHALL compile successfully through the Mist compiler and produce valid C# output.
