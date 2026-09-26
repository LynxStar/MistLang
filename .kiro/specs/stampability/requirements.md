# Stampability Requirements

## Introduction

Stampability is a property of a type: its valid default can be replicated into contiguous storage by value copy without creating per-element objects, invoking per-element constructors, or sharing heap identity. This specification extends the classification used by bare array creation and `stackalloc` with inference for constructed types, a declaration guarantee, and a generic constraint.

This specification supersedes the blanket prohibition on type-parameter fields in [definite assignment](../definite-assignment/requirements.md), Requirement 3. It extends [generic constraints](../generic-constraints/requirements.md) and supplies the shared classification for [stack-only types](../stack-only-types/requirements.md), Requirement 5. The implementation belongs to the isolated v2 compiler. The shared grammar may recognize the syntax without adding the feature to classic.

## Scope

- Infer stampability when a declaration does not specify it.
- Recognize `stampable` as a struct declaration modifier and a generic constraint.
- Substitute actual type arguments before inspecting constructed instance storage.
- Use one proof for declaration validation, constraint satisfaction, bare array creation, and stack allocation.
- Preserve existing type interning, generic closing records, allocation-size evaluation, and allocation diagnostics.

## Non-Goals

- Changing defaults, constructor inference, synthetic default injection, or the `default` constraint.
- Treating all value types, CLR `unmanaged` types, or `readonly` types as stampable.
- Admitting reference types, ref structs, refinements, newtypes, or enums that the existing classifier excludes.
- Changing unrelated generic constraint semantics, allocation grammar, or stack lifetimes. Indirect bounds such as `where U : T` do not gain stampability entailment from this feature; open allocation requires a directly declared `stampable` bound.
- Establishing complete storage-layout proof for arbitrary imported CLR structs. This implementation covers source-defined structs, intrinsic primitives, and supported tuples; imported layouts without that proof remain conservatively non-stampable.
- Reading, porting, or extending the classic semantic implementation to implement v2.

## Glossary

- **Stampable type:** A type proven to satisfy the existing replication property and the classification rules below.
- **Declaration guarantee:** An explicit `stampable` modifier requiring the compiler to prove the property's validity for every permitted instantiation of a struct declaration.
- **Closed construction:** A generic type whose relevant instance storage is resolved by substituting actual type arguments, including enclosing generic arguments.
- **Open proof:** A classification relying on declared `stampable` constraints for unresolved type parameters.
- **Instance storage:** Non-static fields, inherited fields, automatic-property backing storage, and backing storage introduced by explicit `field` use in custom accessors. A computed property without backing storage is not an instance field.

## Requirement 1: Syntax and Representation

1. THE grammar SHALL recognize `stampable` as a reserved keyword in a struct declaration's modifier list and in a generic constraint list.
2. THE parser and pretty printer SHALL preserve `public stampable struct Box<T> where T : stampable` through a parse/print/parse round trip.
3. THE v2 model SHALL retain the explicit declaration modifier independently from inferred stampability.
4. THE v2 model SHALL retain `where T : stampable` as a distinct keyword constraint, including constraints on types, methods, aliases, and supported generic slots.
5. THE compiler SHALL reject the declaration modifier on reference types and ref structs, and SHALL report invalid placements with a Mist diagnostic rather than a downstream C# error.

## Requirement 2: Inferred Classification

1. THE compiler SHALL infer stampability for an unmarked concrete or constructed struct when every part of its instance storage is recursively stampable.
2. WHEN inspecting a constructed type, THE classifier SHALL substitute that receiver's actual type arguments into its instance field types and base receiver before recursing.
3. THE substitution SHALL include enclosing generic arguments and SHALL preserve aliases as transparent names for the same underlying proof.
4. THE classifier SHALL inspect inherited instance fields, automatic-property backing storage, and backing storage introduced by explicit `field` use in custom accessors, and SHALL ignore static fields and computed properties without backing storage.
5. THE classifier SHALL accept intrinsic primitive types and supported tuples whose components are all stampable, subject to the existing category gates.
6. THE classifier SHALL reject reference-bearing storage, reference types, ref structs, and the existing excluded refinement, newtype, enum, and other non-stampable categories.
7. THE classifier SHALL treat mutability and readonly status independently from stampability. A mutable struct containing only stampable instance storage may qualify.
8. A generic parameter that is not stored SHALL NOT require a `stampable` constraint merely because it appears in the type parameter list.
9. An unmarked `Box<T>` with a stored field of type `T` SHALL be a valid declaration. `Box<int>` SHALL be stampable; `Box<string>` SHALL be non-stampable and SHALL remain usable through ordinary explicit initialization.
10. THE classifier SHALL terminate on recursive and expanding generic storage graphs, SHALL refuse actual recursive value storage conservatively, and SHALL accept finite nesting such as `Box<Box<int>>` when its storage is stampable.
11. THE classifier SHALL NOT confuse differently constructed instances of the same generic definition or mutate type identities while proving stampability.
12. THE classifier SHALL NOT infer stampability from an incomplete imported instance layout. Arbitrary imported CLR structs remain outside this implementation's structural proof until ambient projection can establish complete storage information.

