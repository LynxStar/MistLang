# Requirements Document

## Introduction

This document specifies the requirements for correcting the substructural field discipline check in the Mist compiler. The existing `MIST_DROP_FIELD_NOT_OWNED` diagnostic asks only "is the container owned?" to permit or reject substructural fields. This is insufficient — the correct rule must consider the compatibility between the container's discipline and the field's discipline based on their respective drop guarantees.

Additionally, generic type parameters used as fields bypass the field containment check entirely, allowing unsound instantiations (e.g., `linear class Sneaky<T> { T Value; }` compiles but `Sneaky<string>` violates the single-holder invariant).

This spec fixes both issues by:
1. Replacing the binary owned/non-owned check with a discipline-compatibility matrix.
2. Requiring substructural constraints on generic type parameters used as fields in substructural containers (or rejecting the declaration).

Additionally, with the corrected field containment matrix in place, the `MIST_TC_0110` diagnostic (which blanket-prohibits `Task<T>` in fields/properties) becomes obsolete. `Task<T>` has relevant discipline and `ValueTask<T>` has linear discipline — the matrix already enforces that only containers with compatible disciplines may hold them (relevant/linear containers for Task, linear-only for ValueTask), which guarantees the same structured-concurrency invariants as a bare Task local. This spec removes `MIST_TC_0110` and relies on the matrix rules instead.

## Glossary

- **Substructural_Discipline**: One of `affine`, `linear`, `relevant`, or unrestricted (no modifier). Each discipline imposes guarantees on how many times `Drop()` fires.
- **Container**: A class or struct type that declares fields. Its discipline determines how many times the container (and thus its fields) will be dropped.
- **Field_Discipline**: The substructural discipline of the field's declared type. Determines the invariants the field requires.
- **Discipline_Compatibility_Matrix**: The table defining which field disciplines are permitted within each container discipline based on drop guarantees.
- **Drop_Guarantee**: The promise a discipline makes about Drop invocation count. Linear = exactly 1, affine = 0 or 1, relevant = 1+, unrestricted = 0 to many.
- **Substructural_Constraint**: A generic type parameter constraint (`where T : linear`, `where T : affine`, `where T : relevant`) that restricts the type argument's discipline.
- **DropAnalyzer**: The existing analysis pass (`Source/Mist/Analysis/Builtin/DropAnalyzer.cs`) that enforces drop-semantic rules.
- **TypeParameterType**: The semantic model's representation of an unsubstituted type parameter (e.g., `T` in `class Box<T>`).
- **Task_Type**: The built-in `Task<T>` type used for structured concurrency. It has `Relevant` discipline (must be awaited at least once).
- **ValueTask_Type**: The built-in `ValueTask<T>` type used for structured concurrency. It has `Linear` discipline (must be awaited exactly once).
- **MIST_TC_0110**: A legacy diagnostic that blanket-prohibits storing `Task<T>` in fields, properties, or collections. Superseded by the discipline-compatibility matrix.

## Requirements

### Requirement 1: Discipline-Compatible Field Containment

**User Story:** As a Mist developer, I want the compiler to reject field declarations where the container's drop guarantees cannot satisfy the field's discipline invariants, so that substructural soundness is enforced at compile time.

#### Acceptance Criteria

1. WHEN a **linear** container declares a field, THE DropAnalyzer SHALL permit fields of any discipline (linear, affine, relevant, unrestricted) because the container's exactly-once drop satisfies all field invariants.
2. WHEN an **affine** container declares a field, THE DropAnalyzer SHALL permit only affine and unrestricted fields. IF the field's discipline is linear or relevant, THEN THE DropAnalyzer SHALL report exactly one compile-time error per incompatible field, indicating that affine containers may never be dropped and therefore cannot satisfy the field's at-least-once drop requirement.
3. WHEN a **relevant** container declares a field, THE DropAnalyzer SHALL permit only relevant and unrestricted fields. IF the field's discipline is affine or linear, THEN THE DropAnalyzer SHALL report exactly one compile-time error per incompatible field, indicating that relevant containers may be dropped multiple times and therefore cannot satisfy the field's at-most-once drop requirement.
4. WHEN an **unrestricted** container (no substructural modifier, not owned) declares a field, THE DropAnalyzer SHALL permit only unrestricted fields. IF the field's discipline is linear, affine, or relevant, THEN THE DropAnalyzer SHALL report exactly one compile-time error per incompatible field, indicating that unrestricted containers make no drop guarantees.
5. WHEN an **owned** container (explicit `owned` keyword, no substructural modifier) declares a field, THE DropAnalyzer SHALL apply the same permissible-field rules as an unrestricted container (criteria 4), because `owned` alone provides no substructural drop guarantee.
6. THE DropAnalyzer SHALL replace the current binary "is the container owned?" check with the discipline-compatibility matrix described by criteria 1–5.
7. IF a container declares multiple fields with incompatible disciplines, THEN THE DropAnalyzer SHALL report one compile-time error for each incompatible field independently.
8. An array is an unrestricted container. WHEN an array literal or an array creation has a linear or affine element type, including a type parameter constrained `linear` or `affine`, THE Compiler SHALL report `MIST_PA_ARRAY_ELEMENT_DISCIPLINE` at that expression. A ref struct element is refused by its own array-element rule instead.

