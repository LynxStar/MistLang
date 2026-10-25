# Requirements Document

## Introduction

This document specifies the requirements for introducing the `unknown` type as a first-class concept in the Mist programming language. The `unknown` type is a user-facing placeholder for a value whose concrete type is not yet known at the current scope. It is the top type in the sense that any value can be assigned to an `unknown` location, but it is intentionally restricted so that `unknown` cannot be used as the declared type of writable storage (fields, properties, newly-constructed objects, newly-constructed arrays). Pattern matching is the primary mechanism by which an `unknown`-typed binding is narrowed to a concrete type.

The feature introduces the reserved keyword `unknown`, lowered to C# `object` in the backend. The existing internal error sentinel class (`Source/Mist/Semantics/Model/Types/UnknownType.cs`, display name `"?"`) is renamed to `ErrorType` so the user-facing `UnknownType` does not collide with it.

The feature also introduces per-member reachability analysis for generic types: when a generic type is closed with `unknown` at one or more type argument positions, members whose bodies can transitively write a value of that type argument into storage (fields, array elements, properties with fields backing) are hidden from external callers. This allows covariant widening such as `List<int>` → `List<unknown>` while preserving runtime soundness by preventing operations like `Add(item)` that would store an `unknown` into a concrete-typed slot.

Declaration-site variance annotations (`in T`, `out T`), generic constraints, type unions, a bottom/`never` type, and reflection APIs over `unknown` values are out of scope.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Reachability_Analyzer**: A new semantic-model analysis component that classifies each member of a generic type with respect to whether its body can, directly or transitively, perform a write to storage whose declared type is one of the enclosing type's type parameters.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **UnknownType**: The user-facing first-class Mist type spelled `unknown`. A top type for assignment purposes, with restrictions defined below.
- **ErrorType**: The renamed internal error sentinel previously called `UnknownType` in the compiler source. Represents an unresolved or erroneous type during analysis and must not propagate to the backend.
- **Storage_Location**: A named slot that persists a value: a field, an array element, or a property whose implementation has a backing field. Local variables and method parameters are *not* Storage_Locations for the purposes of this specification because they are write-once initialized from a source expression whose type was already validated, or re-bound from another already-typed expression, and are not reachable by external mutation through a reference of declared type `unknown`.
- **T_Typed_Storage_Location**: A Storage_Location whose declared type, inside the body of a generic type `Foo<T>`, is the type parameter `T` itself, an array whose element type is `T`, or any type whose declaration transitively contains `T` at a Storage_Location position.
- **Write_To_T**: An assignment, compound assignment, or element-index assignment whose target is a T_Typed_Storage_Location. Passing a value by `out` or `ref` to a method that writes into a T_Typed_Storage_Location is also a Write_To_T. (The `out`/`ref` cases are noted for completeness; Mist does not have `out`/`ref` parameters in v1, so they cannot arise in practice.)
- **Write_Reachable_Member**: A member of a generic type `Foo<T>` whose body can, directly or via transitively-called members of the same type or its base types, perform a Write_To_T for at least one of the enclosing type's type parameters T. Constructors are always Write_Reachable_Members with respect to every type parameter that appears as a parameter type or in the type of any field that is initialized in their body.
- **Closed_Generic_With_Unknown**: A Constructed_Type in which at least one type argument is `unknown` (directly or transitively). For example, `List<unknown>`, `Map<string, unknown>`, `Optional<List<unknown>>`.
- **Covariant_Unknown_Widening**: The implicit conversion from a Constructed_Type `Foo<A_1, ..., A_n>` to another Constructed_Type `Foo<B_1, ..., B_n>` where for each position i, either `A_i` equals `B_i` or `B_i` is `unknown`. This is the sole implicit generic conversion introduced by this feature.
- **Pattern_Narrowing**: The introduction of a pattern variable with a concrete type via an `is` type pattern (e.g., `x is Foo<int> fi`). After narrowing, the narrowed binding has the concrete type and is not subject to the restrictions that apply to `unknown` or Closed_Generic_With_Unknown receivers.
- **ClassType / StructType / InterfaceType / TraitType**: The existing semantic model representations of user-defined class, struct, interface, and trait declarations.

