# Requirements Document

## Introduction

This document specifies the requirements for adding generics and type arguments to the Mist programming language. Generics allow classes, structs, interfaces, and methods to be parameterized by one or more type parameters, enabling type-safe reusable data structures and algorithms. Mist generics follow the C# reified generics model, where type information is preserved at runtime. The implementation covers the full compiler pipeline: grammar extensions for type parameter lists and type argument lists, pretty-printer support, semantic model construction (type parameter registration, generic type construction, constructed type resolution), type checking (generic type compatibility and assignability), and C# code emission. This spec covers generic type declarations, generic methods, multiple type parameters, nested generics, generic member fields, static members in generic types, inheritance with generics, and constructor syntax for generic types. Generic constraints, type inference, default type arguments, generic type aliases, and variance are out of scope and will be addressed in separate specs.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes: Pass 1 registers types and members, Pass 1b resolves base types and interface lists, Pass 1c detects cycles, Pass 1d validates interface satisfaction, Pass 1e checks member redeclarations, Pass 1f checks base constructor satisfaction, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Type_Parameter**: A placeholder type declared in angle brackets on a generic type or method declaration (e.g., `T` in `class Box<T>`). A Type_Parameter acts as a type within the scope of the declaring type or method.
- **Type_Parameter_List**: A comma-separated list of one or more Type_Parameter names enclosed in angle brackets (e.g., `<T>`, `<K, V>`), appearing on a type or method declaration.
- **Type_Argument**: A concrete type supplied when using a generic type or invoking a generic method (e.g., `int` in `Box<int>`).
- **Type_Argument_List**: A comma-separated list of one or more Type_Argument types enclosed in angle brackets (e.g., `<int>`, `<string, int>`), appearing in type references, `new` expressions, and method invocations.
- **Generic_Type_Declaration**: A class, struct, or interface declaration that includes a Type_Parameter_List (e.g., `class Box<T> { ... }`).
- **Generic_Method_Declaration**: A method declaration that includes its own Type_Parameter_List (e.g., `fun identity<T>(value: T): T`), independent of any enclosing type's type parameters.
- **Open_Generic_Type**: A generic type where one or more type parameters remain unsubstituted (e.g., `Box<T>` within the body of `Box<T>`).
- **Constructed_Type**: A generic type with all type parameters substituted by concrete Type_Arguments (e.g., `Box<int>`, `Map<string, List<int>>`).
- **Type_Parameter_Scope**: The scope in which a Type_Parameter is valid — the body of the declaring type or method, including member declarations, method bodies, and nested type references.
- **ClassType**: The semantic model representation of a user-defined class type, including its member scope, optional base class, list of implemented interfaces, and (with this feature) optional type parameters.
- **StructType**: The semantic model representation of a user-defined struct type, including its member scope, list of implemented interfaces, and (with this feature) optional type parameters.
- **InterfaceType**: The semantic model representation of a user-defined interface type, including its member scope, list of inherited interfaces, and (with this feature) optional type parameters.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors) of a type.
- **Nested_Generic**: A type argument that is itself a generic constructed type (e.g., `List<List<int>>`, `Map<string, List<int>>`).

## Requirements

### Requirement 1: Type Parameter List Grammar for Type Declarations

