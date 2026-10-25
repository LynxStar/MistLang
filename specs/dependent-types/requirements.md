# Requirements Document

## Introduction

This document specifies the requirements for adding dependent type parameters to the Mist programming language. Dependent type parameters allow a generic type parameter position to accept a compile-time constant value instead of a type, making that value part of the type identity. This enables types like `Array<5>` where `5` is a value embedded in the type itself, distinct from `Array<6>`.

Mist already supports generics with type parameters, type arguments, generic constraints, type aliases, classes, interfaces, traits, and pattern matching. This spec extends the type parameter system so that any type parameter can be "lifted" to a dependent type parameter by annotating it with a type constraint using the `:Type` syntax (e.g., `<Size:int>`). The value passed as a dependent type argument becomes an implicit public static get property on the type, accessible as `TypeName<Value>.ParamName`.

Dependent type parameters can appear on classes, structs, interfaces, traits, type aliases, and methods. When placed on a method, the dependent value is available in the method's lexical scope as a constant.

**Key design decisions addressed in this spec:**
- Allowed types for dependent parameters: primitive integral types, `bool`, and `char` only (no `string`, no floating-point, no enums, no class/struct/interface/trait types)
- Only literals and const references are supported as dependent type arguments
- Type equality semantics (types with different dependent values are distinct)
- No refinement types or value constraints (any value valid for the annotated type is accepted)
- No type argument inference (all dependent type arguments must be provided explicitly)
- Pattern matching against closed dependent types is supported
- Interaction with existing generics and inheritance
- C# transpilation strategy: static abstract interface members with carrier structs targeting .NET 10 / C# 14

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Type_Parameter**: A placeholder type declared in angle brackets on a generic type or method declaration (e.g., `T` in `class Box<T>`).
- **Dependent_Type_Parameter**: A type parameter annotated with a value type using the `:Type` syntax (e.g., `Size:int` in `class Array<Size:int>`). Instead of accepting a type argument, it accepts a compile-time constant value of the specified type.
- **Dependent_Value**: The compile-time constant value provided as a type argument for a Dependent_Type_Parameter (e.g., `5` in `Array<5>`).
- **Dependent_Type_Annotation**: The `:Type` suffix on a type parameter name that marks it as a Dependent_Type_Parameter (e.g., `:int`, `:bool`, `:char`).
- **Compile_Time_Constant**: A value that can be fully evaluated at compile time. Includes integer literals, boolean literals, character literals, and references to `const` declarations.
- **Dependent_Property**: The implicit public static get-only property generated on a type for each Dependent_Type_Parameter, allowing access to the dependent value (e.g., `Array<5>.Size` returns `5`).
- **Constructed_Dependent_Type**: A type with all dependent type parameters substituted by concrete Dependent_Values (e.g., `Array<5>`, `Matrix<3, 4>`).
- **Closed_Dependent_Type**: A Constructed_Dependent_Type where all dependent type arguments are concrete values (e.g., `Array<5>`). Pattern matching is supported against closed dependent types.
- **Open_Dependent_Type**: A type where one or more dependent type arguments are unresolved parameters (e.g., `Array<N>` inside `void Foo<N:int>(Array<N> arr)`). Pattern matching against open dependent types is not supported.
- **Type_Parameter_List**: A comma-separated list of type parameters and/or dependent type parameters enclosed in angle brackets.
- **Mixed_Parameter_List**: A Type_Parameter_List containing both regular type parameters and Dependent_Type_Parameters (e.g., `<T, Size:int>`).
- **Carrier_Struct**: A C# struct emitted by the Emitter that implements a const-carrier interface and holds a single dependent value as a static property (e.g., `struct Const_5 : IConstInt { public static int Value => 5; }`).
- **Const_Carrier_Interface**: A C# interface emitted by the Emitter with a static abstract property for a dependent value type (e.g., `interface IConstInt { static abstract int Value { get; } }`).

## Requirements

### Requirement 1: Dependent Type Parameter Grammar

