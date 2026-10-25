# Requirements Document

## Introduction

This document specifies the requirements for adding ValueTuple support to the Mist programming language. ValueTuples are lightweight, structural value types that group multiple values without requiring a named type declaration. The feature includes tuple type syntax, tuple literal expressions, named tuple items, and deconstruction/decomposition. The C# backend maps Mist tuples to `System.ValueTuple<...>`.

## Glossary

- **Tuple_Type**: A structural value type composed of an ordered, fixed-length sequence of element types, written as `(T1, T2, ..., Tn)` where each `Ti` is a type reference.
- **Tuple_Literal**: An expression that constructs a tuple value, written as `(expr1, expr2, ..., exprn)`.
- **Named_Item**: A tuple element annotated with a programmer-visible name, written as `(Type name, ...)` in type position or `(name: expr, ...)` in expression position.
- **Tuple_Deconstruction**: A variable declaration form that binds individual tuple elements to separate local variables.
- **TupleType**: The semantic model type representing a Mist tuple, analogous to `ArrayType` as a structural type marker.
- **Parser**: The Mist parser generated from the Storm grammar.
- **Pretty_Printer**: The Mist pretty printer that formats AST nodes back into source text.
- **Emitter**: The C# transpiler backend that produces C# source from the Mist semantic model.
- **Type_Checker**: The analysis pass that validates type correctness of expressions and statements.
- **Backing_Type_Map**: The C# backend's internal dictionary mapping Mist types to C# source representations.

## Requirements

### Requirement 1: Tuple Type Syntax

**User Story:** As a Mist developer, I want to declare variables and parameters with tuple types using parenthesized syntax, so that I can express composite value types without defining a named struct.

#### Acceptance Criteria

1. WHEN a type reference of the form `(T1, T2, ..., Tn)` is encountered where n >= 2, THE Parser SHALL parse it as a Tuple_Type reference node containing n element type references.
2. WHEN a tuple type reference contains named items of the form `(Type1 name1, Type2 name2)`, THE Parser SHALL parse each element as a typed-and-named tuple element.
3. WHEN a tuple type reference contains fewer than 2 elements, THE Parser SHALL report a diagnostic error indicating that tuples require at least two elements.
4. THE Type_Checker SHALL resolve each element type in a Tuple_Type reference independently, applying the same rules as any other type reference position.
5. THE Pretty_Printer SHALL format a Tuple_Type reference as `(T1, T2, ..., Tn)` for unnamed elements and `(T1 name1, T2 name2, ..., Tn nameN)` for named elements.
6. FOR ALL valid Tuple_Type AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST node (round-trip property).

### Requirement 2: Tuple Literal Expressions

**User Story:** As a Mist developer, I want to create tuple values using parenthesized expression syntax, so that I can construct tuples inline without a constructor call.

#### Acceptance Criteria

1. WHEN an expression of the form `(expr1, expr2, ..., exprn)` is encountered where n >= 2, THE Parser SHALL parse it as a Tuple_Literal expression node containing n element expressions.
2. WHEN a tuple literal contains named items of the form `(name1: expr1, name2: expr2)`, THE Parser SHALL parse each element as a named tuple element expression.
3. THE Type_Checker SHALL infer the type of a Tuple_Literal as a Tuple_Type whose element types are the inferred types of each element expression.
4. WHEN a Tuple_Literal is assigned to a variable with an explicit Tuple_Type annotation, THE Type_Checker SHALL verify that each element expression is assignable to the corresponding element type.
5. THE Pretty_Printer SHALL format a Tuple_Literal as `(expr1, expr2, ..., exprn)` for unnamed elements and `(name1: expr1, name2: expr2)` for named elements.
6. FOR ALL valid Tuple_Literal AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST node (round-trip property).

### Requirement 3: Tuple Element Access

**User Story:** As a Mist developer, I want to access individual elements of a tuple by positional name or declared name, so that I can read values from a tuple without deconstruction.

#### Acceptance Criteria

1. THE TupleType SHALL expose positional element names `Item1`, `Item2`, ..., `ItemN` for each element at position 1 through N.
2. WHEN a tuple type has named items, THE TupleType SHALL expose the declared names as additional member accessors alongside the positional `ItemN` names.
3. WHEN a member access expression `tuple.ItemN` is encountered where `tuple` has a Tuple_Type and N is a valid positional index, THE Type_Checker SHALL resolve the access to the element type at position N.
4. WHEN a member access expression `tuple.name` is encountered where `tuple` has a Tuple_Type with a named item matching `name`, THE Type_Checker SHALL resolve the access to the element type of that named item.
5. IF a member access expression `tuple.name` is encountered where `name` does not match any positional or declared name on the Tuple_Type, THEN THE Type_Checker SHALL report an undefined member diagnostic.

### Requirement 4: Tuple Deconstruction

**User Story:** As a Mist developer, I want to deconstruct a tuple into individual variables in a single declaration, so that I can conveniently extract tuple elements.

#### Acceptance Criteria