**User Story:** As a Mist developer, I want to declare generic classes, structs, and interfaces with type parameters, so that I can create reusable type-safe data structures.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TypeParameterList` rule that consists of a `LESS_THAN` token, one or more comma-separated `IDENTIFIER` tokens, and a `GREATER_THAN` token.
2. THE Grammar SHALL extend the `ClassDeclaration` rule to include an optional `TypeParameterList` after the class name and before the optional base clause (e.g., `class Box<T> : BaseClass`).
3. THE Grammar SHALL extend the `StructDeclaration` rule to include an optional `TypeParameterList` after the struct name and before the optional interface list (e.g., `struct Pair<T, U> : IEquatable`).
4. THE Grammar SHALL extend the `InterfaceDeclaration` rule to include an optional `TypeParameterList` after the interface name and before the optional interface list (e.g., `interface ICollection<T> : IEnumerable`).
5. WHEN a type declaration includes a Type_Parameter_List, THE Parser SHALL produce an AST node that captures the list of type parameter names as distinct from the type name.
6. WHEN a type declaration includes no Type_Parameter_List, THE Parser SHALL produce an AST node with an absent type parameter list, preserving backward compatibility with existing non-generic declarations.

### Requirement 2: Type Parameter List Grammar for Method Declarations

**User Story:** As a Mist developer, I want to declare generic methods with their own type parameters, so that I can write type-safe utility methods independent of the enclosing type's generics.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `MethodDeclaration` rule to include an optional `TypeParameterList` after the method name and before the parameter list (e.g., `void Identity<T>(T value)`).
2. THE Grammar SHALL extend the `InterfaceMethodDeclaration` rule to include an optional `TypeParameterList` after the method name and before the parameter list.
3. WHEN a method declaration includes a Type_Parameter_List, THE Parser SHALL produce an AST node that captures the method's type parameter names separately from the enclosing type's type parameters.
4. WHEN a method declaration includes no Type_Parameter_List, THE Parser SHALL produce an AST node with an absent type parameter list, preserving backward compatibility with existing non-generic methods.

### Requirement 3: Type Argument List Grammar for Type References

**User Story:** As a Mist developer, I want to use generic types with concrete type arguments in type references, so that I can declare variables, fields, parameters, and return types using constructed generic types.

#### Acceptance Criteria

1. THE Grammar SHALL define a `TypeArgumentList` rule that consists of a `LESS_THAN` token, one or more comma-separated `TypeReference` entries, and a `GREATER_THAN` token.
2. THE Grammar SHALL extend the `TypeReference` rule so that a `NamedTypeRef` can include an optional `TypeArgumentList` after the qualified identifier (e.g., `List<int>`, `Map<string, int>`).
3. THE Grammar SHALL support Nested_Generic type arguments where a Type_Argument is itself a constructed generic type (e.g., `List<List<int>>`, `Map<string, List<int>>`).
4. WHEN a type reference includes a Type_Argument_List, THE Parser SHALL produce an AST node that captures the list of type argument references.
5. WHEN a type reference includes no Type_Argument_List, THE Parser SHALL produce an AST node with an absent type argument list, preserving backward compatibility with existing non-generic type references.

### Requirement 4: Type Argument List Grammar for Object Creation and Method Invocation

**User Story:** As a Mist developer, I want to construct generic objects with `new List<int>()` and invoke generic methods with explicit type arguments, so that I can use generics in expressions.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `ObjectCreationExpression` rule so that the type name can include an optional `TypeArgumentList` (e.g., `new List<int>()`, `new Map<string, int>()`).
2. THE Grammar SHALL extend the `MethodCallSuffix` rule so that a method invocation can include an optional `TypeArgumentList` after the method name and before the argument list (e.g., `obj.Identity<int>(42)`).
3. WHEN an object creation expression includes a Type_Argument_List, THE Parser SHALL produce an AST node that captures the type arguments separately from the constructor arguments.
4. WHEN a method invocation includes a Type_Argument_List, THE Parser SHALL produce an AST node that captures the type arguments separately from the method arguments.
5. THE Grammar SHALL disambiguate type argument lists from comparison expressions by restricting Type_Argument_List parsing to the four structural contexts: type declarations, method declarations, `new` expressions, and method invocations.

### Requirement 5: Pretty-Printer Support for Generics

**User Story:** As a Mist developer, I want the pretty-printer to correctly format generic declarations and type arguments, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a generic class declaration as `class Name<T, U> {` with the type parameter list immediately after the class name.
2. THE Pretty_Printer SHALL format a generic struct declaration as `struct Name<T> {` with the type parameter list immediately after the struct name.
3. THE Pretty_Printer SHALL format a generic interface declaration as `interface IName<T> {` with the type parameter list immediately after the interface name.
4. THE Pretty_Printer SHALL format a generic method declaration with the type parameter list immediately after the method name and before the parameter list (e.g., `void Identity<T>(T value)`).
5. THE Pretty_Printer SHALL format type references with type arguments as `Name<Arg1, Arg2>` with no spaces inside the angle brackets.
6. THE Pretty_Printer SHALL format nested generic type arguments correctly (e.g., `List<List<int>>`, `Map<string, List<int>>`).
7. THE Pretty_Printer SHALL format object creation expressions with type arguments as `new Name<Arg>(args)`.
8. THE Pretty_Printer SHALL format method invocations with type arguments as `target.Method<Arg>(args)`.
9. THE Pretty_Printer SHALL format generic type declarations with base clauses as `class Name<T> : Base<T> {` and with interface lists as `class Name<T> : IFoo<T>, IBar {`.
10. FOR ALL valid Mist ASTs containing generic declarations and type arguments, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 6: Type Parameter Registration in the Semantic Model

**User Story:** As a Mist developer, I want the compiler to register type parameters in the semantic model, so that type parameters can be used as types within the declaring scope.

#### Acceptance Criteria

1. WHEN a type declaration includes a Type_Parameter_List, THE Semantic_Model_Builder SHALL create a type parameter representation for each type parameter name and register it in the type's Member_Scope.
2. WHEN a method declaration includes a Type_Parameter_List, THE Semantic_Model_Builder SHALL create a type parameter representation for each type parameter name and register it in the method's scope.
3. THE Semantic_Model_Builder SHALL store the list of type parameters on the ClassType, StructType, or InterfaceType so that the type's arity (number of type parameters) is available for validation.
4. THE Semantic_Model_Builder SHALL make type parameters resolvable as types within the Type_Parameter_Scope, so that fields, method parameters, return types, and local variables can reference them.
5. WHEN a type parameter name duplicates another type parameter name in the same Type_Parameter_List, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating duplicate type parameter names.
6. WHEN a type parameter name shadows the enclosing type name, THE Semantic_Model_Builder SHALL emit a diagnostic warning indicating the shadowing.

### Requirement 7: Constructed Type Resolution in the Semantic Model

**User Story:** As a Mist developer, I want the compiler to resolve type references with type arguments into constructed types, so that `List<int>` is recognized as a distinct type from `List<string>`.

#### Acceptance Criteria

1. WHEN a type reference includes a Type_Argument_List and the referenced type is a Generic_Type_Declaration, THE Semantic_Model_Builder SHALL construct a Constructed_Type by substituting each type parameter with the corresponding type argument.
2. WHEN a type reference includes a Type_Argument_List and the number of type arguments does not match the number of type parameters on the referenced type, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the arity mismatch.
3. WHEN a type reference includes a Type_Argument_List and the referenced type is not a Generic_Type_Declaration, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the type does not accept type arguments.
4. WHEN a type reference uses a type parameter as a type argument (e.g., `List<T>` inside `class Wrapper<T>`), THE Semantic_Model_Builder SHALL resolve the type parameter to its declaration and construct the type accordingly.
5. WHEN a type reference uses nested generics (e.g., `List<List<int>>`), THE Semantic_Model_Builder SHALL resolve the inner type arguments first and then construct the outer type.
6. THE Semantic_Model_Builder SHALL resolve type arguments after all types in the compilation are registered, so that forward references to generic types are supported.

### Requirement 8: Generic Inheritance Resolution

**User Story:** As a Mist developer, I want to use generic types in inheritance clauses, so that I can write `class OrderedList<T> : List<T>` and `class IntList : List<int>`.

#### Acceptance Criteria

1. WHEN a class declaration specifies a base class with type arguments (e.g., `class OrderedList<T> : List<T>`), THE Semantic_Model_Builder SHALL resolve the base class to a Constructed_Type and store it on the ClassType's `BaseType` property.
2. WHEN a class declaration specifies a base class with concrete type arguments (e.g., `class IntList : List<int>`), THE Semantic_Model_Builder SHALL resolve the base class to a Constructed_Type with the concrete type arguments.
3. WHEN a type declaration specifies interfaces with type arguments (e.g., `class Box<T> : IContainer<T>`), THE Semantic_Model_Builder SHALL resolve each interface to a Constructed_Type and store them on the type's `Interfaces` property.
4. WHEN a non-generic type inherits from a generic type without providing type arguments, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that type arguments are required.
5. WHEN a generic type inherits from another generic type, THE Semantic_Model_Builder SHALL correctly propagate type parameter substitutions through the inheritance chain.

### Requirement 9: Generic Member Resolution

**User Story:** As a Mist developer, I want fields, method parameters, and return types inside generic types to use the enclosing type's type parameters, so that `class Box<T> { T value; }` works correctly.

#### Acceptance Criteria

1. WHEN a field declaration inside a Generic_Type_Declaration uses a type parameter as its type (e.g., `T value;`), THE Semantic_Model_Builder SHALL resolve the field's type to the type parameter.
2. WHEN a method inside a Generic_Type_Declaration uses a type parameter as a parameter type or return type, THE Semantic_Model_Builder SHALL resolve those types to the type parameter.
3. WHEN a member of a Constructed_Type is accessed (e.g., `box.value` where `box` is `Box<int>`), THE Type_Checker SHALL substitute the type parameter with the corresponding type argument (e.g., `T` becomes `int`).
4. WHEN a generic method has its own type parameters that shadow the enclosing type's type parameters, THE Semantic_Model_Builder SHALL resolve references within the method body to the method's type parameters.

### Requirement 10: Static Members in Generic Types

**User Story:** As a Mist developer, I want static members in generic types to follow the C# model where each constructed type has its own static state, so that `Box<int>.count` and `Box<string>.count` are independent.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL allow static fields and static methods inside Generic_Type_Declarations.
2. THE Emitter SHALL emit static members inside generic type declarations using the same C# pattern, so that the C# runtime provides separate static state per constructed type.
3. WHEN a static member of a generic type is accessed with type arguments (e.g., `Box<int>.Count`), THE Type_Checker SHALL validate the access against the constructed type's member scope.

### Requirement 11: Type Checking for Generic Types

**User Story:** As a Mist developer, I want the type checker to correctly validate assignments and expressions involving generic types, so that type safety is maintained with generics.

#### Acceptance Criteria

1. WHEN the source type and target type are both Constructed_Types of the same generic type with the same type arguments, THE Type_Checker SHALL treat the assignment as valid.
2. WHEN the source type and target type are Constructed_Types of the same generic type with different type arguments (e.g., `List<int>` to `List<string>`), THE Type_Checker SHALL emit a diagnostic error indicating the types are not assignable.
3. WHEN a Constructed_Type inherits from another Constructed_Type (e.g., `OrderedList<int>` extends `List<int>`), THE Type_Checker SHALL treat assignment from the derived type to the base type as valid.
4. WHEN a Constructed_Type implements a constructed interface (e.g., `Box<int>` implements `IContainer<int>`), THE Type_Checker SHALL treat assignment from the implementing type to the interface type as valid.
5. WHEN a method is invoked with explicit type arguments, THE Type_Checker SHALL substitute the type parameters with the provided type arguments and validate parameter types and return type accordingly.
6. WHEN a generic method is invoked with the wrong number of type arguments, THE Type_Checker SHALL emit a diagnostic error indicating the arity mismatch.

### Requirement 12: C# Code Emission for Generics

**User Story:** As a Mist developer, I want the transpiled C# code to correctly represent generic declarations and type arguments, so that the compiled output preserves generic type safety.

#### Acceptance Criteria

1. THE Emitter SHALL emit each Generic_Type_Declaration with its type parameter list in C# syntax (e.g., `class Box<T>`, `struct Pair<T, U>`, `interface ICollection<T>`).
2. THE Emitter SHALL emit generic method declarations with their type parameter list in C# syntax (e.g., `T Identity<T>(T value)`).
3. THE Emitter SHALL emit type references with type arguments in C# syntax (e.g., `List<int>`, `Map<string, List<int>>`).
4. THE Emitter SHALL emit object creation expressions with type arguments in C# syntax (e.g., `new List<int>()`).
5. THE Emitter SHALL emit method invocations with type arguments in C# syntax (e.g., `obj.Identity<int>(42)`).
6. THE Emitter SHALL emit generic inheritance clauses in C# syntax (e.g., `class OrderedList<T> : List<T>`, `class IntList : List<int>`).
7. THE Emitter SHALL emit generic interface implementation clauses in C# syntax (e.g., `class Box<T> : IContainer<T>`).
8. THE Emitter SHALL emit nested generic type arguments correctly in C# syntax (e.g., `List<List<int>>`).

### Requirement 13: Error Diagnostics for Generics

**User Story:** As a Mist developer, I want clear error messages when I misuse generics, so that I can quickly identify and fix problems in my generic code.

#### Acceptance Criteria

1. WHEN a type reference provides a different number of type arguments than the generic type declares type parameters, THE Compiler SHALL emit a diagnostic error stating the expected and actual arity (e.g., "Type 'Map' expects 2 type arguments but received 1").
2. WHEN a type reference provides type arguments to a non-generic type, THE Compiler SHALL emit a diagnostic error stating that the type does not accept type arguments.
3. WHEN a Type_Parameter_List contains duplicate type parameter names, THE Compiler SHALL emit a diagnostic error for each duplicate name.
4. WHEN a generic method invocation provides a different number of type arguments than the method declares type parameters, THE Compiler SHALL emit a diagnostic error stating the expected and actual arity.
5. WHEN a non-generic type inherits from a generic type without providing type arguments, THE Compiler SHALL emit a diagnostic error stating that type arguments are required.
6. IF a type argument cannot be resolved to a known type, THEN THE Compiler SHALL emit a diagnostic error indicating the type argument is undefined.

### Requirement 14: Generics Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates generics in action, so that I can see how generic classes, structs, interfaces, methods, and inheritance work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `Samples/Generics/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate declaring a generic class with a single type parameter (e.g., `class Box<T>`).
3. THE sample application SHALL demonstrate declaring a generic class with multiple type parameters (e.g., `class Pair<T, U>`).
4. THE sample application SHALL demonstrate declaring a generic struct (e.g., `struct ValueHolder<T>`).
5. THE sample application SHALL demonstrate declaring a generic interface (e.g., `interface IContainer<T>`).
6. THE sample application SHALL demonstrate a generic method on a class (e.g., a method with its own type parameter).
7. THE sample application SHALL demonstrate constructing generic objects with concrete type arguments (e.g., `new Box<int>()`, `new Pair<string, int>()`).
8. THE sample application SHALL demonstrate nested generic type arguments (e.g., `Box<List<int>>`).
9. THE sample application SHALL demonstrate inheritance with generics, including a generic class extending another generic class and a non-generic class extending a constructed generic type.
10. THE sample application SHALL demonstrate a generic type implementing a generic interface.
11. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
12. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated generics features and prints observable output.

### Requirement 15: All-Features Sample Update

**User Story:** As a Mist project maintainer, I want the AllFeatures sample updated to include generics examples, so that the comprehensive sample remains current.

#### Acceptance Criteria

1. THE AllFeatures sample update SHALL add generics examples in separate `.mist` files within the `Samples/AllFeatures/` directory, rather than modifying existing files, to facilitate easier merging.
2. THE AllFeatures sample update SHALL demonstrate at least one generic class, one generic interface, and one usage of constructed generic types.
3. THE AllFeatures sample update SHALL compile and run successfully as part of the existing AllFeatures sample.

### Requirement 16: Syntax Highlighting for Generics

**User Story:** As a Mist developer using an editor, I want type parameters and type arguments in generic declarations to be syntax-highlighted, so that generic code is visually clear.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include patterns that match type parameter lists in class, struct, interface, and method declarations.
2. THE TextMate grammar SHALL assign type parameter names within angle brackets the scope `entity.name.type.parameter.mist` to distinguish them from regular type names.
3. THE TextMate grammar SHALL highlight type arguments in type references (e.g., `List<int>`) with appropriate type scoping.
4. WHEN a Mist source file contains a generic declaration (e.g., `class Box<T>`), THE editor SHALL highlight the type parameter `T` distinctly from the class name `Box`.

### Requirement 17: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented generics features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the generics feature is implemented, THE Compiler team SHALL update the `Generics` section of CSHARP_FEATURES.md to mark `Generic classes`, `Generic methods`, and `Generic interfaces` as implemented.
2. WHEN the generics feature is implemented, THE Compiler team SHALL update the `Type Declarations` section of CSHARP_FEATURES.md to mark `Generic type declarations` as implemented.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.