**User Story:** As a Mist developer, I want to declare dependent type parameters using the `:Type` annotation syntax, so that I can create types parameterized by compile-time values.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `TypeParameterList` rule so that each type parameter entry can optionally include a `COLON` token followed by a type reference (the Dependent_Type_Annotation), producing a Dependent_Type_Parameter (e.g., `Size:int`).
2. THE Grammar SHALL support Dependent_Type_Parameters on `ClassDeclaration`, `StructDeclaration`, `InterfaceDeclaration`, `TraitDeclaration`, `TypeAliasDeclaration`, and `MethodDeclaration` rules.
3. THE Grammar SHALL support Mixed_Parameter_Lists containing both regular type parameters and Dependent_Type_Parameters in any order (e.g., `<T, Size:int>`, `<N:int, T>`).
4. WHEN a type parameter includes a Dependent_Type_Annotation, THE Parser SHALL produce an AST node that captures the parameter name and the value type reference as distinct from a regular type parameter.
5. WHEN a type parameter does not include a Dependent_Type_Annotation, THE Parser SHALL produce a regular type parameter AST node, preserving backward compatibility.
6. THE Grammar SHALL disambiguate the `:Type` annotation from generic constraint syntax (`where T : Type`) by restricting the annotation to appear inline within the angle brackets of the Type_Parameter_List.
7. THE Grammar SHALL accept namespace-qualified names for named dependent domains, resolve them by the existing named type lookup rules, and preserve the permitted scalar domain categories and refinement checks. Unknown and inaccessible domain names SHALL retain source-anchored diagnostics.

### Requirement 2: Dependent Type Argument Grammar

**User Story:** As a Mist developer, I want to provide compile-time constant values as type arguments for dependent type parameters, so that I can construct dependent types like `Array<5>`.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `TypeArgumentList` rule so that each type argument position can accept either a type reference (for regular type parameters) or a Compile_Time_Constant (for Dependent_Type_Parameters).
2. THE Grammar SHALL support integer literals, boolean literals, and character literals as dependent type arguments.
3. THE Grammar SHALL support negated integer literals (e.g., `-1`) as dependent type arguments.
4. THE Grammar SHALL support `const`-qualified identifier references as dependent type arguments, allowing named constants to be used as dependent values.
5. WHEN a type argument is a Compile_Time_Constant, THE Parser SHALL produce an AST node that captures the value as a dependent value argument, distinct from a type reference argument.
6. THE Grammar SHALL support dependent type arguments in object creation expressions (e.g., `new Array<5>()`), type references (e.g., `Array<5> foo`), and method invocations (e.g., `obj.Resize<10>()`).

### Requirement 3: Pretty-Printer Support for Dependent Types

**User Story:** As a Mist developer, I want the pretty-printer to correctly format dependent type parameter declarations and dependent type arguments, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a Dependent_Type_Parameter as `Name:Type` with no spaces around the colon (e.g., `Size:int`).
2. THE Pretty_Printer SHALL format Mixed_Parameter_Lists with both regular and dependent parameters separated by commas (e.g., `<T, Size:int>`).
3. THE Pretty_Printer SHALL format dependent type arguments as their literal value representation (e.g., `Array<5>`, `Flag<true>`).
4. THE Pretty_Printer SHALL format negated dependent type arguments with the minus sign (e.g., `Offset<-1>`).
5. THE Pretty_Printer SHALL format `const` identifier references in dependent type argument positions as the identifier name (e.g., `Array<MAX_SIZE>`).
6. FOR ALL valid Mist ASTs containing dependent type parameter declarations and dependent type arguments, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 4: Allowed Value Types for Dependent Parameters

**User Story:** As a Mist developer, I want clear rules about which types can be used as dependent type parameter annotations, so that I know what kinds of values I can embed in types.

#### Acceptance Criteria

1. THE Compiler SHALL allow the following types as Dependent_Type_Annotations: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `bool`, `char`.
2. THE Compiler SHALL NOT allow `string` as a Dependent_Type_Annotation.
3. THE Compiler SHALL NOT allow `float`, `double`, or `decimal` as Dependent_Type_Annotations because floating-point equality is unreliable for type identity.
4. THE Compiler SHALL NOT allow enum types as Dependent_Type_Annotations.
5. THE Compiler SHALL NOT allow class types, struct types (other than the listed primitives), interface types, or trait types as Dependent_Type_Annotations.
6. IF a Dependent_Type_Annotation references a type that is not in the allowed set, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error of the form: "Type '{Type}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed."

### Requirement 5: Compile-Time Constant Requirement

**User Story:** As a Mist developer, I want the compiler to enforce that dependent type arguments are literals or const references, so that dependent values are always known at compile time.

