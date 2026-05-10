# Requirements Document

## Introduction

This document specifies the requirements for adding an ownership and reference system to the Mist programming language. The system introduces five tiers of reference access — unrestricted, `readonly ref`, `frozen ref`, `ref`, and `owned` — that govern how variables interact with heap-allocated objects: who can read, who can mutate, who can store, and how many simultaneous references are permitted.

The system is inspired by Rust's borrow checker but makes different tradeoffs. Notably, it allows live readonly views (`readonly ref`) that can coexist with owner mutation, introduces `frozen ref` for when true immutability is needed, and retains an unrestricted tier for code that doesn't need ownership guarantees. All enforcement is compile-time with zero runtime cost — the emitted C# is identical whether annotations exist or not.

## Glossary

- **Unrestricted**: The default tier for reference types. No ownership constraints. Multiple variables can read, mutate, and store references freely. Equivalent to standard C# reference behavior.
- **Readonly Ref**: A non-storable, readonly, live view of an object. The holder can only call readonly members. The owner may still mutate the object — the holder accepts that the view may change underneath. Cannot be assigned to fields or collections.
- **Frozen Ref**: A non-storable, readonly view that additionally blocks owner mutation while any frozen ref is outstanding. Guarantees the holder sees a stable, immutable snapshot for the duration of the hold. Cannot be assigned to fields or collections.
- **Ref (Borrowed)**: A non-storable, mutable reference. The holder has temporary exclusive mutation access. Only one `ref` can be outstanding from an owner at a time. The borrower can lend out `readonly ref`, `frozen ref`, or re-lend the `ref` itself (one at a time). Cannot be assigned to fields or collections.
- **Owned**: Sole ownership of an object. The owner has full read/write access, can lend out any ref tier, and can transfer (move) ownership to another variable or field. After a move, the source variable is inaccessible. If the type is not an owned type, moving to a field makes that field an unrestricted reference (ownership is relinquished). If the type IS an owned type, the destination field must be marked `owned` or belong to an owned type.
- **Owned Type**: A class/struct/interface/trait declared with `owned`. Instances must always be held as owned. All fields within an owned type must themselves be owned (or value types). An unrestricted reference cannot be stored inside an owned type.
- **Non-Storable**: A reference that cannot be assigned to any field, collection element, or any location that would allow it to escape its declaring scope.
- **Move**: The act of transferring ownership from one variable to another. The source becomes inaccessible after the move.
- **Lend**: Temporarily providing a ref/readonly ref/frozen ref to another scope. The lender's access is restricted until the loan is returned.

## Requirements

### Requirement 1: Ownership Tier Keywords

**User Story:** As a language developer, I want the parser and lexer to recognize ownership-related keywords, so that developers can annotate variables, parameters, and return types with ownership tiers.

#### Acceptance Criteria

1. THE Lexer SHALL recognize `owned` as a reserved keyword token.
2. THE Lexer SHALL recognize `frozen` as a reserved keyword token.
3. THE Lexer SHALL recognize `ref` as a reserved keyword token.
4. THE Parser SHALL recognize `readonly ref` as a compound ownership specifier (using the existing `readonly` keyword combined with `ref`).
5. THE Parser SHALL recognize `frozen ref` as a compound ownership specifier.
6. THE Parser SHALL recognize `ref` alone as a borrowed ownership specifier.
7. THE Parser SHALL recognize `owned` alone as an owned ownership specifier.
8. WHEN no ownership keyword is present on a reference-type declaration, THE Parser SHALL treat it as unrestricted (no specifier attached).
9. IF more than one ownership specifier appears on the same declaration, THEN THE Parser SHALL report a diagnostic error.
10. WHEN `owned`, `frozen`, or `ref` appear in identifier position, THE Parser SHALL report a syntax error indicating the keyword cannot be used as an identifier.

### Requirement 2: Ownership Specifier Positions

