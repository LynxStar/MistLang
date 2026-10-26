# Requirements Document

## Introduction

This document specifies the requirements for **Ownership v2** — a second-generation ownership system for the Mist programming language. This is a redesign of the existing deep ownership system (spec: `deep-ownership-types`) based on fundamental design insights discovered during implementation.

**Core semantic shift:** Ownership moves from a type-level property to an instance/binding-level property. Any type can be owned at the binding level via `owned var`. The `owned` keyword on types survives with a new meaning: a cooperation contract where the type author guarantees that all mutations maintain ownership invariants internally.

**What this redesign changes:**
- Removes the v1 owned-type field restriction (all fields must be owned types)
- Removes the `owned` field modifier on individual fields
- Removes struct ownership inference (any type can be owned at binding level)
- Removes `MIST_OWN_FIELD_NOT_OWNED` diagnostic
- Removes the structural "is this type eligible for ownership" check (the old meaning of `MIST_OWN_TYPE_REQUIRES_OWNED`)
- Adds method call tier restrictions for non-owned types on owned bindings
- Adds field assignment provenance enforcement on owned bindings
- Adds `IClonable<T>` interface for shared-to-owned bridging
- Redefines `owned` on types as a cooperation contract

**What stays the same:**
- `owned var x = ...` — instance-level ownership binding
- `owned T param` — caller must move ownership in
- `owned T` return — handing out a fresh owned value
- Move semantics (`owned var y = x` makes x dead)
- Lending system (borrowed, frozen, readonly ref from owned bindings)
- Ownership state truth table (Live, LentReadonly, LentFrozen, LentRef, Moved)
- Zero runtime cost — purely compile-time enforcement, erased in emitted C#

## Glossary

- **Owned_Binding**: A variable declaration using `owned var x = ...` or `owned T x = ...`. The binding guarantees the variable is the unique owner of the value and its entire object graph. Valid for any type (owned or non-owned).
- **Owned_Type**: A class or struct declared with the `owned` modifier. The type author guarantees cooperation with ownership semantics — all field assignments inside the type come from move operations or fresh allocations. This is a contract, not a field restriction.
- **Non_Owned_Type**: A type without the `owned` modifier. When held in an owned binding, non-owned types appear readonly — only direct field assignment (with provenance proof) and readonly methods are available.
- **Cooperation_Contract**: The guarantee an owned type author makes: the compiler verifies that all field assignments inside the type's methods come from owned sources (parameters declared `owned`, constructor calls, or owned-returning functions). No external aliases leak into the object graph through mutations.
- **Field_Assignment_Provenance**: The rule that when assigning to a field of an owned-bound instance, the source must be owned (move from an owned binding, constructor call, or owned-returning function). If the source is not owned but implements `IClonable<T>`, the compiler implicitly calls `.Clone()` to produce an owned value.
- **IClonable**: An interface `IClonable<T>` that bridges from shared to owned. When assigning to an owned destination from a non-owned source, if the type implements `IClonable<T>`, `.Clone()` produces a fresh owned value.
- **Readonly_Method**: A method that does not mutate any field of `this`. Determined by readonly inference or explicit annotation.
- **Mutable_Method**: A method that mutates at least one field of `this`.
- **Move_Semantics**: The act of transferring a value from one binding to another. After a move, the source binding is dead and inaccessible.
- **Compiler**: The Mist compiler pipeline including lexer, parser, semantic model, and type checker.
- **Type_Checker**: The analysis phase that enforces ownership rules at compile time.
- **Parser**: The syntax analysis phase that recognizes ownership-related keywords.
- **CSharp_Emitter**: The backend that generates C# code from the Mist semantic model.

## Requirements

### Requirement 1: Owned Binding — Non-Substructural Types Can Be Owned

**User Story:** As a Mist developer, I want to declare any non-substructural type as owned at the binding level, so that I can get unique ownership guarantees regardless of whether the type is declared `owned`.

#### Acceptance Criteria

