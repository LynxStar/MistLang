# Requirements Document

## Introduction

This document specifies the requirements for adding generic constraints to the Mist programming language. Generic constraints restrict the types that can be used as type arguments for generic type parameters, enabling the compiler to guarantee that type arguments satisfy certain contracts. This allows generic code to safely access members defined by the constraint (e.g., calling interface methods on a constrained type parameter) and enables the compiler to reject invalid type arguments at the call site.

Mist already supports unconstrained generics (type parameter declarations, constructed types, generic methods, and generic inheritance). This spec adds constraint clauses using `where` syntax, supporting the following constraint kinds:

- **Interface/trait constraints** (`where T : IComparable<T>`) — the type argument must implement the specified interface or satisfy the specified trait.
- **Base class constraints** (`where T : Animal`) — the type argument must be or derive from the specified class.
- **`class` constraint** (`where T : class`) — the type argument must be a reference type (class or interface).
- **`struct` constraint** (`where T : struct`) — the type argument must be a value type (struct).
- **`stampable` constraint** (`where T : stampable`) — the type argument must have recursively stampable instance storage, as defined by [stampability](../stampability/requirements.md). This stronger property supports allocation through an open parameter and is not implied by `struct` or `default`.
- **Constructor constraints** (`where T : new()`, `where T : new(int)`, `where T : new(int, int, string)`) — the type argument must have a public constructor matching the specified parameter signature. Unlike C#, Mist supports constraining to any constructor signature, not just parameterless. Multiple constructor constraints can be specified on the same type parameter (e.g., `where T : new(), new(int), new(bool)`) and are combined conjunctively — the type argument must have a public constructor matching every specified signature, since the generic body may call any of them (Requirement 6.5).
- **Multiple constraints** (`where T : class, IComparable<T>, new()`) — multiple constraints on the same type parameter are combined conjunctively.
- **Multiple `where` clauses** (`where T : class where U : struct`) — each type parameter can have its own constraint clause.

**Mist-specific divergences from C#:**
- Constructor constraints are not limited to parameterless — any parameter signature is supported.
- Constructor constraints (`new(...)`) may appear at any position in the constraint list, not only at the end.

Generic constraints apply to generic classes, generic structs, generic interfaces, and generic methods.

The [stampability specification](../stampability/requirements.md) extends the grammar, model registration, satisfaction checks, forwarding, and C# representation for that keyword. Its rules also apply to constrained aliases and every ordinary generic closing. Unrelated constraint semantics are unchanged.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes: Pass 1 registers types and members, Pass 1b resolves base types and interface lists, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Type_Parameter**: A placeholder type declared in angle brackets on a generic type or method declaration (e.g., `T` in `class Box<T>`).
- **Type_Parameter_List**: A comma-separated list of one or more Type_Parameter names enclosed in angle brackets (e.g., `<T>`, `<K, V>`).
- **Type_Argument**: A concrete type supplied when using a generic type or invoking a generic method (e.g., `int` in `Box<int>`).
- **Constraint_Clause**: A `where` clause that specifies one or more constraints on a single type parameter (e.g., `where T : class, IComparable<T>`).
- **Constraint_List**: The comma-separated list of constraints following the colon in a Constraint_Clause.
- **Interface_Constraint**: A constraint requiring the type argument to implement a specific interface (e.g., `where T : IComparable<T>`).
- **Trait_Constraint**: A constraint requiring the type argument to satisfy a specific trait (e.g., `where T : Printable`).
- **Base_Class_Constraint**: A constraint requiring the type argument to be or derive from a specific class (e.g., `where T : Animal`).
- **Class_Constraint**: The keyword `class` used as a constraint, requiring the type argument to be a reference type.
- **Struct_Constraint**: The keyword `struct` used as a constraint, requiring the type argument to be a value type.
- **Constructor_Constraint**: The `new(...)` syntax used as a constraint, requiring the type argument to have a public constructor matching the specified parameter type list. `new()` requires a parameterless constructor; `new(int)` requires a constructor accepting a single `int`; `new(int, int, string)` requires a constructor accepting those three parameter types in order. Multiple constructor constraints on the same type parameter (e.g., `new(), new(int), new(bool)`) are combined conjunctively — the type argument must have a public constructor matching every specified signature.
- **TypeParameterType**: The semantic model representation of an unsubstituted type parameter, which will be extended to carry constraint information.
- **ConstructedType**: A generic type with all type parameters substituted by concrete type arguments.
- **ClassType**: The semantic model representation of a user-defined class type.
- **StructType**: The semantic model representation of a user-defined struct type.
- **InterfaceType**: The semantic model representation of a user-defined interface type.
- **TraitType**: The semantic model representation of a user-defined trait type.