**User Story:** As a Mist developer, I want to place ownership specifiers on variables, parameters, and return types, so that I can express ownership intent at all relevant declaration sites.

#### Acceptance Criteria

1. WHEN an ownership specifier precedes a variable declaration type, THE Parser SHALL attach the specifier to the variable declaration node.
2. WHEN an ownership specifier precedes a parameter type in a method signature, THE Parser SHALL attach the specifier to the parameter node.
3. WHEN an ownership specifier precedes a method return type, THE Parser SHALL attach the specifier to the return type node.
4. THE Parser SHALL accept ownership specifiers combined with other modifiers (e.g., `public owned Foo`).
5. WHEN `owned` is used as a return type specifier, THE Parser SHALL accept it. No other ownership specifier is valid on return types (only `owned` is meaningful for returns).

### Requirement 3: Unrestricted Tier Semantics

**User Story:** As a Mist developer, I want the default behavior for reference types to be unrestricted, so that code without ownership annotations behaves like standard C#.

#### Acceptance Criteria

1. WHEN a reference-type variable has no ownership specifier, THE compiler SHALL treat it as unrestricted.
2. THE compiler SHALL permit multiple unrestricted variables to reference the same object simultaneously.
3. THE compiler SHALL permit mutation through unrestricted references.
4. THE compiler SHALL permit storing unrestricted references in fields and collections.
5. WHEN an unrestricted variable is passed to a parameter marked `readonly ref`, THE compiler SHALL permit the call (unrestricted can be viewed as readonly).
6. WHEN an unrestricted variable is passed to a parameter marked `ref`, THE compiler SHALL report an error (cannot guarantee exclusive mutation access for an unrestricted object).
7. WHEN an unrestricted variable is passed to a parameter marked `frozen ref`, THE compiler SHALL report an error (cannot guarantee no other mutations for an unrestricted object).
8. WHEN an unrestricted variable is passed to a parameter marked `owned`, THE compiler SHALL report an error (cannot prove sole ownership of an unrestricted object).

### Requirement 4: Readonly Ref Tier Semantics

**User Story:** As a Mist developer, I want `readonly ref` to provide a live readonly view that cannot be stored, so that I can pass objects for observation without granting mutation or capture rights.

#### Acceptance Criteria

1. WHEN a variable is declared as `readonly ref`, THE compiler SHALL restrict access to only readonly members (readonly fields, readonly methods, readonly property getters).
2. IF code attempts to call a non-readonly method through a `readonly ref`, THEN THE compiler SHALL report an error.
3. IF code attempts to assign to a field through a `readonly ref`, THEN THE compiler SHALL report an error.
4. IF code attempts to assign a `readonly ref` to a field or collection element, THEN THE compiler SHALL report an error (non-storable).
5. THE compiler SHALL permit multiple `readonly ref` variables to the same object to coexist simultaneously.
6. WHEN an owner has outstanding `readonly ref` loans, THE compiler SHALL still permit the owner to mutate the object (live view — no mutation blocking).
7. WHEN a `readonly ref` parameter is received, THE compiler SHALL permit creating local `readonly ref` variables from it within the same scope or narrower.
8. A `readonly ref` variable's lifetime SHALL NOT exceed the scope in which it was declared.

### Requirement 5: Frozen Ref Tier Semantics

**User Story:** As a Mist developer, I want `frozen ref` to guarantee immutability for the duration I hold it, so that I can safely read an object knowing nothing will change it.

#### Acceptance Criteria

