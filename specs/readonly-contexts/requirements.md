# Requirements Document

> **Amended** by [`purity`](../purity/requirements.md) Requirement 11 (2026-09-26): a readonly context may change what it owns -- write a field or index of, or call a setter of, what it created -- and may call a `local` member on an instance it owns, which base rules 6, 7 and 9 below do not yet say. That spec also defines `local` and `pure`, and makes every pure member readonly.

## Introduction

This document specifies the requirements for adding readonly contexts to the Mist programming language. The readonly feature prevents mutation after object creation by restricting what operations are permitted within a readonly context. Within a readonly context, code may only interact with other readonly-qualified sites. Members, types, constructors, parameters, and arbitrary blocks can be marked or inferred as readonly, creating a transitive safety guarantee that prevents post-construction mutation.

This differs from C# `readonly` (which only prevents field reassignment) by extending the concept into a full contextual restriction: readonly code cannot call non-readonly code, cannot assign to non-readonly fields, and cannot invoke non-readonly setters. The system also includes readonly parameters (allowing non-readonly methods to safely accept readonly types) and generic constraints (`where T : readonly`, `where T : mutable`) for expressing mutability requirements on type parameters. The design is inspired by Haskell's purity concept but is less strict — local allocation of readonly types is permitted.

## Glossary

- **Readonly_Context**: A lexical region in which only readonly-qualified operations are permitted. Assigning to any field, calling non-readonly methods, calling non-readonly property getters, and calling any property setter is prohibited within a readonly context. Reading fields is always permitted (fields are just data). Specific contexts (constructors, property setters) may layer exceptions on top of these base rules.
- **Readonly_Member**: A field, property, method, or constructor explicitly annotated with the `readonly` modifier. The member's body executes within a readonly context.
- **Readonly_Type**: A type (class, struct, interface, or trait) annotated with the `readonly` modifier. All members of a readonly type are implicitly readonly.
- **Readonly_Block**: An explicit `readonly { ... }` statement that introduces a readonly context within any block, including constructors.
- **Readonly_Inference_Engine**: The compiler component that performs local analysis to determine which members implicitly satisfy readonly constraints.
- **Readonly_Site**: Any location (field, property, method, constructor, block) that is either explicitly marked readonly or inferred to be readonly.
- **Constructor_Privilege**: The special permission granted to constructors allowing assignment to readonly fields and invocation of readonly property setters on `this`, which is not available in other readonly contexts.
- **Backing_Field_Exemption**: The special rule allowing the `field` keyword inside a readonly property setter body to be assigned, enabling constructors to write through the setter.
- **Parser**: The Storm-generated parser that reads Mist source and produces a concrete syntax tree.
- **Semantic_Model**: The internal representation of resolved types, symbols, and their relationships after semantic analysis.
- **CSharp_Emitter**: The backend that translates the Mist semantic model into C# source code.
- **Type_Checker**: The component that validates type correctness and enforces semantic rules.
- **Readonly_Analyzer**: The analysis pass that enforces readonly context restrictions and reports violations.
- **Readonly_Parameter**: A method parameter annotated with `readonly` that prohibits mutation of the argument within the method body.
- **Mutable_Constraint**: A generic type constraint (`where T : mutable`) requiring the type argument to be a mutable type.
- **Readonly_Constraint**: A generic type constraint (`where T : readonly`) requiring the type argument to be a readonly type.

## Requirements

### Requirement 1: Readonly Modifier Parsing

**User Story:** As a language developer, I want the parser to recognize the `readonly` modifier on members and types, so that developers can explicitly mark declarations as readonly.

#### Acceptance Criteria

