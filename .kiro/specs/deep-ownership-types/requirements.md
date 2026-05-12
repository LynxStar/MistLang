# Requirements Document

## Introduction

This document specifies the requirements for redesigning the `owned` keyword in the Mist programming language to provide **deep ownership guarantees**. The redesign introduces two orthogonal concepts:

1. **Owned Type** (type-level guarantee): Adding `owned` to a type declaration means the type guarantees its entire object graph is self-contained — nothing inside the type can ever have more than one reference pointing to it. This is enforced by requiring all fields of an owned type to themselves be owned types, recursively bootstrapped from primitives in the standard library which are intrinsically owned.

2. **Owned Binding** (instance-level guarantee): `owned var x = new OwnedType()` means x is the unique owner of this value and its entire graph. Only the owner can mutate the graph. Moving to an unrestricted binding (`var y = x`) means x is dead (moved), and y holds the value but without ownership tracking.

This is a semantic shift from the current implementation where `owned` on a type means "instances MUST be owned." The redesign shifts this to: "instances CAN be safely owned because the type guarantees its entire object graph is owned." The restriction is on the type's fields, not on how instances are used.

This spec is a prerequisite for runtime dependent types, where a dependent type argument will require an `owned` binding being moved in — proving that at the moment of the move, exactly one reference to the root exists.

## Glossary

- **Owned_Type**: A class or struct declared with the `owned` modifier. The type guarantees its entire object graph is self-contained by requiring all fields to themselves be owned types. Instances of an owned type CAN be held in owned bindings (deep ownership) or unrestricted bindings (no ownership tracking).
- **Owned_Binding**: A variable declaration using `owned var x = ...` or `owned TypeName x = ...`. The binding guarantees the variable is the unique owner of the value and its entire object graph. Only valid when the type of the value is an owned type.
- **Unrestricted_Binding**: A variable declaration without ownership annotation (`var x = ...`). The variable holds a reference to the value but without ownership tracking. No deep ownership guarantees apply.
- **Deep_Ownership**: The property that owning the root of an object graph implies owning the entire graph, because the type system guarantees the graph is self-contained (no shared references within).
- **Object_Graph**: The transitive closure of all fields reachable from a root object. For an owned type, the entire object graph is guaranteed to be self-contained.
- **Move_Semantics**: The act of transferring a value from one binding to another. After a move, the source binding is dead and inaccessible.
- **Ownership_Inference**: The compiler's ability to determine that a struct consisting only of owned types is itself an owned type, without requiring an explicit `owned` annotation.
- **Intrinsically_Owned**: A type that is owned by definition, without needing to prove its fields are owned. All primitive types in the standard library (Int32, Boolean, Char, etc.) are intrinsically owned.
- **Compiler**: The Mist compiler pipeline including lexer, parser, semantic model, and type checker.
- **Type_Checker**: The analysis phase that enforces ownership rules at compile time.
- **Parser**: The syntax analysis phase that recognizes `owned` in type declarations and variable bindings.
- **CSharp_Emitter**: The backend that generates C# code from the Mist semantic model.

## Requirements

### Requirement 1: Owned Type Declaration — Field Restriction

**User Story:** As a Mist developer, I want to declare a type as `owned` so that the compiler enforces that its entire object graph is self-contained, preventing accidental introduction of shared references.

#### Acceptance Criteria

1. WHEN a class is declared with the `owned` modifier (e.g., `owned class Foo`), THE Parser SHALL attach an owned type modifier to the type declaration node.
2. WHEN a struct is declared with the `owned` modifier (e.g., `owned struct Bar`), THE Parser SHALL attach an owned type modifier to the type declaration node.
3. WHEN an owned type contains a reference-type field, THE Type_Checker SHALL permit the field if the field's declared type is itself an owned type OR the field is declared with the `owned` ownership specifier.
4. IF an owned type contains a reference-type field whose declared type is not an owned type AND the field does not have the `owned` ownership specifier, THEN THE Type_Checker SHALL report a compile error that identifies the field name, the enclosing owned type name, and states that all reference-type fields of an owned type must be declared as `owned` or be of an owned type.
5. WHEN an owned type contains a value-type field (a primitive type or a struct that itself satisfies ownership rules), THE Type_Checker SHALL permit the field without restriction.
6. WHEN an owned type contains a field whose type is another owned type, THE Type_Checker SHALL permit the field (recursive ownership is valid because each owned type proves the next tier).
7. THE Type_Checker SHALL only check immediate fields of an owned type — transitive correctness is guaranteed by each owned type independently proving its own fields.
8. IF the `owned` modifier is applied to an interface or trait declaration, THEN THE Type_Checker SHALL report a compile error stating that the `owned` modifier cannot be applied to interfaces or traits.