1. WHEN a variable is declared as `frozen ref`, THE compiler SHALL restrict access to only readonly members.
2. IF code attempts to assign a `frozen ref` to a field or collection element, THEN THE compiler SHALL report an error (non-storable).
3. WHILE any `frozen ref` is outstanding from an owner, THE compiler SHALL prohibit mutation through the owner.
4. WHILE any `frozen ref` is outstanding from a `ref` (borrower), THE compiler SHALL prohibit mutation through the borrower.
5. THE compiler SHALL permit multiple `frozen ref` variables to the same object to coexist simultaneously.
6. THE compiler SHALL permit `frozen ref` and `readonly ref` to the same object to coexist simultaneously. The frozen constraint (no mutation) applies when any frozen ref is outstanding.
7. WHEN a `ref` (borrower) lends out a `frozen ref`, THE borrower SHALL NOT be able to mutate until the frozen ref is returned.
8. A `frozen ref` variable's lifetime SHALL NOT exceed the scope in which it was declared.

### Requirement 6: Ref (Borrowed) Tier Semantics

**User Story:** As a Mist developer, I want `ref` to provide temporary exclusive mutable access to an object, so that a function can mutate an object without taking ownership.

#### Acceptance Criteria

1. WHEN a variable is declared as `ref`, THE compiler SHALL permit full mutation of the referenced object.
2. IF code attempts to assign a `ref` to a field or collection element, THEN THE compiler SHALL report an error (non-storable).
3. WHEN an owner lends a `ref`, THE owner SHALL NOT be able to read, write, or lend the object until the ref is returned.
4. THE compiler SHALL permit only one outstanding `ref` from an owner at a time.
5. WHEN a `ref` holder lends the ref to another `ref` parameter (re-lending), THE original holder SHALL NOT be able to use the ref until the re-lend is returned.
6. A `ref` holder MAY lend out `readonly ref` references. The `ref` holder can still mutate while `readonly ref` loans are outstanding (live view semantics).
7. A `ref` holder MAY lend out `frozen ref` references. The `ref` holder SHALL NOT mutate while `frozen ref` loans are outstanding.
8. WHEN a `ref` holder attempts to move/transfer ownership of the referenced object, THE compiler SHALL report an error (borrowed references cannot transfer ownership).
9. A `ref` variable's lifetime SHALL NOT exceed the scope in which it was declared.
10. WHEN a function with a `ref` parameter returns, THE compiler SHALL restore full access to the lender's variable.

### Requirement 7: Owned Tier Semantics

**User Story:** As a Mist developer, I want `owned` to guarantee sole ownership of an object, so that I have full control and can reason about exclusive access.

#### Acceptance Criteria

1. WHEN a variable is declared as `owned`, THE compiler SHALL enforce that it is the sole reference to the object.
2. THE compiler SHALL permit full mutation through an owned reference.
3. THE compiler SHALL permit moving an owned reference into a field. If the type is not an owned type, the field becomes unrestricted (ownership is relinquished). If the type IS an owned type, the field must be marked `owned` or the containing type must be an owned type.
4. WHEN an owned variable is assigned to another `owned` variable, THE compiler SHALL treat this as a move — the source becomes inaccessible.
5. WHEN an owned variable is passed to a parameter marked `owned`, THE compiler SHALL move ownership to the callee and mark the caller's variable as inaccessible.
6. WHEN an owned variable is passed to a parameter marked `ref`, THE compiler SHALL lend a mutable borrow — the owner cannot access the object until the call returns.
7. WHEN an owned variable is passed to a parameter marked `readonly ref`, THE compiler SHALL lend a readonly view — the owner can still mutate (live view).
8. WHEN an owned variable is passed to a parameter marked `frozen ref`, THE compiler SHALL lend a frozen view — the owner cannot mutate until the call returns.
9. IF code accesses an owned variable after it has been moved, THEN THE compiler SHALL report a use-after-move error.
10. THE compiler SHALL permit an owner to lend out multiple `readonly ref` simultaneously.
11. THE compiler SHALL permit an owner to lend out multiple `frozen ref` simultaneously (owner cannot mutate while any are outstanding).
12. THE compiler SHALL permit an owner to lend out exactly one `ref` at a time (owner loses all access until returned).
13. WHEN an owned variable of a non-owned type is moved to an unrestricted destination (variable or field), THE compiler SHALL permit the move and the destination becomes unrestricted.
14. WHEN an owned variable of an owned type is moved to a field, THE compiler SHALL require the field to be marked `owned` or the containing type to be an owned type.

