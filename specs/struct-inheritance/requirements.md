# Requirements Document

## Introduction

Struct inheritance introduces structural composition for value types in Mist. A derived struct contains all fields from its base struct plus its own additional fields. Unlike class inheritance, struct inheritance has no virtual dispatch or method overriding — it is purely a data composition mechanism. Derived-to-base assignment is an implicit conversion (safe subset copy), while base-to-derived is a compile error (missing fields). The .NET CLR does not support struct inheritance, so the compiler flattens the inheritance at emit time by copying base fields into the derived struct and synthesizing implicit conversion operators.

## Glossary

- **Mist_Compiler**: The Mist language compiler, including the parser, semantic model builder, type checker, and C# transpiler backend.
- **Parser**: The component that reads Mist source text and produces an abstract syntax tree (AST).
- **Semantic_Model_Builder**: The component that resolves types, builds the symbol table, and populates type metadata from the AST.
- **Type_Checker**: The analysis component that validates type assignability, method resolution, and emits diagnostics for type errors.
- **CSharp_Emitter**: The backend component that translates the Mist semantic model into valid C# source code.
- **StructType**: The semantic model representation of a user-defined struct type, holding fields, interfaces, type parameters, and metadata.
- **Base_Struct**: The struct from which a derived struct inherits fields.
- **Derived_Struct**: A struct that extends a base struct, gaining all base fields plus its own.
- **Field_Flattening**: The emission strategy where base struct fields are copied directly into the derived struct's C# output, since the CLR does not support struct inheritance.
- **Implicit_Conversion_Operator**: A synthesized C# `public static implicit operator` that enables derived-to-base assignment by copying the base field subset.

---

## Requirements

### Requirement 1: Struct Inheritance Syntax

**User Story:** As a Mist developer, I want to declare a struct that extends another struct using the `:` syntax, so that I can compose value types hierarchically.

#### Acceptance Criteria

1. WHEN a struct declaration includes a colon followed by a comma-separated list of type references, THE Parser SHALL parse all entries as a BaseList of BaseTypeRef nodes, each preserving its qualified name, optional constructed enclosing type and optional type arguments.
2. WHEN the Semantic_Model_Builder resolves a struct's base list and the first entry resolves to a struct type, THE Semantic_Model_Builder SHALL record it as the base struct and resolve remaining entries as implemented interfaces.
3. IF a struct declaration's base list contains more than one entry that resolves to a struct type, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error that identifies the declaring struct name and indicates that multiple struct inheritance is not supported.
4. THE Parser SHALL preserve type arguments on any base list entry (e.g., `struct Derived : Base<int>`) by storing them on the corresponding BaseTypeRef node.
5. IF the first entry in a struct's base list resolves to neither a struct type nor an interface type, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error identifying the unresolved type name.

---

### Requirement 2: Semantic Model for Struct Inheritance

**User Story:** As the compiler infrastructure, I want the semantic model to track the base struct relationship, so that the type checker and emitter can reason about inherited fields.

#### Acceptance Criteria

1. WHEN a struct declares a base struct, THE Semantic_Model_Builder SHALL resolve the base struct type and store it on the StructType as a BaseType property.
2. WHEN the base type identifier cannot be resolved to any type declaration in the current scope or imported namespaces, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the base type is undefined.
3. WHEN the base type resolves to a class type or interface type rather than a struct type, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that a struct can only inherit from another struct.
4. WHEN a struct inheritance chain contains a cycle (e.g., A extends B and B extends A, or A extends A), THE Semantic_Model_Builder SHALL emit a diagnostic error indicating circular inheritance and stop further resolution of that chain.
5. WHEN a struct declares a base struct that itself has a base struct (multi-level inheritance such as A extends B extends C), THE Semantic_Model_Builder SHALL resolve the full ancestor chain by following each BaseType reference up to a maximum depth of 64 levels.
6. IF the struct inheritance chain exceeds 64 levels without terminating, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that the maximum inheritance depth has been exceeded.
7. WHEN a struct's base type is successfully resolved, THE Semantic_Model_Builder SHALL make the base struct's fields accessible through the derived struct's MemberScope so that the type checker can resolve field access on the derived type without requiring manual chain traversal.

---

### Requirement 3: Inherited Field Visibility