### Requirement 2: Owned Type — Primitive Bootstrapping via Standard Library Annotations

**User Story:** As a language developer, I want all primitive types in the standard library to be explicitly declared as `owned` types, so that the ownership type system has a well-defined base case without special compiler rules.

#### Acceptance Criteria

1. THE standard library definitions for all primitive types (Int32, Int64, Int16, UInt32, UInt64, UInt16, Byte, SByte, Single, Double, Decimal, Boolean, Char, String) and intrinsic types with no mutable state (Regex) SHALL be annotated with the `owned` modifier.
2. THE Type_Checker SHALL validate primitive types using the same owned-type field restriction as any other type — since primitives have zero fields, they trivially satisfy the requirement that 100% of fields are owned types.
3. THE Compiler SHALL NOT have any special-case logic for primitives in the ownership system — they are owned types because they are declared as such in the standard library and pass the normal field check.
4. WHEN a user-defined type contains only primitive-type fields, THE Type_Checker SHALL accept the type as satisfying owned-type field requirements because each primitive field's type is an owned type (declared in stdlib).

### Requirement 3: Ownership Inference for Structs

**User Story:** As a Mist developer, I want the compiler to infer that a struct is an owned type when all its fields are owned types, so that I don't need to annotate every simple data struct.

#### Acceptance Criteria

1. WHEN a struct declaration does not have an explicit `owned` modifier AND all of its fields are owned types (primitives, other explicitly-owned types, or other inferred-owned structs), THE Type_Checker SHALL infer the struct as an owned type.
2. WHEN a struct is inferred as an owned type, THE Type_Checker SHALL permit the struct to be used anywhere an owned type is required (e.g., as a field in another owned type, or as the type of an owned binding).
3. WHEN a struct has at least one field whose type is not an owned type, THE Type_Checker SHALL NOT infer the struct as an owned type.
4. WHEN a developer explicitly adds the `owned` modifier to a struct, THE Type_Checker SHALL enforce the owned-type field restriction (preventing accidental regression if a non-owned field is later added).
5. THE Type_Checker SHALL NOT infer ownership for class declarations — only structs are eligible for inference.
6. WHEN a struct declaration has zero fields, THE Type_Checker SHALL infer the struct as an owned type (the owned-field requirement is vacuously satisfied).
7. WHEN a generic struct has a field whose type is a type parameter, THE Type_Checker SHALL NOT infer the struct as an owned type at the declaration site — ownership eligibility for generic structs SHALL be determined per-instantiation based on whether the concrete type arguments are owned types.

### Requirement 4: Owned Binding Declaration

**User Story:** As a Mist developer, I want to declare an owned binding (`owned var x = ...`) so that I can establish unique ownership of a value and its entire object graph.

#### Acceptance Criteria

