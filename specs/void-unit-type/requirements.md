# Requirements Document

## Introduction

Formalize `Void` as a fully powered unit type in the Mist programming language compiler. Today, `VoidType` exists as a singleton with struct semantics and is recognized in type references and return statements, but it is not treated as a first-class value type. This feature promotes Void to a true unit type: a value type with a single instance (`Void.Void`), usable anywhere a value is expected except as an array element type. The `void` keyword remains the canonical alias for the `Void` type name.

## Glossary

- **Compiler**: The Mist compiler pipeline, encompassing lexing, parsing, semantic analysis, type checking, and code generation.
- **Type_Checker**: The `TypeCheckerAnalyzer` component that validates type correctness of Mist programs during semantic analysis.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model from the parsed AST, resolving types and building scopes.
- **Emitter**: The `CSharpEmitter` backend component that generates C# source code from the Mist semantic model.
- **Type_Map**: The `CSharpTypeMap` component that maps Mist types to their C# source representations.
- **VoidType**: The semantic model type representing the Mist unit type, implemented as a singleton with `TypeKind.Struct`.
- **Void_Instance**: The single value of the Void type, accessed via the expression `Void.Void`.
- **Unit_Type**: A type that has exactly one value, carrying no information. Void is Mist's unit type.

## Requirements

### Requirement 1: Void Type Identity

**User Story:** As a language designer, I want Void to be a well-defined value type with struct semantics, so that it behaves consistently with other value types in the type system.

#### Acceptance Criteria

1. THE VoidType SHALL have `TypeKind.Struct`.
2. THE VoidType SHALL expose a single static instance via `VoidType.Instance`.
3. THE VoidType SHALL have the display name `"void"`.
4. WHEN the `void` keyword is used as a type reference, THE Semantic_Model_Builder SHALL resolve the type reference to `VoidType.Instance`.
5. WHEN the identifier `Void` is used as a type name, THE Semantic_Model_Builder SHALL resolve the type reference to `VoidType.Instance`.

### Requirement 2: Void Instance Access

**User Story:** As a Mist developer, I want to access the Void instance via `Void.Void`, so that I can explicitly produce and pass around the unit value.

#### Acceptance Criteria

1. WHEN the expression `Void.Void` is evaluated, THE Semantic_Model_Builder SHALL resolve the expression to the Void_Instance with type `VoidType`.
2. WHEN the expression `Void.Void` is type-checked, THE Type_Checker SHALL assign the type `VoidType` to the expression.
3. THE Compiler SHALL treat `Void.Void` as the sole mechanism for explicitly obtaining a Void value.

### Requirement 3: Void as Return Type

**User Story:** As a Mist developer, I want methods with a `void` return type to implicitly return the Void instance, so that void methods participate uniformly in the type system.

#### Acceptance Criteria

1. WHEN a method declares `void` as its return type, THE Semantic_Model_Builder SHALL resolve the return type to `VoidType.Instance`.
2. WHEN a `return;` statement appears in a void method, THE Compiler SHALL treat the statement as equivalent to `return Void.Void;`.
3. WHEN a void method body reaches its end without an explicit return statement, THE Compiler SHALL treat the method as implicitly returning `Void.Void`.
4. WHEN a `return` statement in a void method includes a value expression, THE Type_Checker SHALL report a diagnostic error with id `MIST_TC_0008 (historical)` if the expression type is not `VoidType`.
5. WHEN a `return Void.Void;` statement appears in a void method, THE Type_Checker SHALL accept the statement without error.

### Requirement 4: Void as Variable Type

**User Story:** As a Mist developer, I want to declare variables of type `void` and assign the Void instance to them, so that I can use Void values in local bindings.

#### Acceptance Criteria

1. WHEN a variable is declared with type `void`, THE Semantic_Model_Builder SHALL resolve the variable type to `VoidType.Instance`.
2. WHEN a variable of type `void` is assigned the expression `Void.Void`, THE Type_Checker SHALL accept the assignment without error.
3. WHEN a variable is declared with `var` and initialized with `Void.Void`, THE Semantic_Model_Builder SHALL infer the variable type as `VoidType`.
4. WHEN a non-void expression is assigned to a variable of type `void`, THE Type_Checker SHALL report a diagnostic error indicating a type mismatch.