1. WHEN a variable is declared with the `owned` specifier (e.g., `owned var x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
2. WHEN a variable is declared with the `owned` specifier and an explicit type (e.g., `owned Foo x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
3. WHEN an owned binding is declared with an initializer whose type is a non-substructural type (an unrestricted type or a type declared `owned`), THE Type_Checker SHALL accept the declaration — the type does not need to be declared `owned`.
4. IF an owned binding is declared with an initializer whose type is a substructural type (`linear`, `affine`, or `relevant`), THEN THE Type_Checker SHALL report a compile error indicating that the `owned` binding specifier cannot be applied to substructural types — those types already carry their own usage discipline through drop semantics, and layering ownership tracking on top is neither necessary nor sound.
5. WHEN an owned binding is declared, THE Type_Checker SHALL verify that the initializer is a value-producing expression that yields unique ownership: a constructor call (`new`), a call to a function returning an owned value, or a move from another owned binding (see Requirement 6 for move rules including implicit cloning).
6. IF an owned binding is declared with an initializer that does not yield unique ownership AND cannot be resolved via move semantics (Requirement 6), THEN THE Type_Checker SHALL report a compile error indicating that an owned binding requires a uniquely-owned value.

### Requirement 2: Method Call Rules — Non-Owned Types on Owned Bindings

**User Story:** As a Mist developer, I want the compiler to restrict what methods I can call on an owned binding of a non-owned type, so that mutable operations that could break ownership invariants are prevented without requiring the type to opt in.

#### Acceptance Criteria

1. WHEN a non-owned type is held in an owned binding, THE Type_Checker SHALL permit readonly method calls on the instance.
2. WHEN a non-owned type is held in an owned binding, THE Type_Checker SHALL hide all mutable methods — the instance appears readonly from the binding's perspective.
3. WHEN a non-owned type is held in an owned binding, THE Type_Checker SHALL permit direct field assignment where the source satisfies field assignment provenance (Requirement 4) — this is the mechanism by which non-owned types in owned bindings can still have their fields updated safely.
4. IF code attempts to call a mutable method on a non-owned type held in an owned binding, THEN THE Type_Checker SHALL report a compile error indicating that mutable methods are not available on non-owned types held in owned bindings, and suggesting either declaring the type `owned` (cooperation contract) or using direct field assignment.

### Requirement 3: Method Call Rules — Owned Types on Owned Bindings

**User Story:** As a Mist developer, I want owned types to have full method access when held in owned bindings, so that the type author's cooperation contract grants full mutability.

#### Acceptance Criteria

1. WHEN an owned type is held in an owned binding, THE Type_Checker SHALL permit all method calls (readonly and mutable) on the instance.
2. WHEN an owned type is held in an owned binding, THE Type_Checker SHALL permit direct field assignment where the source satisfies field assignment provenance (Requirement 4).
3. THE Type_Checker SHALL permit mutable method calls on owned types because the cooperation contract guarantees that all internal mutations maintain ownership invariants — the compiler verifies this guarantee by checking that all field assignments within the type's method bodies come from owned sources (this is not blind trust; the contract is enforced at the type definition site).

### Requirement 4: Field Assignment Provenance

**User Story:** As a Mist developer, I want the compiler to enforce that when I assign to a field of an owned-bound instance, the source value is also owned, so that non-owned aliases cannot leak into the owned graph.

#### Acceptance Criteria

1. WHEN assigning to a field of an instance held in an owned binding, THE Type_Checker SHALL require that the source expression yields an owned value: a move from an owned binding, a constructor call (`new`), or a call to a function returning `owned T`. Move semantics (Requirement 6) apply — including implicit cloning for non-owned sources that implement `IClonable<T>`.
2. IF the source expression for a field assignment on an owned-bound instance is a reference type that is not owned and cannot be resolved via move semantics (Requirement 6), THEN THE Type_Checker SHALL report a compile error indicating that field assignment on an owned binding requires an owned source.
3. WHEN the source expression is a value type (struct), THE Type_Checker SHALL permit the assignment without provenance restriction — value types are copied and cannot introduce aliasing. Linear and affine structs instead obey the single-holder moves in [drop-semantics Requirement 1.8](../drop-semantics/requirements.md#requirement-1-substructural-type-modifiers).
4. WHEN the source expression is a primitive type, THE Type_Checker SHALL permit the assignment without provenance restriction.

### Requirement 5: Owned Type Declaration — Cooperation Contract

**User Story:** As a Mist type author, I want to declare my type as `owned` to signal that I proactively cooperate with ownership semantics, so that users of my type get full mutable access when holding it in an owned binding.

#### Acceptance Criteria

1. WHEN a class is declared with the `owned` modifier (e.g., `owned class Foo`), THE Parser SHALL attach an owned type modifier to the type declaration node.
2. WHEN a struct is declared with the `owned` modifier (e.g., `owned struct Bar`), THE Parser SHALL attach an owned type modifier to the type declaration node.
3. WHEN a type is declared `owned`, THE Type_Checker SHALL verify the cooperation contract: all field assignments inside the type's methods and constructors must have sources that are owned. Move semantics (Requirement 6) apply — including implicit cloning for non-owned sources that implement `IClonable<T>`.
4. IF a method body or constructor in an owned type assigns to a field from a non-owned source that cannot be resolved via move semantics (Requirement 6), THEN THE Type_Checker SHALL report a compile error identifying the assignment, the non-owned source, and explaining that owned types must maintain ownership invariants in all mutations.
5. THE Type_Checker SHALL NOT restrict what field types an owned type can have — any type is permitted as a field in an owned type.
6. IF the `owned` modifier is applied to an interface or trait declaration, THEN THE Type_Checker SHALL report a compile error stating that the `owned` modifier cannot be applied to interfaces or traits.

### Requirement 6: Owned Binding — Move Semantics

**User Story:** As a Mist developer, I want owned bindings to support move semantics so that ownership can be cleanly transferred between locations while preventing use-after-move errors.

#### Acceptance Criteria

1. WHEN an owned binding is assigned to another owned binding (e.g., `owned var y = x`), THE Type_Checker SHALL treat this as a move — the source binding becomes dead and inaccessible.
2. WHEN an owned binding is assigned to an unrestricted binding (e.g., `var y = x`), THE Type_Checker SHALL treat this as a move — the source binding becomes dead, and the destination holds the value without ownership tracking.
3. WHEN an owned binding is passed to a parameter marked `owned`, THE Type_Checker SHALL move ownership to the callee and mark the caller's binding as dead.
4. IF code reads, writes, passes as an argument, or takes a reference to an owned binding after it has been moved, THEN THE Type_Checker SHALL report a use-after-move error identifying the binding name, the move location, and the invalid access location.
5. WHEN an owned binding is moved in one branch of a conditional but not another, THE Type_Checker SHALL treat the binding as moved at the join point after the conditional.
6. WHEN an owned binding is moved inside a loop body, THE Type_Checker SHALL report an error unless the binding is re-assigned a new owned value within the same iteration after the move.
7. WHEN a method returns an `owned` value, THE Type_Checker SHALL transfer ownership to the receiving binding at the call site.
8. A move SHALL only be permitted when the variable is in the **Live** state. IF the variable is in LentReadonly, LentFrozen, LentRef, or Moved state, THE Type_Checker SHALL reject the move.
9. WHENEVER a move into an owned destination is required and the source is not already owned, THE Compiler SHALL check if the source type implements `IClonable<T>`. IF it does, THE Compiler SHALL automatically insert a `.Clone()` call to produce a fresh owned value. IF it does not, THE Type_Checker SHALL report a compile error. This is the single mechanism by which non-owned values become owned — it applies uniformly to all owned binding initializations, field assignments on owned instances, and owned parameter passing.
10. THE Type_Checker SHALL treat a `default(T)` expression, and a bare `default` literal where the target type is known (e.g., `owned T x = default`), as a value that yields unique ownership — `default` produces a freshly-allocated, fully-independent value graph (recursive `__CreateDefault()` with only literal leaf values), so it is a valid owned source equivalent to a constructor call.

### Requirement 7: Ownership State Transition Rules

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

- **Live**: Sole owner with exclusive access. All actions permitted.
- **LentReadonly**: Sole owner with one or more live views (readonly refs) outstanding. Read/write/lend permitted. Move/borrow rejected because both create a recipient that assumes exclusive access.
- **LentFrozen**: Sole owner with frozen leases outstanding. Read/additional-freeze/live-view permitted. Write/move/borrow rejected.
- **LentRef** (borrowed out): Sole owner with exclusive access temporarily lent to a borrower. No actions permitted until the borrow returns.
- **Moved**: The value has been moved to another binding. This variable is dead and inaccessible.

**Specific enforcement rules:**

1. WHEN a variable is in the **Live** state, THE Type_Checker SHALL permit all actions: read, write, lending as live view, lending as frozen, lending as borrowed, and moving.
2. WHEN a variable is in the **LentReadonly** state, THE Type_Checker SHALL permit read, write, lending as live view, and lending as frozen. THE Type_Checker SHALL reject borrow and move.
3. WHEN a variable is in the **LentFrozen** state, THE Type_Checker SHALL permit read, lending as live view, and lending as frozen. THE Type_Checker SHALL reject write, borrow, and move.
4. WHEN a variable is in the **LentRef** (borrowed out) state, THE Type_Checker SHALL reject all actions.
5. WHEN a variable is in the **Moved** state, THE Type_Checker SHALL reject all actions and report a use-after-move error.

### Requirement 8: IClonable Interface — Shared-to-Owned Bridge

**User Story:** As a Mist developer, I want a standard mechanism to produce a fresh owned value from a non-owned source, so that I can populate owned bindings and fields from shared data safely.

#### Acceptance Criteria

1. THE standard library SHALL define an `IClonable<T>` interface with a single method: `public owned T Clone()`.
2. WHEN assigning to an owned destination (owned binding or field of an owned-bound instance) from a non-owned source, THE Type_Checker SHALL check if the source type implements `IClonable<T>`.
3. IF the source type implements `IClonable<T>`, THE Type_Checker SHALL accept the assignment — `.Clone()` is invoked (implicitly or explicitly) to produce a fresh owned value.
4. IF the source type does not implement `IClonable<T>` and the source is not already owned, THEN THE Type_Checker SHALL report a compile error indicating that the value cannot be proven unique and suggesting implementation of `IClonable<T>`.
5. WHEN the source of a move into an owned destination is a value held in a lent tier (`borrowed`, `frozen`, or `readonly ref`) whose type implements `IClonable<T>`, THE Compiler SHALL implicitly insert a `.Clone()` call to produce a fresh owned value. Cloning from a lent reference SHALL NOT consume or move the underlying value — the lender retains its value after the lend returns.
6. THE Compiler SHALL auto-derive `IClonable<T>` for a type if ALL of its fields have types that implement `IClonable<T>`. The bootstrap value types (`Int32`, `Boolean`, `Float64`, etc.) and `String` SHALL implement `IClonable<T>` as base cases. This rule applies recursively — a struct whose fields all implement `IClonable<T>` auto-derives it, regardless of whether those fields are structs or classes.
7. WHEN a type has any field whose type does NOT implement `IClonable<T>`, THE Compiler SHALL NOT auto-derive `IClonable<T>` for that type — the developer must implement it manually or the type is not clonable.
8. THE CSharp_Emitter SHALL emit `.Clone()` calls where implicit cloning occurs, except for value types where the compiler may optimize away the call (value semantics already provide the copy).

### Requirement 9: Interaction with Existing Lending Tiers

**User Story:** As a Mist developer, I want owned bindings to integrate cleanly with the existing borrowed/frozen/readonly ref lending system, regardless of whether the type is an owned type or not.

#### Acceptance Criteria

1. WHEN an owned binding is lent as `borrowed`, THE Type_Checker SHALL transition the owner to the LentRef state.
2. WHEN an owned binding is lent as `frozen`, THE Type_Checker SHALL transition the owner to the LentFrozen state.
3. WHEN an owned binding is lent as `readonly ref`, THE Type_Checker SHALL transition the owner to the LentReadonly state.
4. WHEN an owned binding is passed to an `owned` parameter, THE Type_Checker SHALL mark the source binding as moved.
5. WHEN an unrestricted binding is passed to a `readonly ref` parameter, THE Type_Checker SHALL permit the call.
6. WHEN an unrestricted binding is passed to a `borrowed` or `frozen` parameter, THE Type_Checker SHALL report an error indicating that an unrestricted binding cannot guarantee exclusive access.
7. IF an owned binding already has an outstanding `borrowed` loan and code attempts a second lend of any tier, THEN THE Type_Checker SHALL report an error.
8. WHILE an owned binding has one or more outstanding `frozen` loans, THE Type_Checker SHALL permit additional `frozen` or `readonly ref` loans but SHALL reject a `borrowed` loan or a move. (Note: the restriction on borrowed loans and moves applies only while frozen loans are actually outstanding — once all frozen loans expire, the binding returns to Live state and all actions are permitted.)

### Requirement 10: Removal of v1 Owned-Type Field Restriction

**User Story:** As a Mist type author, I want owned types to be free to declare any field type, so that the cooperation contract focuses on behavioral safety rather than structural restriction.

#### Acceptance Criteria

1. WHEN an owned type declares a field of any reference type (class, interface, trait), THE Type_Checker SHALL permit the field without requiring the field's type to itself be an owned type.
2. THE Type_Checker SHALL NOT report `MIST_OWN_FIELD_NOT_OWNED` or any equivalent diagnostic that restricts field types of owned type declarations.
3. THE Type_Checker SHALL NOT report the `MIST_OWN_TYPE_REQUIRES_OWNED` diagnostic — this specific diagnostic (which enforced the v1 structural "is this type eligible for ownership" check) is removed entirely.
4. WHEN an existing codebase uses `owned` type declarations with non-owned field types, THE Compiler SHALL accept these declarations without error under v2 rules.

### Requirement 11: Removal of Owned Field Modifier

**User Story:** As a Mist developer, I want the `owned` field modifier removed entirely, because binding-level ownership makes per-field ownership annotation redundant. There is no backwards compatibility — code using the old modifier must be upgraded.

#### Acceptance Criteria

1. THE `owned` modifier on field declarations SHALL be removed from the grammar entirely — it is no longer a valid field modifier.
2. IF a field declaration uses the `owned` modifier (e.g., `owned MyClass data;`), THE Parser or Type_Checker SHALL report a hard compile error indicating that the `owned` field modifier is no longer supported. No deprecation warning, no silent acceptance.
3. THE `MIST_OWN_FIELD_NOT_OWNED` diagnostic SHALL be completely removed from the compiler — all diagnostic code, error message definitions, and references to this diagnostic SHALL be deleted from the codebase entirely.

### Requirement 12: Removal of Struct Ownership Inference

**User Story:** As a Mist developer, I want struct ownership inference removed because any type can now be owned at the binding level, making inference unnecessary.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT infer ownership status for struct declarations — there is no concept of a struct "being inferred as an owned type."
2. WHEN a struct is used in an owned binding (e.g., `owned var p = new Point(3, 4)`), THE Type_Checker SHALL accept the declaration regardless of the struct's field types.
3. WHEN a struct is declared with the explicit `owned` modifier, THE Type_Checker SHALL treat it as an owned type with the cooperation contract (same as classes).
4. WHEN a struct without the `owned` modifier is held in an owned binding, THE Type_Checker SHALL apply non-owned type rules as defined in Requirement 2.

### Requirement 13: Ownership Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for ownership violations under the v2 rules, so that I can understand and fix issues quickly.

#### Acceptance Criteria

1. WHEN a mutable method is called on a non-owned type held in an owned binding, THE Type_Checker SHALL report an error identifying the method name, the type name, and explaining that non-owned types appear readonly in owned bindings.
2. WHEN a field assignment on an owned-bound instance uses a non-owned source, THE Type_Checker SHALL report an error identifying the field, the source expression, and explaining the provenance requirement.
3. WHEN a use-after-move occurs, THE Type_Checker SHALL report an error identifying the binding name, the move location, and the invalid access location.
4. WHEN an owned type's method body assigns to a field from a non-owned source, THE Type_Checker SHALL report an error identifying the method, the field, and the non-owned source.
5. WHEN an owned type's constructor assigns to a field from a non-owned source, THE Type_Checker SHALL report the same error as method bodies (cooperation contract violation).
6. WHEN the `owned` field modifier is encountered, THE Type_Checker SHALL report a hard compile error stating that the modifier is no longer supported (not a warning — see Requirement 11).
7. WHEN an IClonable clone is needed but the type does not implement IClonable, THE Type_Checker SHALL report an error suggesting the type implement `IClonable<T>`.
8. WHEN the `owned` binding specifier is applied to a substructural type (`linear`, `affine`, or `relevant`), THE Type_Checker SHALL report an error identifying the binding and the substructural type, explaining that substructural types manage their own usage discipline and cannot be combined with `owned` binding tracking.

### Requirement 14: C# Emission Strategy

**User Story:** As a language developer, I want ownership v2 to be purely compile-time with no runtime cost, so that the emitted C# is clean and performant.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL NOT emit any runtime ownership tracking (no reference counting, no state fields, no runtime assertions).
2. THE CSharp_Emitter SHALL emit all owned bindings as standard C# variable declarations.
3. THE CSharp_Emitter SHALL NOT emit the `owned` keyword in generated C# — ownership annotations are erased.
4. WHEN an owned binding is moved, THE CSharp_Emitter SHALL emit a standard assignment (no nulling of source, no runtime move tracking).
5. THE CSharp_Emitter SHALL emit owned type declarations as standard C# class or struct declarations without any ownership-related attributes.
6. THE CSharp_Emitter SHALL emit `IClonable<T>` as a standard C# interface with a `Clone()` method.
7. WHEN implicit cloning occurs, THE CSharp_Emitter SHALL emit an explicit `.Clone()` call at the assignment site.

### Requirement 15: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want ownership-related keywords to be syntax-highlighted correctly under v2 semantics, so that I get visual cues for the ownership system.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `owned` preceding `class` or `struct` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `owned class` or `owned struct` as a type name (scope `entity.name.type.class.mist` or `entity.name.type.struct.mist` respectively).
3. THE TextMate grammar SHALL recognize `owned` preceding a variable type or `var` as a storage modifier keyword (scope `storage.modifier.mist`).
4. THE TextMate grammar SHALL match `owned` only at word boundaries to avoid false matches within identifiers.
5. THE TextMate grammar SHALL NOT require any changes for IClonable — it follows existing interface highlighting patterns.

### Requirement 16: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the ownership system, so that I can see every supported usage pattern and verify correctness.

#### Acceptance Criteria

1. THE old ownership samples `samples/DeepOwnershipTypes/` and `samples/OwnershipSystem/` SHALL both be deleted entirely. Their `RunDemo()` calls SHALL be removed from `samples/AllFeatures/Program.mist` and their `[[references]]` entries removed from `samples/AllFeatures/mist.toml`.
2. A sample project SHALL exist at `samples/Ownership/Program.mist` with a corresponding `mist.toml` where `name = "Ownership"`. It is the single ownership sample — not labeled as a version.
3. The sample SHALL demonstrate each of the following patterns: owned binding of a non-owned type (restricted access), owned binding of an owned type (full access), field assignment provenance enforcement, IClonable bridging from shared to owned (explicit and implicit, including from lent tiers), move semantics, lending tiers from owned bindings (borrowed, frozen, readonly ref), mutable method access on owned types in owned bindings, the cooperation contract in action, `default` as an owned source (both explicit `default(T)` and bare `default` with an explicitly-typed owned LHS), unrestricted aliasing (the baseline owned bindings prevent), value types satisfying all four lending tiers, a borrowed recipient reading/writing/re-lending, and truth-table state demonstrations (Live, LentReadonly, LentFrozen).
4. Each demonstrated pattern SHALL print a unique labeled prefix to stdout so that correctness of each pattern is independently observable.
5. The sample SHALL expose a `public static void RunDemo()` method in `Ownership.Program` that executes all demonstrations.
6. `samples/AllFeatures/Program.mist` SHALL call `Ownership.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../Ownership"`.
7. The sample SHALL compile and run without errors or warnings using the Mist compiler.

#### Proof Code

```mist
// ─── Owned binding of a NON-OWNED type (restricted access) ────────────────────

using Standard;

namespace Ownership;

// Non-owned type — does NOT opt into cooperation contract
public class Widget
{
    public int Value;
    public string Label;

    public Widget(int value, string label)
    {
        this.Value = value;
        this.Label = label;
    }

    // Readonly method — callable from owned binding
    public int GetValue()
    {
        return Value;
    }

    // Readonly method — callable from owned binding
    public string Describe()
    {
        return $"Widget({Value}, {Label})";
    }

    // Mutable method — HIDDEN from owned binding (not callable)
    public void SetValue(int v)
    {
        this.Value = v;
    }
}

// PROOF: Non-owned type in owned binding has readonly-only method access
// PROOF: Direct field assignment with provenance is permitted
// PROOF: Field assignment accepts owned-returning method, moved local, and IClonable source
// PROOF: The SAME type in an unrestricted binding retains full mutable access
public class NonOwnedBindingDemo
{
    // Helper: returns an owned string (owned-returning function — valid provenance)
    public static owned string MakeLabel(int n)
    {
        return $"label-{n}";
    }

    public static void Run()
    {
        owned var w = new Widget(10, "alpha");

        // Readonly methods are callable
        var val = w.GetValue();
        Console.WriteLine($"[non-owned-binding] readonly method={val}");
        Console.WriteLine($"[non-owned-binding] describe={w.Describe()}");

        // Field assignment from an owned-returning method — provenance satisfied
        w.Label = MakeLabel(1);
        Console.WriteLine($"[non-owned-binding] field from method={w.Describe()}");

        // Field assignment by moving a local owned string — provenance satisfied
        owned string moved = MakeLabel(2);
        w.Label = moved;
        // moved is now dead
        Console.WriteLine($"[non-owned-binding] field from move={w.Describe()}");

        // Field assignment from an IClonable source (shared string auto-clones) — provenance satisfied
        var sharedLabel = "shared-label";
        w.Label = sharedLabel;  // string implements IClonable; compiler auto-inserts .Clone()
        // sharedLabel is still usable — clone produced an independent value
        Console.WriteLine($"[non-owned-binding] field from clone={w.Describe()}");
        Console.WriteLine($"[non-owned-binding] shared still usable={sharedLabel}");

        // Move to another owned binding — proves move semantics work on non-owned types
        owned var w2 = w;
        Console.WriteLine($"[non-owned-binding] after move={w2.Describe()}");

        // Contrast: an UNRESTRICTED binding of the same type CAN call mutable methods.
        // The restriction only applies to owned bindings — shared bindings are unaffected.
        var shared = new Widget(50, "shared");
        shared.SetValue(99);  // mutable method callable — not an owned binding
        Console.WriteLine($"[non-owned-binding] unrestricted mutable call={shared.Describe()}");
    }
}
```

**Expected output:**
```
[non-owned-binding] readonly method=10
[non-owned-binding] describe=Widget(10, alpha)
[non-owned-binding] field from method=Widget(10, label-1)
[non-owned-binding] field from move=Widget(10, label-2)
[non-owned-binding] field from clone=Widget(10, shared-label)
[non-owned-binding] shared still usable=shared-label
[non-owned-binding] after move=Widget(10, shared-label)
[non-owned-binding] unrestricted mutable call=Widget(99, shared)
```

```mist
// ─── Owned binding of an OWNED type (full mutable access) ─────────────────────

// Owned type — opts into cooperation contract
public owned class Engine
{
    int Rpm;
    int Temperature;

    public Engine(int rpm, int temperature)
    {
        this.Rpm = rpm;
        this.Temperature = temperature;
    }

    public int GetRpm()
    {
        return Rpm;
    }

    public string Describe()
    {
        return $"Engine(Rpm={Rpm}, Temp={Temperature})";
    }

    // Mutable method — callable because Engine is an owned type
    // Cooperation contract: field assignment from parameter is safe (value type int)
    public void Rev(int amount)
    {
        this.Rpm = Rpm + amount;
        this.Temperature = Temperature + amount / 10;
    }
}

// PROOF: Owned type in owned binding has full mutable method access
// PROOF: Mutable methods ARE callable (unlike non-owned types)
// PROOF: Cooperation contract enables full access without restriction
public class OwnedTypeBindingDemo
{
    public static void Run()
    {
        owned var engine = new Engine(1000, 80);
        Console.WriteLine($"[owned-type-binding] initial={engine.Describe()}");

        // Mutable method callable — Engine opts into cooperation contract
        engine.Rev(500);
        Console.WriteLine($"[owned-type-binding] after rev={engine.Describe()}");

        // Another mutable call
        engine.Rev(200);
        Console.WriteLine($"[owned-type-binding] after second rev={engine.Describe()}");

        // Readonly method also callable
        var rpm = engine.GetRpm();
        Console.WriteLine($"[owned-type-binding] getRpm={rpm}");
    }
}
```

**Expected output:**
```
[owned-type-binding] initial=Engine(Rpm=1000, Temp=80)
[owned-type-binding] after rev=Engine(Rpm=1500, Temp=130)
[owned-type-binding] after second rev=Engine(Rpm=1700, Temp=150)
[owned-type-binding] getRpm=1700
```

```mist
// ─── Field assignment provenance ──────────────────────────────────────────────

public owned class Container
{
    Widget Inner;

    // Constructor takes owned parameter — satisfies cooperation contract
    public Container(owned Widget inner)
    {
        this.Inner = inner;
    }

    public string Describe()
    {
        return $"Container(Inner={Inner.Describe()})";
    }

    // Mutable method takes owned parameter — provenance satisfied
    public void Replace(owned Widget replacement)
    {
        this.Inner = replacement;
    }
}

// PROOF: Field assignment provenance — source must be owned
// PROOF: Constructor call (new) satisfies provenance
// PROOF: Move from owned binding satisfies provenance
// PROOF: Owned-parameter move satisfies provenance inside methods
public class ProvenanceDemo
{
    public static void Run()
    {
        // Constructor call satisfies provenance for owned binding field assignment
        owned var c = new Container(new Widget(1, "first"));
        Console.WriteLine($"[provenance] initial={c.Describe()}");

        // Replace via method that takes owned parameter — provenance satisfied
        owned var replacement = new Widget(2, "second");
        c.Replace(replacement);
        Console.WriteLine($"[provenance] after replace={c.Describe()}");

        // Direct field assignment with fresh constructor call — provenance satisfied
        owned var c2 = new Container(new Widget(3, "third"));
        Console.WriteLine($"[provenance] c2={c2.Describe()}");
    }
}
```

**Expected output:**
```
[provenance] initial=Container(Inner=Widget(1, first))
[provenance] after replace=Container(Inner=Widget(2, second))
[provenance] c2=Container(Inner=Widget(3, third))
```

```mist
// ─── IClonable bridging ───────────────────────────────────────────────────────

public class Config : IClonable<Config>
{
    public int Timeout;
    public string Host;

    public Config(int timeout, string host)
    {
        this.Timeout = timeout;
        this.Host = host;
    }

    public owned Config Clone()
    {
        return new Config(Timeout, Host);
    }

    public string Describe()
    {
        return $"Config(Timeout={Timeout}, Host={Host})";
    }
}

// PROOF: IClonable bridges shared-to-owned
// PROOF: Clone() produces a fresh owned value from a non-owned source
// PROOF: After clone, original and clone are independent
// PROOF: Implicit clone — assigning a non-owned source to an owned binding auto-inserts Clone()
// PROOF: Implicit clone fires at owned-parameter boundary and owned-field assignment
public owned class Vault
{
    Config Setting;

    public Vault(owned Config setting)
    {
        this.Setting = setting;
    }

    // Mutable method taking an owned parameter — implicit clone fires at call site
    public void Replace(owned Config setting)
    {
        this.Setting = setting;
    }

    public string Describe()
    {
        return $"Vault(Setting={Setting.Describe()})";
    }
}

public class ClonableDemo
{
    public static void Run()
    {
        // Non-owned source
        var shared = new Config(30, "localhost");
        Console.WriteLine($"[iclonable] shared={shared.Describe()}");

        // Explicit clone into an owned binding — IClonable bridge
        owned var myConfig = shared.Clone();
        Console.WriteLine($"[iclonable] cloned={myConfig.Describe()}");

        // Modify the shared original — owned clone is unaffected
        shared.Timeout = 60;
        Console.WriteLine($"[iclonable] shared after modify={shared.Describe()}");
        Console.WriteLine($"[iclonable] clone unaffected={myConfig.Describe()}");

        // IMPLICIT clone — assigning the non-owned `shared` directly to an owned binding.
        // The compiler detects the source isn't owned, sees Config implements IClonable,
        // and auto-inserts the .Clone() call. No manual .Clone() written here.
        owned Config implicitClone = shared;
        Console.WriteLine($"[iclonable] implicit clone={implicitClone.Describe()}");

        // shared is still usable — the implicit clone produced an independent value
        shared.Timeout = 90;
        Console.WriteLine($"[iclonable] shared after second modify={shared.Describe()}");
        Console.WriteLine($"[iclonable] implicit clone unaffected={implicitClone.Describe()}");

        // IMPLICIT clone at the owned-PARAMETER boundary — `shared` is non-owned, but the
        // Vault constructor takes `owned Config`. The compiler auto-inserts .Clone().
        owned var vault = new Vault(shared);
        Console.WriteLine($"[iclonable] vault from shared={vault.Describe()}");

        // shared still usable — vault holds an independent clone
        shared.Host = "remote";
        Console.WriteLine($"[iclonable] shared after host change={shared.Describe()}");
        Console.WriteLine($"[iclonable] vault unaffected={vault.Describe()}");

        // IMPLICIT clone at the owned-FIELD assignment via owned-parameter method.
        // Replace takes `owned Config`; passing non-owned `shared` auto-clones.
        vault.Replace(shared);
        Console.WriteLine($"[iclonable] vault after replace={vault.Describe()}");
    }
}
```

**Expected output:**
```
[iclonable] shared=Config(Timeout=30, Host=localhost)
[iclonable] cloned=Config(Timeout=30, Host=localhost)
[iclonable] shared after modify=Config(Timeout=60, Host=localhost)
[iclonable] clone unaffected=Config(Timeout=30, Host=localhost)
[iclonable] implicit clone=Config(Timeout=60, Host=localhost)
[iclonable] shared after second modify=Config(Timeout=90, Host=localhost)
[iclonable] implicit clone unaffected=Config(Timeout=60, Host=localhost)
[iclonable] vault from shared=Vault(Setting=Config(Timeout=90, Host=localhost))
[iclonable] shared after host change=Config(Timeout=90, Host=remote)
[iclonable] vault unaffected=Vault(Setting=Config(Timeout=90, Host=localhost))
[iclonable] vault after replace=Vault(Setting=Config(Timeout=90, Host=remote))
```

```mist
// ─── Implicit clone from lent tiers (borrowed / frozen / readonly ref) ────────

// PROOF: A borrowed, frozen, or readonly ref parameter can be lifted to an owned
//        binding via implicit clone — the lent reference implements IClonable, so
//        the compiler inserts .Clone() to produce an independent owned value.
// PROOF: Cloning from a lent tier does NOT consume or move the underlying value —
//        the lender retains its value after the borrow/freeze/readonly-ref returns.
public class CloneFromLentDemo
{
    // Receives a borrowed Config, clones it into an owned binding
    public static void FromBorrowed(borrowed Config c)
    {
        owned Config mine = c;  // implicit clone — c is borrowed, not owned
        Console.WriteLine($"[clone-lent] from borrowed={mine.Describe()}");
    }

    // Receives a frozen Config, clones it into an owned binding
    public static void FromFrozen(frozen Config c)
    {
        owned Config mine = c;  // implicit clone — c is frozen, not owned
        Console.WriteLine($"[clone-lent] from frozen={mine.Describe()}");
    }

    // Receives a readonly ref Config, clones it into an owned binding
    public static void FromReadonlyRef(readonly ref Config c)
    {
        owned Config mine = c;  // implicit clone — c is a readonly ref, not owned
        Console.WriteLine($"[clone-lent] from readonly ref={mine.Describe()}");
    }

    public static void Run()
    {
        owned var original = new Config(15, "origin");

        // Lend as borrowed — the callee clones it into an owned value
        FromBorrowed(original);
        // Lend as frozen
        FromFrozen(original);
        // Lend as readonly ref
        FromReadonlyRef(original);

        // The original is untouched — clones were independent copies
        Console.WriteLine($"[clone-lent] original intact={original.Describe()}");
    }
}
```

**Expected output:**
```
[clone-lent] from borrowed=Config(Timeout=15, Host=origin)
[clone-lent] from frozen=Config(Timeout=15, Host=origin)
[clone-lent] from readonly ref=Config(Timeout=15, Host=origin)
[clone-lent] original intact=Config(Timeout=15, Host=origin)
```

```mist
// ─── Lending tiers from owned bindings ────────────────────────────────────────

// PROOF: Owned bindings participate in all lending tiers (borrowed, frozen, readonly ref)
// PROOF: State transitions work correctly (Live → LentRef, Live → LentFrozen, etc.)
public class LendingDemo
{
    public static void AcceptReadonly(readonly ref Engine e)
    {
        Console.WriteLine($"[lending] readonly ref={e.Describe()}");
    }

    public static void AcceptFrozen(frozen Engine e)
    {
        Console.WriteLine($"[lending] frozen={e.Describe()}");
    }

    public static void AcceptBorrowed(borrowed Engine e)
    {
        e.Rev(100);
        Console.WriteLine($"[lending] borrowed mutated={e.Describe()}");
    }

    public static void Run()
    {
        owned var e = new Engine(2000, 90);

        // Lend as readonly ref — owner enters LentReadonly, regains Live after
        AcceptReadonly(e);
        Console.WriteLine($"[lending] after readonly={e.Describe()}");

        // Lend as frozen — owner enters LentFrozen, regains Live after
        AcceptFrozen(e);
        e.Rev(50);
        Console.WriteLine($"[lending] after frozen+rev={e.Describe()}");

        // Lend as borrowed — owner enters LentRef, regains Live after
        AcceptBorrowed(e);
        Console.WriteLine($"[lending] after borrowed={e.Describe()}");
    }
}
```

**Expected output:**
```
[lending] readonly ref=Engine(Rpm=2000, Temp=90)
[lending] after readonly=Engine(Rpm=2000, Temp=90)
[lending] frozen=Engine(Rpm=2000, Temp=90)
[lending] after frozen+rev=Engine(Rpm=2050, Temp=95)
[lending] borrowed mutated=Engine(Rpm=2150, Temp=105)
[lending] after borrowed=Engine(Rpm=2150, Temp=105)
```

```mist
// ─── Unrestricted aliasing ────────────────────────────────────────────────────

// PROOF: Unrestricted bindings alias the same object — mutation through one is
//        visible through the other. This is the baseline that owned bindings prevent.
public class AliasingDemo
{
    public static void Run()
    {
        var a = new Widget(1, "aliased");
        var b = a;            // unrestricted — a and b reference the same object
        b.SetValue(42);       // mutate through b
        Console.WriteLine($"[aliasing] a={a.Describe()}");   // change visible through a
        Console.WriteLine($"[aliasing] b={b.Describe()}");
        Console.WriteLine($"[aliasing] same object={a.GetValue() == b.GetValue()}");
    }
}
```

**Expected output:**
```
[aliasing] a=Widget(42, aliased)
[aliasing] b=Widget(42, aliased)
[aliasing] same object=True
```

Linear and affine structs are the exception to copying across tiers: they obey [drop-semantics Requirement 1.8](../drop-semantics/requirements.md#requirement-1-substructural-type-modifiers).

```mist
// ─── Value types satisfy all lending tiers ────────────────────────────────────

public struct Vec2
{
    public int X;
    public int Y;

    public Vec2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"Vec2({X}, {Y})";
    }
}

// PROOF: A value type (struct) satisfies readonly ref, frozen, borrowed, AND owned
//        parameters because it is copied — passing it never aliases the original.
public class ValueTypeTiersDemo
{
    public static void AcceptReadonly(readonly ref Vec2 v) { Console.WriteLine($"[value-tiers] readonly ref={v.Describe()}"); }
    public static void AcceptFrozen(frozen Vec2 v) { Console.WriteLine($"[value-tiers] frozen={v.Describe()}"); }
    public static void AcceptBorrowed(borrowed Vec2 v) { Console.WriteLine($"[value-tiers] borrowed={v.Describe()}"); }
    public static void AcceptOwned(owned Vec2 v) { Console.WriteLine($"[value-tiers] owned={v.Describe()}"); }

    public static void Run()
    {
        var v = new Vec2(3, 4);
        AcceptReadonly(v);
        AcceptFrozen(v);
        AcceptBorrowed(v);
        AcceptOwned(v);
        // Original unaffected — value types are copied across every tier
        Console.WriteLine($"[value-tiers] original intact={v.Describe()}");
    }
}
```

**Expected output:**
```
[value-tiers] readonly ref=Vec2(3, 4)
[value-tiers] frozen=Vec2(3, 4)
[value-tiers] borrowed=Vec2(3, 4)
[value-tiers] owned=Vec2(3, 4)
[value-tiers] original intact=Vec2(3, 4)
```

```mist
// ─── Borrowed recipient can read, write, and re-lend ──────────────────────────

// PROOF: A borrowed recipient has exclusive access — it can read, write, and
//        lend the value further as readonly ref, frozen, or borrowed.
public class BorrowedRecipientDemo
{
    public static void Observe(readonly ref Engine e) { Console.WriteLine($"[borrowed-recipient] re-lent readonly={e.Describe()}"); }
    public static void Freeze(frozen Engine e) { Console.WriteLine($"[borrowed-recipient] re-lent frozen={e.Describe()}"); }
    public static void ReBorrow(borrowed Engine e)
    {
        e.Rev(10);
        Console.WriteLine($"[borrowed-recipient] re-lent borrowed mutated={e.Describe()}");
    }

    public static void UseBorrowed(borrowed Engine e)
    {
        // Read
        Console.WriteLine($"[borrowed-recipient] read={e.GetRpm()}");
        // Write
        e.Rev(100);
        Console.WriteLine($"[borrowed-recipient] write={e.Describe()}");
        // Re-lend further
        Observe(e);
        Freeze(e);
        ReBorrow(e);
    }

    public static void Run()
    {
        owned var engine = new Engine(700, 70);
        UseBorrowed(engine);
        // Owner regains access after the borrow returns
        Console.WriteLine($"[borrowed-recipient] owner after return={engine.Describe()}");
    }
}
```

**Expected output:**
```
[borrowed-recipient] read=700
[borrowed-recipient] write=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent readonly=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent frozen=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent borrowed mutated=Engine(Rpm=810, Temp=81)
[borrowed-recipient] owner after return=Engine(Rpm=810, Temp=81)
```

```mist
// ─── Truth-table state demonstrations ─────────────────────────────────────────

// PROOF: Live state permits read, write, live view (readonly ref), freeze, borrow, move.
// PROOF: LentReadonly (owner has lent a readonly ref) permits read, write, live view, freeze.
// PROOF: LentFrozen (owner has lent a frozen) permits read, live view, freeze — write is blocked
//        only while the frozen lease is outstanding; after it returns the owner can write again.
public class TruthTableDemo
{
    public static void View(readonly ref Engine e) { Console.WriteLine($"[truth-table] view={e.GetRpm()}"); }
    public static void Freeze(frozen Engine e) { Console.WriteLine($"[truth-table] freeze={e.GetRpm()}"); }

    public static void Run()
    {
        // Live: all actions permitted
        owned var e = new Engine(1000, 100);
        Console.WriteLine($"[truth-table] live read={e.GetRpm()}");  // read
        e.Rev(100);                                                  // write
        Console.WriteLine($"[truth-table] live write={e.Describe()}");
        View(e);                                                     // live view (readonly ref)
        Freeze(e);                                                   // freeze

        // After the frozen lease returns, the owner is Live again and may write.
        e.Rev(50);
        Console.WriteLine($"[truth-table] after frozen returns, write={e.Describe()}");
    }
}
```

**Expected output:**
```
[truth-table] live read=1000
[truth-table] live write=Engine(Rpm=1100, Temp=110)
[truth-table] view=1100
[truth-table] freeze=1100
[truth-table] after frozen returns, write=Engine(Rpm=1150, Temp=115)
```

```mist
// ─── Move semantics ──────────────────────────────────────────────────────────

// PROOF: Move from owned to owned makes source dead
// PROOF: Move from owned to unrestricted makes source dead
// PROOF: Move into function parameter makes caller binding dead
public class MoveDemo
{
    public static void ConsumeEngine(owned Engine e)
    {
        e.Rev(1000);
        Console.WriteLine($"[move] consumed={e.Describe()}");
    }

    public static void Run()
    {
        // Move owned → owned
        owned var e1 = new Engine(3000, 100);
        owned var e2 = e1;
        // e1 is now dead
        Console.WriteLine($"[move] after owned-to-owned={e2.Describe()}");

        // Move owned → unrestricted
        owned var e3 = new Engine(4000, 110);
        var e4 = e3;
        // e3 is now dead, e4 holds value without ownership tracking
        Console.WriteLine($"[move] after owned-to-unrestricted={e4.Describe()}");

        // Move into function parameter
        owned var e5 = new Engine(5000, 120);
        ConsumeEngine(e5);
        // e5 is now dead
        Console.WriteLine($"[move] transfer complete");
    }
}
```

**Expected output:**
```
[move] after owned-to-owned=Engine(Rpm=3000, Temp=100)
[move] after owned-to-unrestricted=Engine(Rpm=4000, Temp=110)
[move] consumed=Engine(Rpm=6000, Temp=220)
[move] transfer complete
```

```mist
// ─── default as an owned source ───────────────────────────────────────────────

// PROOF: default(T) produces a fresh unique graph — valid owned source
// PROOF: bare `default` with an explicitly-typed owned LHS also yields ownership
// PROOF: each default produces an independent instance (no shared aliasing)
public class DefaultDemo
{
    public static void Run()
    {
        // Explicit default(T) into an owned binding
        owned var e1 = default(Engine);
        Console.WriteLine($"[default] explicit default(T)={e1.Describe()}");

        // Bare default with explicitly-typed owned LHS
        owned Engine e2 = default;
        Console.WriteLine($"[default] bare default={e2.Describe()}");

        // default produces a fresh graph — e1 and e2 are independent.
        // Mutating e1 does not affect e2 (proves no shared default instance).
        e1.Rev(100);
        Console.WriteLine($"[default] e1 after rev={e1.Describe()}");
        Console.WriteLine($"[default] e2 unaffected={e2.Describe()}");
    }
}
```

**Expected output:**
```
[default] explicit default(T)=Engine(Rpm=0, Temp=0)
[default] bare default=Engine(Rpm=0, Temp=0)
[default] e1 after rev=Engine(Rpm=100, Temp=10)
[default] e2 unaffected=Engine(Rpm=0, Temp=0)
```

```mist
// ─── Main orchestrator ────────────────────────────────────────────────────────

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("--- Ownership ---");
        NonOwnedBindingDemo.Run();
        OwnedTypeBindingDemo.Run();
        ProvenanceDemo.Run();
        ClonableDemo.Run();
        CloneFromLentDemo.Run();
        LendingDemo.Run();
        AliasingDemo.Run();
        ValueTypeTiersDemo.Run();
        BorrowedRecipientDemo.Run();
        TruthTableDemo.Run();
        MoveDemo.Run();
        DefaultDemo.Run();
        Console.WriteLine("--- Ownership V2 Done ---");
    }
}
```

**Expected output (full program):**
```
--- Ownership V2 ---
[non-owned-binding] readonly method=10
[non-owned-binding] describe=Widget(10, alpha)
[non-owned-binding] field from method=Widget(10, label-1)
[non-owned-binding] field from move=Widget(10, label-2)
[non-owned-binding] field from clone=Widget(10, shared-label)
[non-owned-binding] shared still usable=shared-label
[non-owned-binding] after move=Widget(10, shared-label)
[non-owned-binding] unrestricted mutable call=Widget(99, shared)
[owned-type-binding] initial=Engine(Rpm=1000, Temp=80)
[owned-type-binding] after rev=Engine(Rpm=1500, Temp=130)
[owned-type-binding] after second rev=Engine(Rpm=1700, Temp=150)
[owned-type-binding] getRpm=1700
[provenance] initial=Container(Inner=Widget(1, first))
[provenance] after replace=Container(Inner=Widget(2, second))
[provenance] c2=Container(Inner=Widget(3, third))
[iclonable] shared=Config(Timeout=30, Host=localhost)
[iclonable] cloned=Config(Timeout=30, Host=localhost)
[iclonable] shared after modify=Config(Timeout=60, Host=localhost)
[iclonable] clone unaffected=Config(Timeout=30, Host=localhost)
[iclonable] implicit clone=Config(Timeout=60, Host=localhost)
[iclonable] shared after second modify=Config(Timeout=90, Host=localhost)
[iclonable] implicit clone unaffected=Config(Timeout=60, Host=localhost)
[iclonable] vault from shared=Vault(Setting=Config(Timeout=90, Host=localhost))
[iclonable] shared after host change=Config(Timeout=90, Host=remote)
[iclonable] vault unaffected=Vault(Setting=Config(Timeout=90, Host=localhost))
[iclonable] vault after replace=Vault(Setting=Config(Timeout=90, Host=remote))
[clone-lent] from borrowed=Config(Timeout=15, Host=origin)
[clone-lent] from frozen=Config(Timeout=15, Host=origin)
[clone-lent] from readonly ref=Config(Timeout=15, Host=origin)
[clone-lent] original intact=Config(Timeout=15, Host=origin)
[lending] readonly ref=Engine(Rpm=2000, Temp=90)
[lending] after readonly=Engine(Rpm=2000, Temp=90)
[lending] frozen=Engine(Rpm=2000, Temp=90)
[lending] after frozen+rev=Engine(Rpm=2050, Temp=95)
[lending] borrowed mutated=Engine(Rpm=2150, Temp=105)
[lending] after borrowed=Engine(Rpm=2150, Temp=105)
[aliasing] a=Widget(42, aliased)
[aliasing] b=Widget(42, aliased)
[aliasing] same object=True
[value-tiers] readonly ref=Vec2(3, 4)
[value-tiers] frozen=Vec2(3, 4)
[value-tiers] borrowed=Vec2(3, 4)
[value-tiers] owned=Vec2(3, 4)
[value-tiers] original intact=Vec2(3, 4)
[borrowed-recipient] read=700
[borrowed-recipient] write=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent readonly=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent frozen=Engine(Rpm=800, Temp=80)
[borrowed-recipient] re-lent borrowed mutated=Engine(Rpm=810, Temp=81)
[borrowed-recipient] owner after return=Engine(Rpm=810, Temp=81)
[truth-table] live read=1000
[truth-table] live write=Engine(Rpm=1100, Temp=110)
[truth-table] view=1100
[truth-table] freeze=1100
[truth-table] after frozen returns, write=Engine(Rpm=1150, Temp=115)
[move] after owned-to-owned=Engine(Rpm=3000, Temp=100)
[move] after owned-to-unrestricted=Engine(Rpm=4000, Temp=110)
[move] consumed=Engine(Rpm=6000, Temp=220)
[move] transfer complete
[default] explicit default(T)=Engine(Rpm=0, Temp=0)
[default] bare default=Engine(Rpm=0, Temp=0)
[default] e1 after rev=Engine(Rpm=100, Temp=10)
[default] e2 unaffected=Engine(Rpm=0, Temp=0)
--- Ownership V2 Done ---
```

### Requirement 17: Round-Trip Parsing

**User Story:** As a language developer, I want ownership annotations to survive parse-print cycles so that tooling can reliably reconstruct source.

#### Acceptance Criteria

1. FOR ALL valid owned type declarations (`owned class Foo`, `owned struct Bar`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
2. FOR ALL valid owned binding declarations (`owned var x = ...`, `owned Foo x = ...`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
3. FOR ALL valid IClonable interface implementations, parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
4. IF parsing the pretty-printed output produces parse errors, THE round-trip property SHALL be considered violated.

### Requirement 18: Book Documentation

**User Story:** As a Mist developer, I want the language book to document the ownership system, so that I can learn its syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL document the ownership system in `advanced/ownership.md`, covering the cooperation contract, method call rules for owned vs non-owned types, field assignment provenance, and IClonable. The chapter SHALL be written as the single authoritative description of ownership — no references to prior versions or migration.
2. THE book chapter SHALL document: ownership as a binding-level property, the owned type cooperation contract, method visibility rules for non-owned types in owned bindings, field assignment provenance, move semantics, and IClonable bridging (including implicit cloning).
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the `advanced/ownership.md` chapter.

### Requirement 19: Breaking Changes from v1 (No Backwards Compatibility)

**User Story:** As a Mist language maintainer, I want v2 to be a clean break from v1 with no backwards compatibility shims, so that the language stays simple and old unsupported patterns are clearly rejected rather than silently tolerated.

#### Acceptance Criteria

1. THE Compiler SHALL NOT provide any backwards-compatibility behavior for v1 ownership patterns — there are no deprecation warnings, no silent acceptance of removed constructs, and no compatibility flags.
2. WHEN code uses the removed `owned` field modifier, THE Compiler SHALL report a hard compile error (Requirement 11), not a warning.
3. WHEN code relies on the v1 owned-type field restriction (expecting non-owned fields to be rejected at the type-declaration site), THE Compiler SHALL silently accept the field types — the v1 restriction no longer exists (Requirement 10). Any code that depended on that rejection for correctness must be re-validated by the developer.
4. WHEN code relies on v1 struct ownership inference semantics that differ from v2 binding-level ownership, THE developer SHALL update the code — the Compiler provides no inference-compatibility mode.
5. `owned var` bindings and move semantics that were valid in v1 SHALL remain valid in v2 (these mechanics are unchanged), but this is a consequence of the design overlap, not an explicit compatibility guarantee.

