# Requirements Document

## Introduction

This document specifies the requirements for **Owned Fields** — a mechanism to reintroduce the `owned` modifier on individual fields in the Mist programming language. This is a **delta on top of ownership-v2** (spec: `ownership-v2`), modifying and extending specific requirements from that spec.

**Core insight:** Ownership can be "jagged" — parts of an object graph opt into ownership semantics and parts don't. The `owned type` declaration guarantees FULL ownership (all fields participate), but `owned var foo = new Anything()` only guarantees the root binding is owned. Fields within `foo` that aren't explicitly marked may still contain shared references. Owned fields solve this gap: marking a field as `owned` means that field can ONLY be mutated via ownership semantics, regardless of whether the containing type is itself an `owned type`.

**What this changes from ownership-v2:**
- Reverses Requirement 11 (removal of owned field modifier) — `owned` on fields is now VALID again with new semantics
- Modifies Requirement 10 (removal of field restriction) — `owned type` now has a field restriction again: all fields must be owned types or implement `IClonable<T>`
- Modifies Requirement 5 (cooperation contract) — the contract now includes the field-type restriction for owned types
- Adds new semantics: owned field mutation requires an owned binding at the call site
- Potentially relaxes Requirement 2 (method call rules for non-owned types) — since owned fields protect themselves, the blanket "hide all mutable methods" rule on non-owned types in owned bindings may be relaxed or removed

**What stays the same from ownership-v2:**
- `owned var x = ...` binding-level ownership
- Move semantics, lending system, truth table
- `IClonable<T>` bridging
- Zero runtime cost
- `MoveBoundaryResolver` as single enforcement point
- `CooperationContractVerifier` (but scope changes)

## Glossary

- **Owned_Field**: A field declared with the `owned` modifier (e.g., `owned Widget data;`). The field can only be mutated through a method call where the receiver is held in an owned binding. This enforces ownership semantics on that specific field regardless of the containing type's ownership status.
- **Owned_Type**: A class or struct declared with the `owned` modifier. Under this spec, an owned type is equivalent to a type where ALL fields are implicitly `owned`. The type author additionally guarantees cooperation via the cooperation contract.
- **Non_Owned_Type**: A type without the `owned` modifier. May still have individual fields marked `owned`, creating "jagged" ownership where some fields are ownership-protected and some are not.
- **Owned_Binding**: A variable declaration using `owned var x = ...` or `owned T x = ...`. The binding guarantees the variable is the unique owner of the value and its entire object graph.
- **Field_Type_Restriction**: The rule that all fields of an `owned type` must have types that are themselves owned types OR implement `IClonable<T>`. This ensures that an `owned type` guarantees FULL ownership of its entire graph — every field participates in the ownership discipline.
- **Cooperation_Contract**: Under this spec, the cooperation contract from ownership-v2 is subsumed by owned-field provenance enforcement (Requirement 8) applied to all fields of an owned type (which are implicitly owned per Requirement 3). The separate `CooperationContractVerifier` pass is no longer needed — provenance is enforced uniformly on all owned fields. The remaining guarantee specific to `owned type` is the field-type restriction (Requirement 4).
- **Owned_Field_Access_Rule**: The rule governing what operations are permitted on an owned field based on the parent binding's status. Through an owned parent: full access (move, borrow, freeze, readonly ref, write with provenance). Through a shared parent: write with provenance and clone-read only — no extraction (move/borrow/freeze/readonly ref).
- **Composite_State**: The effective ownership state of a parent binding, computed as the most restrictive state among all its owned fields. Uses the ordering: Live < LentReadonly < LentFrozen < LentRef = Moved. The parent's permitted actions are determined by looking up this composite state in the standard truth table.
- **Partial_Move**: Moving a value out of an individual owned field, leaving the field in the Moved state while the parent binding and sibling fields remain accessible (subject to composite state restrictions). Only possible through an owned parent binding.
- **IClonable**: An interface `IClonable<T>` that bridges from shared to owned. Types implementing this interface are acceptable as field types in owned types because they can produce independent copies on demand.
- **Compiler**: The Mist compiler pipeline including lexer, parser, semantic model, and type checker.
- **Type_Checker**: The analysis phase that enforces ownership rules at compile time.
- **Parser**: The syntax analysis phase that recognizes ownership-related keywords.
- **CSharp_Emitter**: The backend that generates C# code from the Mist semantic model.
- **This_Tier**: The ownership tier required on `this` for a method to execute safely. Inferred from the method body's usage of owned fields, or declared explicitly with `with {tier}`. The call site must provide a receiver binding that satisfies this tier.
- **Ownership_Inference**: The process by which the compiler determines the minimum ownership tier of a `var`-declared local variable by analyzing all uses of that variable within the enclosing function body. Avoids requiring explicit annotations on every binding.

## Requirements

### Requirement 1: Owned Field Declaration — Syntax and Parsing

**User Story:** As a Mist type author, I want to mark individual fields with the `owned` modifier, so that I can opt specific parts of my type into ownership semantics without requiring the entire type to be `owned`.

#### Acceptance Criteria

1. WHEN a field is declared with the `owned` ownership specifier (e.g., `owned Widget data;`), THE Parser SHALL accept the declaration and produce an `OwnedSpecifier` node in the ownership-specifier position of the field declaration AST node.
2. WHEN a field is declared with both a visibility modifier and the `owned` ownership specifier (e.g., `public owned Widget data;`), THE Parser SHALL accept the declaration with the visibility modifier in the modifiers list and the `owned` specifier in the ownership-specifier position, preserving the grammar-mandated order (modifiers before ownership specifier).
3. THE Parser SHALL accept the `owned` ownership specifier on fields of any reference type (class, interface, trait).
4. THE Parser SHALL accept the `owned` ownership specifier on fields of value types (structs) without producing any error or warning — the declaration is syntactically and semantically valid, though the ownership guard has no behavioral effect at runtime because value types are copied.
5. IF the `owned` ownership specifier is applied to a static field, THEN THE Type_Checker SHALL report a compile error indicating that static fields cannot be marked `owned` — ownership semantics require an instance-level owner.
6. IF a field declaration combines the `owned` ownership specifier with another ownership specifier (`frozen`, `borrowed`, or `readonly ref`), THEN THE Parser or Type_Checker SHALL report a compile error indicating that only one ownership specifier is permitted per field declaration.