1. WHEN a variable is declared with the `owned` specifier (e.g., `owned var x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
2. WHEN a variable is declared with the `owned` specifier and an explicit type (e.g., `owned Foo x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
3. WHEN an owned binding is declared with an initializer whose type is an owned type, THE Type_Checker SHALL accept the declaration without error.
4. IF an owned binding is declared with a type that is not an owned type, THEN THE Type_Checker SHALL report a compile error indicating that the owned specifier requires a type declared with the `owned` modifier.
5. WHEN an owned binding is declared, THE Type_Checker SHALL verify that the initializer is a value-producing expression that yields unique ownership: a constructor call (`new`), a call to a function returning an owned value, or a move from another owned binding.
6. IF an owned binding is declared with an initializer that does not yield unique ownership (e.g., a reference to a borrowed or shared binding), THEN THE Type_Checker SHALL report a compile error indicating that an owned binding requires a uniquely-owned value from a constructor call, an owned-returning function, or a move from an existing owned binding.

### Requirement 5: Owned Binding — Move Semantics

**User Story:** As a Mist developer, I want owned bindings to support move semantics so that ownership can be cleanly transferred between locations while preventing use-after-move errors.

#### Acceptance Criteria

1. WHEN an owned binding is assigned to another owned binding (e.g., `owned var y = x`), THE Type_Checker SHALL treat this as a move — the source binding becomes dead and inaccessible.
2. WHEN an owned binding is assigned to an unrestricted binding (e.g., `var y = x`), THE Type_Checker SHALL treat this as a move — the source binding becomes dead, and the destination holds the value without ownership tracking.
3. WHEN an owned binding is passed to a parameter marked `owned`, THE Type_Checker SHALL move ownership to the callee and mark the caller's binding as dead.
4. IF code reads, writes, passes as an argument, or takes a reference to an owned binding after it has been moved, THEN THE Type_Checker SHALL report a use-after-move error identifying the binding name, the move location, and the invalid access location.
5. WHEN an owned binding is moved in one branch of a conditional but not another, THE Type_Checker SHALL treat the binding as moved at the join point after the conditional, and SHALL report a use-after-move error if the binding is accessed after the conditional or within a branch where it was moved.
6. WHEN an owned binding is moved inside a loop body, THE Type_Checker SHALL report an error unless the binding is re-assigned a new owned value within the same iteration after the move.
7. WHEN a method returns an `owned` value, THE Type_Checker SHALL transfer ownership to the receiving binding at the call site.
8. A move SHALL only be permitted when the variable is in the **Live** state. IF the variable is in LentReadonly, LentFrozen, LentRef, or Moved state, THE Type_Checker SHALL reject the move (see Requirement 11 truth table).

### Requirement 6: Owned Binding — Mutation Rights

**User Story:** As a Mist developer, I want only the owner of a binding to be able to mutate the object graph, so that I can reason about exclusive access to the entire graph.

#### Acceptance Criteria

1. WHEN a variable is held in an owned binding, THE Type_Checker SHALL permit the owner to mutate top-level fields of the object (replace fields, call mutating methods).
2. WHEN a variable is held in an owned binding, THE Type_Checker SHALL permit the owner to borrow into fields (lend references to nested objects within the graph).
3. WHEN a variable is held in an owned binding, THE Type_Checker SHALL permit the owner to lend the root as `borrowed`, `readonly ref`, or `frozen` following existing lending rules, subject to the state restrictions defined in Requirement 11 (e.g., cannot borrow while in LentReadonly or LentFrozen state).

### Requirement 7: Unrestricted Binding to an Owned Type

**User Story:** As a Mist developer, I want to hold an owned type in an unrestricted binding so that I can use owned types in contexts that don't need ownership tracking, with appropriate access restrictions.

#### Acceptance Criteria

1. WHEN an owned type instance is held in an unrestricted binding (e.g., after `var y = x` where x was an owned binding), THE Type_Checker SHALL permit the holder to assign new values to direct fields of the object and to call mutating methods on the root object.
2. WHEN an owned type instance is held in an unrestricted binding, THE Type_Checker SHALL NOT permit the holder to take a `borrowed` reference to any field in the object graph (direct or nested).
3. WHEN an owned type instance is held in an unrestricted binding, THE Type_Checker SHALL NOT permit the holder to take a `frozen` reference to any field in the object graph (direct or nested).
4. WHEN an owned type instance is held in an unrestricted binding, THE Type_Checker SHALL permit the holder to take a `readonly ref` to direct (top-level) fields only — not to nested fields within the graph.
5. WHEN an owned type instance is held in an unrestricted binding, THE Type_Checker SHALL NOT permit the binding to be used as a dependent type argument (only owned bindings qualify).

### Requirement 8: Semantic Shift — Owned Type Instances Are Not Restricted to Owned Bindings

**User Story:** As a Mist developer, I want owned types to be usable in unrestricted bindings so that the `owned` modifier on a type describes what the type guarantees (self-contained graph) rather than how instances must be held.

#### Acceptance Criteria

1. WHEN code declares a variable of an owned type without the `owned` specifier (e.g., `var x = new OwnedFoo()`), THE Type_Checker SHALL permit the declaration — the instance is held in an unrestricted binding.
2. WHEN code declares a variable of an owned type with the `owned` specifier (e.g., `owned var x = new OwnedFoo()`), THE Type_Checker SHALL permit the declaration — the instance is held in an owned binding with full ownership tracking.
3. THE Type_Checker SHALL NOT report an error when an owned type instance is stored in an unrestricted variable, field, or collection.
4. WHEN an owned type instance is passed to an unrestricted parameter, THE Type_Checker SHALL permit the call.

### Requirement 9: Owned Type as Field in Another Type

**User Story:** As a Mist developer, I want to use owned types as fields in other types so that I can compose owned types into larger owned structures.

#### Acceptance Criteria

1. WHEN an owned type is used as a field type in another owned type, THE Type_Checker SHALL permit the field (owned types satisfy the owned-field requirement).
2. WHEN an owned type is used as a field type in a non-owned type, THE Type_Checker SHALL permit the field — the containing type simply holds a reference to the owned-type instance without ownership guarantees.
3. WHEN a non-owned type contains a field of an owned type, THE Type_Checker SHALL NOT require the containing type to be an owned type.

### Requirement 10: Owned Field Modifier on Non-Owned Types

**User Story:** As a Mist developer, I want to mark individual fields as `owned` in a non-owned type, so that the containing instance exclusively owns the field's value and its graph — even when multiple references to the containing instance exist.

#### Acceptance Criteria

1. WHEN a field in a non-owned type is declared with the `owned` modifier (e.g., `owned MyClass data;`), THE Parser SHALL attach an owned ownership specifier to the field declaration node.
2. WHEN a field is marked `owned`, THE Type_Checker SHALL require that the field's type is an owned type (explicitly declared or inferred). IF the field's type is not an owned type, THEN THE Type_Checker SHALL report a compile error.
3. WHEN assigning to an `owned` field, THE Type_Checker SHALL require that the source value is from an owned binding (a move from an owned variable, a constructor call, or a function returning an owned value). Assigning from an unrestricted binding SHALL be rejected.
4. WHEN an `owned` field is accessed on an instance held in any binding (owned or unrestricted), THE Type_Checker SHALL permit taking a `readonly ref` to the field.
5. WHEN an `owned` field is accessed on an instance held in an unrestricted binding, THE Type_Checker SHALL NOT permit taking a `borrowed` or `frozen` reference to the field, and SHALL NOT permit moving the field out.
6. WHEN an `owned` field is accessed on an instance held in an owned binding, THE Type_Checker SHALL permit the owner to take a `borrowed` or `frozen` reference to the field, and SHALL permit moving the field out (with the field becoming uninitialized until reassigned).
7. THE `owned` field modifier guarantees that the field's value is exclusively owned by the containing instance — no other reference to the field's object graph exists. Multiple references to the containing instance may exist, but only the owner of the containing instance can mutate or borrow from the owned field.

### Requirement 11: Ownership State Transition Rules (Truth Table)

**User Story:** As a Mist developer, I want the compiler to enforce a clear set of rules about what actions are permitted based on the current ownership state of a variable, so that I can reason about access guarantees at each point in my program.

#### Acceptance Criteria

The following truth table defines the permitted actions for each variable ownership state. THE Type_Checker SHALL enforce these rules exactly:

| Variable State | Read | Write | Live View | Freeze | Borrow | Move |
|---|---|---|---|---|---|---|
| **Live** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **LentReadonly** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **LentFrozen** | ✅ | ❌ | ✅ | ✅ | ❌ | ❌ |
| **LentRef** (borrowed out) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Moved** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**State definitions (from the owner's perspective):**

- **Live**: I am the sole owner of this object and the only thing that has access to it. I can do everything.
- **LentReadonly**: I am the sole owner of this object. I have lent out one or more live views (readonly refs). Others can observe the object but cannot mutate it. I retain read/write access, but I cannot move or borrow because both would create a recipient that assumes exclusive access — which is false while a readonly ref exists.
- **LentFrozen**: I am the sole owner of this object. I have agreed not to mutate the object for the duration that frozen leases are outstanding. I cannot move (the new owner would start in Live state and could mutate, violating the frozen guarantee). I cannot borrow (the borrower would assume exclusive mutable access, violating the frozen guarantee). I can hand out additional frozen leases and live views, and I can read.
- **LentRef** (borrowed out): I am the sole owner of this object, but have temporarily lent exclusive access to a borrower. I cannot do anything until the borrow is returned.
- **Moved**: The value has been moved to another binding. This variable is dead and inaccessible. Any access is a compile-time error.

**Specific enforcement rules:**

1. WHEN a variable is in the **Live** state, THE Type_Checker SHALL permit all actions: read, write, lending as live view, lending as frozen, lending as borrowed, and moving.
2. WHEN a variable is in the **LentReadonly** state, THE Type_Checker SHALL permit read, write, lending as live view, and lending as frozen. THE Type_Checker SHALL reject borrow and move because both create a recipient that assumes exclusive access, which is false while a readonly ref exists.
3. WHEN a variable is in the **LentFrozen** state, THE Type_Checker SHALL permit read, lending as live view, and lending as frozen. THE Type_Checker SHALL reject write (violates the immutability promise), borrow (borrower assumes exclusive mutable access), and move (new owner starts in Live state and could mutate).
4. WHEN a variable is in the **LentRef** (borrowed out) state, THE Type_Checker SHALL reject all actions. The owner has no access until the borrow is returned.
5. WHEN a variable is in the **Moved** state, THE Type_Checker SHALL reject all actions and report a use-after-move error.

### Requirement 12: Interaction with Existing Ownership Tiers

**User Story:** As a Mist developer, I want deep ownership to integrate cleanly with the existing borrowed/frozen/readonly ref lending system so that owned bindings can participate in all existing borrowing patterns.

#### Acceptance Criteria

1. WHEN an owned binding is lent as `borrowed`, THE Type_Checker SHALL transition the owner to the LentRef state — the owner has no access until the borrow's scope ends.
2. WHEN an owned binding is lent as `frozen`, THE Type_Checker SHALL transition the owner to the LentFrozen state — the owner cannot mutate until the frozen loan's scope ends, while still permitting reads.
3. WHEN an owned binding is lent as `readonly ref`, THE Type_Checker SHALL transition the owner to the LentReadonly state — the owner retains read/write access but cannot move or borrow.
4. WHEN an owned binding is passed to an `owned` parameter, THE Type_Checker SHALL mark the source binding as moved and report a use-after-move error on any subsequent access without an intervening re-assignment.
5. WHEN an unrestricted binding is passed to a `readonly ref` parameter, THE Type_Checker SHALL permit the call.
6. WHEN an unrestricted binding is passed to a `borrowed` or `frozen` parameter, THE Type_Checker SHALL report an error indicating that an unrestricted binding cannot guarantee exclusive access.
7. IF an owned binding already has an outstanding `borrowed` loan and code attempts a second lend of any tier, THEN THE Type_Checker SHALL report an error indicating the binding is inaccessible while a `borrowed` loan is outstanding.
8. WHILE an owned binding has one or more outstanding `frozen` loans, THE Type_Checker SHALL permit additional `frozen` or `readonly ref` loans from the same binding but SHALL reject a `borrowed` loan or a move.

### Requirement 13: Ownership Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for deep ownership violations so that I can understand and fix issues quickly.

#### Acceptance Criteria

1. WHEN an owned type has a reference-type field whose type is not itself an owned type, THE Type_Checker SHALL report an error identifying the field name, the field's declared type, and explain that all reference-type fields of an owned type must be owned types.
2. WHEN an owned binding is declared with a type that is not an owned type and is not a value type, THE Type_Checker SHALL report an error identifying the binding name, the declared type, and explain that owned bindings require a type declared with the `owned` modifier.
3. WHEN a use-after-move occurs on an owned binding, THE Type_Checker SHALL report an error identifying the binding name, the source location where the move occurred, and the source location of the invalid access.
4. WHEN an unrestricted binding of an owned type attempts to take a `borrowed` or `frozen` reference to one of the object's fields, THE Type_Checker SHALL report an error identifying the binding name and explain that only owned bindings can borrow into the owned object's fields.
5. WHEN a struct that contains at least one reference-type field whose type is not an owned type is used where an owned type is required, THE Type_Checker SHALL report an error identifying the struct name and the specific field that prevents the struct from satisfying the owned type requirement.
6. WHEN an owned type declares a field of type `readonly ref`, `frozen`, or `borrowed`, THE Type_Checker SHALL report an error identifying the field name and explain that non-storable reference tiers cannot appear as fields in an owned type.

### Requirement 14: C# Emission Strategy

**User Story:** As a language developer, I want deep ownership to be purely compile-time with no runtime cost, so that the emitted C# is clean and performant.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL NOT emit any runtime ownership tracking for deep ownership (no reference counting, no state fields, no runtime assertions).
2. THE CSharp_Emitter SHALL emit all owned bindings as standard C# variable declarations regardless of their ownership status.
3. THE CSharp_Emitter SHALL NOT emit the `owned` keyword in generated C# — ownership annotations are erased.
4. WHEN an owned binding is moved, THE CSharp_Emitter SHALL emit a standard assignment (no nulling of source, no runtime move tracking).
5. THE CSharp_Emitter SHALL emit owned type declarations as standard C# class or struct declarations without any ownership-related attributes or markers.

### Requirement 15: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `owned` on type declarations to be syntax-highlighted correctly so that I get visual cues distinguishing owned types from regular types.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `owned` preceding `class` or `struct` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `owned class` or `owned struct` as a type name (scope `entity.name.type.class.mist` or `entity.name.type.struct.mist` respectively).
3. THE TextMate grammar SHALL recognize `owned` preceding a variable type or `var` as a storage modifier keyword (scope `storage.modifier.mist`).
4. THE TextMate grammar SHALL match `owned` only at word boundaries to avoid false matches within identifiers.

### Requirement 16: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating deep ownership types so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DeepOwnershipTypes/Program.mist` with a corresponding `mist.toml` where `name = "DeepOwnershipTypes"`.
2. The sample SHALL demonstrate each of the following patterns, with at least one code example per pattern: owned type declarations with field restrictions, owned bindings with move semantics, unrestricted bindings to owned types (permitted operations and restrictions), ownership inference for structs, primitive bootstrapping (owned types containing only primitives), nested owned types (owned type containing another owned type), interaction with existing lending tiers (borrowed, frozen, readonly ref from owned bindings), the semantic shift (owned type in unrestricted binding is permitted), and moving owned bindings into functions.
3. The sample SHALL demonstrate every permitted action from the truth table (Requirement 11) for each variable state where the action is allowed:
   - **Live**: read, write, live view, freeze, borrow, move
   - **LentReadonly**: read, write, live view, freeze
   - **LentFrozen**: read, live view, freeze
   - **Borrowed (recipient perspective)**: the borrower receives the value and can read, write, lend further (borrowed, frozen, readonly ref)
4. Each demonstrated pattern SHALL print a unique labeled prefix to stdout (e.g., `[owned-type-fields]`, `[owned-binding-move]`, `[unrestricted-owned]`, `[truth-table-live-move]`, `[truth-table-lentreadonly-freeze]`) so that correctness of each pattern is independently observable.
5. The sample SHALL expose a `public static void RunDemo()` method in `DeepOwnershipTypes.Program` that executes all demonstrations.
6. `samples/AllFeatures/Program.mist` SHALL call `DeepOwnershipTypes.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../DeepOwnershipTypes"`.
7. The sample SHALL compile and run without errors or warnings using the Mist compiler.

### Requirement 17: Round-Trip Parsing

**User Story:** As a language developer, I want ownership annotations on type declarations and bindings to survive parse-print cycles so that tooling can reliably reconstruct source.

#### Acceptance Criteria

1. FOR ALL valid owned type declarations (`owned class Foo`, `owned struct Bar`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
2. FOR ALL valid owned binding declarations (`owned var x = ...`, `owned Foo x = ...`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
3. IF parsing the pretty-printed output produces parse errors, THE round-trip property SHALL be considered violated.
