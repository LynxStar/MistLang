# Requirements Document

## Introduction

This document specifies the requirements for adding interface types to the Mist programming language. Interfaces define a contract of methods that implementing types must provide. A class, struct, or interface can implement zero or more interfaces, enabling multi-inheritance at the interface level. For this initial implementation, interfaces support only method signatures (no properties, fields, or modifiers on methods). When a class or struct implements an interface, it must provide implementations for all methods declared in that interface and all transitively inherited interfaces. Method implementations can come from the type itself or from any of its base types (for classes with single class inheritance).

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **InterfaceType**: The semantic model representation of a user-defined interface type, including its member scope and list of implemented interfaces.
- **Interface_Declaration**: The syntax for declaring an interface: `interface IName { ... }`.
- **Interface_Method**: A method signature declared inside an Interface_Declaration, consisting of a return type, name, and parameter list, with no body and no modifiers.
- **Interface_List**: The comma-separated list of interface names following a colon in a type declaration (e.g., `: IFoo, IBar`).
- **Implementing_Type**: A class, struct, or interface that declares one or more interfaces in its Interface_List.
- **Transitive_Interface**: An interface that is inherited indirectly through another interface in the Interface_List (e.g., if `IBar : IFoo`, then a class implementing `IBar` transitively inherits `IFoo`).
- **ClassType**: The semantic model representation of a user-defined class type, including its member scope, optional base class, and list of implemented interfaces.
- **StructType**: The semantic model representation of a user-defined struct type, including its member scope and list of implemented interfaces.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors) of a type.
- **Method_Signature**: The combination of a method's name, return type, and parameter types that uniquely identifies a method for interface satisfaction checking.

## Requirements

### Requirement 1: Interface Keyword and Declaration Grammar

**User Story:** As a Mist developer, I want to declare interfaces using the `interface` keyword, so that I can define method contracts that types must implement.

#### Acceptance Criteria

1. THE Grammar SHALL define an `INTERFACE` keyword token with the value `"interface"`.
2. THE Grammar SHALL define an `InterfaceDeclaration` rule that consists of the `INTERFACE` keyword, a name identifier, an optional Interface_List, a left brace, zero or more Interface_Method declarations, and a right brace.
3. THE Grammar SHALL define an `InterfaceMethodDeclaration` rule that consists of a return type reference, a name identifier, a left parenthesis, an optional parameter list, a right parenthesis, and a semicolon.
4. THE Grammar SHALL include `InterfaceDeclaration` as a variant in the `TypeDeclaration` rule alongside `ClassDeclaration` and `StructDeclaration`.
5. WHEN an interface declaration is parsed, THE Parser SHALL produce an AST node that captures the interface name, the optional Interface_List, and the list of Interface_Method declarations.
6. WHEN an interface declaration contains no methods, THE Parser SHALL produce an AST node with an empty method list.

### Requirement 2: Interface List Grammar for All Type Declarations

**User Story:** As a Mist developer, I want classes, structs, and interfaces to declare which interfaces they implement using `: IFoo, IBar` syntax, so that I can express interface contracts on any type.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `ClassDeclaration` rule so that the optional base clause can include a base class name followed by zero or more comma-separated interface names (e.g., `class Dog : Animal, IWalkable, IFeedable`).
2. THE Grammar SHALL extend the `ClassDeclaration` rule so that a class with no base class can still specify one or more interfaces (e.g., `class Dog : IWalkable, IFeedable`).
3. THE Grammar SHALL extend the `StructDeclaration` rule to include an optional Interface_List (e.g., `struct Point : IComparable`).
4. THE Grammar SHALL extend the `InterfaceDeclaration` rule to include an optional Interface_List for interface-to-interface inheritance (e.g., `interface IBar : IFoo`).
5. WHEN a class declaration includes both a base class and interfaces, THE Parser SHALL produce an AST node that captures the base class name and the list of interface names as separate fields.
6. WHEN a struct declaration includes an Interface_List, THE Parser SHALL produce an AST node that captures the list of interface names.

### Requirement 3: Pretty-Printer Support for Interfaces