1. WHEN the `readonly` keyword precedes a field declaration, THE Parser SHALL produce a FieldDeclaration node with a Readonly modifier attached.
2. WHEN the `readonly` keyword precedes a property declaration, THE Parser SHALL produce a PropertyDeclaration node with a Readonly modifier attached.
3. WHEN the `readonly` keyword precedes a method declaration, THE Parser SHALL produce a MethodDeclaration node with a Readonly modifier attached.
4. WHEN the `readonly` keyword precedes a constructor declaration, THE Parser SHALL produce a ConstructorDeclaration node with a Readonly modifier attached.
5. WHEN the `readonly` keyword precedes a class declaration, THE Parser SHALL produce a ClassDeclaration node with a Readonly modifier attached.
6. WHEN the `readonly` keyword precedes a struct declaration, THE Parser SHALL produce a StructDeclaration node with a Readonly modifier attached.
7. WHEN the `readonly` keyword precedes an interface declaration, THE Parser SHALL produce an InterfaceDeclaration node with a Readonly modifier attached.
8. WHEN the `readonly` keyword precedes a trait declaration, THE Parser SHALL produce a TraitDeclaration node with a Readonly modifier attached.
9. WHEN the `readonly` keyword precedes an indexer declaration, THE Parser SHALL produce an IndexerDeclaration node with a Readonly modifier attached.
10. WHEN the `readonly` modifier is combined with other modifiers such as `public`, `private`, or `static`, THE Parser SHALL accept the combination and attach all modifiers to the declaration node.

### Requirement 2: Readonly Block Parsing

**User Story:** As a language developer, I want the parser to recognize `readonly { ... }` blocks as statements, so that developers can introduce readonly contexts at arbitrary points in code.

#### Acceptance Criteria

1. WHEN a statement begins with `readonly` followed by a block (`{ ... }`), THE Parser SHALL produce a ReadonlyBlockStatement node containing the enclosed statements.
2. WHEN a readonly block appears inside a method body, THE Parser SHALL accept it as a valid statement.
3. WHEN a readonly block appears inside a constructor body, THE Parser SHALL accept it as a valid statement.
4. WHEN a readonly block is nested inside another readonly block, THE Parser SHALL accept it without error.

### Requirement 3: Keyword Registration

**User Story:** As a language developer, I want `readonly` and `mutable` to be reserved keywords, so that the lexer correctly tokenizes them and they cannot be used as identifiers.

#### Acceptance Criteria

1. THE Parser SHALL recognize `readonly` as a reserved keyword token.
2. THE Parser SHALL recognize `mutable` as a reserved keyword token.
3. WHEN `readonly` appears in identifier position, THE Parser SHALL report a syntax error.
4. WHEN `mutable` appears in identifier position, THE Parser SHALL report a syntax error.

### Requirement 4: Readonly Field Semantics

**User Story:** As a Mist developer, I want readonly fields to be immutable after construction, so that I can guarantee fields are not modified after the object is created.

#### Acceptance Criteria

1. WHEN a field is marked `readonly`, THE Type_Checker SHALL prohibit assignment to that field outside of a constructor of the declaring type.
2. WHEN a constructor of the declaring type assigns to a readonly field, THE Type_Checker SHALL accept the assignment without error.
3. WHEN code within a readonly block inside a constructor attempts to assign to a readonly field, THE Readonly_Analyzer SHALL report an error (readonly blocks remove constructor privilege).
4. IF code outside a constructor assigns to a readonly field, THEN THE Type_Checker SHALL report an error indicating the field is readonly.

### Requirement 5: Readonly Property Semantics

**User Story:** As a Mist developer, I want readonly properties to restrict setter access to constructors, so that property values are immutable after construction.

#### Acceptance Criteria