**User Story:** As a Mist developer, I want a derived struct to have access to all fields from its base struct, so that I can use the composed data without re-declaring fields.

#### Acceptance Criteria

1. WHEN a derived struct is constructed, THE Type_Checker SHALL require that all fields from the base struct and all ancestor structs are initialized (either via a base constructor call or direct field assignment in the constructor body).
2. WHEN code accesses a public field defined in the base struct or any ancestor struct through a derived struct instance, THE Type_Checker SHALL accept the access as valid.
3. WHEN a derived struct declares a field with the same name as a field in the base struct or any ancestor struct, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that field hiding is not permitted.
4. WHEN code calls a public method defined in the base struct or any ancestor struct through a derived struct instance, THE Type_Checker SHALL accept the call as valid.
5. WHEN code accesses a private field or calls a private method defined in the base struct through a derived struct instance, THE Type_Checker SHALL emit a diagnostic error indicating that private members are not accessible from the derived struct.
6. WHEN code accesses a public property defined in the base struct or any ancestor struct through a derived struct instance, THE Type_Checker SHALL accept the access as valid.

---

### Requirement 4: Derived-to-Base Implicit Conversion

**User Story:** As a Mist developer, I want to assign a derived struct value to a base struct variable without an explicit cast, so that struct inheritance behaves like safe data narrowing (analogous to numeric widening).

#### Acceptance Criteria

1. WHEN a derived struct value is assigned to a variable of the base struct type, THE Type_Checker SHALL accept the assignment without requiring an explicit cast.
2. WHEN a derived struct value is passed as an argument to a parameter of the base struct type, THE Type_Checker SHALL accept the argument without requiring an explicit cast.
3. WHEN a derived struct value is returned from a method with a base struct return type, THE Type_Checker SHALL accept the return statement without requiring an explicit cast.
4. THE Type_Checker SHALL support transitive implicit conversion by walking the full ancestor chain (if C extends B extends A, then C is assignable to both B and A).
5. WHEN a derived generic struct value (e.g., `Derived<int>`) is assigned to a variable of the base generic struct type with matching type arguments (e.g., `Base<int>`), THE Type_Checker SHALL accept the assignment using the same inheritance-chain walk applied to constructed struct types.
6. IF a struct value is assigned to a variable of a struct type that is not an ancestor in the inheritance chain, THEN THE Type_Checker SHALL emit a type mismatch diagnostic error.

---

### Requirement 5: Base-to-Derived Assignment Rejection

**User Story:** As a Mist developer, I want the compiler to reject assignment of a base struct value to a derived struct variable, so that I cannot accidentally create an incomplete value with missing fields.

#### Acceptance Criteria

1. WHEN a base struct value is assigned to a variable of a derived struct type, THE Type_Checker SHALL emit a diagnostic error indicating that the base type is not assignable to the derived type.
2. WHEN a base struct value is passed as an argument to a parameter of a derived struct type, THE Type_Checker SHALL emit a diagnostic error indicating that the base type is not assignable to the derived type.
3. WHEN a base struct value is returned from a method with a derived struct return type, THE Type_Checker SHALL emit a diagnostic error indicating that the base type is not assignable to the derived type.
4. THE Type_Checker SHALL reject base-to-derived assignment transitively (if C extends B extends A, then A is not assignable to B, A is not assignable to C, and B is not assignable to C).

---

### Requirement 6: C# Emission with Field Flattening

**User Story:** As the compiler backend, I want to emit derived structs with all base fields copied inline, so that the generated C# is valid (since the CLR does not support struct inheritance).

#### Acceptance Criteria

1. WHEN emitting a derived struct, THE CSharp_Emitter SHALL include all fields from the base struct (and all ancestor structs) directly in the emitted C# struct body, ordered ancestors-first (root ancestor fields appear before immediate base fields, which appear before the derived struct's own fields).
2. WHEN emitting a derived struct, THE CSharp_Emitter SHALL emit a `public static implicit operator` for conversion to the base struct, where the operator body constructs the base struct by assigning each base field's value from the corresponding field on the derived struct instance.
3. WHEN a struct has multi-level inheritance (C extends B extends A), THE CSharp_Emitter SHALL emit implicit conversion operators for each ancestor level (C→B and C→A).
4. THE CSharp_Emitter SHALL NOT emit a C# inheritance relationship (no `: BaseStruct` in the emitted C#), since the CLR does not support struct inheritance.
5. WHEN emitting flattened fields from ancestor structs, THE CSharp_Emitter SHALL preserve the original field name, type, and access modifier from the ancestor declaration.