## Requirements

### Requirement 1: Constraint Clause Grammar

**User Story:** As a Mist developer, I want to declare constraints on type parameters using `where` clauses, so that I can restrict which types are valid as type arguments.

#### Acceptance Criteria

1. THE Grammar SHALL define a `WHERE` keyword token with the value `"where"`.
2. THE Grammar SHALL define a `ConstraintClause` rule that consists of the `WHERE` keyword, a type parameter name identifier, a `COLON` token, and a comma-separated Constraint_List of one or more constraint entries.
3. THE Grammar SHALL define a `Constraint` rule with the following variants: a type reference (for interface, trait, or base class constraints), the `CLASS` keyword (for the class constraint), the `STRUCT` keyword (for the struct constraint), and a constructor constraint consisting of `NEW LEFT_PARENTHESIS` followed by an optional comma-separated list of type references (the parameter types) followed by `RIGHT_PARENTHESIS` (e.g., `new()`, `new(int)`, `new(int, int, string)`).
4. THE Grammar SHALL extend the `ClassDeclaration` rule to include zero or more `ConstraintClause` entries after the optional base clause and before the left brace.
5. THE Grammar SHALL extend the `StructDeclaration` rule to include zero or more `ConstraintClause` entries after the optional interface list and before the left brace.
6. THE Grammar SHALL extend the `InterfaceDeclaration` rule to include zero or more `ConstraintClause` entries after the optional interface list and before the left brace.
7. THE Grammar SHALL extend the `MethodDeclaration` rule to include zero or more `ConstraintClause` entries after the parameter list and before the method body.
8. THE Grammar SHALL extend the `InterfaceMethodDeclaration` rule to include zero or more `ConstraintClause` entries after the parameter list and before the semicolon.
9. THE Grammar SHALL extend the `TypeAliasDeclaration` rule to include zero or more `ConstraintClause` entries after the aliased type reference and before the semicolon (e.g., `type StructList<T> = List<T> where T : struct;`).
9. WHEN a type or method declaration includes one or more Constraint_Clauses, THE Parser SHALL produce an AST node that captures each clause with its type parameter name and list of constraints.
10. WHEN a type or method declaration includes no Constraint_Clauses, THE Parser SHALL produce an AST node with an absent constraint list, preserving backward compatibility with existing unconstrained generic declarations.

### Requirement 2: Pretty-Printer Support for Constraint Clauses

**User Story:** As a Mist developer, I want the pretty-printer to correctly format constraint clauses, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a single constraint clause as `where T : constraint1, constraint2` on the same line as the declaration or on a new line following the declaration header, consistent with the existing formatting style.
2. THE Pretty_Printer SHALL format the `class` constraint as the keyword `class`.
3. THE Pretty_Printer SHALL format the `struct` constraint as the keyword `struct`.
4. THE Pretty_Printer SHALL format constructor constraints as `new()` for parameterless, or `new(Type1, Type2, ...)` for parameterized variants, using the standard type reference formatting for each parameter type.
5. THE Pretty_Printer SHALL format interface and class type constraints using their type reference formatting (including type arguments for generic interfaces, e.g., `IComparable<T>`).
6. THE Pretty_Printer SHALL format multiple constraint clauses on the same declaration as separate `where` clauses (e.g., `where T : class where U : struct`).
7. FOR ALL valid Mist ASTs containing generic declarations with constraint clauses, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 3: Constraint Registration in the Semantic Model

