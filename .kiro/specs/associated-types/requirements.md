# Requirements Document

## Introduction

Associated types allow interfaces in Mist to declare abstract type slots — nested type declarations without a body — that implementors must fill in with concrete types satisfying declared constraints. This lifts the concept of dependent type parameters (value-level slots like `Array<TValue, Length:ZeroOrPositiveInt>`) to the type level, enabling patterns like zero-allocation iteration where the concrete enumerator struct is resolved statically at compile time.

## Glossary

- **Interface**: A Mist type declaration that specifies a contract of methods, properties, and (with this feature) abstract type slots that implementors must satisfy.
- **Trait**: A Mist type declaration that specifies a structural contract of fields, methods, properties, and (with this feature) abstract type slots that conforming types must satisfy.
- **Associated_Type_Slot**: A type member declaration within an interface or trait using the syntax `type Name : ConstraintList;` — a name followed by a colon and a constraint list (using the same syntax as generic constraint clauses). Implementors must provide a concrete type alias or nested type satisfying those constraints.
- **Concrete_Type_Member**: A nested type declaration or type alias within a class or struct that fulfills an Associated_Type_Slot from an implemented interface.
- **Constraint_List**: The right-hand side of an Associated_Type_Slot declaration, using the same syntax as generic `where` clause constraints (e.g., `struct`, `class`, interface names, `new()`).
- **Implementor**: A class or struct that implements an interface or conforms to a trait containing one or more Associated_Type_Slots and provides Concrete_Type_Members for each slot.
- **Static_Dispatch**: Resolution of a type member at compile time to its concrete definition, avoiding virtual dispatch or boxing.
- **Parser**: The Mist parser component that transforms token streams into syntax tree nodes according to the grammar.
- **Semantic_Model**: The Mist semantic analysis component that builds type information, resolves symbols, and validates constraints.
- **Type_Checker**: The Mist type checking component that validates type compatibility, constraint satisfaction, and correct usage of associated types in generic contexts.
- **CSharp_Emitter**: The Mist backend component that generates C# source code from the validated semantic model.
- **Pretty_Printer**: The Mist component that formats syntax tree nodes back into Mist source text.

## Requirements

### Requirement 1: Abstract Type Slot Declaration in Interfaces and Traits

**User Story:** As a Mist developer, I want to declare abstract type slots in interfaces and traits using `type Name : Constraints;` syntax, so that I can define contracts where implementors must provide specific types satisfying those constraints.

#### Acceptance Criteria

1. WHEN an interface or trait body contains a declaration of the form `type Name : ConstraintList ;`, THE Parser SHALL parse the declaration as an Associated_Type_Slot node in the syntax tree.
2. THE Parser SHALL parse the Constraint_List using the same syntax as generic where-clause constraints (e.g., `struct`, `class`, interface references, base type references, `new()`).
3. WHEN an Associated_Type_Slot includes generic type parameters on the slot name (e.g., `type Iterator<TState> : IIterator<TValue, TState> where TState : struct;`), THE Parser SHALL parse the type parameter list and any where clauses constraining those type parameters as part of the slot declaration. THE Parser SHALL parse generic type parameters even when no where clause is present.
4. THE Parser SHALL distinguish Associated_Type_Slots (using `:` with a Constraint_List) from concrete type aliases (using `=` with a type expression) within interface and trait bodies.
5. WHEN the Constraint_List contains identifier references (including references to type parameters of the enclosing interface or trait), THE Parser SHALL parse those identifiers as type arguments within the constraints without performing scope validation.
6. IF a class or struct body contains a declaration of the form `type Name : ConstraintList ;` (without `=`), THEN THE Parser SHALL emit a diagnostic error indicating that abstract type slots are only permitted in interfaces and traits.
7. WHEN an interface or trait body contains a declaration of the form `type Name ;` (without `:` or `=`), THE Parser SHALL parse the declaration as an unconstrained Associated_Type_Slot node with an empty Constraint_List.
8. IF an Associated_Type_Slot declaration is preceded by modifier keywords (e.g., `public`, `private`, `static`), THEN THE Parser SHALL emit a diagnostic error indicating that modifiers are not permitted on associated type slot declarations.

### Requirement 2: Semantic Registration of Associated Type Slots

**User Story:** As a Mist developer, I want the compiler to understand associated type slots as abstract members of an interface or trait, so that they participate in type resolution and constraint checking.