1. WHEN a property is marked `readonly`, THE Type_Checker SHALL prohibit calling its setter outside of a constructor of the declaring type.
2. WHEN a constructor of the declaring type calls a readonly property setter, THE Type_Checker SHALL accept the call without error.
3. WHEN a readonly property has a setter body, THE Readonly_Analyzer SHALL enforce readonly context base rules (Requirement 11) on the setter body, plus the backing field exemption (exception 12).
4. WHEN a readonly property setter body uses `field = value`, THE Readonly_Analyzer SHALL permit the assignment (backing field exemption, exception 12 in Requirement 11).
5. WHEN a readonly property has a getter body, THE Readonly_Analyzer SHALL enforce readonly context base rules (Requirement 11) on the getter body with no additional exceptions.
6. WHEN a readonly property has an auto-getter (`get;`), THE Semantic_Model SHALL treat the getter as implicitly readonly.

### Requirement 6: Readonly Indexer Semantics

**User Story:** As a Mist developer, I want readonly indexers to restrict setter access to constructors and enforce readonly context rules on their bodies, so that indexed access respects immutability guarantees.

#### Acceptance Criteria

1. WHEN an indexer is marked `readonly`, THE Type_Checker SHALL prohibit calling its setter outside of a constructor of the declaring type.
2. WHEN a constructor of the declaring type calls a readonly indexer setter, THE Type_Checker SHALL accept the call without error.
3. WHEN a readonly indexer has a getter body, THE Readonly_Analyzer SHALL enforce readonly context base rules (Requirement 11) on the getter body with no additional exceptions.
4. WHEN a readonly indexer has a setter body, THE Readonly_Analyzer SHALL enforce readonly context base rules (Requirement 11) on the setter body with no additional exceptions (indexers have no backing field, so no backing field exemption applies).
5. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit calling a non-readonly indexer getter.
6. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit calling any indexer setter (unless constructor privilege applies to the declaring type's own indexer setters).

### Requirement 7: Readonly Method Semantics

**User Story:** As a Mist developer, I want to mark methods as readonly, so that I can guarantee they do not mutate state.

#### Acceptance Criteria

1. WHEN a method is marked `readonly`, THE Readonly_Analyzer SHALL enforce that the method body operates within a readonly context (subject to the base readonly context rules defined in Requirement 11).
2. A readonly method body SHALL NOT receive any special exceptions beyond the base readonly context rules.

### Requirement 8: Readonly Type Semantics

**User Story:** As a Mist developer, I want to mark an entire type as readonly, so that all members are automatically readonly and the type guarantees immutability.

#### Acceptance Criteria

1. WHEN a type is marked `readonly`, THE Semantic_Model SHALL treat all members of that type as implicitly readonly.
2. WHEN a type is marked `readonly`, THE Readonly_Analyzer SHALL enforce readonly context restrictions on the constructor body (the constructor itself is readonly, subject to base rules in Requirement 11 plus constructor privilege exception 11).
3. WHEN a readonly type constructor calls a static method that is not readonly, THE Readonly_Analyzer SHALL report an error (per base rule 7).
4. WHEN a readonly type constructor accesses a non-readonly getter or method on a parameter, THE Readonly_Analyzer SHALL report an error (per base rules 7 and 8).
5. WHEN a readonly type constructor calls a readonly method on a parameter, THE Readonly_Analyzer SHALL accept the call without error (per base rule 2).

### Requirement 9: Readonly Block Semantics

**User Story:** As a Mist developer, I want to declare arbitrary readonly blocks within code, so that I can enforce immutability guarantees on specific regions.

#### Acceptance Criteria

1. WHEN code is inside a `readonly { ... }` block, THE Readonly_Analyzer SHALL enforce the base readonly context rules (Requirement 11) on all statements within the block.
2. WHEN a readonly block appears inside a constructor, THE Readonly_Analyzer SHALL apply exception 13 (Requirement 11): constructor privilege is removed and base rules apply without exception.
3. WHEN a readonly block appears outside a constructor (e.g., in a regular method), THE Readonly_Analyzer SHALL enforce the base readonly context rules with no additional exceptions.

### Requirement 10: Readonly Inference

**User Story:** As a Mist developer, I want the compiler to infer readonly status for members that naturally satisfy readonly constraints, so that I do not need to annotate everything manually.

#### Acceptance Criteria

1. THE Readonly_Inference_Engine SHALL determine readonly status by examining only the immediate operations within a member's body (local analysis). Each member proves its own readonly status independently.
2. THE Readonly_Inference_Engine SHALL classify literal expressions as implicitly readonly.
3. THE Readonly_Inference_Engine SHALL classify field access expressions (reading a field) as implicitly readonly.
4. THE Readonly_Inference_Engine SHALL classify auto-getter property access as implicitly readonly.
5. THE Readonly_Inference_Engine SHALL classify a block as implicitly readonly when all operations within the block access only readonly sites.
6. THE Readonly_Inference_Engine SHALL classify a method as implicitly readonly when its body only accesses readonly sites (explicit or inferred).
7. THE Readonly_Inference_Engine SHALL classify a constructor as implicitly readonly when it only assigns to its own fields and does not call any external non-readonly members.
8. WHEN a method calls another method, THE Readonly_Inference_Engine SHALL only verify that the called method is readonly; it SHALL NOT traverse into the called method's body.

### Requirement 11: Readonly Context Base Rules

**User Story:** As a Mist developer, I want a single unified set of rules governing what is permitted within any readonly context, so that I can reason about immutability guarantees consistently regardless of whether the context is a method, getter, setter, constructor, or explicit readonly block.

#### Acceptance Criteria

**Permitted operations (base rules):**

1. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit reading any field (fields are always safe to read as they are just data).
2. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit calling readonly methods (explicit or inferred).
3. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit calling readonly property and indexer getters (explicit or inferred).
4. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit local variable declarations and assignments to local variables.
5. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit allocation via `new` only when the target constructor is readonly (explicit, inferred, or belonging to a readonly type).

**Prohibited operations (base rules):**

6. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit assigning to any field (regardless of whether the field itself is marked readonly or not).
7. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit calling non-readonly methods.
8. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit calling non-readonly property or indexer getters.
9. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit calling any property or indexer setter.
10. WHILE in a readonly context, THE Readonly_Analyzer SHALL prohibit allocation via `new` when the target constructor is not readonly.

**Exceptions layered on specific contexts:**

11. WHEN a constructor operates within a readonly context (due to belonging to a readonly type), THE Readonly_Analyzer SHALL grant constructor privilege: assignment to the declaring type's own fields and invocation of the declaring type's own readonly property/indexer setters are permitted despite base rules 6 and 9.
12. WHEN a readonly property setter body operates within a readonly context, THE Readonly_Analyzer SHALL grant the backing field exemption: `field = value` is permitted despite base rule 6.
13. WHEN an explicit `readonly { ... }` block appears inside a constructor, THE Readonly_Analyzer SHALL override constructor privilege within that block — base rules 6 and 9 apply without exception (field assignment and property/indexer setter calls are prohibited).
14. Exception 13 (readonly block) takes precedence over exception 11 (constructor privilege) when both apply.

### Requirement 12: Readonly and Generics Interaction

**User Story:** As a Mist developer, I want readonly to compose with generic type parameters using explicit constraints, so that generic code can express mutability requirements on type arguments.

#### Acceptance Criteria

1. WHEN a generic type parameter has a `where T : readonly` constraint, THE Type_Checker SHALL require that the type argument is a readonly type.
2. WHEN a generic type parameter has a `where T : mutable` constraint, THE Type_Checker SHALL require that the type argument is a mutable type (permits mutation).
3. WHEN a generic type parameter has a `where T : readonly` constraint, THE Readonly_Analyzer SHALL treat all member access on T within the generic body as readonly.
4. WHEN a generic type parameter has a `where T : mutable` constraint, THE Readonly_Analyzer SHALL permit the generic body to mutate T through non-readonly members.
5. WHEN a generic type parameter has no readonly or mutable constraint, THE Readonly_Analyzer SHALL assume the type parameter is mutable (most permissive for the caller).
6. WHEN a readonly type is used as a type argument for an unconstrained or readonly-constrained parameter, THE Semantic_Model SHALL preserve the readonly status of its members through the generic instantiation.
7. WHEN a readonly type is used as a type argument for a mutable-constrained parameter, THE Type_Checker SHALL report an error indicating the type argument does not satisfy the mutable constraint.

### Requirement 13: Readonly and Interface/Trait Contracts

**User Story:** As a Mist developer, I want readonly to propagate through interface and trait implementations, so that contracts can express immutability guarantees.

#### Acceptance Criteria

1. WHEN an interface or trait is marked `readonly`, THE Semantic_Model SHALL treat all members of that interface or trait as implicitly readonly.
2. WHEN an interface or trait method is readonly (explicitly or implicitly via a readonly interface/trait), THE Type_Checker SHALL require that implementing methods are also readonly (explicit or inferred).
3. WHEN an interface or trait property is readonly (explicitly or implicitly via a readonly interface/trait), THE Type_Checker SHALL require that implementing properties are also readonly (explicit or inferred).
4. IF an implementing method is not readonly but the interface or trait method requires readonly, THEN THE Type_Checker SHALL report an error indicating the implementation violates the readonly contract.
5. IF an implementing property is not readonly but the interface or trait property requires readonly, THEN THE Type_Checker SHALL report an error indicating the implementation violates the readonly contract.
6. WHEN a type implements a readonly interface or trait, THE Type_Checker SHALL verify that every member implementation satisfies the readonly requirement independently (the implementing type itself does not need to be readonly, but each member must be).

### Requirement 14: Readonly Parameters

**User Story:** As a Mist developer, I want to mark method parameters as `readonly`, so that a method can promise not to mutate an argument and readonly types can be safely passed to non-readonly methods.

#### Acceptance Criteria

1. WHEN the `readonly` keyword precedes a parameter type in a method signature, THE Parser SHALL produce a ParameterDeclaration node with a Readonly modifier attached.
2. WHEN a method body attempts to call a non-readonly method on a readonly parameter, THE Readonly_Analyzer SHALL report an error.
3. WHEN a method body attempts to assign to a field of a readonly parameter, THE Readonly_Analyzer SHALL report an error.
4. WHEN a method body attempts to call a non-readonly property setter on a readonly parameter, THE Readonly_Analyzer SHALL report an error.
5. WHEN a readonly type is passed as an argument, THE Type_Checker SHALL require that the corresponding parameter position is marked `readonly`.
6. WHEN a non-readonly type is passed as an argument, THE Type_Checker SHALL accept both readonly and non-readonly parameter positions.
7. WHEN a method has only readonly parameters and its body only accesses readonly sites on those parameters, THE Readonly_Inference_Engine SHALL consider the readonly parameters as contributing evidence toward inferring the method as readonly.

### Requirement 15: Readonly Allocation Restrictions

**User Story:** As a Mist developer, I want allocation within readonly contexts to be restricted to readonly constructors, so that newly created objects cannot introduce mutation.

#### Acceptance Criteria

1. WHILE in a readonly context, THE Readonly_Analyzer SHALL permit `new` expressions only when the target constructor is readonly.
2. THE Readonly_Analyzer SHALL consider a constructor readonly when it belongs to a readonly type.
3. THE Readonly_Analyzer SHALL consider a constructor readonly when it is explicitly marked `readonly`.
4. THE Readonly_Analyzer SHALL consider a constructor readonly when the Readonly_Inference_Engine classifies it as implicitly readonly.
5. IF a `new` expression targets a non-readonly constructor within a readonly context, THEN THE Readonly_Analyzer SHALL report an error indicating the constructor is not readonly.

### Requirement 16: Readonly Diagnostics

**User Story:** As a Mist developer, I want clear error messages when I violate readonly constraints, so that I can understand and fix the issue.

#### Acceptance Criteria

1. WHEN a readonly field is assigned outside a constructor, THE Type_Checker SHALL report a diagnostic with the field name and the location of the assignment.
2. WHEN a non-readonly method is called within a readonly context, THE Readonly_Analyzer SHALL report a diagnostic identifying the method and explaining it is not readonly.
3. WHEN a non-readonly property getter is called within a readonly context, THE Readonly_Analyzer SHALL report a diagnostic identifying the property getter and explaining it is not readonly.
4. WHEN any property setter is called within a readonly context (outside of constructor privilege), THE Readonly_Analyzer SHALL report a diagnostic identifying the property.
5. WHEN a non-readonly constructor is used in a `new` expression within a readonly context, THE Readonly_Analyzer SHALL report a diagnostic identifying the constructor.
6. WHEN a readonly block in a constructor attempts field assignment, THE Readonly_Analyzer SHALL report a diagnostic explaining that constructor privilege is suspended within readonly blocks.
7. WHEN an interface implementation violates a readonly contract, THE Type_Checker SHALL report a diagnostic identifying the implementing method and the interface requirement.
8. WHEN code attempts to assign to any field within a readonly context (outside of constructor privilege), THE Readonly_Analyzer SHALL report a diagnostic identifying the field and the readonly context.

### Requirement 17: C# Emission Strategy

**User Story:** As a language developer, I want readonly semantics to be enforced at compile time with no runtime overhead in the emitted C#, so that the feature is zero-cost.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL emit readonly fields using the C# `readonly` modifier.
2. THE CSharp_Emitter SHALL emit readonly properties with init-only setters (`init`) where the property has a setter that is only callable from constructors.
3. THE CSharp_Emitter SHALL NOT emit any runtime checks for readonly context violations (all enforcement is compile-time).
4. WHEN a readonly type is emitted, THE CSharp_Emitter SHALL apply `readonly` to the struct declaration if the type is a struct.
5. WHEN a readonly method is emitted on a struct, THE CSharp_Emitter SHALL apply the C# `readonly` modifier to the method.

### Requirement 18: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `readonly` to be syntax-highlighted correctly, so that I get visual cues for readonly declarations and blocks.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `readonly` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL recognize `mutable` as a storage modifier keyword (scope `storage.modifier.mist`).
3. THE TextMate grammar SHALL highlight `readonly` when it appears before type declarations, member declarations, parameter types, and as a block statement keyword.
4. THE TextMate grammar SHALL highlight the `readonly { ... }` block construct with appropriate scoping for the keyword and braces.
5. THE TextMate grammar SHALL highlight `mutable` when it appears in generic constraint positions (`where T : mutable`).

### Requirement 19: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating readonly contexts, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ReadonlyContexts/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: readonly fields, readonly properties with auto-getters, readonly properties with custom getter and setter bodies, readonly methods, readonly types (class and struct), readonly constructors, readonly blocks within methods, readonly blocks within constructors (demonstrating privilege removal), readonly inference on literals and field access, readonly inference on methods, readonly parameters (passing readonly types to non-readonly methods), generic constraints (`where T : readonly` and `where T : mutable`), allocation within readonly contexts using readonly constructors, readonly interaction with generics, and readonly interface contract enforcement.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `ReadonlyContexts.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 20: Round-Trip Parsing

**User Story:** As a language developer, I want readonly declarations and blocks to survive a parse-then-print cycle, so that tooling can reliably reconstruct source from the syntax tree.

#### Acceptance Criteria

1. FOR ALL valid readonly member declarations, parsing the source then printing the syntax tree back to source then parsing again SHALL produce an equivalent syntax tree (round-trip property).
2. FOR ALL valid readonly block statements, parsing the source then printing the syntax tree back to source then parsing again SHALL produce an equivalent syntax tree (round-trip property).