**User Story:** As a Mist developer, I want the pretty-printer to correctly format interface declarations and interface lists, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format an interface declaration as `interface IName {` followed by its method signatures and a closing `}`.
2. THE Pretty_Printer SHALL format each Interface_Method as `returnType methodName(parameters);` with proper indentation inside the interface body.
3. THE Pretty_Printer SHALL format a class declaration with both a base class and interfaces as `class Name : Base, IFoo, IBar {`.
4. THE Pretty_Printer SHALL format a class declaration with interfaces but no base class as `class Name : IFoo, IBar {`.
5. THE Pretty_Printer SHALL format a struct declaration with interfaces as `struct Name : IFoo {`.
6. THE Pretty_Printer SHALL format an interface declaration with inherited interfaces as `interface IBar : IFoo {`.
7. FOR ALL valid Mist ASTs containing interface declarations and interface lists, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 4: InterfaceType Semantic Model

**User Story:** As a Mist developer, I want the compiler to represent interfaces in the semantic model, so that interface contracts can be validated during compilation.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL create an `InterfaceType` for each Interface_Declaration, storing the interface name, a Member_Scope containing its Interface_Method declarations, and a list of implemented interfaces.
2. THE Semantic_Model_Builder SHALL register a symbol with `SymbolKind.Interface` for each Interface_Declaration in the parent scope.
3. THE Semantic_Model_Builder SHALL register a symbol with `SymbolKind.Method` for each Interface_Method in the InterfaceType's Member_Scope.
4. THE `TypeKind` enum SHALL include an `Interface` variant to classify InterfaceType instances.

### Requirement 5: Interface Resolution on Types

**User Story:** As a Mist developer, I want the compiler to resolve interface names in type declarations to the corresponding InterfaceType, so that the compiler can validate interface contracts.

#### Acceptance Criteria

1. WHEN a type declaration includes an Interface_List, THE Semantic_Model_Builder SHALL resolve each interface name to the corresponding InterfaceType and store the list on the Implementing_Type.
2. WHEN an interface name in an Interface_List does not correspond to any declared interface, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the interface type is undefined.
3. WHEN an interface name in an Interface_List resolves to a class or struct type instead of an interface, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that only interfaces can appear in the Interface_List.
4. WHEN a class declaration includes both a base class and interfaces, THE Semantic_Model_Builder SHALL resolve the base class separately from the interfaces, storing the base class on the `BaseType` property and the interfaces on a separate `Interfaces` property.
5. THE Semantic_Model_Builder SHALL resolve interface references after all types in the compilation unit have been registered, so that forward references to interfaces are supported.

### Requirement 6: Interface Inheritance Cycle Detection

**User Story:** As a Mist developer, I want the compiler to detect circular interface inheritance, so that I receive a clear error instead of an infinite loop.

#### Acceptance Criteria

1. WHEN an interface declares itself in its own Interface_List, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that an interface cannot inherit from itself.
2. WHEN a set of interfaces forms a circular inheritance chain (e.g., `IFoo : IBar` and `IBar : IFoo`), THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that circular interface inheritance is detected.
3. WHEN circular interface inheritance is detected, THE Semantic_Model_Builder SHALL break the cycle to prevent infinite loops during subsequent compilation phases.

### Requirement 7: Interface Implementation Validation

**User Story:** As a Mist developer, I want the compiler to verify that my class or struct implements all required interface methods, so that I receive clear errors when methods are missing.

#### Acceptance Criteria

1. WHEN a class or struct implements an interface, THE Semantic_Model_Builder SHALL verify that the Implementing_Type provides a method matching each Interface_Method's Method_Signature (name, return type, and parameter types).
2. WHEN a class or struct implements an interface that itself inherits other interfaces, THE Semantic_Model_Builder SHALL verify that the Implementing_Type provides methods for all transitively inherited Interface_Methods.
3. WHEN a class implements an interface and the required method is defined in a base class (not the class itself), THE Semantic_Model_Builder SHALL accept the base class method as satisfying the interface contract.
4. WHEN a class or struct is missing one or more required interface methods, THE Semantic_Model_Builder SHALL emit a diagnostic error for each missing method, indicating the method name and the interface that requires the method.
5. WHEN a class or struct provides a method with the correct name but a mismatched return type or parameter types compared to the Interface_Method, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the signature mismatch.