### Requirement 8: Owned Type Declarations

**User Story:** As a Mist developer, I want to declare types as `owned`, so that the type system enforces that instances are always held exclusively and all internal state is also owned.

#### Acceptance Criteria

1. WHEN a class is declared with the `owned` keyword (e.g., `owned class Foo`), THE Parser SHALL attach an owned type modifier to the type declaration.
2. WHEN a struct is declared with the `owned` keyword, THE Parser SHALL attach an owned type modifier to the type declaration.
3. ALL reference-type fields within an owned type SHALL themselves be declared as `owned` or be of an owned type. THE compiler SHALL report an error if an unrestricted reference-type field exists in an owned type.
4. Value-type fields within an owned type SHALL be permitted without restriction (value types are always pass-by-value).
5. WHEN code attempts to store an instance of an owned type in an unrestricted variable, THE compiler SHALL report an error.
6. WHEN code attempts to declare a variable of an owned type without the `owned` specifier, THE compiler SHALL report an error (instances must always be held as owned).
7. WHEN an owned type is used as a field type in another type, THE containing type SHALL also be an owned type. THE compiler SHALL report an error otherwise.
8. AN owned type SHALL NOT implement any interfaces or satisfy any traits. THE compiler SHALL report an error if an owned type declares interface implementations or trait conformance.
9. THE compiler SHALL NOT permit `owned` as a modifier on interface or trait declarations (reserved for a future spec).

### Requirement 9: Ownership in Method Signatures (Interface/Trait Matching)

**User Story:** As a Mist developer, I want ownership specifiers on parameters and return types to be part of a method's signature, so that interface satisfaction and trait structural matching account for ownership requirements.

#### Acceptance Criteria

1. WHEN checking if a method satisfies an interface method, THE compiler SHALL require that ownership specifiers on all parameters match exactly between the interface declaration and the implementing method.
2. WHEN checking if a method satisfies an interface method, THE compiler SHALL require that the return type ownership specifier matches exactly.
3. WHEN checking structural trait matching, THE compiler SHALL require that ownership specifiers on parameters and return types match between the trait method and the candidate method.
4. IF a method implementation has different ownership specifiers than the interface/trait declaration, THEN THE compiler SHALL report an error identifying the mismatched parameter or return type.

### Requirement 10: Ownership Transfer (Move) Semantics

**User Story:** As a Mist developer, I want move semantics for owned variables, so that ownership can be cleanly transferred between locations.

#### Acceptance Criteria

1. WHEN an owned variable is assigned to another owned variable, THE compiler SHALL mark the source as moved and inaccessible.
2. WHEN an owned variable is passed to an `owned` parameter, THE compiler SHALL mark the source as moved.
3. IF a moved variable is accessed without an intervening re-assignment, THEN THE compiler SHALL report a use-after-move error identifying the variable name, the location of the invalid access, and the location where the move occurred.
4. WHEN an owned variable is moved in one branch of a conditional but not another, THE compiler SHALL track state per branch and report an error if the variable is accessed in a branch where it was moved.
5. WHEN an owned variable is moved inside a loop body, THE compiler SHALL report an error unless the variable is re-assigned within the same iteration after the move.
6. WHEN an owned variable is moved to an unrestricted destination, THE compiler SHALL permit the move only if the variable's type is not an owned type.
7. WHEN a method returns an `owned` value, THE compiler SHALL transfer ownership to the receiving variable at the call site.

### Requirement 11: Lifetime and Scope Rules

**User Story:** As a Mist developer, I want the compiler to enforce that non-storable references cannot escape their declaring scope, so that dangling references are impossible.

#### Acceptance Criteria