## Requirements

### Requirement 1: `unknown` Keyword and Grammar

**User Story:** As a Mist developer, I want the compiler to recognize `unknown` as a reserved keyword denoting the user-facing UnknownType, so that I can use it in type references.

#### Acceptance Criteria

1. THE Grammar SHALL define an `UNKNOWN` keyword token with the value `"unknown"`, declared alongside the other primitive type keywords in `keywords.storm`.
2. THE Grammar SHALL extend the `TypeReference` rule so that `UNKNOWN` is accepted as a primitive-like type reference, producing an AST node that denotes the user-facing UnknownType.
3. WHEN the parser encounters the token `unknown` in a type reference position, THE Parser SHALL produce an AST node that is distinguishable from user-defined named type references and from the other primitive type keywords.
4. THE Grammar SHALL reject `unknown` as an identifier in all non-type-reference positions (e.g., as a variable name, class name, member name, namespace segment, or type parameter name).

### Requirement 2: Rename the Existing `UnknownType` Sentinel to `ErrorType`

**User Story:** As a Mist compiler contributor, I want the internal error sentinel to have a name that does not collide with the new user-facing UnknownType, so that the two concepts remain distinct in the codebase.

#### Acceptance Criteria

1. THE Compiler source SHALL rename the class currently at `Source/Mist/Semantics/Model/Types/UnknownType.cs` to `ErrorType` (file: `ErrorType.cs`).
2. THE Compiler source SHALL update every reference to the previous `UnknownType` sentinel, including its `UnknownType.Instance` static accessor, to the renamed `ErrorType` and `ErrorType.Instance` accessor.
3. THE renamed ErrorType SHALL retain its existing semantics: display name `"?"`, no `TypeKind`, used only during analysis, and SHALL NOT propagate to the backend.
4. AFTER the rename, THE Compiler SHALL continue to build successfully with zero warnings.
5. AFTER the rename, THE Compiler's existing test suite SHALL continue to pass with no behavioral changes.

### Requirement 3: UnknownType Semantic Model Representation

**User Story:** As a Mist developer, I want the semantic model to represent the user-facing UnknownType as a first-class type, so that analyzers and the emitter can reason about it uniformly with other types.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL introduce a semantic model type representation for UnknownType with display name `"unknown"` and a distinct `TypeKind` value.
2. THE UnknownType representation SHALL be a singleton accessible through a static `Instance` property, consistent with how other built-in primitive types are exposed.
3. THE UnknownType representation SHALL report itself as the user-facing `unknown` type for diagnostic and pretty-printing purposes.
4. THE UnknownType representation SHALL have no declared members: no fields, no methods, no properties, no constructors, no operators.
5. THE UnknownType representation SHALL NOT participate in inheritance relationships as either a base or a derived type.
6. WHEN the Semantic_Model_Builder encounters an `unknown` type reference in an AST, THE Semantic_Model_Builder SHALL resolve it to the UnknownType singleton.

### Requirement 4: Assignability Into `unknown`

**User Story:** As a Mist developer, I want any value to be assignable to an `unknown`-typed local or parameter, so that I can hold a placeholder whose concrete type will be determined later.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat every non-`void` type T as implicitly convertible to UnknownType.
2. THE Type_Checker SHALL reject conversion from `void` to UnknownType with a diagnostic error.
3. WHEN a value of any concrete type is assigned to a local variable, parameter, or return slot whose declared type is UnknownType, THE Type_Checker SHALL accept the assignment without requiring an explicit cast.
4. WHEN a value of UnknownType is assigned to a location whose declared type is not UnknownType and is not `object` (if `object` becomes available in the future), THE Type_Checker SHALL reject the assignment with a diagnostic error unless the conversion is mediated by pattern matching.
5. WHEN a value of UnknownType is assigned to another location whose declared type is UnknownType, THE Type_Checker SHALL accept the assignment.

### Requirement 5: UnknownType Storage Prohibition