### Requirement 2: Owned Field Access Through Shared vs. Owned Parent

**User Story:** As a Mist developer, I want the compiler to enforce that extracting data from an owned field (move, borrow, freeze, readonly ref) is only permitted through an owned parent binding, while writing to an owned field (with provenance) is permitted through any binding.

#### Acceptance Criteria

1. WHEN the parent binding is an owned binding, THE Type_Checker SHALL permit all ownership operations on the owned field: move out, borrow, freeze, readonly ref, and write (with provenance). Per-field state tracking (Requirement 16) applies.
2. WHEN the parent binding is a shared (non-owned) binding, THE Type_Checker SHALL permit writing to an owned field provided the source satisfies provenance (Requirement 8) — multiple aliases can independently write owned values into the field.
3. WHEN the parent binding is a shared (non-owned) binding, THE Type_Checker SHALL permit reading the owned field if the field's type implements `IClonable<T>` — the read produces a clone (the field is not consumed).
4. WHEN the parent binding is a shared (non-owned) binding, THE Type_Checker SHALL NOT permit moving out of, borrowing, freezing, or taking a readonly ref of an owned field — these operations require exclusive access that only an owned parent can guarantee.
5. IF code attempts to move out of, borrow, freeze, or readonly-ref an owned field through a shared parent binding, THEN THE Type_Checker SHALL report a compile error explaining that extracting data from an owned field requires an owned binding to the parent.
6. WHEN the parent binding is a shared (non-owned) binding and code reads an owned field whose type does NOT implement `IClonable<T>`, THE Type_Checker SHALL report a compile error — the field cannot be cloned and cannot be moved out through a shared parent.

### Requirement 3: Owned Type Implies All Fields Are Owned

**User Story:** As a Mist type author, I want `owned type` to implicitly treat all fields as `owned`, so that declaring a type `owned` guarantees full ownership of the entire instance graph without explicitly marking each field.

#### Acceptance Criteria

1. WHEN a type is declared `owned` (e.g., `owned class Vault`), THE Type_Checker SHALL treat every instance field of that type as if it were declared `owned`, even when the field declaration does not explicitly carry the `owned` modifier.
2. WHEN an `owned type` has a field with an explicit `owned` modifier, THE Type_Checker SHALL accept the declaration without error — the explicit annotation is redundant but not harmful.
3. THE Type_Checker SHALL apply the owned-field access rules (Requirement 2) and provenance enforcement (Requirement 8) to ALL fields of an owned type, not just explicitly marked ones.
4. THE Type_Checker SHALL apply per-field state tracking (Requirement 16) to ALL fields of an owned type when the instance is held in an owned binding.

### Requirement 4: Owned Type Field-Type Restriction

**User Story:** As a Mist type author, I want the compiler to enforce that all fields in an `owned type` have types that are themselves owned types or implement `IClonable<T>`, so that `owned type` guarantees FULL ownership of the entire object graph — every field participates.

#### Acceptance Criteria

1. WHEN an `owned type` declares a field whose type is another `owned type`, THE Type_Checker SHALL accept the field declaration — owned types satisfy the field-type restriction.
2. WHEN an `owned type` declares a field whose type implements `IClonable<T>`, THE Type_Checker SHALL accept the field declaration — clonable types can produce independent copies and bridge into ownership.
3. WHEN an `owned type` declares a field whose type is a value type (struct or primitive), THE Type_Checker SHALL accept the field declaration without restriction — value types use copy semantics at the field level and cannot introduce aliasing regardless of their internal structure.
4. IF an `owned type` declares one or more fields whose types are non-owned reference types that do NOT implement `IClonable<T>`, THEN THE Type_Checker SHALL report a compile error for each violating field, identifying the field name, its declared type, and explaining that all fields of an owned type must be owned types or implement `IClonable<T>` to guarantee full graph ownership.
5. THE Type_Checker SHALL evaluate the field-type restriction after IClonable derivation is complete, so auto-derived IClonable implementations are considered.
6. IF an `owned type` declares a field whose type is a generic type parameter that is not constrained to be an owned type or to implement `IClonable<T>`, THEN THE Type_Checker SHALL report a compile error identifying the field and the unconstrained type parameter — generic fields must be statically provable as owned or clonable to satisfy the field-type restriction.

### Requirement 5: Owned Type Field-Type Restriction Enforcement

**User Story:** As a Mist type author, I want the compiler to verify that all fields of an `owned type` satisfy the field-type restriction (Requirement 4) as a standalone check, so that `owned type` guarantees full graph ownership.

#### Acceptance Criteria

1. WHEN a type is declared `owned`, THE Type_Checker SHALL verify the field-type restriction (Requirement 4) — all fields must have types that are owned types, implement `IClonable<T>`, or are value types.
2. IF an owned type has a field violating the field-type restriction, THEN THE Type_Checker SHALL report a compile error specific to the field-type violation, identifying the field, its type, and the restriction.
3. THE field-type restriction check SHALL run independently of provenance enforcement — a provenance violation does not suppress the field-type check, and vice versa.
4. THE behavioral cooperation contract from ownership-v2 (verifying field assignment sources) is subsumed by owned-field provenance enforcement (Requirement 8) applied to all fields (which are all implicitly owned per Requirement 3). No separate behavioral contract pass is needed for owned types.

### Requirement 6: Removal of Method Visibility Hiding

**User Story:** As a Mist developer, I want the blanket "hide mutable methods on non-owned types in owned bindings" rule from ownership-v2 removed entirely, because the this-tier system (Requirement 17) replaces it with precise per-method access control.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT hide mutable methods on non-owned types held in owned bindings — the `MethodTierVisibilityFilter` blanket hiding logic from ownership-v2 is removed.
2. THE `MIST_OWN_MUTABLE_HIDDEN` diagnostic SHALL be removed from the compiler — it is replaced by `MIST_OWN_THIS_TIER_MISMATCH` (Requirement 17 AC 16) which provides precise per-method feedback.
3. ALL method calls SHALL be gated solely by the this-tier system: a method is callable if the caller's binding tier satisfies the method's inferred or declared this-tier (Requirement 17 AC 15). No other visibility restriction applies.
4. THE `MethodTierVisibilityFilter` component from ownership-v2 SHALL be removed or replaced by the this-tier call-site check in the flow-sensitive analysis.