#### Acceptance Criteria

1. WHEN the Semantic_Model processes an interface or trait containing Associated_Type_Slots, THE Semantic_Model SHALL register each slot as an abstract type member with its declared Constraint_List, making the slot discoverable by name within the declaring type's scope.
2. WHEN a Constraint_List includes `struct` or `class`, THE Semantic_Model SHALL record the kind constraint as part of the slot's metadata. WHEN a Constraint_List includes interface or trait references, THE Semantic_Model SHALL record each interface or trait constraint as part of the slot's metadata.
3. WHEN a Constraint_List references type parameters of the enclosing interface or trait, THE Semantic_Model SHALL resolve those references to the enclosing type's type parameters.
4. WHEN an Associated_Type_Slot declares its own generic type parameters (e.g., `type Iterator<TState> : IIterator<TValue, TState>;`), THE Semantic_Model SHALL register those type parameters on the slot and resolve any where-clause constraints declared on them.
5. THE Semantic_Model SHALL allow an interface or trait to contain both Associated_Type_Slots and concrete nested type declarations in the same body.
6. THE Semantic_Model SHALL allow an interface or trait to contain both Associated_Type_Slots and concrete type aliases (using `=`) in the same body.
7. IF an interface or trait body contains two or more Associated_Type_Slots with the same name, THEN THE Semantic_Model SHALL emit a diagnostic error indicating the duplicate type slot name.
8. IF a Constraint_List references a type that cannot be resolved in the enclosing scope, THEN THE Semantic_Model SHALL emit a diagnostic error identifying the unresolved type name within the constraint.

### Requirement 3: Implementor Obligation for Associated Type Slots

**User Story:** As a Mist developer, I want the compiler to enforce that implementors provide concrete types for each associated type slot, so that the contract is fully satisfied.

#### Acceptance Criteria

1. WHEN a class or struct implements an interface or conforms to a trait containing Associated_Type_Slots, THE Type_Checker SHALL verify that the Implementor provides a Concrete_Type_Member for each slot.
2. IF an Implementor does not provide a Concrete_Type_Member for an Associated_Type_Slot, THEN THE Type_Checker SHALL emit a diagnostic error identifying the missing type member.
3. WHEN an Implementor provides a Concrete_Type_Member, THE Type_Checker SHALL verify that the concrete type satisfies all constraints in the Constraint_List of the corresponding Associated_Type_Slot.
4. WHEN a Constraint_List includes `struct`, THE Type_Checker SHALL verify that the Concrete_Type_Member is declared as a struct.
5. WHEN a Constraint_List includes `class`, THE Type_Checker SHALL verify that the Concrete_Type_Member is declared as a class.
6. WHEN a Constraint_List includes an interface reference, THE Type_Checker SHALL verify that the Concrete_Type_Member implements that interface.
7. WHEN a Constraint_List includes `new()`, THE Type_Checker SHALL verify that the Concrete_Type_Member has a public parameterless constructor.
8. IF a Concrete_Type_Member does not satisfy the Constraint_List of its corresponding Associated_Type_Slot, THEN THE Type_Checker SHALL emit a diagnostic error describing the unsatisfied constraint.
9. THE Type_Checker SHALL match a Concrete_Type_Member to an Associated_Type_Slot by name and arity (number of generic type parameters on the slot).

### Requirement 4: Usage of Associated Types in Interface and Trait Member Signatures

**User Story:** As a Mist developer, I want to use associated type names in method signatures and property types within the declaring interface or trait, so that the contract can reference its own abstract type members.

#### Acceptance Criteria

1. WHEN an interface or trait method or property references the name of an Associated_Type_Slot declared in the same interface or trait, THE Semantic_Model SHALL resolve the reference to the abstract type slot.
2. THE Type_Checker SHALL allow Associated_Type_Slot names to appear as return types, parameter types, property types, and generic type arguments within the declaring interface or trait.
3. WHEN an Implementor provides a method whose signature uses an Associated_Type_Slot name, THE Type_Checker SHALL verify that the method signature uses the corresponding Concrete_Type_Member in place of the abstract slot name.
4. IF an Implementor method signature does not substitute the Concrete_Type_Member where the interface or trait signature uses the Associated_Type_Slot name, THEN THE Type_Checker SHALL emit a diagnostic error indicating the signature mismatch.

### Requirement 5: Static Resolution of Associated Types in Generic Contexts