**User Story:** As a Mist developer, I want the compiler to forbid declaring fields and other long-lived storage of type `unknown`, so that the language provides strong compile-time guarantees that no untyped value is persisted in a way that could be mutated to a type-incompatible value later.

#### Acceptance Criteria

1. WHEN a class, struct, or trait declares a field whose declared type is UnknownType, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that `unknown` cannot be used as the type of a field.
2. WHEN a class, struct, or trait declares a property whose declared type is UnknownType, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that `unknown` cannot be used as the type of a property.
3. WHEN a class, struct, or trait declares an indexer whose indexed element type is UnknownType, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that `unknown` cannot be used as the element type of an indexer.
4. WHEN a `new` expression attempts to construct a value whose type is UnknownType (e.g., `new unknown(...)`), THE Type_Checker SHALL emit a diagnostic error indicating that `unknown` cannot be instantiated.
5. WHEN a `new` array expression attempts to construct an array whose element type is UnknownType (e.g., `new unknown[n]`), THE Type_Checker SHALL emit a diagnostic error indicating that `unknown` cannot be used as an array element type in a `new` array expression.
6. WHEN a `default` expression or `default`-inferred initializer would produce a value of type UnknownType (e.g., `unknown x = default;` or `default(unknown)` where supported), THE Type_Checker SHALL emit a diagnostic error indicating that `unknown` has no default value.
7. WHEN a `new` expression attempts to construct a generic type with UnknownType as one or more of its type arguments (e.g., `new List<unknown>()`, `new Map<unknown, int>()`), THE Type_Checker SHALL emit a diagnostic error indicating that a generic type cannot be constructed with `unknown` as a type argument.
8. WHERE a location is a local variable, a method parameter, a constructor parameter, or a method return type, THE Type_Checker SHALL permit UnknownType as its declared type.

### Requirement 6: UnknownType Inheritance Prohibition

**User Story:** As a Mist developer, I want the compiler to forbid inheriting from or implementing `unknown`, so that UnknownType remains a true top type and not a user-extensible base.

#### Acceptance Criteria

1. WHEN a class declaration names UnknownType in its base clause, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that classes cannot inherit from `unknown`.
2. WHEN a struct declaration names UnknownType in its interface list, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that structs cannot implement `unknown`.
3. WHEN an interface declaration names UnknownType in its base interface list, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that interfaces cannot inherit from `unknown`.
4. WHEN a trait declaration names UnknownType as a base trait or in any trait-satisfaction-bearing position, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that traits cannot reference `unknown` as a base.

### Requirement 7: UnknownType as Type Argument

**User Story:** As a Mist developer, I want to use `unknown` as a type argument to a generic type, so that I can hold a placeholder for a container whose element type is not yet known.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL allow UnknownType to appear as a type argument in any `TypeArgumentList` (e.g., `Optional<unknown>`, `List<unknown>`, `Map<string, unknown>`).
2. THE Semantic_Model_Builder SHALL support nested occurrences of UnknownType in type arguments at arbitrary depth (e.g., `Optional<List<unknown>>`, `List<Optional<unknown>>`, `Map<unknown, List<unknown>>`).
3. WHEN a type reference uses UnknownType as a type argument at one or more positions, THE Semantic_Model_Builder SHALL construct a Constructed_Type in the usual manner, producing a Closed_Generic_With_Unknown.
4. THE Semantic_Model_Builder SHALL permit a Closed_Generic_With_Unknown as the declared type of a local variable, method parameter, and method return type.
5. THE Semantic_Model_Builder SHALL reject a Closed_Generic_With_Unknown as the declared type of a field, property, or indexer element when any occurrence of UnknownType appears at a position that would make the enclosing storage location transitively of type UnknownType after substitution. For example, `List<unknown> items;` declared as a field is a diagnostic error; `Map<string, unknown> m;` as a field is a diagnostic error because the value slot is transitively `unknown`.
6. WHEN a Closed_Generic_With_Unknown appears as a type argument to another generic type used as a storage type (e.g., a field of type `List<Optional<unknown>>`), THE Semantic_Model_Builder SHALL reject the storage declaration if the recursive application of the storage rule from Requirement 5 would produce storage of type UnknownType.