## Requirement 3: Open Generic Proof

1. THE classifier SHALL treat an unresolved type parameter as stampable when its constraints provide `stampable` proof.
2. THE classifier SHALL refuse an unresolved stored parameter when only `struct`, `readonly`, `default`, constructor, or unconstrained information is available.
3. THE classifier SHALL accept directly stampable-constrained parameters forwarded into stampable-constrained generic types or methods. This feature SHALL NOT add indirect stampability entailment through type-parameter bounds such as `where U : T`.
4. An unmarked `Box<T>` with a stored `T` SHALL be provably stampable in a context where `T : stampable`.
5. THE absence of the declaration modifier SHALL NOT prevent either a closed or an open proof of stampability.

## Requirement 4: Declaration Guarantee

1. WHEN a struct is marked `stampable`, THE compiler SHALL validate all its instance storage at declaration, using only the constraints supplied by the declaration and its enclosing generic context.
2. THE declaration guarantee SHALL be checked even when the type is never allocated or instantiated.
3. A marked struct containing an unconstrained stored `T` SHALL receive a diagnostic identifying the offending storage and the missing stampability proof.
4. A marked struct containing a stored `T` constrained by `where T : stampable` SHALL pass that proof when all its other instance storage is stampable.
5. Adding reference-bearing storage, inherited non-stampable storage, or non-stampable property backing storage to a marked declaration SHALL produce a diagnostic, including backing storage introduced by explicit `field` use in custom accessors.
6. THE modifier SHALL demand the property rather than override the classifier or coerce fields into stampable types.
7. A marked struct with an unused unconstrained type parameter SHALL pass when its instance storage is otherwise stampable.

## Requirement 5: Constraint Satisfaction

1. EVERY recorded generic closing SHALL check `stampable` constraints using the shared classifier, including named type references, method calls, aliases, allocation element types, and generic forwarding.
2. THE compiler SHALL accept inferred stampable arguments regardless of whether their declarations carry the modifier.
3. THE compiler SHALL reject `string`, reference-bearing structs, ref structs, and other excluded arguments for `where T : stampable` with a relevant Mist diagnostic naming the argument and required constraint.
4. THE compiler SHALL reject forwarding an unconstrained or merely `struct`-constrained parameter into a stampable-constrained parameter.
5. THE compiler SHALL accept forwarding a parameter with a directly declared `stampable` bound without requiring it to be closed.
6. `class` combined with `stampable` SHALL be diagnosed as incompatible. `struct` combined with `stampable` SHALL be accepted without weakening the stronger property.
7. THE implementation SHALL preserve satisfaction checking for all existing constraint kinds and SHALL NOT derive a new `default` or constructor capability from `stampable`.

## Requirement 6: Allocation and Backend

1. Bare array creation and `stackalloc` SHALL use the same classifier for concrete, closed, and open element types.
2. `new Box<int>[2]`, an alias equivalent, and `stackalloc Box<int>[2]` SHALL compile and run when the substituted instance storage is stampable.
3. A generic allocation of `T` or a struct containing `T` SHALL compile when `where T : stampable` supplies the open proof.
4. Existing stampability and ref-struct allocation refusals SHALL remain active for invalid elements; an explicit modifier SHALL NOT bypass them.
5. Allocation size expressions SHALL retain their existing evaluation count, dependent-length inference, and runtime behavior.
6. THE C# backend SHALL express the representation guarantee needed by generic stack allocation using `unmanaged` constraints where appropriate, while Mist analysis continues to enforce its stronger source-level category rules.
7. THE backend SHALL NOT emit a `stampable` C# declaration modifier or per-element constructor calls to implement this property.
8. Unmarked closed constructions SHALL preserve type interning and ordinary generic constraint-closing records.

## Requirement 7: Evidence and Documentation

1. Model coverage SHALL exercise substituted own, enclosing, inherited, aliased, tuple, automatic-property storage, and explicit `field` accessor storage; finite nesting; recursion termination; ignored static storage; and existing excluded categories.
2. Analyzer coverage SHALL exercise unused invalid marked declarations, valid marked open declarations, invalid constraint arguments, invalid forwarding, and the existing allocation gates.
3. End-to-end coverage in `samples/StampableTypes` SHALL demonstrate inferred closed arrays and spans, declaration guarantees, open constrained allocation, aliases, and unchanged computed-size evaluation. The sample SHALL be banked as v2-only because classic does not implement the new modifier and constraint.
4. The language book SHALL explain inference, declaration guarantees, and constraints as three uses of the same property, with generated C# tabs for compiling positive snippets and prose or diagnostics for rejected declarations.
5. Book syntax-highlighting definitions SHALL recognize the keyword consistently.
6. The implementation SHALL complete the required build, test-ledger, formatter, sample, snippet-generation, and register checks with zero compiler warnings.