### Requirement 7: Removal of MIST_OWN_FIELD_MODIFIER_REMOVED Diagnostic

**User Story:** As a Mist developer, I want the `MIST_OWN_FIELD_MODIFIER_REMOVED` hard error removed from the compiler since the `owned` field modifier is now valid syntax again with new semantics.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT emit the `MIST_OWN_FIELD_MODIFIER_REMOVED` diagnostic — the diagnostic ID definition, message builder, and all emission sites SHALL be deleted from the compiler source.
2. THE `RejectOwnedFieldModifier` pass in the `OwnershipAnalyzer` SHALL be removed and its orchestrator call site deleted; provenance enforcement (Requirement 8) and field access rules (Requirement 2) replace its validation responsibility.
3. WHEN existing code uses `owned` on field declarations that previously triggered `MIST_OWN_FIELD_MODIFIER_REMOVED`, THE Compiler SHALL accept the syntax without emitting any error or warning from the removed diagnostic, and SHALL apply the owned-field semantics defined in Requirements 1 through 10 of this spec.
4. THE unit tests in `OwnershipV2DiagnosticTests` that assert `MIST_OWN_FIELD_MODIFIER_REMOVED` is emitted SHALL be removed or replaced with tests that assert the compiler accepts `owned` field declarations and applies the new owned-field validation rules.

### Requirement 8: Owned Field Provenance Enforcement

**User Story:** As a Mist developer, I want the compiler to enforce that assignments to owned fields always come from owned sources, so that non-owned aliases cannot leak into ownership-protected fields.

#### Acceptance Criteria

1. WHEN assigning to an owned field (either in a constructor, a method body, or direct field assignment from an owned binding), THE Type_Checker SHALL require that the source expression yields an owned value — a move from an owned binding, a constructor call, a call to a function returning `owned T`, or a `default`/`default(T)` expression. The same provenance rules as ownership-v2 Requirement 4 apply, including the value-type and primitive exemption (value types and primitives are permitted without provenance restriction because they are copied).
2. THE MoveBoundaryResolver SHALL be used for all owned-field assignment provenance checks — no separate mechanism is introduced.
3. IF the source for an owned-field assignment does not yield an owned value and the source type implements `IClonable<T>`, THE Compiler SHALL implicitly clone the source by inserting a `.Clone()` call to produce a fresh owned value (existing IClonable semantics apply).
4. IF the source for an owned-field assignment does not yield an owned value and the source type does NOT implement `IClonable<T>`, THEN THE Type_Checker SHALL report a compile error indicating the provenance violation, identifying the field name and the non-owned source expression.

### ~~Requirement 9~~ (Removed)

Subsumed by Requirement 8. The statics problem and non-owned constructor problem described in `project/TODO.md` are solved as a direct consequence of Requirement 8's provenance enforcement: static reads are not owned sources, so they fail the MoveBoundaryResolver for any owned field assignment — regardless of whether the containing type is an owned type or not.

### ~~Requirement 10~~ (Removed)

Subsumed by Requirement 8. Non-owned types with owned fields get the same provenance enforcement as owned types per Requirement 8 AC 1 ("either in a constructor, a method body, or direct field assignment"). The MoveBoundaryResolver applies uniformly to all owned-field assignments regardless of the containing type's status.

### Requirement 11: Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for owned-field violations, so that I can understand and fix issues quickly.

#### Acceptance Criteria

1. WHEN code attempts to move out of, borrow, freeze, or readonly-ref an owned field through a shared (non-owned) parent binding, THE Type_Checker SHALL report an error identifying the field, the operation attempted, and explaining that extracting data from an owned field requires an owned binding to the parent.
2. WHEN an `owned type` has a field whose type is a reference type that is not an owned type and does not implement `IClonable<T>`, THE Type_Checker SHALL report an error identifying the field, its declared type, and the requirement for owned types to have fully-owned-or-clonable fields — value-type fields are exempt from this check.
3. WHEN an owned-field assignment uses a non-owned source that is not IClonable, THE Type_Checker SHALL report an error identifying the target field name, the source expression, and explaining the provenance requirement for owned fields.
4. WHEN a static field is marked `owned`, THE Type_Checker SHALL report an error explaining that the `owned` modifier cannot be applied to static fields.
5. THE `MIST_OWN_FIELD_MODIFIER_REMOVED` diagnostic SHALL be deleted entirely from the compiler.
6. IF multiple owned-field violations occur in the same compilation unit, THEN THE Type_Checker SHALL report each violation as an independent error rather than stopping at the first.
7. WHEN code reads an owned field through a shared parent binding and the field's type does not implement `IClonable<T>`, THE Type_Checker SHALL report an error explaining that the field cannot be read through a shared binding because it cannot be cloned and cannot be moved out without an owned parent.

### Requirement 12: C# Emission

**User Story:** As a language developer, I want owned fields to remain purely compile-time with no runtime cost, so that the emitted C# is clean and performant.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL emit fields declared `owned` as standard C# field declarations without any ownership-related modifiers, attributes, or wrappers — the `owned` annotation is erased and does not appear in generated C#.
2. THE CSharp_Emitter SHALL NOT emit any runtime tracking, reference counting, state fields, or assertions for owned fields — enforcement is purely compile-time.
3. WHEN the MoveBoundaryResolver resolves an owned-field assignment as `ImplicitClone`, THE CSharp_Emitter SHALL emit an explicit `.Clone()` call on the source expression at the assignment site.
4. WHEN the source expression of an owned-field assignment is a value type (struct), THE CSharp_Emitter SHALL NOT emit a `.Clone()` call — value-type copy semantics already produce an independent value.

### Requirement 13: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `owned` fields to be syntax-highlighted correctly, so that I get visual cues for which fields are ownership-protected.

#### Acceptance Criteria