### Requirement 8: Covariant Unknown Widening

**User Story:** As a Mist developer, I want to assign a `List<int>` to a variable of type `List<unknown>`, so that I can hold a reference to an existing typed container in a context where the concrete element type is not yet relevant.

Covariant_Unknown_Widening is restricted to class (reference-type) generic definitions. Widening a struct generic (e.g., `Optional<int>` to `Optional<unknown>` when `Optional` is a struct) is rejected at compile time because the C# emission strategy — a reference cast through `object` — is unsound for value-type layouts.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat Covariant_Unknown_Widening from `Foo<A_1, ..., A_n>` to `Foo<B_1, ..., B_n>` as an implicit conversion, where for each position i, either `A_i` equals `B_i` or `B_i` is UnknownType, AND the generic definition `Foo` has `TypeKind.Class`.
2. THE Type_Checker SHALL apply Covariant_Unknown_Widening transitively through the type hierarchy: a type assignable to `Foo<A_1, ..., A_n>` is also assignable to `Foo<B_1, ..., B_n>` under the same position-wise rule and the same class-generic restriction.
3. THE Type_Checker SHALL apply Covariant_Unknown_Widening to nested generic positions: `Optional<Foo<A_1>>` is implicitly convertible to `Optional<Foo<B_1>>` when `B_1` is UnknownType or equals `A_1` and `Foo` has `TypeKind.Class`. The nested rule is independent of whether the outer generic is a class or struct, because nested widening only requires reference-covariance at the nested position.
4. THE Type_Checker SHALL NOT extend Covariant_Unknown_Widening to any form of structural subtyping other than replacing concrete type arguments with UnknownType. In particular, `Foo<Derived>` is not implicitly convertible to `Foo<Base>` under this rule unless `Base` is UnknownType.
5. THE Type_Checker SHALL NOT introduce an implicit conversion in the reverse direction: a value of type `List<unknown>` is not implicitly convertible to `List<int>`.
6. WHEN a Covariant_Unknown_Widening conversion is attempted against a generic definition `Foo` whose `TypeKind` is `Struct`, THE Type_Checker SHALL emit a diagnostic error indicating that covariant `unknown` widening is only available for class (reference-type) generics, so that value-type generics are not silently widened into an unsound C# emission.

### Requirement 9: Reachability Analysis of Writes to Type Parameters

**User Story:** As a Mist compiler contributor, I want the compiler to classify each member of a generic type by whether it can write to storage of any of the enclosing type's type parameters, so that member access on a Closed_Generic_With_Unknown can be soundly filtered.

#### Acceptance Criteria

1. THE Reachability_Analyzer SHALL classify every member (field, property, indexer, method, constructor, operator) of every Generic_Type_Declaration with respect to each of the enclosing type's type parameters T, producing one of: `WritesT`, `DoesNotWriteT`.
2. THE Reachability_Analyzer SHALL classify a field whose declared type is T, is `T[]`, or transitively contains T at a Storage_Location position, as `WritesT` with respect to write access (assignment to the field) and `DoesNotWriteT` with respect to read access.
3. THE Reachability_Analyzer SHALL classify a property as follows: read access is classified `DoesNotWriteT` unless the property's getter body performs a Write_To_T; write access (assignment to the property) is classified `WritesT` if the property is auto-implemented with a T-typed backing, is backed by a field whose type is T (directly or transitively), or the setter body performs a Write_To_T.
4. THE Reachability_Analyzer SHALL classify an indexer analogously to a property with respect to read access and write access.
5. THE Reachability_Analyzer SHALL classify a method as `WritesT` if the method body, considering all statements and expressions transitively reachable from the method entry, contains at least one Write_To_T.
6. THE Reachability_Analyzer SHALL perform interprocedural analysis: a call expression is treated as performing a Write_To_T if the callee is known and its classification is `WritesT`. For calls to members of the same generic type or its base types, the analyzer SHALL use the classifications already computed for those members.
7. THE Reachability_Analyzer SHALL classify a constructor as `WritesT` with respect to a type parameter T when any constructor parameter is of type T (directly or transitively), when the constructor body performs a Write_To_T, or when the constructor invokes another `WritesT` constructor via `this(...)` or `base(...)`.
8. THE Reachability_Analyzer SHALL handle virtual dispatch, trait satisfaction, and generic method invocation conservatively: a call whose callee cannot be fully determined statically SHALL be treated as `WritesT` with respect to T if the argument being passed has a type that is T, `T[]`, or transitively contains T at a Storage_Location position.
9. THE Reachability_Analyzer SHALL handle calls to methods defined in external C# types as `WritesT` if any argument being passed has a type that is T, `T[]`, or transitively contains T at a Storage_Location position; otherwise `DoesNotWriteT`.
10. THE Reachability_Analyzer SHALL resolve cyclic classifications (mutually recursive methods) via fixed-point iteration, treating an as-yet-unknown classification as `DoesNotWriteT` at the start and updating to `WritesT` upon discovery of a writing path.
11. THE Reachability_Analyzer SHALL compute a `read` classification and a `write` classification for each property and indexer, so that the getter and setter can be filtered independently when the type is closed with UnknownType.