### Requirement 2: Updated Diagnostic Messages

**User Story:** As a Mist developer, I want clear error messages that explain why a specific container/field discipline combination is invalid, so that I understand what invariant would be violated.

#### Acceptance Criteria

1. WHEN an affine container contains a linear or relevant field, THE DropAnalyzer SHALL report using the `MIST_DROP_FIELD_NOT_OWNED` diagnostic ID: "Affine type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container may never be dropped, violating the field's requirement of at least one drop." where `{field_modifier}` is the lowercase discipline keyword of the field's type (e.g., "linear" or "relevant").
2. WHEN a relevant container contains an affine or linear field, THE DropAnalyzer SHALL report using the `MIST_DROP_FIELD_NOT_OWNED` diagnostic ID: "Relevant type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container may be dropped multiple times, violating the field's at-most-once drop requirement." where `{field_modifier}` is the lowercase discipline keyword of the field's type (e.g., "affine" or "linear").
3. WHEN an unrestricted container or an owned container (which follows unrestricted rules per Requirement 1 AC 5) contains a linear, affine, or relevant field, THE DropAnalyzer SHALL report using the `MIST_DROP_FIELD_NOT_OWNED` diagnostic ID: "Type '{ContainerName}' cannot contain {field_modifier} field '{FieldName}'. The container has no substructural discipline and cannot guarantee the field's drop requirements." where `{field_modifier}` is the lowercase discipline keyword of the field's type (e.g., "linear", "affine", or "relevant").
4. THE existing diagnostic ID `MIST_DROP_FIELD_NOT_OWNED` SHALL be retained as the single diagnostic ID for all three message variants (the ID is stable and referenced by other specs) but its message templates SHALL be updated to match the new formulations defined in criteria 1–3, replacing the current single "Non-owned type ... single-holder invariant" message.

### Requirement 3: Substructural Constraints on Type Parameters

**User Story:** As a Mist developer, I want to constrain generic type parameters with substructural modifiers (e.g., `where T : linear`), so that generic types can safely contain substructural fields.

#### Acceptance Criteria

1. THE Parser SHALL recognize `linear`, `affine`, and `relevant` as valid constraint keywords in `where` clauses (e.g., `where T : linear`, `where T : affine`, `where T : relevant`).
2. THE Semantic_Model SHALL store substructural constraints on `TypeParameterConstraints` with three new boolean properties: `HasLinearConstraint`, `HasAffineConstraint`, `HasRelevantConstraint`.
3. IF a `where` clause specifies more than one substructural constraint keyword on the same type parameter (e.g., `where T : linear, affine`), THEN THE Compiler SHALL emit a diagnostic error indicating that multiple substructural constraints cannot be combined on a single type parameter.
4. THE Compiler SHALL permit combining a single substructural constraint with other non-substructural constraint kinds on the same type parameter (e.g., `where T : linear, class`, `where T : affine, ISomething`).
5. WHEN a type parameter has a `linear` constraint, THE Compiler SHALL treat it as having the `Linear` discipline for the purposes of the field containment check defined in the substructural compatibility table (container may hold any discipline).
6. WHEN a type parameter has an `affine` constraint, THE Compiler SHALL treat it as having the `Affine` discipline for the purposes of the field containment check defined in the substructural compatibility table (container must permit affine fields).
7. WHEN a type parameter has a `relevant` constraint, THE Compiler SHALL treat it as having the `Relevant` discipline for the purposes of the field containment check defined in the substructural compatibility table (container must permit relevant fields).
8. IF a type argument supplied at an instantiation site does not satisfy the substructural constraint declared on the corresponding type parameter (e.g., a non-linear type is passed for `where T : linear`), THEN THE Compiler SHALL emit a diagnostic error indicating the type argument violates the substructural constraint.
9. IF a type parameter has no substructural constraint and is used as a field in a type with a substructural discipline, THEN THE Compiler SHALL emit a diagnostic error indicating that the type parameter requires a substructural constraint to be held in that container.