#### Acceptance Criteria

1. THE Type_Checker SHALL require that every dependent type argument is a Compile_Time_Constant.
2. THE Type_Checker SHALL accept integer literals, boolean literals, character literals, and references to `const` declarations as valid dependent type arguments.
3. IF a dependent type argument contains a variable, method call, property access, or any expression that is not a literal or `const` reference, THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Dependent type argument must be a compile-time constant. '{Expression}' cannot be evaluated at compile time."

### Requirement 6: Dependent Type Parameter Registration in the Semantic Model

**User Story:** As a Mist developer, I want the compiler to register dependent type parameters in the semantic model, so that dependent values are available for type checking and code generation.

#### Acceptance Criteria

1. WHEN a type declaration includes a Dependent_Type_Parameter, THE Semantic_Model_Builder SHALL create a dependent type parameter representation that stores the parameter name and the value type.
2. THE Semantic_Model_Builder SHALL register the Dependent_Type_Parameter in the type's scope so that the parameter name is resolvable as a compile-time constant within the type body.
3. WHEN a method declaration includes a Dependent_Type_Parameter, THE Semantic_Model_Builder SHALL register the parameter name as a compile-time constant in the method's lexical scope, with the type specified by the Dependent_Type_Annotation.
4. THE Semantic_Model_Builder SHALL store the list of dependent type parameters on the type alongside regular type parameters, preserving their declared order and positions.
5. IF a Dependent_Type_Parameter name collides with a member name (field, property, or method) declared in the same type, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error of the form: "Dependent type parameter '{Name}' conflicts with member '{Name}' declared in type '{Type}'."
6. IF a Dependent_Type_Parameter name collides with another type parameter (regular or dependent) in the same Type_Parameter_List, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating duplicate parameter names.

### Requirement 7: Dependent Property Generation

**User Story:** As a Mist developer, I want each dependent type parameter to be accessible as a public static get property on the constructed type, so that I can retrieve the dependent value from the type itself.

#### Acceptance Criteria

1. FOR EACH Dependent_Type_Parameter on a type, THE Semantic_Model_Builder SHALL generate an implicit public static get-only property with the same name as the parameter and the same type as the Dependent_Type_Annotation.
2. WHEN a Constructed_Dependent_Type is accessed with the dependent parameter name (e.g., `Array<5>.Size`), THE Type_Checker SHALL resolve the access to the generated Dependent_Property and evaluate it to the dependent value.
3. THE Dependent_Property SHALL be a property of the type itself (the constructed type), not of instances of the type. It is accessed via `TypeName<Value>.ParamName`, not via `instance.ParamName`.
4. THE Type_Checker SHALL treat the Dependent_Property as read-only. Attempts to assign to it SHALL produce a diagnostic error.
5. WHEN a type alias has a Dependent_Type_Parameter (e.g., `type Foo<T:int> = string | bool;`), THE Semantic_Model_Builder SHALL generate the Dependent_Property on the alias so that `Foo<7>.T` returns `7`.

### Requirement 8: Type Equality and Compatibility

**User Story:** As a Mist developer, I want types with different dependent values to be treated as distinct types, so that `Array<5>` and `Array<6>` are not interchangeable, while dependent type parameters follow the same scoping and closure rules as regular generics.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat two Constructed_Dependent_Types as equal only when they have the same base generic type AND all dependent type arguments have identical values.
2. THE Type_Checker SHALL treat `Array<5>` and `Array<6>` as distinct, incompatible types. Assignment from one to the other SHALL produce a type mismatch error.
3. Dependent_Type_Parameters SHALL follow identical scoping and closure rules to regular type parameters. A dependent type parameter declared on a method is open within the method body and closed at the call site. A dependent type parameter declared on a type is open within the type body and closed when the type is constructed.
4. WHEN a method declares a Dependent_Type_Parameter (e.g., `void Process<N:int>(Array<N> arr)`), THE Type_Checker SHALL allow `N` to be used in parameter types, local variable types, and the return type within the method body, without requiring a concrete value — the value is supplied at the call site.
5. WHEN a type declares a Dependent_Type_Parameter (e.g., `class Array<Size:int>`), THE Type_Checker SHALL allow `Size` to be used in field types, property types, method signatures, and constructor parameters within the type body.
6. A Dependent_Type_Parameter declared on a method SHALL NOT be usable in field or property declarations of the enclosing type. To use a dependent type parameter in a field or property, it must be declared on the type itself.
7. THE scoping, closure, and usage rules for Dependent_Type_Parameters SHALL be identical to those for regular type parameters — the only difference is that a value is passed instead of a type.