---

### Requirement 7: Constructor Chaining

**User Story:** As a Mist developer, I want to call the base struct's constructor from a derived struct's constructor using `: base(...)` syntax, so that base fields are initialized consistently.

#### Acceptance Criteria

1. WHEN a derived struct constructor includes `: base(args)`, THE Parser SHALL produce a `BaseChain` ConstructorChain node in the AST containing the parsed argument list.
2. WHEN a derived struct constructor includes `: base(args)`, THE Type_Checker SHALL verify that the number and types of arguments are compatible with a constructor defined on the base struct.
3. IF a derived struct constructor includes `: base(args)` and no constructor on the base struct accepts the provided argument count and types, THEN THE Type_Checker SHALL emit a diagnostic error indicating which base struct constructor signatures are available.
4. IF a derived struct constructor does not include `: base(...)` and the base struct has no parameterless constructor, THEN THE Type_Checker SHALL emit a diagnostic error indicating that the base struct requires constructor arguments.
5. WHEN emitting a derived struct constructor with `: base(args)`, THE CSharp_Emitter SHALL emit the base struct's field assignments inline at the start of the constructor body, passing the provided arguments to compute initial values, rather than emitting `: base()` syntax.

---

### Requirement 8: Pretty Printer Round-Trip

**User Story:** As a developer working on the compiler, I want the pretty printer to faithfully reproduce struct inheritance syntax, so that parse-then-print-then-parse produces an equivalent AST.

#### Acceptance Criteria

1. WHEN a struct declaration has a single base type in its inheritance list, THE Pretty_Printer SHALL format the inheritance clause as `: BaseName` immediately after the struct name and type parameters.
2. WHEN a struct declaration has multiple types in its inheritance list, THE Pretty_Printer SHALL format the inheritance clause as a comma-separated list (`: Type1, Type2, Type3`) preserving the declaration order of all entries.
3. WHEN a struct declaration has type arguments on any base type (e.g., `IComparable<int>`), THE Pretty_Printer SHALL include the type argument list in the emitted base type reference.
4. FOR ALL valid struct declarations with an inheritance list containing 1 to 10 base type entries, parsing the pretty-printed output SHALL produce zero parse errors, and pretty-printing the re-parsed AST SHALL produce output identical to the first pretty-printed output (round-trip idempotence).
5. IF a struct declaration has no inheritance list, THEN THE Pretty_Printer SHALL emit the struct header without a colon or base type clause.

---

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating struct inheritance, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/StructInheritance/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: single-level inheritance, multi-level inheritance, base constructor chaining, derived-to-base implicit conversion (assignment, parameter passing, return), field access on derived instances, method access on derived instances, overloading (same name different params), and generic struct inheritance.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `StructInheritance.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

---

### Requirement 10: Book Documentation Update

**User Story:** As a reader of the Mist book, I want the Classes vs Structs page to reflect that structs now support single inheritance, so that I understand the (minimal) differences between class and struct inheritance.

#### Acceptance Criteria

1. THE Classes vs Structs page (`book/src/object-model/structs.md`) SHALL update the comparison table to state that structs support single inheritance (previously "No inheritance").
2. THE page SHALL explain that both class and struct inheritance use static dispatch — there are no virtual methods on either concrete type.
3. THE page SHALL note the key behavioral difference: class inheritance preserves a runtime subtype relationship (reference semantics), while struct inheritance is a compile-time composition mechanism where derived-to-base assignment copies the base field subset (value semantics).
4. THE page SHALL include at least one Tab_Block showing a struct extending another struct with the emitted C# demonstrating field flattening.

---

### Requirement 11: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want struct inheritance declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL continue to recognize `struct` as a declaration keyword (scope `keyword.declaration.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `struct` as a type name (scope `entity.name.type.struct.mist`), including when followed by a `:` and base type list.
3. THE TextMate grammar SHALL highlight type names in the base list after `:` with the appropriate type scope (e.g., `entity.name.type.mist` or `support.type.mist`).