### Requirement 4: Generic Field Containment Validation

**User Story:** As a Mist developer, I want the compiler to enforce substructural field rules for generic type parameters used as fields, so that unsound instantiations are rejected at the definition site rather than silently accepted.

#### Acceptance Criteria

1. WHEN a substructural container declares a field whose type is a type parameter (e.g., `T Value;`), THE DropAnalyzer SHALL evaluate the type parameter's discipline based on its declared constraints.
2. IF the type parameter has no substructural constraint, THEN THE DropAnalyzer SHALL treat it as unrestricted for the containment check.
3. IF a type parameter used as a field in a substructural container (linear, affine, or relevant) has no substructural constraint, THEN THE DropAnalyzer SHALL report a compile-time error indicating that the type parameter needs a constraint, regardless of whether the container's discipline would permit unrestricted fields in the concrete-type case.
4. WHEN a linear container declares a field of type `T` with no substructural constraint, THE DropAnalyzer SHALL report: "Linear type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : linear') to ensure the field's discipline is compatible."
5. WHEN an affine container declares a field of type `T` with no substructural constraint, THE DropAnalyzer SHALL report: "Affine type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : affine') to ensure the field's discipline is compatible."
6. WHEN a relevant container declares a field of type `T` with no substructural constraint, THE DropAnalyzer SHALL report: "Relevant type '{ContainerName}' contains field '{FieldName}' of unconstrained type parameter '{T}'. Add a substructural constraint (e.g., 'where {T} : relevant') to ensure the field's discipline is compatible."
7. WHEN a type parameter has a substructural constraint, THE DropAnalyzer SHALL determine the field's effective discipline from the constraint and apply the Requirement 1 compatibility matrix. IF the constraint's discipline is compatible with the container's discipline per that matrix, THEN THE DropAnalyzer SHALL permit the field. IF it is not compatible, THEN THE DropAnalyzer SHALL report the corresponding Requirement 2 diagnostic for the container/field discipline mismatch.

### Requirement 5: Instantiation-Site Constraint Satisfaction

**User Story:** As a Mist developer, I want the compiler to verify that type arguments satisfy substructural constraints at instantiation sites, so that I cannot pass an unrestricted type where a linear type is required.

#### Acceptance Criteria

1. WHEN a type argument is provided for a type parameter with a `linear` constraint, THE Compiler SHALL verify the type argument has `Linear` discipline. IF the type argument does not have `Linear` discipline, THEN THE Compiler SHALL report a diagnostic indicating that the type argument does not satisfy the 'linear' constraint on that type parameter.
2. WHEN a type argument is provided for a type parameter with an `affine` constraint, THE Compiler SHALL verify the type argument has `Affine` or `Linear` discipline. IF the type argument has neither `Affine` nor `Linear` discipline, THEN THE Compiler SHALL report a diagnostic indicating that the type argument does not satisfy the 'affine' constraint on that type parameter.
3. WHEN a type argument is provided for a type parameter with a `relevant` constraint, THE Compiler SHALL verify the type argument has `Relevant` or `Linear` discipline. IF the type argument has neither `Relevant` nor `Linear` discipline, THEN THE Compiler SHALL report a diagnostic indicating that the type argument does not satisfy the 'relevant' constraint on that type parameter.
4. THE Compiler SHALL perform the substructural constraint satisfaction check at all instantiation sites: type arguments in `new` expressions, base class specifications, field declarations with generic types, method call type arguments, and variable declarations with explicit type arguments.
5. IF the type argument is itself a type parameter with its own substructural constraint, THEN THE Compiler SHALL accept the type argument when its constraint is equal to or stricter than the required constraint according to the discipline satisfaction hierarchy (linear satisfies all; affine satisfies only affine; relevant satisfies only relevant; unconstrained satisfies none).
6. IF the type argument resolves to an error type or is unresolvable, THEN THE Compiler SHALL suppress the substructural constraint diagnostic for that type argument to avoid cascading errors.