### Requirement 8: Interface Subtype Assignability

**User Story:** As a Mist developer, I want to assign a class or struct instance to a variable typed as an interface it implements, so that I can write polymorphic code using interface references.

#### Acceptance Criteria

1. WHEN the source type is a class or struct that implements an interface (directly or transitively) and the target type is that interface, THE Type_Checker SHALL treat the assignment as valid.
2. WHEN the source type is a class or struct that does not implement the target interface, THE Type_Checker SHALL emit a diagnostic error indicating the types are not assignable.
3. WHEN the source type is an interface and the target type is a different interface that the source interface inherits from (directly or transitively), THE Type_Checker SHALL treat the assignment as valid.
4. WHEN a method parameter is typed as an interface, THE Type_Checker SHALL accept an argument of any type that implements that interface (directly or transitively).
5. WHEN a method return type is an interface, THE Type_Checker SHALL accept a return statement returning a value of any type that implements that interface (directly or transitively).

### Requirement 9: C# Code Emission for Interfaces

**User Story:** As a Mist developer, I want the transpiled C# code to correctly represent interfaces and interface implementation, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. THE Emitter SHALL emit each Interface_Declaration as a C# interface declaration: `interface IName { ... }`.
2. THE Emitter SHALL emit each Interface_Method as a C# interface method signature: `returnType MethodName(parameters);`.
3. WHEN a class declaration includes interfaces, THE Emitter SHALL emit the C# class declaration with the full inheritance list (e.g., `class Dog : Animal, IWalkable`).
4. WHEN a class declaration includes only interfaces and no base class, THE Emitter SHALL emit the C# class declaration with the interface list (e.g., `class Dog : IWalkable, IFeedable`).
5. WHEN a struct declaration includes interfaces, THE Emitter SHALL emit the C# struct declaration with the interface list (e.g., `struct Point : IComparable`).
6. WHEN an interface inherits other interfaces, THE Emitter SHALL emit the C# interface declaration with the inherited interface list (e.g., `interface IBar : IFoo`).

### Requirement 10: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented interface features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the interface feature is implemented, THE Compiler team SHALL update the `Type Declarations` section of CSHARP_FEATURES.md to mark `Interfaces` as ✅ Implemented.
2. WHEN the interface feature is implemented, THE Compiler team SHALL update the `Inheritance & Polymorphism` section of CSHARP_FEATURES.md to mark `Interface implementation (: IFoo)` and `Multiple interface implementation` as ✅ Implemented.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 11: Interface Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates interfaces in action, so that I can see how interface declaration, implementation, multi-inheritance, and polymorphism work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `samples/Interfaces/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate declaring an interface with multiple methods.
3. THE sample application SHALL demonstrate a class implementing a single interface.
4. THE sample application SHALL demonstrate a class implementing multiple interfaces.
5. THE sample application SHALL demonstrate interface-to-interface inheritance where a derived interface extends a base interface.
6. THE sample application SHALL demonstrate a class that inherits from a base class and also implements interfaces, with some interface methods satisfied by the base class.
7. THE sample application SHALL demonstrate assigning an implementing type to an interface-typed variable and calling methods through the interface reference.
8. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
9. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated interface features and prints observable output.

### Requirement 12: Syntax Highlighting for Interface Keyword

**User Story:** As a Mist developer using an editor, I want the `interface` keyword and interface declaration names to be syntax-highlighted, so that interface code is visually consistent with class and struct declarations.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `interface` in the `keyword.declaration.mist` pattern alongside `class` and `struct`.
2. THE TextMate grammar SHALL include a `declaration-names` pattern that matches `interface` followed by an identifier and assigns the identifier the scope `entity.name.type.interface.mist`.
3. WHEN a Mist source file contains an `interface` keyword, THE editor SHALL highlight it with the same style as `class` and `struct` keywords.
4. WHEN a Mist source file contains an interface declaration name (e.g., `IFoo` in `interface IFoo {`), THE editor SHALL highlight the name as a type declaration, consistent with class and struct declaration names.
