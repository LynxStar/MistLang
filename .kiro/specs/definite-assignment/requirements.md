# Requirements Document

## Introduction

This document specifies definite-assignment analysis for the Mist programming language. The goal is to close a class of soundness holes where a value can be read before it has been given a real value — currently masked by the CLR's habit of zero-filling all memory. In Mist, `null` does not exist and every binding is supposed to hold a fully-formed value, but three code locations can currently observe a zeroed-but-never-assigned value:

1. **Constructor field reads** — a field can be read inside a constructor before the constructor assigns it, observing the CLR zero value (e.g. a `null` reference-typed field).
2. **Local variables** — a `var`/typed local can (in principle) be read before assignment.
3. **Array element creation** — `new T[size]` allocates `size` slots that the CLR zero-fills; for reference-typed `T` those slots are `null`, and for struct `T` with a non-zero `default` those slots silently disagree with the type's declared default.

The design adopts Rust's enforcement philosophy: soundness is enforced at the **read site** (you may not read what has not been definitely assigned), not papered over at the **allocation site** (no silent fill, no hidden per-element cost). For arrays specifically, Mist follows Rust's tiered model — the bare/stamped form is restricted to types whose default is a flat copyable value, and all other element types require an explicit initializer that provably supplies a value for every slot.

This feature supersedes the interim "emit a default-fill loop for array creations" behavior (commit `30a85aa`), which hid an O(n) allocation cost behind innocuous-looking `new T[n]` syntax.

### Non-Goals

- **Stampability extension.** Inference for constructed types, the `stampable` declaration modifier, and `where T : stampable` are specified by [stampability](../stampability/requirements.md). They supersede this feature's original blanket prohibition on type-parameter fields without changing default construction or assignment analysis.
- **No clone-replication construction form.** The `vec![x; n]` analog (clone one value into N slots) is expressible via the index→value function (`new Array<T, N>(i => original.Clone())`) and is not given dedicated syntax.
- **No change to `default` semantics for non-array contexts.** `default(T)`, defaultable-field synthetic injection, and `default {}` blocks are unchanged except where the array-fill use of `__CreateDefault()` is removed.

## Glossary

- **Definite_Assignment**: A static dataflow property: a storage location (field, local, or array element) has been assigned a value on every control-flow path that reaches a given read of that location.
- **Definite_Assignment_Analyzer**: The semantic analysis component that computes definite-assignment state and emits diagnostics for reads of not-definitely-assigned locations.
- **Stampable_Type**: A type whose `default` value is a flat, copyable bit pattern with no heap identity, such that replicating it into N slots is a pure copy (no constructor/factory call, no aliasing). Primitive types and ordinary structs with recursively stampable instance storage qualify, including substituted constructed types and open types with sufficient `stampable` proof. Reference types and structs containing reference-bearing storage never qualify. The complete category rules and declaration guarantees are defined in [stampability](../stampability/requirements.md).
- **Stampable_Struct**: A struct type whose every field's type is transitively a primitive or another Stampable_Struct (no reference-typed fields, no type-parameter fields that could bind to a reference type).
- **Stamp_Fill**: Array initialization that replicates a single stampable default value into every slot via a bitwise/value copy, with no per-element constructor or factory invocation.
- **Initializer_Array_Construction**: Array construction that supplies a value for every slot via an explicit index→value function passed to the `Array<TValue, Length>` constructor — `new Array<T, Size>(i => ...)` — so that no slot is ever observable without an assigned value. The constructor invokes the function once per index in `[0, Length)`, assigning result `i` to slot `i`.
- **Read_Before_Assignment**: A read of a storage location on a control-flow path where that location has not been definitely assigned.
- **Type_Checker**: The `TypeCheckerAnalyzer` semantic-analysis pass that validates Mist type rules.
- **Semantic_Model_Builder**: The pass that constructs the semantic model (symbols, scopes, types) from ASTs.
- **CSharp_Emitter**: The backend that transpiles the Mist AST/semantic model into C# source.
- **Synthetic_Default_Injection**: The existing mechanism by which a field whose type is defaultable and which has no explicit initializer is treated as pre-assigned (its default is materialized for it).

## Requirements

### Requirement 1: Constructor Field Read-Before-Assignment Detection

**User Story:** As a Mist developer, I want the compiler to reject reading a field before it has been assigned in a constructor, so that I never observe a zero/null value that the type system claims cannot exist.

#### Acceptance Criteria