### Requirement 6: Constraint Satisfaction Subtyping Rules

**User Story:** As a language designer, I want the substructural constraint satisfaction to follow the discipline lattice, so that linear types (which guarantee exactly-once) can satisfy both affine (≤1) and relevant (≥1) constraints.

#### Acceptance Criteria

1. WHEN a type argument with `linear` discipline is provided for a type parameter constrained to `linear`, THEN THE constraint checker SHALL accept the type argument.
2. WHEN a type argument with `linear` discipline is provided for a type parameter constrained to `affine`, THEN THE constraint checker SHALL accept the type argument (exactly-once ⊆ at-most-once).
3. WHEN a type argument with `linear` discipline is provided for a type parameter constrained to `relevant`, THEN THE constraint checker SHALL accept the type argument (exactly-once ⊆ at-least-once).
4. WHEN a type argument with `affine` discipline is provided for a type parameter constrained to `affine`, THEN THE constraint checker SHALL accept the type argument.
5. IF a type argument with `affine` discipline is provided for a type parameter constrained to `linear` or `relevant`, THEN THE constraint checker SHALL reject the type argument with a diagnostic indicating the discipline does not satisfy the constraint.
6. WHEN a type argument with `relevant` discipline is provided for a type parameter constrained to `relevant`, THEN THE constraint checker SHALL accept the type argument.
7. IF a type argument with `relevant` discipline is provided for a type parameter constrained to `linear` or `affine`, THEN THE constraint checker SHALL reject the type argument with a diagnostic indicating the discipline does not satisfy the constraint.
8. IF a type argument with no substructural discipline (unrestricted) is provided for a type parameter constrained to `linear`, `affine`, or `relevant`, THEN THE constraint checker SHALL reject the type argument with a diagnostic indicating the discipline does not satisfy the constraint.

### Requirement 7: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the corrected substructural field discipline and generic constraints, so that I can see every valid and invalid combination verified in one place.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/SubstructuralFieldDiscipline/Program.mist` with a `mist.toml` project file.
2. The sample SHALL demonstrate at minimum the following valid combinations: a linear container with linear, affine, relevant, and unrestricted fields; an affine container with affine and unrestricted fields; a relevant container with relevant and unrestricted fields; at least one generic substructural container with a constrained type parameter (e.g., `where T : affine`); at least one instantiation site that satisfies the generic constraint; a relevant container holding a `Task<T>` field (proving MIST_TC_0110 removal — Task is relevant, relevant containers hold relevant fields); a linear container holding a `Task<T>` field (linear holds anything); and a linear container holding a `ValueTask<T>` field (ValueTask is linear, linear containers hold linear fields).
3. Each demonstrated pattern SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `SubstructuralFieldDiscipline.Program.RunDemo()`, and `samples/AllFeatures/mist.toml` SHALL include a reference entry `SubstructuralFieldDiscipline = { path = "../SubstructuralFieldDiscipline" }`.
5. WHEN the sample is compiled via the Mist compiler, THE system SHALL produce zero diagnostic errors and zero diagnostic warnings, and WHEN the compiled output is executed, THE system SHALL exit with code 0 and produce no output on stderr.

#### Proof Code