1. THE TextMate grammar SHALL assign the scope `storage.modifier.mist` to the token `owned` when it appears immediately before a field type annotation (e.g., `owned int count`, `owned MyResource handle`) within a struct or class body.
2. THE TextMate grammar SHALL match `owned` only at word boundaries (`\b` anchors on both sides) so that identifiers containing the substring "owned" (e.g., `ownedCount`, `preowned`) are not assigned the `storage.modifier.mist` scope.
3. IF the existing `storage.modifier.mist` pattern already matches `owned` at word boundaries in all positions including the field-modifier position, THEN THE TextMate grammar SHALL NOT require a new rule, and the implementation task SHALL document the existing pattern and its coverage with a test case demonstrating `owned` before a field type receives the correct scope.
4. WHEN a test file containing an `owned` field declaration (e.g., `owned int count`) is opened in VS Code with the Mist extension, THE TextMate grammar SHALL tokenize `owned` with the scope `storage.modifier.mist` and the subsequent type token with its standard type scope (e.g., `storage.type.primitive.mist` for primitives or `entity.name.type.mist` for user-defined types).

### Requirement 14: Sample Application

**User Story:** As a Mist developer, I want the Ownership sample application updated to demonstrate owned fields, so that I can see the new feature patterns and verify correctness.

#### Acceptance Criteria

1. The existing `samples/Ownership/Program.mist` SHALL be updated to include demonstrations of owned fields, while preserving all existing demonstrations (`NonOwnedBindingDemo`, `OwnedTypeBindingDemo`, `ProvenanceDemo`, `ClonableDemo`, `CloneFromLentDemo`, `LendingDemo`, `AliasingDemo`, `ValueTypeTiersDemo`, `BorrowedRecipientDemo`, `TruthTableDemo`, `MoveDemo`, `DefaultDemo`).
2. The sample SHALL demonstrate each of the following patterns: declaring an owned field on a non-owned type, declaring an owned field on an owned type (redundant but valid), owned-field mutation through an owned binding, rejection pattern (documented as commented-out code with explanation) showing that mutation through a non-owned binding fails, owned-field provenance from constructor calls, owned-field provenance from moved locals, owned-field provenance from IClonable sources, the "jagged ownership" pattern (some fields owned, some not), an `owned type` with the field-type restriction satisfied, moving out of an owned field (partial move), reading an owned field into a shared destination (move semantics), cloning out of an owned field (IClonable fallback), restoring a moved field, composite state restriction (parent cannot be moved while a field is Moved), sibling field independence, field lending (freeze a field, show sibling independence and composite restriction), parent-to-child restriction (commented-out code showing parent-frozen blocks child write), this-tier inference (methods with different inferred tiers called from different binding tiers), explicit `with {tier}` annotation, `with owned` consuming the receiver, this-tier mismatch (commented-out showing shared cannot call borrowed-requiring method), and ownership inference on local bindings (var inferring tiers from usage).
3. Each demonstrated pattern SHALL print a unique labeled prefix in square-bracket format (e.g., `[owned-field]`, `[owned-type-restrict]`) to stdout so correctness of each pattern is independently observable.
4. `samples/AllFeatures/Program.mist` SHALL continue to call `Ownership.Program.RunDemo()` — no change to AllFeatures wiring needed since the sample name and entry point are unchanged.
5. The sample SHALL compile and run without errors or warnings using the Mist compiler.

#### Proof Code