1. WHEN a constructor body reads an instance field of the constructed type on a control-flow path where that field has not been definitely assigned, THE Definite_Assignment_Analyzer SHALL emit a `MIST_FIELD_READ_BEFORE_ASSIGNMENT` error naming the field and the constructor.
2. WHERE a field has an explicit field initializer, THE Definite_Assignment_Analyzer SHALL treat that field as definitely assigned at the start of the constructor body.
3. WHERE a field's declared type is defaultable and the field has no explicit initializer (Synthetic_Default_Injection applies), THE Definite_Assignment_Analyzer SHALL treat that field as definitely assigned at the start of the constructor body.
4. WHEN a field is assigned on some but not all branches of a conditional and is then read after the merge point, THE Definite_Assignment_Analyzer SHALL treat the field as not definitely assigned at the read and SHALL emit `MIST_FIELD_READ_BEFORE_ASSIGNMENT`.
5. WHEN a field is assigned on every branch of a conditional before a read after the merge point, THE Definite_Assignment_Analyzer SHALL treat the field as definitely assigned at that read and SHALL NOT emit a diagnostic.
6. WHEN a field is read after it has been definitely assigned earlier in the same constructor, THE Definite_Assignment_Analyzer SHALL NOT emit a diagnostic.
7. WHERE the existing end-of-constructor `MIST_FIELD_NOT_DEFINITELY_ASSIGNED` check applies, THE Definite_Assignment_Analyzer SHALL continue to emit that diagnostic for fields not assigned on all paths by the end of the constructor, independently of the read-before-assignment check.
8. WHEN `this` is passed as an argument, returned, or has a method invoked on it before all non-defaultable fields are definitely assigned, THE Definite_Assignment_Analyzer SHALL treat that as a read of all not-yet-assigned fields and SHALL emit `MIST_FIELD_READ_BEFORE_ASSIGNMENT`.

### Requirement 2: Local Variable Read-Before-Assignment Detection

**User Story:** As a Mist developer, I want the compiler to reject reading a local variable before it has been assigned, so that locals always hold a real value.

#### Acceptance Criteria

1. WHEN a local variable declared without an initializer is read on a control-flow path where it has not been definitely assigned, THE Definite_Assignment_Analyzer SHALL emit a `MIST_LOCAL_READ_BEFORE_ASSIGNMENT` error naming the local.
2. WHERE a local variable is declared with an initializer expression, THE Definite_Assignment_Analyzer SHALL treat it as definitely assigned immediately after its declaration.
3. WHEN a local variable is assigned on some but not all branches of a conditional and is then read after the merge point, THE Definite_Assignment_Analyzer SHALL treat the local as not definitely assigned at that read and SHALL emit `MIST_LOCAL_READ_BEFORE_ASSIGNMENT`.
4. WHEN a local variable is assigned on every branch of a conditional before a read after the merge point, THE Definite_Assignment_Analyzer SHALL treat the local as definitely assigned at that read and SHALL NOT emit a diagnostic.
5. WHEN a local variable is read within a loop body on a path where it may not have been assigned in a prior iteration or before the loop, THE Definite_Assignment_Analyzer SHALL treat it as not definitely assigned and SHALL emit `MIST_LOCAL_READ_BEFORE_ASSIGNMENT`.
6. WHEN a local variable is used as the target of a compound assignment (e.g. `x += 1`) before being definitely assigned, THE Definite_Assignment_Analyzer SHALL treat the compound assignment as a read and SHALL emit `MIST_LOCAL_READ_BEFORE_ASSIGNMENT`.

### Requirement 3: Stampable Type Classification

**User Story:** As a Mist compiler, I need a precise definition of which types are stampable, so that bare array creation can be restricted to types whose default replicates safely with no hidden cost.

#### Acceptance Criteria

1. THE compiler SHALL classify a struct receiver as Stampable_Type when every part of its non-static instance storage is recursively stampable after receiver substitution, including inherited fields and automatic-property or explicit `field` accessor backing storage, subject to the category gates in the stampability specification.
2. THE Semantic_Model_Builder SHALL classify every class (reference) type as NOT a Stampable_Type.
3. THE Semantic_Model_Builder SHALL classify a struct that contains at least one reference-typed field (directly or transitively) as NOT a Stampable_Type.
4. THE compiler SHALL classify stored type parameters after substituting available actual type arguments. An unresolved stored parameter SHALL require `stampable` proof; `default`, `struct`, or readonly information alone SHALL be insufficient. An unused type parameter SHALL NOT prevent stampability. See stampability Requirements 2–5 for inference, declaration guarantees, and constraint satisfaction.
5. THE stampability computation SHALL terminate on recursive and expanding generic storage graphs, SHALL refuse actual recursive value storage conservatively, and SHALL accept finite substituted nesting such as `Box<Box<int>>` when its storage is otherwise stampable.