```mist
// SubstructuralFieldDiscipline — Demonstrates corrected field containment rules
// and generic substructural constraints.

using Standard;

namespace SubstructuralFieldDiscipline;

// ─── Leaf types with each discipline ─────────────────────────────────────────

public linear class LinearToken
{
    int Id;
    public LinearToken(int id) { this.Id = id; }
    public int GetId() { return Id; }
    public void Drop() { Console.WriteLine($"  LinearToken({Id}).Drop()"); }
}

public affine class AffineToken
{
    int Id;
    public AffineToken(int id) { this.Id = id; }
    public int GetId() { return Id; }
    public void Drop() { Console.WriteLine($"  AffineToken({Id}).Drop()"); }
}

public relevant class RelevantToken
{
    int Id;
    bool Dropped;
    public RelevantToken(int id) { this.Id = id; this.Dropped = false; }
    public int GetId() { return Id; }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine($"  RelevantToken({Id}).Drop()");
    }
}

public class PlainToken
{
    int Id;
    public PlainToken(int id) { this.Id = id; }
    public int GetId() { return Id; }
}

// ─── Linear container: can hold anything ─────────────────────────────────────

// PROOF: linear container permits linear field — exactly-once drop covers linear's exactly-once need
public linear class LinearHoldsLinear
{
    LinearToken Inner;
    public LinearHoldsLinear(int id) { this.Inner = new LinearToken(id); }
    public void Drop()
    {
        Console.WriteLine("  LinearHoldsLinear.Drop()");
    }
}

// PROOF: linear container permits affine field — exactly-once drop covers affine's at-most-once need
public linear class LinearHoldsAffine
{
    AffineToken Inner;
    public LinearHoldsAffine(int id) { this.Inner = new AffineToken(id); }
    public void Drop()
    {
        Console.WriteLine("  LinearHoldsAffine.Drop()");
    }
}

// PROOF: linear container permits relevant field — exactly-once drop covers relevant's at-least-once need
public linear class LinearHoldsRelevant
{
    RelevantToken Inner;
    public LinearHoldsRelevant(int id) { this.Inner = new RelevantToken(id); }
    public void Drop()
    {
        Console.WriteLine("  LinearHoldsRelevant.Drop()");
    }
}

// PROOF: linear container permits unrestricted field — no constraints on unrestricted
public linear class LinearHoldsPlain
{
    PlainToken Inner;
    public LinearHoldsPlain(int id) { this.Inner = new PlainToken(id); }
    public void Drop()
    {
        Console.WriteLine("  LinearHoldsPlain.Drop()");
    }
}

// ─── Affine container: only affine and unrestricted ──────────────────────────

// PROOF: affine container permits affine field
public affine class AffineHoldsAffine
{
    AffineToken Inner;
    public AffineHoldsAffine(int id) { this.Inner = new AffineToken(id); }
    public void Drop()
    {
        Console.WriteLine("  AffineHoldsAffine.Drop()");
    }
}

// PROOF: affine container permits unrestricted field
public affine class AffineHoldsPlain
{
    PlainToken Inner;
    public AffineHoldsPlain(int id) { this.Inner = new PlainToken(id); }
    public void Drop()
    {
        Console.WriteLine("  AffineHoldsPlain.Drop()");
    }
}

// ─── Relevant container: only relevant and unrestricted ──────────────────────

// PROOF: relevant container permits relevant field
public relevant class RelevantHoldsRelevant
{
    RelevantToken Inner;
    bool Dropped;
    public RelevantHoldsRelevant(int id) { this.Inner = new RelevantToken(id); this.Dropped = false; }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine("  RelevantHoldsRelevant.Drop()");
    }
}

// PROOF: relevant container permits unrestricted field
public relevant class RelevantHoldsPlain
{
    PlainToken Inner;
    bool Dropped;
    public RelevantHoldsPlain(int id) { this.Inner = new PlainToken(id); this.Dropped = false; }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine("  RelevantHoldsPlain.Drop()");
    }
}

// ─── Generic containers with substructural constraints ───────────────────────

// PROOF: generic linear container requires 'where T : linear' for field of type T
// PROOF: linear constraint allows T to be used as a field in a linear container
public linear class LinearBox<T> where T : linear
{
    T Value;
    public LinearBox(T value) { this.Value = value; }
    public T Drop()
    {
        Console.WriteLine("  LinearBox<T>.Drop()");
        return Value;
    }
}

// PROOF: generic affine container requires 'where T : affine' for field of type T
// PROOF: affine constraint allows T to be used as a field in an affine container
public affine class AffineBox<T> where T : affine
{
    T Value;
    public AffineBox(T value) { this.Value = value; }
    public void Drop()
    {
        Console.WriteLine("  AffineBox<T>.Drop()");
    }
}

// PROOF: generic relevant container requires 'where T : relevant' for field of type T
// PROOF: relevant constraint allows T to be used as a field in a relevant container
public relevant class RelevantBox<T> where T : relevant
{
    T Value;
    bool Dropped;
    public RelevantBox(T value) { this.Value = value; this.Dropped = false; }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine("  RelevantBox<T>.Drop()");
    }
}

// ─── Constraint satisfaction: linear satisfies affine and relevant ────────────

// PROOF: linear type argument satisfies affine constraint (exactly-once ⊆ at-most-once)
public affine class AffineBoxWithLinear
{
    AffineBox<LinearToken> Boxed;
    public AffineBoxWithLinear(int id)
    {
        this.Boxed = new AffineBox<LinearToken>(new LinearToken(id));
    }
    public void Drop()
    {
        Console.WriteLine("  AffineBoxWithLinear.Drop()");
    }
}

// PROOF: linear type argument satisfies relevant constraint (exactly-once ⊆ at-least-once)
public relevant class RelevantBoxWithLinear
{
    RelevantBox<LinearToken> Boxed;
    bool Dropped;
    public RelevantBoxWithLinear(int id)
    {
        this.Boxed = new RelevantBox<LinearToken>(new LinearToken(id));
        this.Dropped = false;
    }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine("  RelevantBoxWithLinear.Drop()");
    }
}

// ─── Task<T> and ValueTask<T> in compatible containers (MIST_TC_0110 removal) ─

// PROOF: Task<T> is relevant — it can be stored in a relevant container
// PROOF: MIST_TC_0110 no longer fires — the discipline matrix handles correctness
// PROOF: a relevant container holding Task<T> compiles without error because
//        relevant containers can hold relevant fields (Requirement 1 AC 3 permits relevant fields)
public relevant class RelevantTaskHolder<T>
{
    Task<T> Pending;
    bool Dropped;
    public RelevantTaskHolder(Task<T> task) { this.Pending = task; this.Dropped = false; }
    public async T Resolve()
    {
        Console.WriteLine("  RelevantTaskHolder<T>.Resolve()");
        return await Pending;
    }
    public void Drop()
    {
        if (Dropped) { return; }
        this.Dropped = true;
        Console.WriteLine("  RelevantTaskHolder<T>.Drop()");
    }
}

// PROOF: Task<T> is relevant — it can also be stored in a linear container
// PROOF: linear containers hold anything (Requirement 1 AC 1)
public linear class LinearTaskHolder<T>
{
    Task<T> Pending;
    public LinearTaskHolder(Task<T> task) { this.Pending = task; }
    public async T Resolve()
    {
        Console.WriteLine("  LinearTaskHolder<T>.Resolve()");
        return await Pending;
    }
    public void Drop()
    {
        Console.WriteLine("  LinearTaskHolder<T>.Drop()");
    }
}

// PROOF: ValueTask<T> is linear — it can be stored in a linear container
// PROOF: linear containers hold linear fields (Requirement 1 AC 1)
public linear class LinearValueTaskHolder<T>
{
    ValueTask<T> Pending;
    public LinearValueTaskHolder(ValueTask<T> task) { this.Pending = task; }
    public async T Resolve()
    {
        Console.WriteLine("  LinearValueTaskHolder<T>.Resolve()");
        return await Pending;
    }
    public void Drop()
    {
        Console.WriteLine("  LinearValueTaskHolder<T>.Drop()");
    }
}

// ─── Entry point ─────────────────────────────────────────────────────────────

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Substructural Field Discipline Sample ===");

        // 1. Linear container holds all disciplines
        Console.WriteLine("--- 1. Linear container holds linear field ---");
        {
            var holder = new LinearHoldsLinear(1);
            // PROOF: auto-drop at scope exit drops container, which drops inner LinearToken
        }

        Console.WriteLine("--- 2. Linear container holds affine field ---");
        {
            var holder = new LinearHoldsAffine(2);
        }

        Console.WriteLine("--- 3. Linear container holds relevant field ---");
        {
            var holder = new LinearHoldsRelevant(3);
        }

        Console.WriteLine("--- 4. Linear container holds unrestricted field ---");
        {
            var holder = new LinearHoldsPlain(4);
        }

        // 2. Affine container with valid fields
        Console.WriteLine("--- 5. Affine container holds affine field ---");
        with (var holder = new AffineHoldsAffine(5))
        {
            // PROOF: affine container dropped in with block
        }

        Console.WriteLine("--- 6. Affine container holds unrestricted field ---");
        with (var holder = new AffineHoldsPlain(6))
        {
        }

        // 3. Relevant container with valid fields
        Console.WriteLine("--- 7. Relevant container holds relevant field ---");
        {
            var holder = new RelevantHoldsRelevant(7);
            // PROOF: auto-drop at scope exit for relevant type
        }

        Console.WriteLine("--- 8. Relevant container holds unrestricted field ---");
        {
            var holder = new RelevantHoldsPlain(8);
        }

        // 4. Generic containers with substructural constraints
        Console.WriteLine("--- 9. Generic linear box with linear type argument ---");
        {
            var box = new LinearBox<LinearToken>(new LinearToken(9));
            // PROOF: LinearBox<LinearToken> is valid — LinearToken satisfies 'where T : linear'
            // Drop returns the LinearToken
        }

        Console.WriteLine("--- 10. Generic affine box with affine type argument ---");
        with (var box = new AffineBox<AffineToken>(new AffineToken(10)))
        {
            // PROOF: AffineBox<AffineToken> is valid — AffineToken satisfies 'where T : affine'
        }

        Console.WriteLine("--- 11. Generic relevant box with relevant type argument ---");
        {
            var box = new RelevantBox<RelevantToken>(new RelevantToken(11));
            // PROOF: RelevantBox<RelevantToken> is valid — RelevantToken satisfies 'where T : relevant'
        }

        // 5. Constraint satisfaction: linear satisfies affine and relevant
        Console.WriteLine("--- 12. Linear satisfies affine constraint ---");
        with (var holder = new AffineBoxWithLinear(12))
        {
            // PROOF: LinearToken satisfies 'where T : affine' — exactly-once ⊆ at-most-once
        }

        Console.WriteLine("--- 13. Linear satisfies relevant constraint ---");
        {
            var holder = new RelevantBoxWithLinear(13);
            // PROOF: LinearToken satisfies 'where T : relevant' — exactly-once ⊆ at-least-once
        }

        // 6. Task<T> and ValueTask<T> stored in compatible containers (MIST_TC_0110 removal)
        Console.WriteLine("--- 14. Task<T> stored in relevant container ---");
        {
            var holder = new RelevantTaskHolder<int>(async { return 42; });
            // PROOF: Task<int> is relevant and is stored as a field in relevant RelevantTaskHolder<T>
            // PROOF: MIST_TC_0110 would have rejected this — now the matrix permits it
            var result = await holder.Resolve();
            Console.WriteLine($"  result={result}");
        }

        Console.WriteLine("--- 15. Task<T> stored in linear container ---");
        {
            var holder = new LinearTaskHolder<int>(async { return 99; });
            // PROOF: Task<int> is relevant, linear container holds anything
            var result = await holder.Resolve();
            Console.WriteLine($"  result={result}");
        }

        Console.WriteLine("--- 16. ValueTask<T> stored in linear container ---");
        {
            var holder = new LinearValueTaskHolder<int>(async valuetask { return 7; });
            // PROOF: ValueTask<int> is linear, linear container holds linear fields
            var result = await holder.Resolve();
            Console.WriteLine($"  result={result}");
        }

        Console.WriteLine("=== Done ===");
    }
}
```