```mist
// ─── Owned fields on a NON-OWNED type (jagged ownership) ──────────────────────

using Standard;

namespace Ownership;

// Non-owned type with SOME fields opted into ownership
public class Document
{
    // Owned field — only mutatable through an owned binding
    public owned string Content;

    // Non-owned field — freely mutatable
    public int Version;

    public Document(owned string content, int version)
    {
        this.Content = content;
        this.Version = version;
    }

    // This method mutates an owned field → requires owned binding at call site
    public void UpdateContent(owned string newContent)
    {
        this.Content = newContent;
        this.Version = this.Version + 1;
    }

    // This method only mutates non-owned fields → no owned binding required
    public void BumpVersion()
    {
        this.Version = this.Version + 1;
    }

    // Readonly method — no mutation, always callable
    public string Describe()
    {
        return $"Document(v{Version}: {Content})";
    }
}

// PROOF: Non-owned type with owned field — provenance enforced on writes
// PROOF: BumpVersion only mutates non-owned field — no owned semantics involved
// PROOF: Owned field provenance is enforced in constructor
// PROOF: Jagged ownership — owned and non-owned fields coexist
// PROOF: Shared parent can write to owned field (provenance satisfied)
// PROOF: Shared parent can clone-read from owned field (IClonable)
// PROOF: Owned parent can move out of owned field (partial move)
public class OwnedFieldDemo
{
    public static void Run()
    {
        // Owned binding — full access including extraction
        owned var doc = new Document("Hello, world", 1);
        Console.WriteLine($"[owned-field] initial={doc.Describe()}");

        // UpdateContent writes to owned field — provenance via owned parameter
        doc.UpdateContent("Updated content");
        Console.WriteLine($"[owned-field] after update={doc.Describe()}");

        // BumpVersion only mutates Version (non-owned) — no ownership semantics
        doc.BumpVersion();
        Console.WriteLine($"[owned-field] after bump={doc.Describe()}");

        // Direct field assignment to owned field from an owned source — provenance satisfied
        owned var freshContent = "Direct assignment";
        doc.Content = freshContent;
        Console.WriteLine($"[owned-field] direct assign={doc.Describe()}");

        // IClonable source into owned field — auto-clone
        var shared = "Cloned content";
        doc.Content = shared;  // string is IClonable → .Clone() inserted
        Console.WriteLine($"[owned-field] cloned={doc.Describe()}");
        Console.WriteLine($"[owned-field] shared still alive={shared}");

        // Non-owned field freely assignable (int is a value type)
        doc.Version = 99;
        Console.WriteLine($"[owned-field] version set={doc.Describe()}");

        // Shared parent — can write (provenance) and clone-read, but NOT extract
        var sharedDoc = new Document("shared-hello", 1);
        sharedDoc.UpdateContent("shared-updated");  // allowed — provenance on param
        Console.WriteLine($"[owned-field] shared write={sharedDoc.Describe()}");
        var readContent = sharedDoc.Content;  // clone-read — string is IClonable
        Console.WriteLine($"[owned-field] shared clone-read={readContent}");

        // WOULD FAIL: owned var stolen = sharedDoc.Content; // cannot move from shared parent
        // WOULD FAIL: borrowed ref b = sharedDoc.Content;   // cannot borrow from shared parent
    }
}

// ─── Owned type with field-type restriction ───────────────────────────────────

// All fields must be owned types or IClonable
public owned class Vault
{
    // String implements IClonable — accepted
    public owned string Secret;

    // Int is a value type — accepted
    public int AccessCount;

    public Vault(owned string secret)
    {
        this.Secret = secret;
        this.AccessCount = 0;
    }

    public void RotateSecret(owned string newSecret)
    {
        this.Secret = newSecret;
        this.AccessCount = this.AccessCount + 1;
    }

    public string Describe()
    {
        return $"Vault(accesses={AccessCount}, secret={Secret})";
    }
}

// PROOF: owned type satisfies field-type restriction (String is IClonable, int is value type)
// PROOF: owned type methods require owned binding at call site (all fields implicitly owned)
// PROOF: owned type cooperation contract still enforced
public class OwnedTypeFieldRestrictionDemo
{
    public static void Run()
    {
        owned var vault = new Vault("top-secret-123");
        Console.WriteLine($"[owned-type-restrict] initial={vault.Describe()}");

        vault.RotateSecret("new-secret-456");
        Console.WriteLine($"[owned-type-restrict] rotated={vault.Describe()}");

        // Direct field assignment — provenance from owned-returning expression
        vault.Secret = "replaced-789";
        Console.WriteLine($"[owned-type-restrict] replaced={vault.Describe()}");
    }
}

// ─── Partial moves and field-level state tracking ─────────────────────────────

// Type with two owned fields for demonstrating independent field states
public class TwoFields
{
    public owned string Left;
    public owned string Right;

    public TwoFields(owned string left, owned string right)
    {
        this.Left = left;
        this.Right = right;
    }
}

// PROOF: Moving out of an owned field transitions that field to Moved
// PROOF: Sibling fields remain accessible after one field is moved out
// PROOF: Reading an owned field into a shared destination is a move
// PROOF: Cloning out of an owned field (IClonable fallback) does not move the field
// PROOF: Restoring a moved field brings it back to Live
// PROOF: Parent cannot be moved/borrowed/frozen while any field is Moved (composite state)
public class PartialMoveDemo
{
    public static void Run()
    {
        owned var pair = new TwoFields("alpha", "beta");

        // Move Left out — Left enters Moved, Right is still Live
        owned var extracted = pair.Left;
        Console.WriteLine($"[partial-move] extracted={extracted}");

        // Sibling field Right is still accessible
        Console.WriteLine($"[partial-move] right still live={pair.Right}");

        // Clone out of Right — Right stays Live (IClonable fallback, no move)
        var cloned = pair.Right;  // string is IClonable → .Clone(), Right stays Live
        Console.WriteLine($"[partial-move] cloned from right={cloned}");
        Console.WriteLine($"[partial-move] right after clone={pair.Right}");

        // Restore Left — move a new value back in, field returns to Live
        pair.Left = "restored";
        Console.WriteLine($"[partial-move] left restored={pair.Left}");

        // Now both fields are Live again — parent composite is Live
        // Parent can be frozen/borrowed/moved again
        Console.WriteLine($"[partial-move] both live, left={pair.Left}, right={pair.Right}");
    }
}

// ─── Field lending and composite state ────────────────────────────────────────

// PROOF: Freezing an owned field transitions it to LentFrozen
// PROOF: While a field is frozen, sibling fields remain fully accessible
// PROOF: While a field is frozen, parent composite is LentFrozen (can still be read/frozen)
// PROOF: Parent-to-child: parent frozen externally blocks child write/borrow/move
// PROOF: After freeze scope ends, field returns to Live and parent composite is Live
public class FieldLendingDemo
{
    public static void Run()
    {
        owned var pair = new TwoFields("alpha", "beta");

        // Freeze Left — Left enters LentFrozen, composite is LentFrozen
        frozen var frozenLeft = pair.Left;
        Console.WriteLine($"[field-lending] frozen left={frozenLeft}");

        // Sibling Right is still fully accessible (sibling independence)
        Console.WriteLine($"[field-lending] right during freeze={pair.Right}");

        // Can still write to Right (sibling is Live, unaffected by Left's freeze)
        pair.Right = "modified-during-freeze";
        Console.WriteLine($"[field-lending] right after write={pair.Right}");

        // WOULD FAIL: pair.Left = "new"; // Left is LentFrozen, cannot write
        // WOULD FAIL: owned var moved = pair; // parent composite is LentFrozen, cannot move

        // Freeze scope ends — Left returns to Live, composite returns to Live
        Console.WriteLine($"[field-lending] left after freeze ends={pair.Left}");

        // Now parent can be passed around again (composite is Live)
        Console.WriteLine($"[field-lending] both live again, left={pair.Left}, right={pair.Right}");
    }
}

// ─── This-tier inference and explicit annotation ──────────────────────────────

// Type demonstrating methods with different inferred this-tiers
public class Account
{
    public owned string Name;
    public owned int Balance;

    public Account(owned string name, owned int balance)
    {
        this.Name = name;
        this.Balance = balance;
    }

    // Inferred this-tier: borrowed (writes to owned field)
    public void Deposit(owned int amount)
    {
        this.Balance = this.Balance + amount;
    }

    // Inferred this-tier: readonly ref (only reads owned fields)
    public string Summary()
    {
        return $"Account({Name}, balance={Balance})";
    }

    // Explicit this-tier annotation: frozen
    public string PeekName() with frozen
    {
        return this.Name;
    }

    // Explicit this-tier annotation: owned (consumes the receiver)
    public string Close() with owned
    {
        return $"Closed account: {Name} with balance {Balance}";
    }
}

// PROOF: Method with inferred this-tier=borrowed requires owned/borrowed binding at call site
// PROOF: Method with inferred this-tier=readonly ref is callable from any binding
// PROOF: Explicit 'with frozen' annotation works at call site
// PROOF: 'with owned' consumes the receiver (caller's binding is dead after call)
// PROOF: Shared binding cannot call method requiring borrowed (this-tier mismatch)
public class ThisTierDemo
{
    public static void Run()
    {
        owned var acct = new Account("Alice", 100);

        // Summary() only reads → inferred readonly ref → callable from owned binding
        Console.WriteLine($"[this-tier] summary={acct.Summary()}");

        // Deposit() writes to owned field → inferred borrowed → owned satisfies borrowed
        acct.Deposit(50);
        Console.WriteLine($"[this-tier] after deposit={acct.Summary()}");

        // PeekName() declared 'with frozen' → owned satisfies frozen
        Console.WriteLine($"[this-tier] peek={acct.PeekName()}");

        // Shared binding — can call Summary() (readonly ref) but NOT Deposit() (borrowed)
        var shared = new Account("Bob", 200);
        Console.WriteLine($"[this-tier] shared summary={shared.Summary()}");
        // WOULD FAIL: shared.Deposit(10); // this-tier mismatch: shared cannot satisfy borrowed

        // 'with owned' — consumes the receiver
        owned var closing = new Account("Charlie", 300);
        var result = closing.Close();  // closing is moved into Close()
        Console.WriteLine($"[this-tier] closed={result}");
        // WOULD FAIL: closing.Summary(); // use-after-move — closing was consumed by Close()
    }
}

// ─── Ownership inference on local bindings ────────────────────────────────────

// PROOF: var infers 'owned' when initialized with new and used for ownership operations
// PROOF: var infers 'shared' when no ownership operations are needed
// PROOF: var downgrades from owned to borrowed when the original is still used after
public class OwnershipInferenceDemo
{
    public static void Run()
    {
        // 'var x = new Account(...)' — inferred as owned (new yields ownership, Deposit needs borrowed)
        var inferred = new Account("Inferred", 50);
        inferred.Deposit(25);  // requires borrowed → var infers at least owned (satisfies borrowed)
        Console.WriteLine($"[inference] inferred owned={inferred.Summary()}");

        // 'var copy = existingOwned' — would be move if owned, but existingOwned is used later
        // → inference downgrades copy to clone (shared), existingOwned stays live
        owned var original = new Account("Original", 100);
        var copy = original.Summary();  // Summary() returns string (readonly ref on this, no move)
        Console.WriteLine($"[inference] original still live={original.Summary()}");
        Console.WriteLine($"[inference] copy={copy}");

        // 'var simple' — no ownership operations → inferred as shared
        var simple = "just a string";
        var alias = simple;  // no move, both can coexist → shared
        Console.WriteLine($"[inference] simple={simple}");
        Console.WriteLine($"[inference] alias={alias}");
    }
}
```