### Requirement 9: Pattern Matching with Dependent Types

**User Story:** As a Mist developer, I want to pattern match against dependent types, so that I can branch on the dependent value embedded in a type at compile time.

#### Acceptance Criteria

1. THE Type_Checker SHALL support pattern matching against Closed_Dependent_Types using the `is` operator (e.g., `if (x is Array<5>)`).
2. THE Type_Checker SHALL support pattern matching against Closed_Dependent_Types in `match` expressions with type patterns.
3. THE Type_Checker SHALL NOT allow pattern matching against Open_Dependent_Types. IF a pattern uses an unresolved dependent type parameter (e.g., `if (x is Array<N>)` where `N` is a type parameter), THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Cannot pattern match against open dependent type 'Array<N>'. Pattern matching requires concrete dependent values."
4. WHEN a function has an open dependent type parameter (e.g., `void Foo<T:int>(Array<T> arr)`), THE Type_Checker SHALL allow the parameter `T` to be used in expressions within the lexical scope but SHALL NOT allow it in pattern match positions.
5. THE Type_Checker SHALL allow pattern matching against multiple closed dependent types in the same match expression (e.g., matching `Array<1>`, `Array<2>`, `Array<3>` as separate cases).

### Requirement 10: Dependent Types on Interfaces and Traits

**User Story:** As a Mist developer, I want to use dependent type parameters on interfaces and traits, so that I can define contracts parameterized by compile-time values.

#### Acceptance Criteria

1. THE Grammar SHALL support Dependent_Type_Parameters on interface declarations (e.g., `interface IFixedCollection<Size:int>`).
2. THE Grammar SHALL support Dependent_Type_Parameters on trait declarations (e.g., `trait FixedCapacity<Cap:int>`).
3. WHEN a class implements an interface with a Dependent_Type_Parameter, THE Type_Checker SHALL require the implementing class to provide a concrete dependent value (e.g., `class SmallBuffer : IFixedCollection<16>`).
4. WHEN a generic class implements an interface with a dependent parameter forwarded from the class's own dependent parameter (e.g., `class Buffer<N:int> : IFixedCollection<N>`), THE Semantic_Model_Builder SHALL propagate the dependent parameter correctly.
5. THE Dependent_Property generated for an interface's dependent parameter SHALL be accessible on the interface type (e.g., `IFixedCollection<16>.Size` returns `16`).

### Requirement 11: Dependent Types on Methods

**User Story:** As a Mist developer, I want to use dependent type parameters on methods, so that I can write methods parameterized by compile-time values with the value available in the method body.

#### Acceptance Criteria

1. THE Grammar SHALL support Dependent_Type_Parameters on method declarations (e.g., `void Repeat<N:int>(string text)`).
2. WHEN a method has a Dependent_Type_Parameter, THE Semantic_Model_Builder SHALL make the parameter name available as a compile-time constant in the method's lexical scope.
3. WHEN a method with a Dependent_Type_Parameter is invoked with a dependent type argument (e.g., `Repeat<3>("hello")`), THE Type_Checker SHALL bind the parameter to the provided value within the method body.
4. THE Type_Checker SHALL enforce that method dependent type arguments are Compile_Time_Constants, following the same rules as type-level dependent arguments.
5. WHEN a method has both regular type parameters and Dependent_Type_Parameters (e.g., `T Create<T, N:int>()`), THE Type_Checker SHALL handle each parameter according to its kind.

### Requirement 12: Inheritance and Dependent Type Propagation

**User Story:** As a Mist developer, I want dependent type parameters to propagate correctly through inheritance, so that derived types can forward or fix dependent values from their base types.

#### Acceptance Criteria