**User Story:** As a Mist developer, I want the compiler to register constraints on type parameters in the semantic model, so that constraint information is available for type checking and code emission.

#### Acceptance Criteria

1. WHEN a type or method declaration includes Constraint_Clauses, THE Semantic_Model_Builder SHALL resolve each constraint and store the constraint information on the corresponding TypeParameterType.
2. THE Semantic_Model_Builder SHALL resolve interface constraints to the corresponding InterfaceType (or ConstructedType for generic interfaces like `IComparable<T>`).
3. THE Semantic_Model_Builder SHALL resolve trait constraints to the corresponding TraitType.
4. THE Semantic_Model_Builder SHALL resolve base class constraints to the corresponding ClassType (or ConstructedType for generic base classes).
5. THE Semantic_Model_Builder SHALL represent the `class` constraint as a reference-type constraint on the TypeParameterType.
6. THE Semantic_Model_Builder SHALL represent the `struct` constraint as a value-type constraint on the TypeParameterType.
7. THE Semantic_Model_Builder SHALL represent each constructor constraint as a constructor constraint entry on the TypeParameterType, storing the list of parameter types for that constructor signature (an empty list for `new()`, a list like `[int]` for `new(int)`, etc.).
8. WHEN multiple constructor constraints are specified on the same type parameter (e.g., `new(), new(int), new(bool)`), THE Semantic_Model_Builder SHALL store all of them as conjunctive requirements — the type argument must have a public constructor matching every specified signature.
9. WHEN a Constraint_Clause references a type parameter name that does not exist in the enclosing type or method's Type_Parameter_List, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the type parameter is undefined.

### Requirement 4: Constraint Validation Rules

**User Story:** As a Mist developer, I want the compiler to validate that my constraint combinations are legal, so that I receive clear errors for invalid constraint declarations.

#### Acceptance Criteria

1. WHEN a Constraint_Clause specifies both `class` and `struct` on the same type parameter, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that `class` and `struct` constraints are mutually exclusive.
2. WHEN a Constraint_Clause specifies `struct` and a parameterless constructor constraint `new()` on the same type parameter, THE Semantic_Model_Builder SHALL emit a diagnostic warning indicating that `new()` is redundant because all structs have a parameterless constructor.
3. WHEN a Constraint_Clause specifies more than one base class constraint on the same type parameter, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that at most one base class constraint is allowed.
4. WHEN a Constraint_Clause specifies a base class constraint that references a struct type, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a struct cannot be used as a base class constraint.
5. WHEN a Constraint_Clause specifies a base class constraint combined with `struct`, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a base class constraint is incompatible with the `struct` constraint.
6. WHEN multiple Constraint_Clauses reference the same type parameter name, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a type parameter cannot have multiple `where` clauses (constraints must be combined in a single clause).
7. WHEN a Constraint_Clause references a type that cannot be resolved, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the constraint type is undefined.
8. WHEN a base class constraint references a sealed class (if sealed classes are supported in the future), THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a sealed class cannot be used as a constraint.

### Requirement 5: Type Argument Constraint Satisfaction Checking

**User Story:** As a Mist developer, I want the compiler to verify that type arguments satisfy all constraints declared on the corresponding type parameters, so that invalid type arguments are rejected at the usage site.

#### Acceptance Criteria

