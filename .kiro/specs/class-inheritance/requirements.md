# Requirements Document

> **Amended** by [`defaultability`](../defaultability/requirements.md) Requirement 9.7 (2026-10-01): a Derived_Class that declares no constructor is constructed as its default, which starts from the Base_Class's default and runs no Base_Class constructor. It needs a defaultable Base_Class rather than a parameterless constructor, so criteria 8.1 and 8.2 below do not apply to it, and a Base_Class with no default makes it an error (defaultability Requirement 9.1). Criterion 8.3 still governs a constructor the Derived_Class declares.

## Introduction

This document specifies the requirements for adding single class inheritance to the Mist programming language. Mist is a C#-like language that transpiles to C#. This feature allows a class to extend exactly one base class, inheriting its fields and methods. The inheritance model is additive-only with static dispatch — derived classes can add new members but cannot override base class behavior, and method resolution is always determined by the declared (compile-time) type of the variable, not the runtime type.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **ClassType**: The semantic model representation of a user-defined class type, including its member scope and optional base type.
- **Base_Class**: The class from which another class inherits fields and methods.
- **Derived_Class**: A class that extends a Base_Class using the `: BaseClassName` syntax.
- **Inheritance_Clause**: The optional `: BaseClassName` syntax in a class declaration that specifies the Base_Class.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors) of a class.
- **Static_Dispatch**: Method resolution based on the declared (compile-time) type of the variable, not the runtime type of the object.
- **Subtype_Assignability**: The rule that a Derived_Class value is assignable to a variable, parameter, or return type declared as the Base_Class.

## Requirements

### Requirement 1: Inheritance Clause Grammar

**User Story:** As a Mist developer, I want to declare that a class extends another class using `: BaseClassName` syntax, so that I can reuse fields and methods from an existing class.

#### Acceptance Criteria

1. WHEN a class declaration includes an Inheritance_Clause, THE Parser SHALL parse the declaration into an AST node that captures both the class name and the base class name.
2. WHEN a class declaration does not include an Inheritance_Clause, THE Parser SHALL parse the declaration as it does today, with no base class reference.
3. THE Grammar SHALL define the Inheritance_Clause as an optional `COLON IDENTIFIER` sequence between the class name and the opening left brace in a `ClassDeclaration` rule.
4. THE Pretty_Printer SHALL format a class declaration with an Inheritance_Clause as `class DerivedName : BaseName {`.
5. THE Pretty_Printer SHALL format a class declaration without an Inheritance_Clause as `class Name {` (unchanged from current behavior).
6. FOR ALL valid Mist ASTs containing class declarations with or without an Inheritance_Clause, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 2: Base Type Resolution

**User Story:** As a Mist developer, I want the compiler to resolve the base class name in an Inheritance_Clause to the corresponding ClassType, so that the derived class can access inherited members.

#### Acceptance Criteria

1. WHEN a class declaration includes an Inheritance_Clause referencing a valid class name, THE Semantic_Model_Builder SHALL resolve the base class name to the corresponding ClassType and store it on the Derived_Class's ClassType as a `BaseType` property.
2. WHEN a class declaration includes an Inheritance_Clause referencing a name that does not correspond to any declared class, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the base type is undefined.
3. WHEN a class declaration includes an Inheritance_Clause referencing a struct type, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a class cannot inherit from a struct.
4. WHEN a class declaration does not include an Inheritance_Clause, THE Semantic_Model_Builder SHALL set the ClassType's `BaseType` property to null.
5. THE Semantic_Model_Builder SHALL resolve base type references during Pass 1 (type registration), after all types in the compilation unit have been registered, so that forward references to base classes are supported.

### Requirement 3: Single Inheritance Constraint

**User Story:** As a Mist developer, I want the compiler to enforce that a class can extend at most one base class, so that the inheritance model remains simple and predictable.

#### Acceptance Criteria

1. THE Grammar SHALL permit at most one base class name in the Inheritance_Clause (the grammar rule itself enforces single inheritance by allowing only one IDENTIFIER after the COLON).
2. WHEN a class declaration specifies itself as its own base class, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a class cannot inherit from itself.
3. WHEN a class inheritance chain contains a cycle (e.g., A extends B and B extends A), THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that circular inheritance is detected.

### Requirement 4: Inherited Member Access

**User Story:** As a Mist developer, I want to access fields and methods defined in a base class through a derived class instance, so that I can reuse base class functionality without redeclaring members.

#### Acceptance Criteria