1. WHEN a generic class with a Dependent_Type_Parameter is used as a base class with a concrete dependent value (e.g., `class SmallArray : Array<10>`), THE Semantic_Model_Builder SHALL construct the base type with the fixed dependent value.
2. WHEN a generic class with a Dependent_Type_Parameter is used as a base class with a forwarded dependent parameter (e.g., `class ResizableArray<N:int> : Array<N>`), THE Semantic_Model_Builder SHALL propagate the dependent parameter from the derived type to the base type.
3. WHEN a derived type forwards a dependent parameter to a base type, THE Type_Checker SHALL verify that the dependent type annotation on the derived class is naturally assignable (widening) to the dependent type annotation on the base class (e.g., `int` forwarded to `long` is valid, but `long` forwarded to `int` is not).
4. IF a derived type's dependent type annotation is not assignable to the base type's dependent type annotation (narrowing), THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Dependent type parameter '{Name}' of type '{DerivedType}' on '{DerivedClass}' cannot be forwarded to parameter '{BaseName}' of type '{BaseType}' on '{BaseClass}' because '{DerivedType}' is not implicitly convertible to '{BaseType}'."
5. THE Dependent_Property generated on a base type SHALL be accessible on derived types with the correct value (e.g., if `SmallArray : Array<10>`, then `SmallArray.Size` returns `10`).

### Requirement 13: C# Transpilation of Dependent Types

**User Story:** As a Mist developer, I want dependent types to be correctly transpiled to C# using static abstract interface members with carrier structs, so that the generated code compiles on .NET 10 / C# 14 and preserves dependent value semantics.

#### Acceptance Criteria

1. THE Emitter SHALL target .NET 10 / C# 14 and use static abstract interface members as the transpilation strategy for dependent types.
2. FOR EACH primitive type that can be a dependent parameter, THE Emitter SHALL emit a const-carrier interface with a static abstract property returning the value (e.g., `interface IConstInt { static abstract int Value { get; } }`).
3. FOR EACH unique dependent value used in the program, THE Emitter SHALL emit a Carrier_Struct implementing the appropriate Const_Carrier_Interface (e.g., `struct Const_5 : IConstInt { public static int Value => 5; }`).
4. THE Emitter SHALL transpile each Dependent_Type_Parameter as a regular C# generic type parameter constrained to the corresponding Const_Carrier_Interface (e.g., `class Array<TSize> where TSize : IConstInt`).
5. THE Emitter SHALL emit the Dependent_Property as a public static get-only property that delegates to the carrier struct's static property (e.g., `public static int Size => TSize.Value;`).
6. THE Emitter SHALL ensure that `Array<Const_5>` and `Array<Const_6>` are distinct C# types at runtime, preserving the type identity semantics.
7. THE Emitter SHALL transpile open dependent types naturally using the carrier interface constraint (e.g., `void Foo<TSize>(Array<TSize> arr) where TSize : IConstInt`).
8. THE Emitter SHALL handle Mixed_Parameter_Lists by emitting both regular type parameters and the carrier-constrained type parameters in the correct positions.
9. THE Emitter SHALL produce C# code that compiles without errors for all valid dependent type usages.

### Requirement 14: Serialization and Reflection

**User Story:** As a Mist developer, I want dependent type values to be accessible at runtime through reflection, so that serialization frameworks and runtime inspection can discover dependent values.

#### Acceptance Criteria

1. THE Emitter SHALL ensure that the Dependent_Property is a real C# property accessible via standard .NET reflection.
2. THE Emitter SHALL ensure that the Carrier_Struct is the primary reflection mechanism — inspecting the generic type argument of a constructed dependent type reveals the carrier struct, whose static `Value` property provides the dependent value.
3. THE Emitter SHALL emit a custom attribute (e.g., `[DependentTypeValue("Size", 5)]`) on the generated type or property as supplementary metadata so that reflection-based tools can discover dependent type metadata without navigating the generic type arguments.
4. THE runtime representation SHALL preserve the dependent value so that `typeof(Array<Const_5>)` and `typeof(Array<Const_6>)` are distinguishable at runtime via their distinct generic type arguments.

### Requirement 15: Error Diagnostics for Dependent Types

**User Story:** As a Mist developer, I want clear error messages when I misuse dependent types, so that I can quickly identify and fix problems.

#### Acceptance Criteria