**User Story:** As a Mist developer, I want to write generic code constrained by an interface with associated types and have the compiler resolve the concrete associated type statically, so that I get zero-cost abstraction without boxing.

#### Acceptance Criteria

1. WHEN a generic type parameter is constrained to an interface or trait with Associated_Type_Slots, THE Type_Checker SHALL allow the Associated_Type_Slot name to be used as a nested type reference via dot notation on the type parameter (e.g., `T.Enumerator` where `T : Iterable<V>`) in any type position where a type is expected, including local variable types, parameter types, return types, and generic type arguments.
2. WHEN the generic type parameter is instantiated with a concrete type, THE Type_Checker SHALL resolve each Associated_Type_Slot reference to the Concrete_Type_Member of the instantiating type.
3. THE Type_Checker SHALL propagate the Constraint_List of the Associated_Type_Slot through generic contexts so that member access, method calls, and constraint satisfaction checks valid on the Constraint_List are permitted on values whose type is the associated type reference.
4. IF code in a generic context uses dot notation on a constrained type parameter to reference a name that is not an Associated_Type_Slot of any constraining interface or trait, THEN THE Type_Checker SHALL emit a diagnostic error indicating that the type parameter does not have an associated type with that name.

### Requirement 6: C# Code Generation for Associated Types

**User Story:** As a Mist developer, I want the compiler to emit correct C# code for types with associated type members, so that the compiled output runs correctly on the .NET runtime.

#### Acceptance Criteria