### Requirement 4: Bare Array Creation Restricted to Stampable Types

**User Story:** As a Mist developer, I want `new T[size]` to work only when every slot can be filled by a zero-cost stamp, so that array allocation never hides a per-element constructor cost or produces a null/zero slot that violates the type's default.

#### Acceptance Criteria

1. WHEN the Type_Checker encounters a bare array creation `new T[size]` and `T` is a Stampable_Type, THE Type_Checker SHALL accept it and the CSharp_Emitter SHALL emit a Stamp_Fill (CLR zero-fill or replicated stamp value) with no per-element factory call.
1a. WHERE a Stampable_Type's stamp value is equal to the CLR zero-fill bit pattern, THE CSharp_Emitter SHALL rely on the CLR zero-fill and SHALL NOT emit a replication step.
1b. WHERE a Stampable_Type's stamp value differs from the CLR zero-fill bit pattern, THE CSharp_Emitter SHALL emit the replicated stamp value into every slot.
2. WHEN the Type_Checker encounters a bare array creation `new T[size]` and `T` is NOT a Stampable_Type, THE Type_Checker SHALL emit a `MIST_ARRAY_REQUIRES_INITIALIZER` error stating that `T` is not stampable and that an initializer-based array construction is required.
3. WHERE `T` is a Stampable_Type whose `default` is the all-zero value, THE CSharp_Emitter SHALL rely on the CLR's zero-fill and SHALL NOT emit any per-element initialization loop.
4. THE CSharp_Emitter SHALL NOT emit the default-fill loop introduced by the interim array-default behavior; that behavior is removed by this feature.
5. WHEN the Type_Checker encounters a bare array creation whose element type is a reference type, THE Type_Checker SHALL emit `MIST_ARRAY_REQUIRES_INITIALIZER` (a specialization of AC 2).
6. THE bare `new T[size]` form SHALL be lowered as a compiler intrinsic (Stamp_Fill) and SHALL NOT invoke any `Array` constructor.

### Requirement 5: Initializer-Based Array Construction

**User Story:** As a Mist developer, I want to construct an array of any element type by supplying an index→value function, so that I can build arrays of reference types without nulls and with the per-element cost visible in my source.

#### Acceptance Criteria

1. THE language SHALL provide an initializer-based array construction form `new Array<T, Size>(fn)` where `fn` is an index→value function; the constructed array's element at index `i` SHALL be the result of invoking `fn(i)`.
2. WHEN `new Array<T, Size>(fn)` is used, THE Type_Checker SHALL accept any element type `T` (stampable, defaultable, reference, or otherwise) provided `fn`'s result type is assignable to `T`.
3. WHEN `new Array<T, Size>(fn)` is used, THE CSharp_Emitter SHALL emit code that invokes `fn` once per index in `[0, Size)`, assigning result `i` to slot `i`, such that every slot holds an assigned value before the array is observable.
4. THE Definite_Assignment_Analyzer SHALL treat an array produced by `new Array<T, Size>(fn)` as fully definitely assigned (every element initialized).
5. THE Type_Checker SHALL require `fn` to accept a single integer index parameter and return a value assignable to the element type `T`; otherwise it SHALL emit a diagnostic.
6. WHERE a developer needs an array of N copies of a single clonable value, THE developer SHALL express it via the index→value function (e.g. `new Array<T, N>(i => original.Clone())`); the language SHALL NOT provide a dedicated clone-replication construction form in this feature.
7. WHEN the element type `T` is non-stampable, `new Array<T, Size>(fn)` SHALL be the supported construction form (the bare `new T[size]` form is rejected per Requirement 4).
8. WHERE all element values are explicitly provided via an array literal (`[a, b, c]`), THE Type_Checker SHALL accept it for any element type `T` (stampable or not), since every slot is explicitly assigned; this feature SHALL NOT restrict array literal construction.
9. THE Definite_Assignment_Analyzer SHALL treat an array produced by an array literal as fully definitely assigned (every element explicitly provided).

### Requirement 6: Array Element Reads Are Sound

**User Story:** As a Mist developer, I want every read of an array element to return a real value, so that indexing never yields a null or zero-but-invalid value.

#### Acceptance Criteria