1. Any `ref` (whether `ref`, `readonly ref`, or `frozen ref`) SHALL NOT be assigned to any location whose lifetime exceeds the declaring scope of the ref variable.
2. WHEN a `ref` is re-lent to a nested function call, THE compiler SHALL verify the inner borrow's lifetime is contained within the outer borrow's lifetime.
3. WHEN a `frozen ref` is lent from a `ref`, THE compiler SHALL verify the frozen ref's lifetime is contained within the ref's lifetime.
4. IF any ref escapes its scope (via field assignment, collection insertion, or return), THEN THE compiler SHALL report a lifetime error.
5. THE compiler SHALL NOT permit `ref`, `readonly ref`, or `frozen ref` as method return types (they cannot escape the method scope).

### Requirement 12: Lending Compatibility Rules

**User Story:** As a Mist developer, I want clear rules about which tiers can lend to which, so that I know what I can pass where.

#### Acceptance Criteria

1. An `owned` variable MAY lend to: `ref`, `readonly ref`, `frozen ref`, or transfer to `owned`.
2. A `ref` variable MAY lend to: `ref` (re-lend, one at a time), `readonly ref`, `frozen ref`.
3. A `readonly ref` variable MAY lend to: `readonly ref` only.
4. A `frozen ref` variable MAY lend to: `frozen ref` or `readonly ref`.
5. An unrestricted variable MAY be passed to: `readonly ref` only (among ownership-annotated parameters).
6. An unrestricted variable MAY be passed to: unrestricted parameters.
7. WHEN a variable is lent to `ref`, THE lender SHALL lose all access until the borrow returns.
8. WHEN a variable is lent to `frozen ref`, THE lender SHALL lose mutation access until the frozen ref returns.
9. WHEN a variable is lent to `readonly ref`, THE lender SHALL retain full access (live view — no restrictions on lender).

### Requirement 13: Mutation Blocking Rules

**User Story:** As a Mist developer, I want clear rules about when mutation is blocked, so that I can reason about object stability.

#### Acceptance Criteria

1. WHILE any `frozen ref` is outstanding from an owner, THE owner SHALL NOT mutate the object.
2. WHILE any `frozen ref` is outstanding from a `ref` holder, THE `ref` holder SHALL NOT mutate the object.
3. WHILE a `ref` is outstanding from an owner, THE owner SHALL NOT access the object at all (read or write).
4. WHILE `readonly ref` loans are outstanding from an owner, THE owner MAY still mutate (live view semantics).
5. WHILE `readonly ref` loans are outstanding from a `ref` holder, THE `ref` holder MAY still mutate (live view semantics).
6. WHEN all `frozen ref` loans are returned, mutation access SHALL be restored to the lender.
7. WHEN the `ref` loan is returned, full access SHALL be restored to the owner.

### Requirement 14: Value Type Behavior

**User Story:** As a Mist developer, I want value types to always satisfy ownership requirements implicitly, so that structs work naturally with the ownership system.

#### Acceptance Criteria

1. THE compiler SHALL treat pure value types (structs without reference-type fields) as always satisfying any ownership tier requirement.
2. WHEN a struct is passed to a parameter with any ownership specifier, THE compiler SHALL permit the call (pass-by-value produces a fresh copy).
3. Structs SHALL default to owned without requiring an explicit annotation.
4. WHEN a struct contains reference-type fields, THE compiler SHALL apply reference-type ownership rules to the struct (hybrid type).

### Requirement 15: Tier Transitions

**User Story:** As a Mist developer, I want to be able to transition between ownership tiers when it's safe, so that I can move objects between ownership-managed and unmanaged code.

#### Acceptance Criteria