1. WHEN a type reference provides type arguments for a constrained generic type, THE Type_Checker SHALL verify that each type argument satisfies all constraints declared on the corresponding type parameter.
2. WHEN a type argument does not implement a required interface constraint, THE Type_Checker SHALL emit a diagnostic error naming the type argument, the type parameter, and the unsatisfied interface.
3. WHEN a type argument does not satisfy a required trait constraint, THE Type_Checker SHALL emit a diagnostic error naming the type argument, the type parameter, and the unsatisfied trait.
4. WHEN a type argument does not derive from a required base class constraint, THE Type_Checker SHALL emit a diagnostic error naming the type argument, the type parameter, and the required base class.
5. WHEN a `class` constraint is declared and the type argument is a struct type, THE Type_Checker SHALL emit a diagnostic error indicating that a reference type is required.
6. WHEN a `struct` constraint is declared and the type argument is a class or interface type, THE Type_Checker SHALL emit a diagnostic error indicating that a value type is required.
7. WHEN a `new()` constraint is declared and the type argument does not have a public parameterless constructor, THE Type_Checker SHALL emit a diagnostic error indicating that a parameterless constructor is required.
8. WHEN a parameterized constructor constraint (e.g., `new(int)` or `new(int, string)`) is declared and the type argument does not have a public constructor with a matching parameter type list, THE Type_Checker SHALL emit a diagnostic error naming the type argument, the type parameter, and the required constructor signature.
9. WHEN multiple constructor constraints are declared on the same type parameter (e.g., `new(), new(int), new(bool)`), THE Type_Checker SHALL verify that the type argument has a matching public constructor for every one of the specified signatures, and SHALL report each missing signature.
10. WHEN a generic method is invoked with explicit type arguments, THE Type_Checker SHALL verify that the type arguments satisfy the method's type parameter constraints.
11. WHEN a constrained generic type is used in an inheritance clause (e.g., `class Foo<T> : Bar<T> where T : IComparable<T>`), THE Type_Checker SHALL verify that the constraints on the derived type's type parameters are sufficient to satisfy the base type's constraints.

### Requirement 6: Constrained Member Access

**User Story:** As a Mist developer, I want to access members defined by a constraint on a type parameter, so that I can call interface methods or access base class members on values of the constrained type parameter.

#### Acceptance Criteria

1. WHEN a type parameter has an interface constraint, THE Semantic_Model_Builder SHALL make the interface's members accessible on values of that type parameter within the generic body.
2. WHEN a type parameter has a trait constraint, THE Semantic_Model_Builder SHALL make the trait's members accessible on values of that type parameter within the generic body.
3. WHEN a type parameter has a base class constraint, THE Semantic_Model_Builder SHALL make the base class's members (including inherited members) accessible on values of that type parameter within the generic body.
4. WHEN a type parameter has multiple interface constraints, THE Semantic_Model_Builder SHALL make all members from all constrained interfaces accessible on values of that type parameter.
5. WHEN a type parameter has one or more constructor constraints, THE Type_Checker SHALL allow `new T(...)` expressions within the generic body only when the argument types match one of the declared constructor constraint signatures. For example, given `where T : new(), new(int), new(bool)`, `new T()`, `new T(someInt)`, and `new T(someBool)` are valid, but `new T(someString)` is a compile error.
6. WHEN a `new T(...)` expression is used within a generic body and the argument types do not match any declared constructor constraint signature on that type parameter, THE Type_Checker SHALL emit a diagnostic error indicating that no constructor constraint matches the provided arguments.
6. WHEN a member access is attempted on a type parameter that does not have a constraint providing that member, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the member is not available on the unconstrained type parameter.

### Requirement 7: Constraint Propagation Through Inheritance

**User Story:** As a Mist developer, I want constraints to be correctly propagated when a constrained generic type is used as a base type or interface, so that derived types maintain the constraint guarantees.

#### Acceptance Criteria

1. WHEN a generic type with constrained type parameters is used as a base class (e.g., `class Derived<T> : Base<T>` where `Base<T>` has `where T : IFoo`), THE Semantic_Model_Builder SHALL verify that the derived type's constraints on `T` are at least as restrictive as the base type's constraints.
2. WHEN a generic type with constrained type parameters is used as an implemented interface (e.g., `class Foo<T> : IBar<T>` where `IBar<T>` has `where T : class`), THE Semantic_Model_Builder SHALL verify that the implementing type's constraints on `T` are at least as restrictive as the interface's constraints.
3. WHEN a derived type does not declare constraints that are at least as restrictive as the base type's constraints, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the derived type must satisfy the base type's constraints.
4. WHEN a non-generic type extends a constrained generic type with concrete type arguments (e.g., `class IntList : SortedList<int>` where `SortedList<T>` has `where T : IComparable<T>`), THE Type_Checker SHALL verify that the concrete type argument satisfies the constraints.

