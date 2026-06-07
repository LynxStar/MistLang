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

### Requirement 1: Owned Binding — Any Type Can Be Owned

**User Story:** As a Mist developer, I want to declare any type as owned at the binding level, so that I can get unique ownership guarantees regardless of the type's declaration.

#### Acceptance Criteria

1. WHEN a variable is declared with the `owned` specifier (e.g., `owned var x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
2. WHEN a variable is declared with the `owned` specifier and an explicit type (e.g., `owned Foo x = new Foo()`), THE Parser SHALL attach an owned ownership specifier to the variable declaration node.
3. WHEN an owned binding is declared with an initializer of any class or struct type, THE Type_Checker SHALL accept the declaration — the type does not need to be declared `owned`.
4. WHEN an owned binding is declared, THE Type_Checker SHALL verify that the initializer is a value-producing expression that yields unique ownership: a constructor call (`new`), a call to a function returning an owned value, or a move from another owned binding.
5. IF an owned binding is declared with an initializer that does not yield unique ownership (e.g., a reference to a shared or unrestricted binding), AND the initializer's type implements `IClonable<T>`, THEN THE Compiler SHALL automatically insert a `.Clone()` call to produce a fresh owned value.
6. IF an owned binding is declared with an initializer that does not yield unique ownership AND the initializer's type does not implement `IClonable<T>`, THEN THE Type_Checker SHALL report a compile error indicating that an owned binding requires a uniquely-owned value.

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

1. WHEN assigning to a field of an instance held in an owned binding, THE Type_Checker SHALL require that the source expression yields an owned value: a move from an owned binding, a constructor call (`new`), or a call to a function returning `owned T`.
2. IF the source expression for a field assignment on an owned-bound instance is an unrestricted binding of a reference type that is not owned, not a fresh value, AND the type does not implement `IClonable<T>`, THEN THE Type_Checker SHALL report a compile error indicating that field assignment on an owned binding requires an owned source.
3. IF the source expression for a field assignment on an owned-bound instance is an unrestricted binding of a reference type that is not owned but its type implements `IClonable<T>`, THEN THE Compiler SHALL automatically insert a `.Clone()` call to produce a fresh owned value.
4. WHEN the source expression is a value type (struct), THE Type_Checker SHALL permit the assignment without provenance restriction — value types are copied and cannot introduce aliasing.
5. WHEN the source expression is a primitive type, THE Type_Checker SHALL permit the assignment without provenance restriction.

### Requirement 5: Owned Type Declaration — Cooperation Contract

**User Story:** As a Mist type author, I want to declare my type as `owned` to signal that I proactively cooperate with ownership semantics, so that users of my type get full mutable access when holding it in an owned binding.

#### Acceptance Criteria

1. WHEN a class is declared with the `owned` modifier (e.g., `owned class Foo`), THE Parser SHALL attach an owned type modifier to the type declaration node.
2. WHEN a struct is declared with the `owned` modifier (e.g., `owned struct Bar`), THE Parser SHALL attach an owned type modifier to the type declaration node.
3. WHEN a type is declared `owned`, THE Type_Checker SHALL verify the cooperation contract: all field assignments inside the type's method bodies must have sources that are owned (parameters declared `owned`, constructor calls, owned-returning functions, or `this`-field reads).
4. IF a method body in an owned type assigns to a field from a non-owned source (e.g., an unrestricted parameter, a static field, a global), THEN THE Type_Checker SHALL report a compile error identifying the assignment, the non-owned source, and explaining that owned types must maintain ownership invariants in all mutations.
5. WHEN a field assignment source is not owned but its type implements `IClonable<T>`, THE Compiler SHALL automatically insert a `.Clone()` call to produce a fresh owned value — this is the same implicit cloning behavior as Requirement 1 AC 5. This applies regardless of the source's binding tier (borrowed, frozen, readonly ref, or unrestricted).
6. THE Type_Checker SHALL NOT restrict what field types an owned type can have — any type is permitted as a field in an owned type.
7. IF the `owned` modifier is applied to an interface or trait declaration, THEN THE Type_Checker SHALL report a compile error stating that the `owned` modifier cannot be applied to interfaces or traits.

### Requirement 6: Owned Type — Constructor Safety

**User Story:** As a Mist type author, I want the compiler to verify that my owned type's constructors only assign from safe sources, so that the cooperation contract holds from the moment of construction.

#### Acceptance Criteria

1. WHEN an owned type's constructor assigns to a field, THE Type_Checker SHALL require the source to be an owned value: a parameter declared `owned`, a constructor call, an owned-returning function call, or a literal/primitive.
2. IF an owned type's constructor assigns to a field from a non-owned parameter or a static/global reference, THEN THE Type_Checker SHALL report a compile error.
3. WHEN an owned type's constructor assigns a value type (struct or primitive) to a field, THE Type_Checker SHALL permit the assignment without ownership restriction.

### Requirement 7: Owned Binding — Move Semantics

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

### Requirement 8: Ownership State Transition Rules

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

### Requirement 9: IClonable Interface — Shared-to-Owned Bridge

**User Story:** As a Mist developer, I want a standard mechanism to produce a fresh owned value from a non-owned source, so that I can populate owned bindings and fields from shared data safely.

#### Acceptance Criteria

1. THE standard library SHALL define an `IClonable<T>` interface with a single method: `public owned T Clone()`.
2. WHEN assigning to an owned destination (owned binding or field of an owned-bound instance) from a non-owned source, THE Type_Checker SHALL check if the source type implements `IClonable<T>`.
3. IF the source type implements `IClonable<T>`, THE Type_Checker SHALL accept the assignment — `.Clone()` is invoked (implicitly or explicitly) to produce a fresh owned value.
4. IF the source type does not implement `IClonable<T>` and the source is not already owned, THEN THE Type_Checker SHALL report a compile error indicating that the value cannot be proven unique and suggesting implementation of `IClonable<T>`.
5. THE Compiler SHALL auto-derive `IClonable<T>` for a type if ALL of its fields have types that implement `IClonable<T>`. The bootstrap value types (`Int32`, `Boolean`, `Float64`, etc.) and `String` SHALL implement `IClonable<T>` as base cases. This rule applies recursively — a struct whose fields all implement `IClonable<T>` auto-derives it, regardless of whether those fields are structs or classes.
6. WHEN a type has any field whose type does NOT implement `IClonable<T>`, THE Compiler SHALL NOT auto-derive `IClonable<T>` for that type — the developer must implement it manually or the type is not clonable.
7. THE CSharp_Emitter SHALL emit `.Clone()` calls where implicit cloning occurs, except for pure-struct types where the compiler may optimize away the call (value semantics already provide the copy).

### Requirement 10: Interaction with Existing Lending Tiers

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

### Requirement 11: Removal of v1 Owned-Type Field Restriction

**User Story:** As a Mist type author, I want owned types to be free to declare any field type, so that the cooperation contract focuses on behavioral safety rather than structural restriction.

#### Acceptance Criteria

1. WHEN an owned type declares a field of any reference type (class, interface, trait), THE Type_Checker SHALL permit the field without requiring the field's type to itself be an owned type.
2. THE Type_Checker SHALL NOT report `MIST_OWN_FIELD_NOT_OWNED` or any equivalent diagnostic that restricts field types of owned type declarations.
3. THE Type_Checker SHALL NOT report the `MIST_OWN_TYPE_REQUIRES_OWNED` diagnostic — this specific diagnostic (which enforced the v1 structural "is this type eligible for ownership" check) is removed entirely.
4. WHEN an existing codebase uses `owned` type declarations with non-owned field types, THE Compiler SHALL accept these declarations without error under v2 rules.

### Requirement 12: Removal of Owned Field Modifier

**User Story:** As a Mist developer, I want the `owned` field modifier removed, because binding-level ownership makes per-field ownership annotation redundant.

#### Acceptance Criteria

1. IF a field declaration uses the `owned` modifier (e.g., `owned MyClass data;`), THE Parser SHALL continue to parse it without error for migration compatibility.
2. THE Type_Checker SHALL emit a deprecation warning when an `owned` field modifier is encountered, indicating that field-level ownership is superseded by binding-level ownership in v2.
3. THE Type_Checker SHALL NOT enforce any special semantics for the `owned` field modifier — it has no effect under v2 rules.
4. THE `MIST_OWN_FIELD_NOT_OWNED` diagnostic SHALL be completely removed from the compiler — all diagnostic code, error message definitions, and references to this diagnostic SHALL be deleted from the codebase entirely.

### Requirement 13: Removal of Struct Ownership Inference

**User Story:** As a Mist developer, I want struct ownership inference removed because any type can now be owned at the binding level, making inference unnecessary.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT infer ownership status for struct declarations — there is no concept of a struct "being inferred as an owned type."
2. WHEN a struct is used in an owned binding (e.g., `owned var p = new Point(3, 4)`), THE Type_Checker SHALL accept the declaration regardless of the struct's field types.
3. WHEN a struct is declared with the explicit `owned` modifier, THE Type_Checker SHALL treat it as an owned type with the cooperation contract (same as classes).
4. WHEN a struct without the `owned` modifier is held in an owned binding, THE Type_Checker SHALL apply non-owned type rules as defined in Requirement 2.

### Requirement 14: Ownership Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for ownership violations under the v2 rules, so that I can understand and fix issues quickly.

#### Acceptance Criteria

1. WHEN a mutable method is called on a non-owned type held in an owned binding, THE Type_Checker SHALL report an error identifying the method name, the type name, and explaining that non-owned types appear readonly in owned bindings.
2. WHEN a field assignment on an owned-bound instance uses a non-owned source, THE Type_Checker SHALL report an error identifying the field, the source expression, and explaining the provenance requirement.
3. WHEN a use-after-move occurs, THE Type_Checker SHALL report an error identifying the binding name, the move location, and the invalid access location.
4. WHEN an owned type's method body assigns to a field from a non-owned source, THE Type_Checker SHALL report an error identifying the method, the field, and the non-owned source.
5. WHEN an owned type's constructor assigns to a field from a non-owned source, THE Type_Checker SHALL report the same error as method bodies (cooperation contract violation).
6. WHEN a deprecated `owned` field modifier is encountered, THE Type_Checker SHALL report a warning indicating that the modifier is deprecated and has no effect in v2.
7. WHEN an IClonable clone is needed but the type does not implement IClonable, THE Type_Checker SHALL report an error suggesting the type implement `IClonable<T>`.

### Requirement 15: C# Emission Strategy

**User Story:** As a language developer, I want ownership v2 to be purely compile-time with no runtime cost, so that the emitted C# is clean and performant.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL NOT emit any runtime ownership tracking (no reference counting, no state fields, no runtime assertions).
2. THE CSharp_Emitter SHALL emit all owned bindings as standard C# variable declarations.
3. THE CSharp_Emitter SHALL NOT emit the `owned` keyword in generated C# — ownership annotations are erased.
4. WHEN an owned binding is moved, THE CSharp_Emitter SHALL emit a standard assignment (no nulling of source, no runtime move tracking).
5. THE CSharp_Emitter SHALL emit owned type declarations as standard C# class or struct declarations without any ownership-related attributes.
6. THE CSharp_Emitter SHALL emit `IClonable<T>` as a standard C# interface with a `Clone()` method.
7. WHEN implicit cloning occurs, THE CSharp_Emitter SHALL emit an explicit `.Clone()` call at the assignment site.

### Requirement 16: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want ownership-related keywords to be syntax-highlighted correctly under v2 semantics, so that I get visual cues for the ownership system.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `owned` preceding `class` or `struct` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `owned class` or `owned struct` as a type name (scope `entity.name.type.class.mist` or `entity.name.type.struct.mist` respectively).
3. THE TextMate grammar SHALL recognize `owned` preceding a variable type or `var` as a storage modifier keyword (scope `storage.modifier.mist`).
4. THE TextMate grammar SHALL match `owned` only at word boundaries to avoid false matches within identifiers.
5. THE TextMate grammar SHALL NOT require any changes for IClonable — it follows existing interface highlighting patterns.

### Requirement 17: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the v2 ownership system, so that I can see every supported usage pattern and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/OwnershipV2/Program.mist` with a corresponding `mist.toml` where `name = "OwnershipV2"`.
2. The sample SHALL demonstrate each of the following patterns: owned binding of a non-owned type (restricted access), owned binding of an owned type (full access), field assignment provenance enforcement, IClonable bridging from shared to owned, move semantics, lending tiers from owned bindings (borrowed, frozen, readonly ref), mutable method access on owned types in owned bindings, the cooperation contract in action.
3. Each demonstrated pattern SHALL print a unique labeled prefix to stdout so that correctness of each pattern is independently observable.
4. The sample SHALL expose a `public static void RunDemo()` method in `OwnershipV2.Program` that executes all demonstrations.
5. `samples/AllFeatures/Program.mist` SHALL call `OwnershipV2.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../OwnershipV2"`.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.

#### Proof Code

```mist
// ─── Owned binding of a NON-OWNED type (restricted access) ────────────────────

using Standard;

namespace OwnershipV2;

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
public class NonOwnedBindingDemo
{
    public static void Run()
    {
        owned var w = new Widget(10, "alpha");

        // Readonly methods are callable
        var val = w.GetValue();
        Console.WriteLine($"[non-owned-binding] readonly method={val}");
        Console.WriteLine($"[non-owned-binding] describe={w.Describe()}");

        // Direct field assignment with provenance (new = constructor call = owned)
        w.Label = "beta";
        Console.WriteLine($"[non-owned-binding] field assign={w.Describe()}");

        // Move to another owned binding — proves move semantics work on non-owned types
        owned var w2 = w;
        Console.WriteLine($"[non-owned-binding] after move={w2.Describe()}");
    }
}
```

**Expected output:**
```
[non-owned-binding] readonly method=10
[non-owned-binding] describe=Widget(10, alpha)
[non-owned-binding] field assign=Widget(10, beta)
[non-owned-binding] after move=Widget(10, beta)
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
public class ClonableDemo
{
    public static void Run()
    {
        // Non-owned source
        var shared = new Config(30, "localhost");
        Console.WriteLine($"[iclonable] shared={shared.Describe()}");

        // Clone into an owned binding — IClonable bridge
        owned var myConfig = shared.Clone();
        Console.WriteLine($"[iclonable] cloned={myConfig.Describe()}");

        // Modify the shared original — owned clone is unaffected
        shared.Timeout = 60;
        Console.WriteLine($"[iclonable] shared after modify={shared.Describe()}");
        Console.WriteLine($"[iclonable] clone unaffected={myConfig.Describe()}");
    }
}
```

**Expected output:**
```
[iclonable] shared=Config(Timeout=30, Host=localhost)
[iclonable] cloned=Config(Timeout=30, Host=localhost)
[iclonable] shared after modify=Config(Timeout=60, Host=localhost)
[iclonable] clone unaffected=Config(Timeout=30, Host=localhost)
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
// ─── Main orchestrator ────────────────────────────────────────────────────────

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("--- Ownership V2 ---");
        NonOwnedBindingDemo.Run();
        OwnedTypeBindingDemo.Run();
        ProvenanceDemo.Run();
        ClonableDemo.Run();
        LendingDemo.Run();
        MoveDemo.Run();
        Console.WriteLine("--- Ownership V2 Done ---");
    }
}
```

**Expected output (full program):**
```
--- Ownership V2 ---
[non-owned-binding] readonly method=10
[non-owned-binding] describe=Widget(10, alpha)
[non-owned-binding] field assign=Widget(10, beta)
[non-owned-binding] after move=Widget(10, beta)
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
[lending] readonly ref=Engine(Rpm=2000, Temp=90)
[lending] after readonly=Engine(Rpm=2000, Temp=90)
[lending] frozen=Engine(Rpm=2000, Temp=90)
[lending] after frozen+rev=Engine(Rpm=2050, Temp=95)
[lending] borrowed mutated=Engine(Rpm=2150, Temp=105)
[lending] after borrowed=Engine(Rpm=2150, Temp=105)
[move] after owned-to-owned=Engine(Rpm=3000, Temp=100)
[move] after owned-to-unrestricted=Engine(Rpm=4000, Temp=110)
[move] consumed=Engine(Rpm=6000, Temp=220)
[move] transfer complete
--- Ownership V2 Done ---
```

### Requirement 18: Round-Trip Parsing

**User Story:** As a language developer, I want ownership annotations to survive parse-print cycles so that tooling can reliably reconstruct source.

#### Acceptance Criteria

1. FOR ALL valid owned type declarations (`owned class Foo`, `owned struct Bar`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
2. FOR ALL valid owned binding declarations (`owned var x = ...`, `owned Foo x = ...`), parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
3. FOR ALL valid IClonable interface implementations, parsing then pretty-printing then parsing again SHALL produce an equivalent syntax tree.
4. IF parsing the pretty-printed output produces parse errors, THE round-trip property SHALL be considered violated.

### Requirement 19: Book Documentation

**User Story:** As a Mist developer, I want the language book to document the v2 ownership system, so that I can learn the revised semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL update the existing `advanced/ownership.md` chapter to reflect v2 semantics, including the cooperation contract, method call rules for owned vs non-owned types, field assignment provenance, and IClonable.
2. THE book chapter SHALL document: the semantic shift (ownership is binding-level, not type-level), owned type cooperation contract meaning, method visibility rules for non-owned types in owned bindings, field assignment provenance, IClonable bridging, and migration notes from v1.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL retain the existing reference to `advanced/ownership.md`.

### Requirement 20: Migration from v1

**User Story:** As a Mist developer with existing v1 code, I want a clear migration path to v2, so that my code continues to work with minimal changes.

#### Acceptance Criteria

1. WHEN existing code declares an `owned` type with fields that are all owned types, THE Compiler SHALL accept the declaration without changes — the code is valid under both v1 and v2 (the cooperation contract is satisfied if all field assignments use owned sources).
2. WHEN existing code uses `owned var` bindings with move semantics, THE Compiler SHALL accept the code without changes — binding-level ownership is unchanged.
3. WHEN existing code uses the `owned` field modifier, THE Compiler SHALL emit a deprecation warning but continue compilation — the modifier has no semantic effect in v2.
4. WHEN existing code relies on struct ownership inference, THE Compiler SHALL continue to accept the code — structs can be owned at the binding level without needing inference.