**Expected output:**
```
[owned-field] initial=Document(v1: Hello, world)
[owned-field] after update=Document(v2: Updated content)
[owned-field] after bump=Document(v3: Updated content)
[owned-field] direct assign=Document(v3: Direct assignment)
[owned-field] cloned=Document(v3: Cloned content)
[owned-field] shared still alive=Cloned content
[owned-field] version set=Document(v99: Cloned content)
[owned-field] shared write=Document(v2: shared-updated)
[owned-field] shared clone-read=shared-updated
[owned-type-restrict] initial=Vault(accesses=0, secret=top-secret-123)
[owned-type-restrict] rotated=Vault(accesses=1, secret=new-secret-456)
[owned-type-restrict] replaced=Vault(accesses=1, secret=replaced-789)
[partial-move] extracted=alpha
[partial-move] right still live=beta
[partial-move] cloned from right=beta
[partial-move] right after clone=beta
[partial-move] left restored=restored
[partial-move] both live, left=restored, right=beta
[field-lending] frozen left=alpha
[field-lending] right during freeze=beta
[field-lending] right after write=modified-during-freeze
[field-lending] left after freeze ends=alpha
[field-lending] both live again, left=alpha, right=modified-during-freeze
[this-tier] summary=Account(Alice, balance=100)
[this-tier] after deposit=Account(Alice, balance=150)
[this-tier] peek=Alice
[this-tier] shared summary=Account(Bob, balance=200)
[this-tier] closed=Closed account: Charlie with balance 300
[inference] inferred owned=Account(Inferred, balance=75)
[inference] original still live=Account(Original, balance=100)
[inference] copy=Account(Original, balance=100)
[inference] simple=just a string
[inference] alias=just a string
```

### Requirement 16: Owned Field State Tracking — Partial Moves and Lending

**User Story:** As a Mist developer, I want to move, borrow, freeze, or readonly-ref individual owned fields independently, so that I can work with parts of an object graph without requiring full exclusive access to the entire parent.

#### Acceptance Criteria

**Per-field state:**

1. EACH owned field of an instance held in an owned binding SHALL have its own independent ownership state (Live, LentReadonly, LentFrozen, LentRef, Moved), tracked separately from the parent binding's state and from sibling fields' states.
2. WHEN an owned field is moved out of an instance (e.g., `owned var extracted = obj.Content`), THE Type_Checker SHALL transition that field's state to Moved. The parent binding remains accessible for operations on other fields but is restricted based on its composite state (see below).
3. WHEN an owned field is lent as `borrowed`, THE Type_Checker SHALL transition that field's state to LentRef.
4. WHEN an owned field is lent as `frozen`, THE Type_Checker SHALL transition that field's state to LentFrozen.
5. WHEN an owned field is lent as `readonly ref`, THE Type_Checker SHALL transition that field's state to LentReadonly.
6. WHEN a new owned value is assigned back into a Moved field, THE Type_Checker SHALL transition that field's state back to Live — the field is restored.

**Reading an owned field (shared destination):**

7. WHEN an owned field is read into a non-owned destination (e.g., `var x = obj.Content`), THE Type_Checker SHALL treat this as a move — the field enters Moved state and the destination holds the value without ownership tracking. This follows the normal rule: a shared destination from an owned source is a move.
8. IF an owned field cannot be moved (because its state is not Live), THE Compiler SHALL fall back to cloning via `IClonable<T>`. IF the field's type implements `IClonable<T>`, a `.Clone()` is inserted and the field remains in its current state (no transition). IF the type does not implement `IClonable<T>`, THE Type_Checker SHALL report an error.