### Requirement 8: C# Code Emission for Constraints

**User Story:** As a Mist developer, I want the transpiled C# code to correctly represent generic constraints, so that the compiled output preserves constraint enforcement.

#### Acceptance Criteria

1. THE Emitter SHALL emit each Constraint_Clause as a C# `where` clause with the same syntax (e.g., `where T : class, IComparable<T>, new()`).
2. THE Emitter SHALL emit the `class` constraint as the C# keyword `class` in the constraint clause.
3. THE Emitter SHALL emit the `struct` constraint as the C# keyword `struct` in the constraint clause.
4. THE Emitter SHALL emit parameterless constructor constraints as `new()` in the C# constraint clause. For parameterized constructor constraints (e.g., `new(int)`, `new(int, string)`), THE Emitter SHALL emit them as factory method constraints or equivalent C# pattern, since C# does not natively support parameterized constructor constraints. The specific lowering strategy (e.g., factory delegate parameter, factory interface) SHALL be defined in the design document.
5. THE Emitter SHALL emit interface constraints using the C# type name (e.g., `IComparable<T>`).
6. THE Emitter SHALL emit base class constraints using the C# type name.
7. THE Emitter SHALL emit trait constraints as the corresponding generated C# interface name (since traits are lowered to interfaces in C#).
8. WHEN a type parameter has multiple constraints, THE Emitter SHALL emit them in the C# required order: `class`/`struct` first, then base class, then interfaces, then `new()` last. Parameterized constructor constraints that require a non-C#-native lowering SHALL be handled separately from the `where` clause.
9. WHEN a declaration has multiple `where` clauses (one per type parameter), THE Emitter SHALL emit each as a separate C# `where` clause.

### Requirement 9: Constraint Ordering in Emission

**User Story:** As a Mist developer, I want the compiler to emit constraints in the correct C# order regardless of the order I write them in Mist, so that the transpiled code is always valid C#.

#### Acceptance Criteria

1. WHEN a Mist developer writes constraints in any order (e.g., `where T : new(), IFoo, class`), THE Emitter SHALL reorder them to the C# required order: `class`/`struct` first, then base class, then interfaces/traits, then `new()` last.
2. THE Parser SHALL accept constraints in any order within a Constraint_Clause without emitting an error. Constructor constraints (`new(...)`) are NOT required to be last in Mist source, unlike C#.
3. THE Pretty_Printer SHALL preserve the original source order of constraints (not reorder them), since reordering is only required for C# emission.

### Requirement 10: Error Diagnostics for Constraints

**User Story:** As a Mist developer, I want clear error messages when I misuse generic constraints, so that I can quickly identify and fix problems.

#### Acceptance Criteria

1. WHEN a type argument violates an interface constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' does not implement interface '{Interface}' required by type parameter '{TypeParam}' of '{GenericType}'."
2. WHEN a type argument violates a trait constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' does not satisfy trait '{Trait}' required by type parameter '{TypeParam}' of '{GenericType}'."
3. WHEN a type argument violates a base class constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' does not derive from '{BaseClass}' required by type parameter '{TypeParam}' of '{GenericType}'."
4. WHEN a type argument violates a `class` constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' must be a reference type to satisfy the 'class' constraint on type parameter '{TypeParam}'."
5. WHEN a type argument violates a `struct` constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' must be a value type to satisfy the 'struct' constraint on type parameter '{TypeParam}'."
6. WHEN a type argument violates a `new()` constraint, THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' must have a public parameterless constructor to satisfy the 'new()' constraint on type parameter '{TypeParam}'."
7. WHEN a type argument violates a parameterized constructor constraint (e.g., `new(int)`), THE Compiler SHALL emit a diagnostic error of the form: "Type '{TypeArg}' must have a public constructor with parameters '({ParamTypes})' to satisfy the 'new({ParamTypes})' constraint on type parameter '{TypeParam}'."
8. WHEN a `new T(...)` expression is used in a generic body and the argument types do not match any declared constructor constraint, THE Compiler SHALL emit a diagnostic error of the form: "No constructor constraint on type parameter '{TypeParam}' matches arguments '({ArgTypes})'. Available constructor constraints: {list of new(...) signatures}."
9. WHEN a `where` clause references an undefined type parameter, THE Compiler SHALL emit a diagnostic error of the form: "Type parameter '{Name}' is not defined on '{DeclaringType}'."
10. WHEN `class` and `struct` constraints are combined, THE Compiler SHALL emit a diagnostic error of the form: "The 'class' and 'struct' constraints are mutually exclusive on type parameter '{TypeParam}'."