### Requirement 10: Member Filtering on Closed_Generic_With_Unknown

**User Story:** As a Mist developer, I want the compiler to hide operations that could violate runtime type safety on a `List<unknown>`, so that I cannot accidentally store a value into a list that has a specific concrete element type at runtime.

#### Acceptance Criteria

1. WHEN a member access expression has a receiver whose static type is a Closed_Generic_With_Unknown, THE Type_Checker SHALL treat any member classified `WritesT` with respect to a type parameter whose corresponding type argument is UnknownType as not-a-member at that receiver, emitting the standard "member not found" diagnostic.
2. WHEN a member access reads a property or indexer whose read classification is `DoesNotWriteT` with respect to each type parameter closed with UnknownType, THE Type_Checker SHALL allow the access and substitute UnknownType for those type parameters in the result type.
3. WHEN an assignment expression targets a property or indexer on a receiver of a Closed_Generic_With_Unknown, THE Type_Checker SHALL treat the write access as not-available if the property's or indexer's write classification is `WritesT` with respect to any type parameter whose corresponding type argument is UnknownType, emitting the same "member not found" diagnostic as for a nonexistent member.
4. WHEN a method invocation has a receiver whose static type is a Closed_Generic_With_Unknown and the method's classification is `WritesT` with respect to a type parameter whose corresponding type argument is UnknownType, THE Type_Checker SHALL emit the same "member not found" diagnostic.
5. WHEN a constructor is invoked through a `new` expression on a Closed_Generic_With_Unknown, THE Type_Checker SHALL emit the diagnostic from Requirement 5 (item 7) rather than a member-not-found diagnostic, because constructing a Closed_Generic_With_Unknown is forbidden regardless of the constructor's classification.
6. THE Type_Checker SHALL NOT apply member filtering on receivers whose static type is a fully open generic type (i.e., inside the declaring body of the generic type itself), because inside the declaring body the type parameters are not yet substituted with UnknownType.
7. WHEN a member is filtered out per items 1, 3, or 4, the diagnostic message SHALL name the member, the receiver's static type, and state that the operation is not available on a type closed with `unknown` because it would write to storage of a type parameter bound to `unknown`.

### Requirement 11: Pattern Narrowing Recovers Full Access

**User Story:** As a Mist developer, I want pattern matching against a concrete type to produce a binding of that concrete type with full member access, so that I can use pattern matching to regain typed access to a value that was held as `unknown` or as a Closed_Generic_With_Unknown.

Pattern narrowing to UnknownType or to another Closed_Generic_With_Unknown is rejected at compile time because such a pattern is tautologically true (UnknownType is the top type) and either provides no new information or loses information.

#### Acceptance Criteria