**Composite state — child-to-parent restriction:**

9. THE Type_Checker SHALL compute a composite state for the parent binding by taking the most restrictive state among all its owned fields, using the ordering: Live < LentReadonly < LentFrozen < LentRef = Moved.
10. WHEN any action (read, write, live view, freeze, borrow, move) is attempted on the parent binding as a whole (i.e., passing the entire object to another binding or parameter), THE Type_Checker SHALL apply the standard truth table (ownership-v2 Requirement 7) using the parent's composite state. If the composite state does not permit the action, THE Type_Checker SHALL report an error.
11. IF any owned field is in the Moved state, THE composite state SHALL be Moved — the parent object is incomplete and cannot be sent anywhere (moved, borrowed, frozen, or readonly-ref'd) as a whole.
12. IF any owned field is in the LentRef state and no field is Moved, THE composite state SHALL be LentRef — the parent is fully locked until the borrow returns.
13. IF the most restrictive field state is LentFrozen, THE composite state SHALL be LentFrozen — the parent can be read, frozen, or readonly-ref'd but not written, borrowed, or moved.
14. IF the most restrictive field state is LentReadonly, THE composite state SHALL be LentReadonly — the parent can be read, written, frozen, or readonly-ref'd but not borrowed or moved.

**Parent binding actual state vs. composite state:**

15. THE parent binding's actual ownership state SHALL NOT transition to Moved or LentRef due to a child field's partial move or borrow-out. The parent binding itself remains Live (it is still on the stack and still addressable). The composite state is a *restriction on whole-object operations*, not a state transition on the parent.
16. BECAUSE the parent binding remains actually Live, THE Type_Checker SHALL permit direct field assignment to a Moved field on the parent (e.g., `parent.MovedField = newOwnedValue`) — this is the restoration mechanism that brings the field back to Live. Without this exception, partial moves would be irrecoverable because writing to a Moved binding is normally forbidden.
17. SIMILARLY, when a child field's borrow (LentRef) returns, THE Type_Checker SHALL automatically restore that field to Live — the composite state naturally recalculates. No special write to the field is needed; the loan scope ending is sufficient.
18. THE Type_Checker SHALL distinguish between "parent is actually Moved" (the binding was consumed by a move — completely dead, no operations permitted including field access) and "parent has composite Moved state from a child" (the binding is alive but incomplete — field restoration is permitted, sibling access is permitted, whole-object operations are blocked).

**Parent-to-child restriction:**

19. WHEN the parent binding has a state imposed by an external loan (e.g., the parent itself was frozen or readonly-ref'd to another binding), THE Type_Checker SHALL apply the same truth table to constrain what actions are permitted on each child field. The parent's externally-imposed state restricts child operations using the normal truth table.
20. IF the parent binding is in LentRef state (borrowed out externally), THE Type_Checker SHALL reject all actions on all child fields — the borrower has exclusive access to the entire object.
21. IF the parent binding is in LentFrozen state (frozen externally), THE Type_Checker SHALL permit read, freeze, and readonly-ref on child fields but SHALL reject write, borrow, and move on child fields.

**Sibling independence:**

22. WHEN two sibling owned fields (fields on the same instance) have independent states, THE Type_Checker SHALL NOT restrict one sibling's operations based on another sibling's state. Field A being borrowed does NOT prevent operations on field B (assuming the parent's composite state permits the operation on B via the parent-to-child rule).

**Method calls with partial field states:**

23. WHEN a method call is attempted on an instance that has one or more owned fields in a non-Live state, THE Type_Checker SHALL reject the call if the method touches (reads or writes) any field that is in a state that does not permit the required action. A method that only touches Live fields is permitted.
24. THE Type_Checker SHALL use the this-tier inference analysis (Requirement 17) extended with field-read analysis to determine which fields a method accesses, and SHALL check each accessed field's current state against the required action (read or write) in the truth table.

### Requirement 17: Method This-Tier — Inference and Explicit Annotation

**User Story:** As a Mist developer, I want the compiler to infer what ownership tier a method requires on `this` (based on how it uses fields), and I want the option to declare it explicitly, so that methods on non-owned types can safely interact with owned fields without requiring the entire type to opt into a cooperation contract.

#### Acceptance Criteria

**Inference:**

1. THE Type_Checker SHALL infer the minimum ownership tier required for `this` in each method by analyzing the method body's usage of instance fields. The inferred tier is the minimum tier that satisfies all field operations performed in the body.
2. IF a method body moves out of an owned field or writes to an owned field, THE Type_Checker SHALL infer the `this` tier as at least `borrowed` — exclusive mutable access is required.
3. IF a method body only freezes or takes a frozen ref of an owned field (no writes, no moves), THE Type_Checker SHALL infer the `this` tier as at least `frozen`.
4. IF a method body only reads owned fields (no writes, no moves, no freezes), THE Type_Checker SHALL infer the `this` tier as at least `readonly ref`.
5. IF a method body does not access any owned fields, THE Type_Checker SHALL infer no `this` tier requirement — the method is callable from any binding (shared or owned).
6. THE inference SHALL compose across multiple field accesses: if a method reads one owned field and writes another, the inferred tier is `borrowed` (the most restrictive single operation determines the tier).
7. THE inference SHALL compose transitively through helper method calls on `this`: if method A calls method B on `this`, and B requires `borrowed`, then A requires at least `borrowed`.

**Explicit annotation:**

8. THE Parser SHALL accept a `with {tier}` annotation on method declarations (e.g., `void Foo() with borrowed`, `string Describe() with frozen`, `void Consume() with owned`), where `{tier}` is one of: `owned`, `borrowed`, `frozen`, `readonly ref`.
9. `void Foo() with {tier}` SHALL be sugar for `void Foo() with {tier} {ParentType} this` — the tier applies to the implicit `this` parameter.
10. IF a method has an explicit `with` annotation, THE Type_Checker SHALL use the declared tier instead of inferring — the annotation overrides inference.
11. IF a method has an explicit `with` annotation that is LESS restrictive than what the body requires (e.g., `with frozen` but the body writes to an owned field), THE Type_Checker SHALL report a compile error indicating that the declared tier is insufficient for the method's actual usage.
12. IF a method has an explicit `with` annotation that is MORE restrictive than necessary, THE Type_Checker SHALL accept it without warning — over-constraining is valid (like declaring a parameter `owned` when you could accept `borrowed`).

**`owned` as a this-tier:**

13. WHEN a method is declared `with owned` (or inferred as requiring `owned` this), THE Type_Checker SHALL treat the method call as consuming the receiver — the caller's binding is moved into the method and becomes dead after the call. This is analogous to passing the receiver as an `owned` parameter.
14. A method with `owned` this-tier SHALL be callable only when the receiver is in the Live state (not lent, not moved) — same as any move.

**Call-site checking:**

15. WHEN a method with a this-tier requirement is called, THE Type_Checker SHALL verify that the caller's binding of the receiver satisfies the required tier: `owned` binding satisfies all tiers, `borrowed` satisfies `borrowed`/`frozen`/`readonly ref`, `frozen` satisfies `frozen`/`readonly ref`, `readonly ref` satisfies only `readonly ref`, shared (no tier) satisfies only methods with no tier requirement.
16. IF the caller's binding tier does not satisfy the method's this-tier requirement, THE Type_Checker SHALL report a compile error identifying the method, the required tier, and the actual tier of the receiver at the call site.
17. FOR owned types, THE Type_Checker SHALL apply the same inference/checking — owned types do not get a blanket override. Their methods naturally infer `borrowed` or higher because all fields are owned. The cooperation contract verifies that the body is sound, and the call-site check verifies that the caller has sufficient access.

### Requirement 18: Ownership Inference on Local Bindings

**User Story:** As a Mist developer, I want `var` to infer the ownership tier of a local variable from its usage within the function, so that I don't need to explicitly annotate every binding while still getting full ownership safety.

#### Acceptance Criteria

**Inference algorithm:**

1. WHEN a local variable is declared with `var` (no explicit ownership specifier), THE Type_Checker SHALL infer the ownership tier by finding the least permissive tier that satisfies all uses of that variable within the enclosing function body.
2. THE inference SHALL try tiers in order from most permissive to least: `owned` → `borrowed` → `frozen` → `readonly ref` → `shared`. The first tier that does not produce a contradiction with any use of the variable is selected.
3. IF the variable is initialized with a `new` expression (constructor call) or an owned-returning function, THE Type_Checker SHALL start the inference at `owned` as the initial candidate tier.
4. IF the variable is initialized from another variable (e.g., `var y = x`), THE Type_Checker SHALL consider the relationship: if `y` being `owned` would require consuming `x` (a move), but `x` is used again later, THE Type_Checker SHALL try downgrading `y` to `borrowed`, then `frozen`, then `readonly ref`, then `shared` — selecting the first tier that does not create a use-after-move or other violation.
5. IF no ownership tier produces a consistent assignment for all variables in the function, THE Type_Checker SHALL report a compile error explaining the conflicting usage and suggesting the developer use explicit annotations to resolve the ambiguity.

**Shared as ultimate fallback:**

6. WHEN a variable's uses never require ownership semantics (never passed to an `owned` parameter, never has owned fields moved out of it, never lent as `borrowed`/`frozen`/`readonly ref`, never used as a receiver for a method requiring a this-tier), THE Type_Checker SHALL infer the variable as shared — no ownership tracking, multiple aliases permitted.
7. SHARED SHALL always be a valid inference result — it never produces a contradiction because it imposes no restrictions. The only scenario where inference fails is when the variable's uses *require* a specific tier but other uses contradict that tier.

**Interaction with explicit annotations:**

8. WHEN a variable is declared with an explicit ownership specifier (`owned var`, `borrowed var`, `frozen var`), THE Type_Checker SHALL NOT apply inference — the explicit specifier takes precedence and is validated against all uses.
9. IF an explicit specifier is too restrictive for the variable's uses (e.g., `owned var x` but the variable is used after being passed to a non-owned parameter), THE Type_Checker SHALL report an error — same as today.
10. IF an explicit specifier is less restrictive than inference would produce, THE Type_Checker SHALL accept it — the developer is opting into a weaker guarantee (e.g., explicitly choosing `shared` when inference would have given `owned`).

**Scope:**

11. THE inference SHALL operate within a single function scope — it does not cross function boundaries. At function call boundaries, parameter tier declarations (or their this-tier inference from Requirement 17) determine what the caller must provide.
12. THE inference SHALL be computed as a fixed-point over all `var` bindings in the function simultaneously — variables may depend on each other's inferred tiers (e.g., `var y = x` constrains both `x` and `y`).

### Requirement 19: Book Documentation

**User Story:** As a Mist developer, I want the language book's ownership chapter updated to cover owned fields, so that I can learn the new syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book's ownership chapter (`book/src/advanced/ownership.md`) SHALL be updated to include a section on owned fields, placed after the existing "Field Assignment Provenance" section and before "Move Semantics."
2. THE owned-fields section SHALL cover each of the following sub-topics, with at least one compiled snippet per sub-topic: (a) owned field declaration syntax; (b) the this-tier system (inference and explicit `with` annotation); (c) jagged ownership patterns demonstrating a type with a mix of owned and non-owned fields; (d) the `owned type` field-type restriction; (e) how owned fields solve the static-aliasing problem and the non-owned-constructor problem; (f) per-field state tracking and partial moves (composite state, sibling independence, read-as-move semantics); (g) ownership inference on local bindings.
3. All code examples in the owned-fields section SHALL be compiled snippets stored in `book/snippets/advanced/ownership/` with corresponding `.cs` files generated by the SnippetEmitter tool, following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL NOT need changes since the owned-fields content is added as a section within the existing ownership chapter.
5. THE owned-fields section SHALL contain a minimum of 5 and a maximum of 12 compiled snippet pairs (`.mist` + `.cs`), each demonstrating a single concept from criterion 2.