1. WHEN an owned variable is moved to an unrestricted destination, THE compiler SHALL permit the transition only if the type is not declared as an owned type.
2. WHEN an unrestricted variable needs to become owned, THE compiler SHALL require an explicit upgrade mechanism (TBD — possibly a built-in function or cast that asserts sole ownership).
3. THE compiler SHALL NOT permit implicit tier upgrades from unrestricted to owned.
4. THE compiler SHALL permit owned-to-unrestricted moves as an explicit downgrade (for non-owned-types only).

### Requirement 16: Ownership Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for ownership violations, so that I can understand and fix issues.

#### Acceptance Criteria

1. WHEN a use-after-move occurs, THE compiler SHALL report the variable name, the move location, and the invalid access location.
2. WHEN a non-storable ref is assigned to a field, THE compiler SHALL report the variable name and explain that the ref tier cannot be stored.
3. WHEN mutation is attempted through a readonly or frozen ref, THE compiler SHALL report the operation and explain the restriction.
4. WHEN an unrestricted variable is passed to a `ref` or `frozen ref` parameter, THE compiler SHALL explain that unrestricted objects cannot provide the required guarantees.
5. WHEN an owned type's field violates the owned-fields rule, THE compiler SHALL identify the field and explain that all reference-type fields in an owned type must be owned.
6. WHEN mutation is attempted while a frozen ref is outstanding, THE compiler SHALL identify the frozen ref and explain that mutation is blocked until it is returned.
7. WHEN a ref is lent while another ref is already outstanding, THE compiler SHALL explain that only one mutable borrow is permitted at a time.

### Requirement 17: C# Emission Strategy

**User Story:** As a language developer, I want ownership to be purely compile-time with no runtime cost, so that the emitted C# is clean and performant.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL NOT emit any runtime ownership tracking (no reference counting, no state fields, no runtime assertions).
2. THE CSharp_Emitter SHALL emit all variables as standard C# reference variables regardless of their ownership tier.
3. THE CSharp_Emitter SHALL NOT emit the ownership keywords (`owned`, `ref`, `frozen`, `readonly ref`) in generated C# — they are erased.
4. WHEN an owned variable is moved, THE CSharp_Emitter SHALL emit a standard assignment (no nulling of source).
5. THE CSharp_Emitter SHALL emit readonly members using the existing readonly emission strategy.

### Requirement 18: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want ownership keywords to be syntax-highlighted correctly, so that I get visual cues for ownership annotations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `owned` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL recognize `frozen` as a storage modifier keyword (scope `storage.modifier.mist`).
3. THE TextMate grammar SHALL recognize `ref` as a storage modifier keyword (scope `storage.modifier.mist`).
4. THE TextMate grammar SHALL match these keywords only at word boundaries.
5. THE TextMate grammar SHALL highlight `readonly ref` and `frozen ref` as compound modifiers when they appear together.

### Requirement 19: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample demonstrating the ownership system, so that I can see every pattern in action.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/OwnershipSystem/` containing a `mist.toml` and `Program.mist`.
2. The sample SHALL demonstrate: unrestricted default behavior, `readonly ref` parameters and local variables, `frozen ref` with mutation blocking, `ref` borrowing with re-lending, `owned` with move semantics, owned type declarations with field restrictions, lending compatibility (what can pass to what), value types satisfying all tiers, and tier transitions.
3. Each demonstrated pattern SHALL print labeled output (e.g., `[readonly-ref]`, `[frozen-blocks-mutation]`) so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `OwnershipSystem.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a reference to the OwnershipSystem project.
5. The sample SHALL compile and run with exit code 0 and no warnings.

### Requirement 20: Round-Trip Parsing

**User Story:** As a language developer, I want ownership annotations to survive parse-print cycles, so that tooling can reliably reconstruct source.

#### Acceptance Criteria

1. FOR ALL valid ownership-annotated declarations, parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
2. Compound specifiers (`readonly ref`, `frozen ref`) SHALL be preserved through round-trip cycles.
3. IF parsing the pretty-printed output produces parse errors, THE round-trip property SHALL be considered violated.