1. WHEN an `is` expression uses a type pattern `T identifier` on a receiver of type UnknownType or of a Closed_Generic_With_Unknown, THE Semantic_Model_Builder SHALL introduce `identifier` into the appropriate scope with static type T, consistent with the existing pattern matching specification.
2. WHEN the type pattern target T is a Constructed_Type (e.g., `foo is Optional<int> oi`), THE Type_Checker SHALL permit full member access on `oi` based on T's members after normal type argument substitution, with no member filtering applied.
3. WHEN the type pattern target T is UnknownType (e.g., `x is unknown u`), THE Type_Checker SHALL emit a diagnostic error indicating that narrowing to `unknown` is not meaningful: the pattern is tautologically true because UnknownType accepts every value, and the introduced binding has no more specific type than the scrutinee.
4. WHEN the type pattern target T is a Closed_Generic_With_Unknown (e.g., `items is List<unknown> lu` where `items` is `List<int>` or `List<unknown>`), THE Type_Checker SHALL emit a diagnostic error indicating that narrowing to a Closed_Generic_With_Unknown is not meaningful: the Covariant_Unknown_Widening rule makes the pattern tautologically true, and the introduced binding loses information (members that write to type-parameter storage are filtered per Requirement 10) without gaining any.
5. THE Emitter SHALL lower such `is`-pattern expressions to C# `is` pattern expressions in the usual manner, relying on the C# runtime's preserved generic type information to perform the runtime check on the full constructed type.

### Requirement 12: Trait Satisfaction and UnknownType

**User Story:** As a Mist developer, I want traits to be able to specify members that return or accept `unknown`, so that I can declare an abstraction over values whose concrete type I do not want to commit to, while keeping structural type safety.

#### Acceptance Criteria

1. WHEN a trait declares a member (method, property, indexer) whose return type is UnknownType, THE Type_Checker SHALL treat any candidate member on a concrete type whose return type is any non-`void` type T as satisfying the trait member for the purposes of return-position matching.
2. WHEN a trait declares a member whose return type is a Closed_Generic_With_Unknown, THE Type_Checker SHALL treat any candidate member whose return type is a Constructed_Type of the same generic with type arguments that differ only by replacing concrete arguments with those of the trait member under the UnknownType-is-top rule, as satisfying the trait member's return position.
3. WHEN a trait declares a member with one or more parameters, THE Type_Checker SHALL continue to require exact parameter-type matching for those parameters. UnknownType in a trait parameter position SHALL require exactly UnknownType in the implementer's parameter position, and UnknownType in an implementer's parameter position SHALL require exactly UnknownType in the trait's parameter position.
4. WHEN a trait declares a field whose declared type is UnknownType, THE Semantic_Model_Builder SHALL apply Requirement 5 and emit a diagnostic error, because UnknownType is forbidden as a field type for traits by the same rule that applies to classes and structs.
5. WHEN a trait member is invoked on a receiver whose static type is the trait, THE Type_Checker SHALL apply the Reachability_Analyzer classifications defined in Requirement 9 to the trait as if the trait were a generic type whose type parameters are any UnknownType occurrences within its signature, filtering members per Requirement 10.

### Requirement 13: UnknownType Member Access Restrictions on Bare unknown

**User Story:** As a Mist developer, I want the compiler to reject any member access on a bare `unknown`-typed value, so that I am forced to pattern-match before operating on the underlying data.

#### Acceptance Criteria

