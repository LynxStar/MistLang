# Requirements Document

## Introduction

This document specifies the requirements for fixing interface member dispatch and completing interface integration in the Mist programming language's AllFeatures sample. The Mist compiler already supports interface declarations, interface implementation on classes/structs, interface subtype assignability, and C# emission of interface declarations. However, a critical gap exists: calling methods on interface-typed variables fails because the semantic model builder's `LookupMemberType` method does not handle `InterfaceType`. Additionally, the C# emitter does not emit interface lists on class declarations (only the base class name is emitted), and the AllFeatures sample does not yet include Mist source files or code exercising interfaces. This spec addresses all of these gaps to make interfaces fully functional end-to-end.

## Glossary

- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST, including the `LookupMemberType` method that resolves member access expressions to their types.
- **LookupMemberType**: The static method in `SemanticModelBuilder` that, given a target type and a member name, resolves the member's type by searching the target type's member scope and (for classes) walking the base type chain.
- **InterfaceType**: The semantic model representation of a user-defined interface type, containing a `MemberScope` with method symbols and an `Interfaces` list of inherited interfaces.
- **ClassType**: The semantic model representation of a user-defined class type, containing a `MemberScope`, optional `BaseType`, and an `Interfaces` list.
- **StructType**: The semantic model representation of a user-defined struct type, containing a `MemberScope` and an `Interfaces` list.
- **Member_Scope**: The scope containing all declared members (fields, methods, constructors) of a type.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **AllFeatures_Sample**: The comprehensive sample application in `samples/AllFeatures/` that exercises every implemented Mist language feature.
- **Interface_Reference**: A variable, parameter, or return type declared with an interface type, through which methods are called via dynamic dispatch.

## Requirements

### Requirement 1: Interface Member Lookup in SemanticModelBuilder

**User Story:** As a Mist developer, I want to call methods on interface-typed variables, so that I can use polymorphism through interface references.

#### Acceptance Criteria

1. WHEN the target type of a member access expression is an `InterfaceType`, THE Semantic_Model_Builder SHALL search the InterfaceType's Member_Scope for a member matching the given name.
2. WHEN the target type is an `InterfaceType` and the member is not found in the InterfaceType's own Member_Scope, THE Semantic_Model_Builder SHALL search the Member_Scopes of all inherited interfaces (transitively) for a member matching the given name.
3. WHEN the target type is an `InterfaceType` and the member is found in the InterfaceType's Member_Scope or any inherited interface's Member_Scope, THE Semantic_Model_Builder SHALL return the member's declared type.
4. WHEN the target type is an `InterfaceType` and the member is not found in the InterfaceType's Member_Scope or any inherited interface's Member_Scope, THE Semantic_Model_Builder SHALL emit a `MIST_UNDEFINED_MEMBER (historical)` diagnostic error.
5. WHEN the target type is an `InterfaceType` with diamond inheritance (two inherited interfaces both inheriting from a common ancestor), THE Semantic_Model_Builder SHALL resolve the member without duplication errors or infinite loops.

### Requirement 2: C# Emission of Interface Lists on Class Declarations

**User Story:** As a Mist developer, I want the transpiled C# code to include the full interface list on class declarations, so that the compiled output correctly implements the declared interfaces.

#### Acceptance Criteria

1. WHEN a class declaration includes both a base class and one or more interfaces, THE Emitter SHALL emit the C# class declaration with the complete inheritance list including the base class and all interface names (e.g., `class Dog : Animal, IWalkable, IFeedable`).
2. WHEN a class declaration includes only interfaces and no base class, THE Emitter SHALL emit the C# class declaration with all interface names (e.g., `class Circle : IShape`).
3. WHEN a class declaration includes no base class and no interfaces, THE Emitter SHALL emit the C# class declaration with no inheritance clause.
4. THE Emitter SHALL preserve the order of interface names as they appear in the Mist source when emitting the C# inheritance list.

### Requirement 3: AllFeatures Interface Source Files

**User Story:** As a Mist developer evaluating the language, I want the AllFeatures sample to include interface definition and implementation source files, so that I can see how interfaces work in practice.

#### Acceptance Criteria

1. THE AllFeatures_Sample SHALL include an `IShape.mist` file that declares an `IShape` interface with `Describe` and `Area` methods, and an `IResizable` interface that inherits from `IShape` and adds a `ResizedArea` method.
2. THE AllFeatures_Sample SHALL include a `Circle.mist` file that declares a `Circle` class implementing `IShape`, with a constructor accepting a radius, and implementations of `Describe` and `Area`.
3. THE AllFeatures_Sample SHALL include a `Rectangle.mist` file that declares a `Rectangle` class implementing `IResizable`, with a constructor accepting width and height, and implementations of `Describe`, `Area`, and `ResizedArea`.
4. THE AllFeatures_Sample source files SHALL compile successfully through the Mist compiler pipeline without semantic or type-checking errors.

### Requirement 4: AllFeatures Program Interface Exercising

**User Story:** As a Mist developer evaluating the language, I want the AllFeatures Program.mist to exercise interface features end-to-end, so that I can see interface declaration, implementation, assignment, and method dispatch all working together.

#### Acceptance Criteria

1. THE AllFeatures_Sample Program.mist SHALL create instances of `Circle` and `Rectangle` and call methods directly on the concrete types.
2. THE AllFeatures_Sample Program.mist SHALL assign a `Circle` instance to an `IShape`-typed variable and call `Describe` and `Area` through the Interface_Reference.
3. THE AllFeatures_Sample Program.mist SHALL assign a `Rectangle` instance to an `IShape`-typed variable and call `Describe` and `Area` through the Interface_Reference.
4. THE AllFeatures_Sample Program.mist SHALL assign a `Rectangle` instance to an `IResizable`-typed variable and call `ResizedArea` through the Interface_Reference.
5. THE AllFeatures_Sample Program.mist SHALL print observable output from each interface method call using `Console.WriteLine`, so that correct dispatch can be verified at runtime.
6. THE AllFeatures_Sample Program.mist SHALL compile and run successfully through the Mist compiler pipeline, producing correct output that demonstrates polymorphic dispatch through interface references.

### Requirement 5: Interface-Typed Method Parameters

**User Story:** As a Mist developer, I want to pass interface-typed variables as method parameters and call methods on them inside the receiving method, so that I can write generic code that operates on any type implementing an interface.

#### Acceptance Criteria

1. WHEN a method parameter is typed as an interface, THE Semantic_Model_Builder SHALL resolve member access expressions on that parameter using the interface's Member_Scope and inherited interface Member_Scopes.
2. THE AllFeatures_Sample Program.mist SHALL include a static helper method that accepts an `IShape` parameter and calls `Describe` and `Area` on the parameter, demonstrating that interface member dispatch works through method parameters.
3. THE AllFeatures_Sample Program.mist SHALL call the helper method with both a `Circle` and a `Rectangle` argument, printing the results to demonstrate polymorphic dispatch through parameters.

### Requirement 6: Interface Method Calls on Inherited Interface Members

**User Story:** As a Mist developer, I want to call methods defined in a parent interface through a child interface reference, so that inherited interface methods are accessible through the derived interface type.

#### Acceptance Criteria

1. WHEN the target type is a child interface (e.g., `IResizable`) and the member being accessed is defined in a parent interface (e.g., `IShape.Describe`), THE Semantic_Model_Builder SHALL resolve the member by walking the inherited interface chain.
2. THE AllFeatures_Sample Program.mist SHALL call a method defined in `IShape` (such as `Describe`) through an `IResizable`-typed variable, demonstrating that inherited interface members are accessible through derived interface references.