1. WHEN a variable declaration of the form `var (name1, name2, ..., nameN) = tupleExpr;` is encountered, THE Parser SHALL parse it as a tuple deconstruction statement with N target variables.
2. WHEN a variable declaration of the form `(Type1 name1, Type2 name2, ..., TypeN nameN) = tupleExpr;` is encountered, THE Parser SHALL parse it as a typed tuple deconstruction statement.
3. THE Type_Checker SHALL verify that the right-hand side expression has a Tuple_Type with exactly N elements matching the number of deconstruction targets.
4. WHEN a typed deconstruction is used, THE Type_Checker SHALL verify that each element type of the tuple is assignable to the corresponding declared type.
5. WHEN a `var` deconstruction is used, THE Type_Checker SHALL infer the type of each target variable from the corresponding element type of the tuple.
6. IF the number of deconstruction targets does not match the tuple arity, THEN THE Type_Checker SHALL report a diagnostic error indicating the arity mismatch.
7. THE Pretty_Printer SHALL format a `var` deconstruction as `var (name1, name2, ..., nameN) = expr;` and a typed deconstruction as `(Type1 name1, Type2 name2, ..., TypeN nameN) = expr;`.
8. FOR ALL valid tuple deconstruction AST nodes, parsing then pretty-printing then parsing SHALL produce an equivalent AST node (round-trip property).

### Requirement 5: Semantic Model Representation

**User Story:** As a compiler developer, I want a TupleType in the semantic model that represents tuple types structurally, so that the type system can reason about tuples without special-casing generic ValueTuple arities.

#### Acceptance Criteria

1. THE TupleType SHALL implement the IType interface with `TypeKind.Struct` and a DisplayName of the form `(T1, T2, ..., Tn)` for unnamed elements or `(T1 name1, T2 name2, ..., Tn nameN)` for named elements.
2. THE TupleType SHALL store an ordered list of element types and an optional parallel list of element names.
3. WHEN two TupleType instances have the same element types in the same order, THE Type_Checker SHALL treat them as assignable to each other regardless of element name differences.
4. WHEN a TupleType has named elements, THE Type_Checker SHALL preserve those names for member access resolution but SHALL treat name differences as non-breaking for assignability.

### Requirement 6: C# Backend Emission

**User Story:** As a Mist developer, I want my tuple code to compile to idiomatic C# using `System.ValueTuple`, so that the emitted code interoperates naturally with the .NET ecosystem.

#### Acceptance Criteria

1. THE Emitter SHALL map a Mist Tuple_Type with unnamed elements `(T1, T2, ..., Tn)` to the C# tuple syntax `(T1, T2, ..., Tn)`.
2. THE Emitter SHALL map a Mist Tuple_Type with named elements `(T1 name1, T2 name2)` to the C# named tuple syntax `(T1 name1, T2 name2)`.
3. THE Emitter SHALL map a Mist Tuple_Literal `(expr1, expr2, ..., exprn)` to the C# tuple expression `(expr1, expr2, ..., exprn)`.
4. THE Emitter SHALL map a Mist named Tuple_Literal `(name1: expr1, name2: expr2)` to the C# named tuple expression `(name1: expr1, name2: expr2)`.
5. THE Emitter SHALL map tuple element access `tuple.Item1` to C# `tuple.Item1` and named access `tuple.name` to C# `tuple.name`.
6. THE Emitter SHALL map a `var` tuple deconstruction `var (a, b) = expr;` to C# `var (a, b) = expr;`.
7. THE Emitter SHALL map a typed tuple deconstruction `(int a, string b) = expr;` to C# `(int a, string b) = expr;`.

### Requirement 7: Grammar Disambiguation

**User Story:** As a compiler developer, I want the parser to correctly distinguish tuple syntax from parenthesized expressions, so that existing code continues to parse correctly.

#### Acceptance Criteria

1. WHEN a parenthesized expression contains exactly one expression and no comma, THE Parser SHALL continue to parse it as a ParenthesizedExpression (existing behavior preserved).
2. WHEN a parenthesized expression contains two or more comma-separated expressions, THE Parser SHALL parse it as a Tuple_Literal.
3. WHEN a parenthesized type reference contains exactly one type and no comma, THE Parser SHALL continue to parse it as a parenthesized grouping (if applicable) or report an error.
4. WHEN a parenthesized type reference contains two or more comma-separated typed elements, THE Parser SHALL parse it as a Tuple_Type reference.
5. WHEN a `var` keyword is followed by `(`, THE Parser SHALL attempt to parse a tuple deconstruction pattern before falling back to a standard inferred variable declaration.

### Requirement 8: Tuple Nesting

**User Story:** As a Mist developer, I want to nest tuples within other tuples, so that I can represent complex structured data without named types.

#### Acceptance Criteria

1. THE Parser SHALL accept Tuple_Type references where any element type is itself a Tuple_Type, such as `((int, int), string)`.
2. THE Parser SHALL accept Tuple_Literal expressions where any element expression is itself a Tuple_Literal, such as `((1, 2), "hello")`.
3. THE Type_Checker SHALL resolve nested tuple types recursively, applying the same element-type and arity rules at each nesting level.
4. THE Emitter SHALL emit nested tuples as nested C# tuple syntax, such as `((int, int), string)` for types and `((1, 2), "hello")` for expressions.