**Expected output:**
```
=== Substructural Field Discipline Sample ===
--- 1. Linear container holds linear field ---
  LinearHoldsLinear.Drop()
  LinearToken(1).Drop()
--- 2. Linear container holds affine field ---
  LinearHoldsAffine.Drop()
  AffineToken(2).Drop()
--- 3. Linear container holds relevant field ---
  LinearHoldsRelevant.Drop()
  RelevantToken(3).Drop()
--- 4. Linear container holds unrestricted field ---
  LinearHoldsPlain.Drop()
--- 5. Affine container holds affine field ---
  AffineHoldsAffine.Drop()
  AffineToken(5).Drop()
--- 6. Affine container holds unrestricted field ---
  AffineHoldsPlain.Drop()
--- 7. Relevant container holds relevant field ---
  RelevantHoldsRelevant.Drop()
  RelevantToken(7).Drop()
--- 8. Relevant container holds unrestricted field ---
  RelevantHoldsPlain.Drop()
--- 9. Generic linear box with linear type argument ---
  LinearBox<T>.Drop()
  LinearToken(9).Drop()
--- 10. Generic affine box with affine type argument ---
  AffineBox<T>.Drop()
  AffineToken(10).Drop()
--- 11. Generic relevant box with relevant type argument ---
  RelevantBox<T>.Drop()
  RelevantToken(11).Drop()
--- 12. Linear satisfies affine constraint ---
  AffineBoxWithLinear.Drop()
  AffineBox<T>.Drop()
  LinearToken(12).Drop()
--- 13. Linear satisfies relevant constraint ---
  RelevantBoxWithLinear.Drop()
  RelevantBox<T>.Drop()
  LinearToken(13).Drop()
--- 14. Task<T> stored in relevant container ---
  RelevantTaskHolder<T>.Resolve()
  result=42
  RelevantTaskHolder<T>.Drop()
--- 15. Task<T> stored in linear container ---
  LinearTaskHolder<T>.Resolve()
  result=99
  LinearTaskHolder<T>.Drop()
--- 16. ValueTask<T> stored in linear container ---
  LinearValueTaskHolder<T>.Resolve()
  result=7
  LinearValueTaskHolder<T>.Drop()
=== Done ===
```