1. WHEN a member access expression has a receiver whose static type is exactly UnknownType (not a Constructed_Type with unknown arguments), THE Type_Checker SHALL emit a diagnostic error indicating that the member does not exist on `unknown`, regardless of the member name.
2. WHEN a binary or unary operator is applied to an operand whose static type is UnknownType, THE Type_Checker SHALL emit a diagnostic error indicating that the operator is not defined for `unknown`. This applies to `==`, `!=`, `<`, `>`, `<=`, `>=`, `+`, `-`, `*`, `/`, `%`, `&&`, `||`, `!`, and any other operators defined by the language or user-defined operators.
3. WHEN an `is` expression is applied to a receiver of type UnknownType, THE Type_Checker SHALL permit it per Requirement 11 because `is` is the designated mechanism for narrowing.
4. WHEN a cast expression `(T) expr` is applied to an operand of type UnknownType, THE Type_Checker SHALL resolve the cast against the cast-operator rules defined by the cast-operators feature, searching for an implicit or explicit cast operator whose source type is UnknownType and whose target type is T. WHEN no such operator is defined, THE Type_Checker SHALL emit a diagnostic error indicating that no cast operator is defined from `unknown` to T, consistent with Mist's general rule that explicit casts require a statically-resolvable cast operator.
5. WHEN a cast expression `(unknown) expr` is applied to an operand of any type S, THE Type_Checker SHALL accept the cast because assignability into `unknown` is implicit for every non-`void` type (Requirement 4). The Emitter SHALL lower it as a C# cast to `object` (no operator dispatch required).
6. Mist's compile-time cast-operator requirement eliminates the class of runtime cast failures that C# permits when casting `object` to an arbitrary type. Any `(T) expr` where `expr` is typed `unknown` compiles if and only if an operator `(T) from (unknown)` has been declared somewhere in the program.

### Requirement 14: Pretty-Printer Support for UnknownType

**User Story:** As a Mist developer, I want the pretty-printer to correctly format the `unknown` type reference in all positions, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a bare UnknownType reference as `unknown`.
2. THE Pretty_Printer SHALL format UnknownType appearing as a type argument as `unknown` inside the surrounding angle brackets (e.g., `Optional<unknown>`, `Map<string, unknown>`, `List<Optional<unknown>>`).
3. THE Pretty_Printer SHALL format UnknownType appearing as a local variable type, parameter type, or return type using the bare spelling `unknown`.
4. FOR ALL valid Mist ASTs containing UnknownType references in any permitted position, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 15: C# Code Emission for UnknownType

**User Story:** As a Mist developer, I want the transpiled C# code to represent `unknown` faithfully, so that the compiled output behaves as specified at runtime.

#### Acceptance Criteria