1. WHEN an array is created via Stamp_Fill of a Stampable_Type, THE language SHALL guarantee that every element read returns a valid stamped value of that type.
2. WHEN an array is created via Initializer_Array_Construction, THE language SHALL guarantee that every element read returns the value the initializer produced for that index.
2a. WHEN an array is created via an array literal, THE language SHALL guarantee that every element read returns the explicitly-provided value at that index.
3. THE language SHALL NOT provide any array-creation form that yields an element observable as `null` or as a zero value inconsistent with the element type's declared `default`.
4. THE language SHALL NOT provide a parameterless `new Array<T, Size>()` construction path; declaring the explicit initializer constructor SHALL suppress the inferred `new()`, eliminating the zeroed/empty-array path that would otherwise yield null/zero-invalid slots.

### Requirement 7: Diagnostic Quality

**User Story:** As a Mist developer, I want clear diagnostics when I read something before assigning it or use a non-stampable type in a bare array, so that I understand how to fix the code.

#### Acceptance Criteria

1. WHEN `MIST_FIELD_READ_BEFORE_ASSIGNMENT` is emitted, THE diagnostic message SHALL name the field, the constructor, and SHALL be located at the offending read expression.
2. WHEN `MIST_LOCAL_READ_BEFORE_ASSIGNMENT` is emitted, THE diagnostic message SHALL name the local variable and SHALL be located at the offending read expression.
3. WHEN `MIST_ARRAY_REQUIRES_INITIALIZER` is emitted, THE diagnostic message SHALL name the element type, SHALL state why it is not stampable (reference type, or struct with a non-stampable field), and SHALL direct the developer to use the initializer-based construction form.
4. THE diagnostic messages SHALL be phrased in terms of the Mist source construct, not the underlying CLR/C# emission.

### Requirement 8: Migration of Affected Samples and Standard Library

**User Story:** As a maintainer, I want existing samples and the Standard Library updated to the new array rules, so that the whole codebase compiles cleanly under definite-assignment enforcement.

#### Acceptance Criteria

1. WHEN any existing sample under `samples/` uses bare `new T[size]` with a non-stampable `T`, THE migration SHALL replace it with an array literal (when the values are known) or the `new Array<T, Size>(fn)` constructor form.
2. WHEN any Standard Library code uses bare `new T[size]` with a non-stampable `T`, THE migration SHALL replace it with an array literal or the `new Array<T, Size>(fn)` constructor form.
3. THE `Array<TValue, Length>` bootstrap definition SHALL be reconciled with the new rules: the bare `new T[size]` path SHALL require `TValue` stampable, and the `new Array<TValue, Length>(fn)` constructor path SHALL accept any `TValue`.
3a. THE `Array<TValue, Length>` bootstrap definition SHALL declare an explicit constructor taking the index→value function. Declaring this constructor SHALL suppress the compiler-inferred parameterless `new()`, so that the unsound "construct a zeroed/empty array" path is not available via `new Array<T, Size>()`.
3b. THE bare `new T[size]` form SHALL be a compiler intrinsic that performs Stamp_Fill directly and SHALL NOT invoke any `Array` constructor; it is the only construction path that does not go through the explicit initializer constructor, and it is restricted to stampable `T` per Requirement 4.
4. AFTER migration, `samples/AllFeatures` SHALL compile and run with zero errors, zero warnings, and zero informational diagnostics.
5. WHEN a sample read a field/local before assignment in a way newly rejected by this feature, THE migration SHALL restructure the sample so the value is assigned before use, without weakening what the sample demonstrates.

#### Known Affected Samples (reference-type arrays)

The following samples use bare `new T[size]` where `T` is a reference type (`string` or a `class`) and MUST be migrated to an array literal or the `new Array<T, Size>(fn)` constructor:

6. `samples/Iterators/Program.mist` — `new string[3]` (`wordData`) SHALL be migrated.
7. `samples/ClrRefInterop/Program.mist` — the `[ref-param-array]` block uses `new string[2]` solely to demonstrate `Array.Resize(ref T[], int)`. Because `Array.Resize` reassigns the array reference (changing its length, which in Mist is part of the fixed `Array<T, Length>` type identity), it is fundamentally unsafe in Mist's model. THE migration SHALL **remove** the `Array.Resize` demonstration entirely and replace it with a different CLR `ref`-parameter demonstration that does not reassign an array — for example `Interlocked.Increment(ref int)` or `Interlocked.Add(ref int, int)` — preserving the sample's intent (proving CLR `ref` interop) without introducing the unsafe array-resize pattern. The `new string[2]` array SHALL be removed along with the resize demo.
8. `samples/TopLevelStatements/Disambiguation.mist` — `new string[2]` (`demoArgs`) SHALL be migrated.
9. `samples/TopLevelStatements/Program.mist` — `new string[3]` (`items`) SHALL be migrated.
10. `samples/OptionalDefaultability/Program.mist` — `new Point[2]` where `Point` is a **class** (reference type) SHALL be migrated. **Note:** this sample's central theme is the old "array requires `TValue : default`" model that this feature replaces; the migration MUST update the sample's PROOF comments and narrative to reflect the new stampable-vs-initializer model rather than the superseded defaultable-element-array rule, without weakening the demonstration.