### Requirement 8: Book Documentation

**User Story:** As a Mist developer, I want the language book to document substructural field discipline rules and generic substructural constraints, so that I can learn the correct containment rules and how to write sound generic types.

#### Acceptance Criteria

1. THE book SHALL include a new section as a separate markdown file covering substructural field discipline, placed in the Advanced Features part of the book after the existing Drop Semantics chapter, with compiled code examples demonstrating at least one snippet per key concept (minimum 4 snippets).
2. THE book section SHALL demonstrate: the discipline compatibility matrix (which containers can hold which fields), the reasoning behind each rule (drop guarantee analysis), how to use substructural constraints on generic type parameters, and constraint satisfaction subtyping (linear satisfies both affine and relevant).
3. All code examples SHALL be compiled snippets following the Book Snippet Policy: each example stored as a `.mist` file in `book/snippets/` with a corresponding `.cs` file generated by the SnippetEmitter tool, and included in the markdown via `{{#include}}` references using the tabbed format.
4. THE book's SUMMARY.md SHALL reference the new section in the Advanced Features part, listed immediately after the "Drop Semantics" entry.

### Requirement 9: Remove MIST_TC_0110 Task Storage Restriction

**User Story:** As a Mist developer, I want to store `Task<T>` in fields and properties of types with appropriate substructural discipline, so that I can build abstractions (e.g., task combinators, deferred executors) that hold pending tasks without the compiler rejecting them.