1. THE Emitter SHALL emit every Mist UnknownType reference as the C# type `object`.
2. THE Emitter SHALL emit Covariant_Unknown_Widening conversions using the C# cast `(Foo<object>) value` when a cast is required, and SHALL rely on C#'s reference-type covariance and explicit casts to satisfy the C# compiler. If a direct assignment is not accepted by C#, the Emitter SHALL insert an explicit C# cast to preserve Mist's semantics.
3. THE Emitter SHALL emit an `is` pattern match that tests for a Constructed_Type (e.g., Mist `foo is Optional<int> oi`) as a C# pattern match against the corresponding constructed C# type (e.g., `foo is Optional<int> oi`), relying on C#'s preserved generic type information.
4. WHEN a cast expression from UnknownType to a concrete type T has a resolved user-defined cast operator, THE Emitter SHALL lower it as a static method call on the declaring type (per the cast-operators feature's emission rules). When the target is UnknownType, THE Emitter SHALL lower it as a C# cast to `object` with no operator dispatch.
5. THE Emitter SHALL NOT emit any declaration of an `unknown` Mist field, property, indexer, or generic constructor with an unknown argument, because the Compiler rejects these at an earlier stage.

### Requirement 16: Diagnostic Quality

**User Story:** As a Mist developer, I want clear, specific diagnostics when I misuse `unknown`, so that I can quickly identify why an operation is rejected.

#### Acceptance Criteria

1. WHEN UnknownType is used as a field, property, or indexer element type per Requirement 5, THE Compiler SHALL emit a diagnostic that names the declaring type, the member, and states that `unknown` is not permitted as the type of writable storage.
2. WHEN a `new` expression attempts to construct a Closed_Generic_With_Unknown per Requirement 5 (item 7), THE Compiler SHALL emit a diagnostic that names the generic type and the position(s) where UnknownType appears.
3. WHEN a member is filtered per Requirement 10, THE Compiler SHALL emit a diagnostic that names the receiver's static type, names the member, and indicates that the operation would write to storage of a type parameter bound to `unknown`.
4. WHEN an operator or member access is rejected on bare UnknownType per Requirement 13, THE Compiler SHALL emit a diagnostic that suggests using an `is` pattern to narrow the type before performing the operation.
5. WHEN `unknown` is used as an identifier in a non-type-reference position per Requirement 1, THE Compiler SHALL emit a diagnostic that names `unknown` as a reserved keyword.
6. WHEN a class, struct, interface, or trait attempts to inherit from or implement UnknownType per Requirement 6, THE Compiler SHALL emit a diagnostic that names the declaring type and states that `unknown` cannot be a base or implemented type.

### Requirement 17: Syntax Highlighting for UnknownType

**User Story:** As a Mist developer using an editor, I want the `unknown` keyword to be syntax-highlighted consistently with other primitive type keywords, so that it is visually identifiable.

#### Acceptance Criteria

1. THE TextMate grammar (`mist.tmLanguage.json`) SHALL include `unknown` in the same pattern as the other primitive type keywords (`int`, `string`, `bool`, etc.).
2. WHEN a Mist source file contains the `unknown` keyword, THE editor SHALL highlight it with the scope used for other primitive type keywords.

### Requirement 18: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the UnknownType feature, so that the feature tracking document remains accurate.

#### Acceptance Criteria

1. THE Compiler team SHALL add an entry for `unknown` in the `Reference Types` section of CSHARP_FEATURES.md, marked as implemented, with a note that this is a Mist-specific feature distinct from C# `object` (and explicitly distinct from C# `dynamic`, which remains skipped).
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation count.
3. WHERE an explanatory subsection is appropriate, THE Compiler team SHALL add a short prose description of the semantics: top-type assignability, storage prohibition, reachability-based member filtering on closed generics, and pattern-matching narrowing.

### Requirement 19: Sample Application

**User Story:** As a Mist developer evaluating the language, I want a sample application that demonstrates the `unknown` type in action, so that I can see covariant widening, member filtering, and pattern-match narrowing in practice.

#### Acceptance Criteria

1. THE sample application SHALL be located in a new `Samples/UnknownType/` directory following the existing sample app convention.
2. THE sample application SHALL demonstrate declaring an `unknown`-typed local variable initialized from values of multiple concrete types.
3. THE sample application SHALL demonstrate declaring an `unknown`-typed method parameter and passing values of multiple concrete types to it.
4. THE sample application SHALL demonstrate pattern-match narrowing from `unknown` to a concrete type via an `is` type pattern, then calling a method on the narrowed binding.
5. THE sample application SHALL demonstrate Covariant_Unknown_Widening by assigning a `List<int>` to a `List<unknown>` local, then calling members that remain accessible (e.g., reading length, iterating, reading an element).
6. THE sample application SHALL demonstrate pattern-match narrowing from a Closed_Generic_With_Unknown to a concrete constructed type (e.g., `foo is List<int> li`) and performing full operations on the narrowed binding.
7. THE sample application SHALL include at least one inline comment that describes, in prose, an operation that would be rejected by the Compiler (e.g., `// foo.Add(x) would not compile here because List<unknown> hides Add`), demonstrating the filtering rule by contrast.
8. THE sample application SHALL compile and run successfully through the Mist compiler pipeline, producing correct output via `Console.WriteLine`.
9. THE sample application SHALL include a `static void Main` entry point that exercises all demonstrated features and prints observable output.

### Requirement 20: AllFeatures Sample Update

**User Story:** As a Mist project maintainer, I want the AllFeatures sample to include a minimal UnknownType example, so that the comprehensive sample remains current.

#### Acceptance Criteria

1. THE AllFeatures sample update SHALL add UnknownType examples in a new `.mist` file within the `Samples/AllFeatures/` directory, rather than modifying existing files, to facilitate easier merging.
2. THE AllFeatures sample update SHALL demonstrate at least one `unknown`-typed local, one Covariant_Unknown_Widening assignment, and one pattern-match narrowing.
3. THE AllFeatures sample update SHALL compile and run successfully as part of the existing AllFeatures sample.