1. WHEN the CSharp_Emitter processes an interface or trait with Associated_Type_Slots, THE CSharp_Emitter SHALL emit the interface without the abstract type slot declarations (since C# interfaces do not support abstract nested type slots).
2. WHEN the CSharp_Emitter processes an Implementor with Concrete_Type_Members, THE CSharp_Emitter SHALL emit the concrete nested types as standard C# nested type declarations within the implementing class or struct.
3. WHEN an interface or trait method signature references an Associated_Type_Slot name as a parameter type or return type, THE CSharp_Emitter SHALL emit the corresponding type parameter name introduced by the emitter's generic lifting strategy (e.g., a synthesized type parameter on the interface) so that the emitted C# interface remains valid without the abstract type slot declaration.
4. WHEN an Implementor method signature references a Concrete_Type_Member that fulfills an Associated_Type_Slot, THE CSharp_Emitter SHALL substitute the fully-qualified concrete nested type name (e.g., `ImplementorName.ConcreteTypeName`) in the emitted C# method signature.
5. WHEN a generic function or method is constrained by an interface with Associated_Type_Slots and the type parameter is instantiated with a concrete Implementor at a call site, THE CSharp_Emitter SHALL resolve each Associated_Type_Slot reference within that generic context to the Implementor's Concrete_Type_Member in the emitted C# code.
6. WHEN a Constraint_List includes `struct`, THE CSharp_Emitter SHALL emit the Concrete_Type_Member as a C# struct to preserve value-type semantics and avoid boxing. WHEN a Constraint_List includes `class`, THE CSharp_Emitter SHALL emit the Concrete_Type_Member as a C# class.

### Requirement 7: Pretty Printing of Associated Type Slot Declarations

**User Story:** As a Mist developer, I want the pretty printer to correctly format associated type slot declarations, so that round-trip formatting preserves the source code.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format Associated_Type_Slot declarations as `type {Name} : {Constraint_List};`, with a single space separating the `type` keyword from the name, a single space before and after the colon, and a terminating semicolon with no preceding space.
2. WHEN an Associated_Type_Slot includes generic type parameters, THE Pretty_Printer SHALL format the declaration as `type {Name}{TypeParameterList} : {Constraint_List}{WhereClauses};`, placing the type parameter list immediately after the name (no space) and any where clauses after the Constraint_List using the same formatting as existing where clauses on methods and type aliases.
3. THE Pretty_Printer SHALL format Associated_Type_Slot declarations appearing in trait bodies using the same formatting rules as those in interface bodies.
4. WHEN a valid interface or trait declaration containing Associated_Type_Slots is parsed and then pretty-printed and then parsed again, THE Pretty_Printer output SHALL produce a syntax tree that is structurally identical to the original (ignoring whitespace trivia differences).

### Requirement 8: Diagnostic Messages for Associated Type Errors

**User Story:** As a Mist developer, I want clear error messages when I misuse associated types, so that I can quickly understand and fix problems.

#### Acceptance Criteria

1. WHEN an Implementor is missing a Concrete_Type_Member for an Associated_Type_Slot, THE Type_Checker SHALL emit an error-severity diagnostic that names the missing type slot, the interface or trait requiring the type, and the Implementor that is incomplete, with the source location pointing to the Implementor's declaration.
2. WHEN a Concrete_Type_Member does not satisfy a constraint from the Constraint_List (including kind constraints and interface requirements), THE Type_Checker SHALL emit an error-severity diagnostic that names the Concrete_Type_Member, the unsatisfied constraint, and the Associated_Type_Slot that declares the constraint, following the same message structure as existing generic constraint violation diagnostics (naming the offending type, the constraint, and the constraining entity).
3. WHEN code attempts to use an Associated_Type_Slot name as a type reference and the usage is not within the declaring interface or trait body, and is not within a generic context where a type parameter is constrained to the declaring interface or trait, THEN THE Type_Checker SHALL emit an error-severity diagnostic that names the unresolved Associated_Type_Slot and indicates the context in which it was used is not constrained by the declaring interface or trait.
4. THE Type_Checker SHALL assign a distinct DiagnosticId to each of the three associated-type error scenarios (missing type member, unsatisfied constraint, and unresolved slot usage) so that diagnostics are individually identifiable by tooling.

### Requirement 9: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want associated type slot declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `type` followed by an identifier and `:` (without `=`) inside an interface or trait body as an Associated_Type_Slot declaration pattern.
2. THE TextMate grammar SHALL highlight the identifier following `type` in an Associated_Type_Slot as a type name (scope `entity.name.type.alias.mist`).
3. THE TextMate grammar SHALL highlight constraint keywords (`struct`, `class`) in the Constraint_List with scope `storage.modifier.mist`.
4. THE TextMate grammar SHALL highlight interface references in the Constraint_List as type names (scope `entity.name.type.mist`).

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating associated types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AssociatedTypes/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements: associated type slot in an interface with a single interface constraint, associated type slot in a trait, associated type slot with `struct` kind constraint, associated type slot with `class` kind constraint, associated type slot with multiple constraints, associated type with generic type parameters and where clauses, associated type used as return type in interface method, associated type used as parameter type in interface method, implementor providing a concrete nested struct, implementor providing a concrete nested class, generic function constrained by an interface with associated type resolving the associated type statically, and interaction with existing generic constraints.
3. Each demonstrated pattern SHALL print a distinct section header (e.g., `--- Pattern Name ---`) followed by result values to stdout, so that a tester can verify each pattern independently by inspecting the corresponding output section.
4. `samples/AllFeatures/Program.mist` SHALL call `AssociatedTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 11: Language Book Documentation

**User Story:** As a Mist developer, I want the language book to document associated types and explain how they enable zero-allocation iteration, so that I can learn the feature and understand its design rationale.

#### Acceptance Criteria

1. THE language book SHALL include a dedicated section on associated types in the Type System chapter (at `book/src/type-system/associated-types.md`).
2. THE associated types section SHALL contain subsections covering: the declaration syntax (`type Name : ConstraintList;`), how implementors satisfy the contract by providing a concrete nested type or type alias, how associated types are resolved in generic contexts via constrained type parameters, and a design rationale subsection explaining how associated types enable zero-allocation iteration by allowing the compiler to resolve concrete enumerator types statically without boxing.
3. THE associated types section SHALL include a worked example showing: an `Iterable<TValue>` interface declaring an associated type slot for the enumerator, an `IEnumerator<TValue>` interface used as a constraint on that slot, a concrete struct implementor that satisfies the slot with a nested struct enumerator, and a generic function that iterates using the associated enumerator type to demonstrate static dispatch.
4. THE language book SHALL update the foreach loop note in `book/src/basics/control-flow.md` to state that the intended design is to back foreach using `Iterable<TValue>` and `IEnumerator<TValue>` interfaces with associated types, similar to Rust's `IntoIterator` / `Iterator` pattern.
5. THE language book SUMMARY (`book/src/SUMMARY.md`) SHALL include the associated types section in the Type System chapter listing, placed after the "Generic Constraints" entry.
6. THE associated types section SHALL include a note or cross-reference directing readers to the Generic Constraints section (`type-system/generic-constraints.md`) for background on constraint syntax reused by associated type slot declarations.