#### Acceptance Criteria

1. THE Compiler SHALL remove the `MIST_TC_0110` diagnostic and all associated checks that blanket-prohibit `Task<T>` from appearing in fields, properties, or collection element types.
2. WHEN `Task<T>` (relevant discipline) is used as a field type in a relevant or linear container, THE DropAnalyzer SHALL permit the field because the discipline-compatibility matrix (Requirement 1 AC 1, AC 3) allows relevant fields in relevant and linear containers.
3. WHEN `Task<T>` is used as a field type in an affine or unrestricted container, THE DropAnalyzer SHALL reject the field using the standard Requirement 2 diagnostic messages (not `MIST_TC_0110`), because affine containers may never be dropped (violating relevant's at-least-once requirement) and unrestricted containers make no drop guarantees.
4. WHEN `ValueTask<T>` (linear discipline) is used as a field type in a linear container, THE DropAnalyzer SHALL permit the field because the discipline-compatibility matrix (Requirement 1 AC 1) allows linear fields in linear containers.
5. WHEN `ValueTask<T>` is used as a field type in an affine, relevant, or unrestricted container, THE DropAnalyzer SHALL reject the field using the standard Requirement 2 diagnostic messages (not `MIST_TC_0110`), because affine may never drop (violating linear's exactly-once), relevant may drop multiple times (violating linear's at-most-once), and unrestricted makes no guarantees.
6. THE Compiler SHALL continue to permit `Task<T>` and `ValueTask<T>` in local variables, method parameters, and return types (existing behavior preserved).
7. WHEN a generic type parameter constrained to `relevant` (e.g., `where T : relevant`) is instantiated with `Task<T>`, THE constraint checker SHALL accept the type argument because `Task<T>` has relevant discipline. WHEN a generic type parameter constrained to `linear` is instantiated with `ValueTask<T>`, THE constraint checker SHALL accept the type argument because `ValueTask<T>` has linear discipline.
8. THE existing test files `TaskStorageRestrictionDiagnosticTests.cs` and `TaskStorageRestrictionPropertyTests.cs` SHALL be updated to verify: (a) `Task<T>` IS permitted in relevant and linear container fields without any diagnostic, (b) `ValueTask<T>` IS permitted in linear container fields without any diagnostic, and (c) `Task<T>` in affine or unrestricted container fields and `ValueTask<T>` in affine, relevant, or unrestricted container fields produce the standard discipline-compatibility diagnostic from Requirement 2 (not `MIST_TC_0110`).
9. IF any code path previously guarded by `MIST_TC_0110` becomes reachable after removal, THEN THE DropAnalyzer's discipline-compatibility matrix SHALL handle it correctly — no new diagnostic ID is needed because the matrix rules from Requirement 1 already cover all cases.