#### Stampable Sample Verification

11. THE following samples use bare `new int[size]` (primitive, stampable) and SHALL continue to compile unchanged: `Iterators` (`numbers`), `ArrayBoundsFlowAnalysis`, `AssemblyTypeProvider`, `PatternMatching`, `Properties`, `RefParameters`, `Primitives`, `RefStructs`, `HelloMist`, `SwitchExpressions`, `AdhocRefinementConstraints`. The migration SHALL verify each still compiles; if any newly fails, THE migration SHALL treat it as a stampability-classification defect and fix the classifier, not the sample.

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating definite assignment and the new array construction model, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DefiniteAssignment/Program.mist`.
2. The sample SHALL demonstrate every permutation defined in the preceding requirements, including but not limited to: constructor field assignment on all paths; local variable assignment before use; conditional-branch assignment that merges to definitely-assigned; bare `new T[size]` for a stampable struct; bare `new T[size]` for a primitive; initializer-based construction for a reference type; initializer-based construction for a non-stampable struct; an array literal of reference types.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `DefiniteAssignment.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// PROOF: a constructor that assigns every field on all paths compiles
// PROOF: a local assigned before use compiles and reads the assigned value
// PROOF: bare new T[size] works for a primitive (stampable) element type
// PROOF: bare new T[size] works for a stampable struct element type
// PROOF: initializer-based construction fills a reference-type array with no nulls
// PROOF: an array literal of reference types is accepted (every element explicit)
// PROOF: every array element read returns a real, assigned value

namespace DefiniteAssignment;

using Standard;

public struct Point
{
    public int X;
    public int Y;
}

public class Label
{
    public string Text;

    public Label(string text)
    {
        this.Text = text;
    }

    public string Describe()
    {
        return $"Label({Text})";
    }
}

public class Program
{
    // Constructor assigns every field on all paths — definite assignment satisfied.
    public static void RunDemo()
    {
        // Local assigned before use.
        var greeting = "definite";
        Console.WriteLine($"[da] local={greeting}");

        // Conditional assignment that is definite at the merge point.
        var n = 3;
        int category;
        if (n > 0)
        {
            category = 1;
        }
        else
        {
            category = 0;
        }
        Console.WriteLine($"[da] category={category}");

        // Bare new T[size] for a primitive (stampable) — zero-filled stamp.
        var ints = new int[3];
        Console.WriteLine($"[da] ints[0]={ints[0]} ints[2]={ints[2]}");

        // Bare new T[size] for a stampable struct — zero-filled stamp.
        var points = new Point[2];
        Console.WriteLine($"[da] points[0].X={points[0].X} points[1].Y={points[1].Y}");

        // Initializer-based construction for a reference type — no nulls.
        var labels = new Array<Label, 3>(i => new Label($"item-{i}"));
        Console.WriteLine($"[da] labels[0]={labels[0].Describe()}");
        Console.WriteLine($"[da] labels[2]={labels[2].Describe()}");

        // Array literal for a reference type — every element explicitly provided.
        var pair = [new Label("first"), new Label("second")];
        Console.WriteLine($"[da] pair[0]={pair[0].Describe()}");
        Console.WriteLine($"[da] pair[1]={pair[1].Describe()}");
    }
}
```

**Expected output:**
```
[da] local=definite
[da] category=1
[da] ints[0]=0 ints[2]=0
[da] points[0].X=0 points[1].Y=0
[da] labels[0]=Label(item-0)
[da] labels[2]=Label(item-2)
[da] pair[0]=Label(first)
[da] pair[1]=Label(second)
```

### Requirement 10: Book Documentation

**User Story:** As a Mist developer, I want the language book to document definite assignment and the array construction model, so that I can learn the rules from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a chapter or section covering definite assignment (fields in constructors, locals) and the stampable-vs-initializer array construction model, with compiled code examples.
2. THE book section SHALL demonstrate: read-before-assignment rejection for fields and locals; bare array creation for stampable types; initializer-based construction for non-stampable types; the soundness guarantee that no array element is ever null/zero-invalid.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new chapter or section in the appropriate section.
```