### Requirement 11: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `where` constraint clauses to be syntax-highlighted correctly, so that I get the same visual cues as other language constructs.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `where` as a keyword (scope `keyword.other.mist`) when it appears in the context of a generic constraint clause.
2. THE TextMate grammar SHALL highlight `class` and `struct` within constraint clauses with the scope `keyword.type.mist` or `storage.type.mist` to distinguish them from their declaration usage.
3. THE TextMate grammar SHALL highlight `new` within the `new()` constructor constraint with the scope `keyword.other.mist`.
4. THE TextMate grammar SHALL highlight the type parameter name following `where` with the scope `entity.name.type.parameter.mist`.

### Requirement 12: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating generic constraints, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/GenericConstraints/Program.mist`.
2. The sample SHALL demonstrate a `class` constraint restricting a type parameter to reference types.
3. The sample SHALL demonstrate a `struct` constraint restricting a type parameter to value types.
4. The sample SHALL demonstrate an interface constraint (e.g., `where T : IComparable<T>`) and calling the constrained interface method on a value of type `T`.
5. The sample SHALL demonstrate a trait constraint (e.g., `where T : Printable`) and calling the constrained trait member on a value of type `T`.
6. The sample SHALL demonstrate a base class constraint (e.g., `where T : Animal`) and accessing base class members on a value of type `T`.
7. The sample SHALL demonstrate a parameterless `new()` constructor constraint and using `new T()` within the generic body.
8. The sample SHALL demonstrate a parameterized constructor constraint (e.g., `where T : new(int)`) and using `new T(someInt)` within the generic body.
9. The sample SHALL demonstrate multiple constructor constraints on a single type parameter (e.g., `where T : new(), new(int), new(bool)`) and calling each valid `new T(...)` variant.
10. The sample SHALL demonstrate multiple constraints on a single type parameter (e.g., `where T : class, IComparable<T>, new()`).
11. The sample SHALL demonstrate multiple `where` clauses on a multi-parameter generic (e.g., `where K : IComparable<K> where V : new()`).
12. The sample SHALL demonstrate constraints on generic methods (not just generic types).
13. The sample SHALL demonstrate constraint propagation through inheritance (a derived generic type inheriting a constrained base).
14. The sample SHALL demonstrate that constructor constraints can appear at any position in the constraint list (not just last), e.g., `where T : new(int), IComparable<T>, class`.
15. The sample SHALL demonstrate constraints on a generic type alias (e.g., `type StructList<T> = List<T> where T : struct;`) and using the alias with valid and invalid type arguments.
15. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
16. The sample SHALL include a `public static void RunDemo()` entry point.
17. `samples/AllFeatures/Program.mist` SHALL call `GenericConstraints.Program.RunDemo()` to include the feature in the full-suite sample run.
18. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 13: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented generic constraints feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the generic constraints feature is implemented, THE Compiler team SHALL update the `Generics` section of CSHARP_FEATURES.md to mark `Type constraints (where T : class)` as ✅ Implemented.
2. WHEN the generic constraints feature is implemented, THE Compiler team SHALL update the `Type Declarations` section of CSHARP_FEATURES.md to mark `Generic constraints (where T : ...)` as ✅ Implemented.
3. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.