### Requirement 5: Void as Generic Type Argument

**User Story:** As a Mist developer, I want to use `void` as a type argument in generic types, so that I can express concepts like `Result<void>` where the success value carries no information.

#### Acceptance Criteria

1. WHEN `void` is used as a type argument in a generic type instantiation, THE Semantic_Model_Builder SHALL resolve the type argument to `VoidType.Instance`.
2. WHEN a generic type is instantiated with `void` as a type argument, THE Type_Checker SHALL accept the instantiation without error.
3. WHEN a generic method or type operates on a `void` type argument, THE Compiler SHALL treat the type parameter as `VoidType` throughout the generic body.

### Requirement 6: Void as Parameter Type

**User Story:** As a Mist developer, I want to declare method parameters of type `void`, so that Void participates uniformly in method signatures, particularly in generic contexts.

#### Acceptance Criteria

1. WHEN a method parameter is declared with type `void`, THE Semantic_Model_Builder SHALL resolve the parameter type to `VoidType.Instance`.
2. WHEN a method with a `void` parameter is called with `Void.Void` as the argument, THE Type_Checker SHALL accept the call without error.
3. WHEN a method with a `void` parameter is called with a non-void argument, THE Type_Checker SHALL report a diagnostic error indicating a type mismatch.

### Requirement 7: Void as Field Type

**User Story:** As a Mist developer, I want to declare fields of type `void`, so that Void participates uniformly in type declarations, particularly in generic struct contexts.

#### Acceptance Criteria

1. WHEN a field is declared with type `void`, THE Type_Checker SHALL accept the declaration without error.
2. WHEN a field is declared with type `Void` (using the type name rather than the keyword), THE Type_Checker SHALL accept the declaration without error.
3. WHEN a field of type `void` is assigned the expression `Void.Void`, THE Type_Checker SHALL accept the assignment without error.

### Requirement 8: Void Array Restriction

**User Story:** As a language designer, I want to prohibit arrays of Void, so that developers do not create collections of a type that carries no information.

#### Acceptance Criteria

1. WHEN an array type `void[]` is declared, THE Type_Checker SHALL report a diagnostic error indicating that Void is not permitted as an array element type.
2. WHEN an array creation expression `new void[n]` is evaluated, THE Type_Checker SHALL report a diagnostic error indicating that Void is not permitted as an array element type.

### Requirement 9: Void Equality

**User Story:** As a Mist developer, I want all Void values to be equal, so that equality comparisons on Void behave predictably.

#### Acceptance Criteria

1. WHEN two expressions of type `VoidType` are compared with `==`, THE Type_Checker SHALL accept the comparison without error.
2. WHEN two expressions of type `VoidType` are compared with `!=`, THE Type_Checker SHALL accept the comparison without error.
3. WHEN a `VoidType` expression is compared with a non-void expression using `==` or `!=`, THE Type_Checker SHALL report a diagnostic error indicating incompatible types.

### Requirement 10: C# Code Generation for Void Values

**User Story:** As a compiler developer, I want the C# backend to correctly emit code for Void as a unit type, so that Mist programs using Void compile and run correctly on .NET.

#### Acceptance Criteria

1. WHEN the Emitter generates code for a `Void.Void` expression, THE Emitter SHALL emit a valid C# expression that produces the Void instance.
2. WHEN the Emitter generates code for a variable of type `void`, THE Emitter SHALL emit a C# type that represents the Mist Void unit type (not the C# `void` keyword, which is not a valid variable type in C#).
3. WHEN the Emitter generates code for a method that returns `void` and is used as a value-returning method (e.g., assigned to a variable), THE Emitter SHALL emit a C# method that returns the Void struct type rather than C# `void`.
4. WHEN the Emitter generates code for a void method that is not used as a value-returning method, THE Emitter SHALL emit a standard C# `void` method for interoperability and performance.
5. THE Type_Map SHALL map `VoidType` to the appropriate C# representation depending on context: `void` for fire-and-forget method signatures, and the Void struct type for value contexts.