1. WHEN a dependent type argument is not a compile-time constant, THE Compiler SHALL emit a diagnostic error of the form: "Dependent type argument must be a compile-time constant. '{Expression}' cannot be evaluated at compile time."
2. WHEN a dependent type argument's type does not match the Dependent_Type_Annotation (e.g., passing a boolean where `int` is expected), THE Compiler SHALL emit a diagnostic error of the form: "Dependent type argument '{Value}' of type '{ActualType}' is not assignable to dependent parameter '{ParamName}' of type '{ExpectedType}'."
3. WHEN a Dependent_Type_Parameter name conflicts with a member name, THE Compiler SHALL emit a diagnostic error of the form: "Dependent type parameter '{Name}' conflicts with member '{Name}' declared in type '{Type}'. Rename either the parameter or the member."
4. WHEN an unsupported type is used as a Dependent_Type_Annotation, THE Compiler SHALL emit a diagnostic error of the form: "Type '{Type}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed."
5. WHEN a type with dependent parameters is used without providing the dependent arguments, THE Compiler SHALL emit a diagnostic error of the form: "Type '{Type}' requires {N} dependent type argument(s) but none were provided."
6. WHEN an expression (arithmetic, bitwise, logical, or otherwise) is used in a dependent type argument position, THE Compiler SHALL emit a diagnostic error of the form: "Expressions are not supported in dependent type argument positions. Use a literal value or a const reference."
7. WHEN a pattern match is attempted against an Open_Dependent_Type, THE Compiler SHALL emit a diagnostic error of the form: "Cannot pattern match against open dependent type '{Type}'. Pattern matching requires concrete dependent values."

### Requirement 16: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want dependent type parameter declarations to be syntax-highlighted correctly, so that I get visual cues distinguishing dependent parameters from regular type parameters.

#### Acceptance Criteria

1. THE TextMate grammar SHALL highlight the Dependent_Type_Annotation colon and type (e.g., `:int` in `Size:int`) with appropriate scoping to distinguish it from regular type parameters.
2. THE TextMate grammar SHALL highlight the dependent parameter name (e.g., `Size` in `<Size:int>`) with the scope `entity.name.type.parameter.mist`, consistent with regular type parameters.
3. THE TextMate grammar SHALL highlight constant literal values in type argument positions (e.g., `5` in `Array<5>`) with appropriate literal scopes (e.g., `constant.numeric.mist`).

### Requirement 17: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating dependent types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DependentTypes/Program.mist`.
2. The sample SHALL demonstrate a class with a single dependent type parameter (e.g., `class FixedBuffer<Size:int>`).
3. The sample SHALL demonstrate a class with multiple dependent type parameters (e.g., `class Matrix<Rows:int, Cols:int>`).
4. The sample SHALL demonstrate a Mixed_Parameter_List with both regular and dependent type parameters (e.g., `class TypedBuffer<T, Size:int>`).
5. The sample SHALL demonstrate constructing dependent types with literal values (e.g., `new FixedBuffer<5>()`).
6. The sample SHALL demonstrate accessing the Dependent_Property (e.g., `FixedBuffer<5>.Size`).
7. The sample SHALL demonstrate dependent types on interfaces (e.g., `interface IFixedCollection<Size:int>`).
8. The sample SHALL demonstrate dependent types on traits.
9. The sample SHALL demonstrate dependent types on type aliases (e.g., `type Foo<T:int> = string | bool;` and accessing `Foo<7>.T`).
10. The sample SHALL demonstrate dependent types on methods (e.g., `void Repeat<N:int>(string text)`).
11. The sample SHALL demonstrate a generic function accepting any dependent value (e.g., `void Process<N:int>(FixedBuffer<N> buf)`).
12. The sample SHALL demonstrate inheritance with dependent types: a derived class fixing a dependent value and a derived class forwarding a dependent parameter.
13. The sample SHALL demonstrate pattern matching against closed dependent types (e.g., `if (x is FixedBuffer<5>)`).
14. The sample SHALL demonstrate each allowed value type: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `bool`, `char`.
15. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
16. The sample SHALL include a `public static void RunDemo()` entry point.
17. `samples/AllFeatures/Program.mist` SHALL call `DependentTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
18. The sample SHALL compile and run without errors using the Mist compiler.
19. The sample SHALL NOT use `Array` as a type name to avoid collision with `System.Array` in the C# emission.

### Requirement 18: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented dependent types feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the dependent types feature is implemented, THE Compiler team SHALL update the `Generics` section of CSHARP_FEATURES.md to add a `Dependent type parameters` entry marked as ✅ Implemented.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.