1. WHEN a member access expression references a member name that is not declared in the Derived_Class but is declared in the Base_Class, THE Semantic_Model_Builder SHALL resolve the member by walking up the inheritance chain and returning the first matching member.
2. WHEN a member access expression references a member name that is not declared in any class in the inheritance chain, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the member is undefined.
3. WHEN a Derived_Class declares a field with the same name as a field in the Base_Class, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the Derived_Class cannot redeclare a field already defined in the Base_Class.
4. WHEN a Derived_Class declares a method with the same name as a method in the Base_Class, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the Derived_Class cannot redeclare a method already defined in the Base_Class.
5. WHEN a constructor in the Derived_Class uses `this` to access a field or method inherited from the Base_Class, THE Semantic_Model_Builder SHALL resolve the member through the inheritance chain.

### Requirement 5: Subtype Assignability

**User Story:** As a Mist developer, I want to assign a derived class instance to a variable typed as the base class, so that I can write polymorphic code that operates on base class references.

#### Acceptance Criteria

1. WHEN the source type is a Derived_Class and the target type is its Base_Class (direct or transitive), THE Type_Checker SHALL treat the assignment as valid.
2. WHEN the source type is a Base_Class and the target type is a Derived_Class, THE Type_Checker SHALL emit a diagnostic error indicating that a base type is not assignable to a derived type.
3. WHEN a method parameter is typed as a Base_Class, THE Type_Checker SHALL accept an argument of any Derived_Class type that directly or transitively extends the Base_Class.
4. WHEN a method return type is a Base_Class, THE Type_Checker SHALL accept a return statement returning a Derived_Class value that directly or transitively extends the Base_Class.
5. THE Type_Checker SHALL treat two unrelated class types (neither is an ancestor of the other) as not assignable to each other.

### Requirement 6: Static Dispatch Semantics

**User Story:** As a Mist developer, I want method calls to always resolve based on the declared type of the variable, so that behavior is predictable and there are no hidden virtual dispatch surprises.

#### Acceptance Criteria

1. WHEN a method is called on a variable declared as a Base_Class type, THE Compiler SHALL resolve the call to the Base_Class's method definition, regardless of the runtime type of the object.
2. WHEN a variable is declared as a Derived_Class type, THE Compiler SHALL resolve method calls to the Derived_Class's own members first, then walk up the inheritance chain if the member is not found in the Derived_Class.
3. THE Emitter SHALL emit C# code that uses the `new` keyword on Derived_Class methods that share a name with a Base_Class method, so that C# also uses static dispatch (hiding) rather than virtual dispatch.

### Requirement 7: C# Code Emission

**User Story:** As a Mist developer, I want the transpiled C# code to correctly represent the inheritance relationship, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. WHEN a class declaration includes an Inheritance_Clause, THE Emitter SHALL emit the C# class declaration as `class DerivedName : BaseName`.
2. WHEN a class declaration does not include an Inheritance_Clause, THE Emitter SHALL emit the C# class declaration as `class Name` (unchanged from current behavior).
3. WHEN a Derived_Class declares a method with the same name as a Base_Class method (if such redeclaration is permitted in future), THE Emitter SHALL emit the `new` keyword modifier on the Derived_Class method to ensure C# uses static dispatch.

### Requirement 8: Constructor Behavior

**User Story:** As a Mist developer, I want derived class constructors to work correctly with base class fields, so that objects are properly initialized.

#### Acceptance Criteria

1. WHEN a Derived_Class does not declare any constructor, THE Compiler SHALL allow construction of the Derived_Class using `new DerivedClass()` provided the Base_Class also has a parameterless constructor or no explicit constructor.
2. WHEN a Derived_Class is constructed, THE Emitter SHALL emit a call to the Base_Class constructor (via `: base()`) in the generated C# code if the Base_Class has an explicit constructor.
3. IF the Base_Class requires constructor arguments and the Derived_Class does not provide a way to supply them, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the base class constructor arguments are not satisfied.

### Requirement 9: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented inheritance features, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the class inheritance feature is implemented, THE Compiler team SHALL update the `Inheritance & Polymorphism` section of CSHARP_FEATURES.md to mark `Class inheritance (: BaseClass)` as ✅ Implemented.
2. WHEN the base constructor call feature is implemented, THE Compiler team SHALL update the `Constructors` section of CSHARP_FEATURES.md to mark `Base constructor calls (: base(...))` as ✅ Implemented.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 10: Inheritance Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates class inheritance in action, so that I can see how single inheritance, subtype assignability, inherited member access, and base constructor chaining work in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `samples/Inheritance/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate single-level inheritance (a class extending one base class) with the derived class adding new fields and methods.
3. THE sample application SHALL demonstrate multi-tier inheritance (at least three levels deep, e.g., `Animal` → `Dog` → `GuideDog`) where each level adds new members and the deepest derived class can access members from all ancestors.
4. THE sample application SHALL demonstrate subtype assignability by passing a derived class instance to a method that accepts a base class parameter.
5. THE sample application SHALL demonstrate base constructor chaining using `: base(args)` syntax to initialize base class fields from a derived class constructor.
6. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
7. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated inheritance features and prints observable output.
