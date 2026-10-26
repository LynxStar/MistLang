# Requirements Document

## Introduction

This document specifies the requirements for adding drop semantics to the Mist programming language. The feature introduces substructural type modifiers (`affine`, `linear`, `relevant`) as first-class concepts, a unified `Drop()` method for deterministic cleanup, and compile-time enforcement of drop discipline.

The design unifies cleanup into a single mechanism: the `Drop()` method. The type's substructural discipline determines when and how many times `Drop()` fires. There is no IDisposable vs IDroppable split — one method, different guarantees based on the type modifier.

`Drop()` can return a value, enabling the container to die while yielding something to the caller. This unifies `await` with drop: `await task` desugars to `task.Drop()` which returns `T`.

## Substructural Disciplines

| Modifier | Drop called... | Owned? | Can alias? | Can forget? | Example |
|---|---|---|---|---|---|
| (none) | Only in `with` blocks (0 to many) | No | Yes | Yes | Plain objects with optional cleanup |
| `affine` | Only in `with` blocks (0 or 1) | Yes | No | Yes (outside `with`) | One-shot tokens |
| `linear` | Always at scope exit (exactly 1) | Yes | No | No | `ValueTask<T>` |
| `relevant` | Always at scope exit (1+, idempotent) | No | Yes | No | `Task<T>` |

## Glossary

- **Drop()**: A method on a type that performs deterministic cleanup. May return a value (`T Drop()`) or return void (`void Drop()`). The compiler inserts calls based on the type's substructural discipline.
- **Substructural_Modifier**: One of `affine`, `linear`, or `relevant` — declared on the type. Determines the compile-time guarantees around Drop invocation.
- **Auto_Drop_Insertion**: The compiler pass that inserts `Drop()` calls at scope exit for linear and relevant types.
- **Drop_As_Move**: For owned types (affine, linear), calling `Drop()` consumes the value — the variable is inaccessible afterward.
- **Synthetic_Drop**: A compiler-generated `Drop()` method for types that don't explicitly define one but contain fields with `Drop()` obligations.
- **Scope_Exit**: The point at which a local variable's scope ends — the closing brace of the enclosing block, an early return, or a break/continue that exits the variable's scope.
- **Drop_Elision**: For owned types (affine, linear), the compiler skips `Drop()` insertion when the value was moved or returned before scope exit.

## Requirements

### Requirement 1: Substructural Type Modifiers

**User Story:** As a Mist developer, I want to declare types with `affine`, `linear`, or `relevant` modifiers, so that the compiler enforces the correct drop discipline for each type.

#### Acceptance Criteria

1. THE Parser SHALL recognize `affine`, `linear`, and `relevant` as type declaration modifiers, valid on `class` and `struct` declarations.
2. THE Parser SHALL accept these modifiers combined with existing modifiers (e.g., `public linear class ValueTask<T>`, `public relevant class Task<T>`, `public affine class AuthToken`).
3. THE `affine` modifier SHALL imply `owned` — an affine type has single-holder move semantics. THE Compiler SHALL reject `affine` combined with an explicit `owned` modifier (redundant).
4. THE `linear` modifier SHALL imply `owned` — a linear type has single-holder move semantics. THE Compiler SHALL reject `linear` combined with an explicit `owned` modifier (redundant).
5. THE `relevant` modifier SHALL NOT imply `owned` — relevant types are aliasable.
6. THE Compiler SHALL reject combining `affine`, `linear`, or `relevant` with each other on the same type (mutually exclusive).
7. THE Compiler SHALL reject combining `owned` with `relevant` (relevant types must be aliasable).
8. WHEN a `linear` or `affine` value is a struct, a `ref struct`, or a constructed struct, THE Compiler SHALL apply the same single-holder moves as for a class: initializing or assigning another local, passing to an `owned` parameter, calling a `with owned` receiver, storing into an owned field, returning, and calling `Drop()` consume the value and leave the source binding inaccessible; later reads, writes, passes and member accesses SHALL report `MIST_PA_USE_AFTER_MOVE` at the use, naming whether the value was moved or dropped. A physical copy in the backend is an implementation detail hidden by flow analysis. Neither discipline may be an array element, whatever the shape ([substructural-field-discipline Requirement 1.8](../substructural-field-discipline/requirements.md#requirement-1-discipline-compatible-field-containment)).

### Requirement 2: Drop() Method Declaration

**User Story:** As a Mist developer, I want to define a `Drop()` method on my types that performs cleanup and optionally returns a value, so that I have full control over what happens when the type is consumed.

#### Acceptance Criteria

1. THE Compiler SHALL recognize `Drop()` as a special method name with compiler-enforced calling semantics.
2. THE `Drop()` method SHALL support any return type, including `void` (e.g., `public void Drop()`, `public T Drop()`, `public int Drop()`).
3. THE `Drop()` method SHALL NOT accept any parameters.
4. WHEN a type declares a `Drop()` method, THE Compiler SHALL enforce the type's substructural discipline around calls to that method.
5. THE Compiler SHALL permit any type (unrestricted, affine, linear, or relevant) to declare a `Drop()` method.
6. FOR unrestricted types with `Drop()`, THE Compiler SHALL only auto-invoke `Drop()` in `with` blocks — never at scope exit.
7. FOR affine types, THE Compiler SHALL only auto-invoke `Drop()` in `with` blocks, and SHALL guarantee it is called at most once.
8. FOR linear types, THE Compiler SHALL always insert `Drop()` at scope exit if not explicitly called, and SHALL guarantee exactly-once invocation.
9. FOR relevant types, THE Compiler SHALL always insert `Drop()` at scope exit for every reference, and the `Drop()` implementation MUST be idempotent.

### Requirement 3: Drop() Consumes Owned Types

**User Story:** As a Mist developer, I want calling `Drop()` on an affine or linear type to consume the value, so that use-after-drop is a compile-time error.

#### Acceptance Criteria

1. WHEN `Drop()` is called on a local variable of an affine or linear type, THE Compiler SHALL treat the call as consuming the value — the variable is inaccessible after the call.
2. IF a variable is accessed after `Drop()` has been called on it (and the type is affine or linear), THEN THE Compiler SHALL report a compile-time error (use-after-drop).
3. WHEN `Drop()` is called on a relevant type, THE Compiler SHALL NOT treat it as consuming — the variable remains accessible (idempotent semantics).
4. WHEN `Drop()` returns a value on an owned type (affine/linear), THE Compiler SHALL allow the return value to be captured by the caller: `T result = value.Drop();`.
5. WHEN the compiler auto-inserts `Drop()` at scope exit and `Drop()` returns a non-void type, THE Compiler SHALL discard the return value.

### Requirement 4: Auto-Drop Insertion at Scope Exit

**User Story:** As a Mist developer, I want the compiler to automatically insert `Drop()` calls for linear and relevant types when they fall out of scope, so that I get deterministic cleanup without manual calls.

#### Acceptance Criteria

1. WHEN a local variable of a **linear** type reaches scope exit without having been explicitly dropped, moved, or returned, THE Compiler SHALL insert a call to `Drop()` at the scope exit point.
2. WHEN a local variable of a **linear** type has been moved before scope exit, THE Compiler SHALL NOT insert a `Drop()` call (drop elision).
3. WHEN a local variable of a **linear** type is returned from the method, THE Compiler SHALL NOT insert a `Drop()` call (ownership transferred to caller).
4. WHEN a local variable of a **relevant** type reaches scope exit, THE Compiler SHALL always insert a `Drop()` call regardless of whether other references exist.
5. WHEN a scope has multiple linear/relevant variables, THE Compiler SHALL insert `Drop()` calls in reverse declaration order (last declared, first dropped).
6. WHEN a linear variable is moved in one branch of a conditional but not another, THE Compiler SHALL insert `Drop()` only in the branches where the variable was not moved.
7. WHEN a method has multiple exit points (early returns, break, continue), THE Compiler SHALL insert `Drop()` calls for all live linear/relevant variables at each exit point.
8. THE Compiler SHALL NOT auto-insert `Drop()` for **unrestricted** or **affine** types at scope exit (affine only fires in `with` blocks).

### Requirement 5: Drop in With Blocks

**User Story:** As a Mist developer, I want `with` forms to invoke `Drop()` at scope exit for any droppable type, so that I can deterministically scope the lifetime of a type with cleanup. This composes with the existing structured-context feature: the `with(expr) { ... }`, `with var x = expr;`, `with Type x = expr;`, `with Type _ = expr;`, and `with expr;` constructs already push a value onto an ambient context stack and pop it at scope exit, and drop behavior is layered additively on top of that existing push/pop without replacing it.

#### Acceptance Criteria

1. WHEN a `with(expr) { ... }` block is used and the expression's type has a `Drop()` method, THE Compiler SHALL insert a `Drop()` call at block exit in addition to the existing ambient-context pop.
2. WHEN a `with var x = expr` (or `with Type x = expr`) binding's type has a `Drop()` method, THE Compiler SHALL insert a `Drop()` call at the enclosing scope exit, after the context pop.
3. THE Compiler SHALL preserve the existing ambient-context push/pop semantics unchanged for all `with` forms, applying drop behavior additively only when the bound type has a `Drop()` method.
4. FOR affine types, THE `with` family SHALL be the only mechanism that triggers `Drop()`; outside `with`, an affine value MAY be forgotten.
5. FOR linear types bound by a `with` form, THE `with` block exit SHALL satisfy the linear drop obligation, with no additional auto-drop at the enclosing scope exit.
6. WHEN `Drop()` returns a non-void value in a `with` context, THE Compiler SHALL discard the return value.
7. WHEN a type bound by `with` is both a context type and has a `Drop()` method, THE Compiler SHALL emit the context pop and then the `Drop()` call within the same `finally` block (pop before drop).

### Requirement 6: No Forget Escape Hatch (Linear and Relevant)

**User Story:** As a language designer, I want to enforce that linear and relevant values cannot be silently forgotten, so that resource leaks are structurally impossible.

#### Acceptance Criteria

1. THE Compiler SHALL NOT provide a `forget` function or mechanism that suppresses auto-drop insertion for linear or relevant values.
2. WHEN a local variable of a linear type reaches scope exit, THE Compiler SHALL guarantee that exactly one of: the value was moved, the value was returned, `Drop()` was explicitly called, or `Drop()` was auto-inserted.
3. WHEN a local variable of a relevant type reaches scope exit, THE Compiler SHALL guarantee that `Drop()` is inserted (relevant types cannot be moved away from the obligation since they're non-owned).
4. IF a linear or relevant value is assigned to a field, THE containing type SHALL inherit the drop obligation (see Requirement 10: Synthetic Drop).

### Requirement 7: Task<T> as Relevant Type

**User Story:** As a Mist developer, I want `Task<T>` to be a relevant type whose `Drop()` returns `T` via await, so that tasks are always awaited and unawaited tasks are impossible.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Task<TResult>` as `public relevant class Task<TResult>`.
2. THE `Drop()` method on `Task<TResult>` SHALL have signature `public TResult Drop()` and SHALL await the task to completion, returning the result.
3. WHEN a `Task<TResult>` local variable reaches scope exit, THE Compiler SHALL insert `Drop()` which awaits the task. The return value is discarded on auto-drop.
4. WHEN the programmer explicitly calls `Drop()` on a `Task<TResult>`, THE return value can be captured: `TResult result = task.Drop();`.
5. `await task` SHALL be syntactic sugar for `task.Drop()` — both invoke Drop and capture the result.
6. SINCE `Task<TResult>` is relevant (non-owned, aliasable), passing a task to another function does NOT prevent auto-drop at the original scope exit. The `Drop()` implementation (await) is idempotent — awaiting a completed task is a no-op.
7. THE `Task` type alias (`type Task = Task<void>`) SHALL inherit the relevant discipline and `Drop()` from `Task<TResult>`.
8. IF a `Task<TResult>` faults and `Drop()` is invoked, THEN `Drop()` SHALL propagate the exception.
9. THE Drop-based await mechanism SHALL replace the existing `LiveTaskInfo`-based live-task tracking system in the semantic model.
9.1. THE migration SHALL preserve or correctly supersede the `ISemanticModel.GetLiveTasks(...)` contract method so that no consumer of the semantic model regresses.
9.2. THE migration SHALL preserve or correctly supersede the thread-static live-task machinery in `SemanticModelBuilder` (the per-method `LiveTaskInfo` scope tracking) so that task obligations are still recorded under the Drop-based model.
9.3. THE migration SHALL preserve or correctly supersede the emitter's scope-exit await emission (`EmitScopeExitAwait`) so that auto-dropped tasks are still awaited at scope exit.
9.4. THE migration SHALL preserve the behavior verified by the existing structured-concurrency tests so that no regression occurs in structured-concurrency scenarios.
10. THE CSharp_Emitter SHALL emit `await` expressions when emitting auto-drop for `Task<TResult>`.

### Requirement 8: ValueTask<T> as Linear Type

**User Story:** As a Mist developer, I want `ValueTask<T>` to be a linear type whose `Drop()` returns `T` via await, so that I get zero-overhead task semantics with compile-time exactly-once enforcement.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `ValueTask<TResult>` as an intrinsic linear type (`public linear class ValueTask<TResult>`, or the intrinsic equivalent matching how `Task<T>` is declared as `intrinsic public class`), with `Drop()` returning `TResult` via await.
2. THE `Drop()` method on `ValueTask<TResult>` SHALL have signature `public TResult Drop()` and SHALL await the value task to completion, returning the result.
3. WHEN a `ValueTask<TResult>` is moved before scope exit, THE Compiler SHALL NOT insert `Drop()` for the source variable.
4. WHEN a `ValueTask<TResult>` reaches scope exit without being moved or explicitly dropped, THE Compiler SHALL insert `Drop()`.
5. `await vtask` SHALL be syntactic sugar for `vtask.Drop()`.
6. THE Compiler SHALL NOT permit aliasing a `ValueTask<TResult>` — assignment to another variable is a move.
7. THE `ValueTask` type alias (`type ValueTask = ValueTask<void>`) SHALL inherit the linear discipline and `Drop()` from `ValueTask<TResult>`.
8. WHEN emitting auto-drop for `ValueTask<TResult>`, THE CSharp_Emitter SHALL emit `await` expressions.
9. THE C#-ambient/CLR type provider SHALL recognize inbound `System.Threading.Tasks.ValueTask` and `System.Threading.Tasks.ValueTask<T>` from referenced assemblies and project them to the Mist `ValueTask` type.
10. THE CSharp_Emitter SHALL emit `System.Threading.Tasks.ValueTask<T>` for the generic Mist `ValueTask<TResult>` type and `System.Threading.Tasks.ValueTask` for the non-generic `ValueTask` void alias.
11. THE linear `ValueTask<TResult>` user-facing type SHALL be reconciled with the existing `ValueTaskUsageAnalysisPass` / `Symbol.UseValueTask` async-return-type optimization so that the two notions do not conflict: the linear `ValueTask<TResult>` is an explicit user-facing type written in source, distinct from the compiler's automatic, internal choice of an async method's return type.
12. WHEN an async method's return type is determined, THE explicit user-written `ValueTask<TResult>` return type SHALL take precedence over the `Symbol.UseValueTask` optimization, and the `ValueTaskUsageAnalysisPass` optimization SHALL apply only to async methods whose return type was inferred rather than explicitly written as `ValueTask<TResult>`.

### Requirement 9: Interaction with Ownership System

**User Story:** As a Mist developer, I want drop semantics to integrate correctly with the existing ownership tiers, so that lending, borrowing, and moving all interact predictably with auto-drop.

#### Acceptance Criteria

1. WHEN a linear/affine variable is lent as `readonly ref`, THE Compiler SHALL still insert `Drop()` on the owner at scope exit (lending does not transfer drop responsibility).
2. WHEN a linear/affine variable is lent as `frozen`, THE Compiler SHALL still insert `Drop()` on the owner at scope exit.
3. WHEN a linear/affine variable is lent as `borrowed`, THE Compiler SHALL still insert `Drop()` on the owner at scope exit (borrowing is temporary).
4. WHEN a linear/affine variable is moved to another variable in the same scope, THE Compiler SHALL insert `Drop()` on the destination variable at scope exit (not the source).
5. WHEN a linear/affine variable is moved to a function parameter, THE Compiler SHALL NOT insert `Drop()` in the caller (drop responsibility transfers with ownership).
6. WHEN a function receives a linear/affine parameter, THE Compiler SHALL insert `Drop()` at end of function if the parameter was not moved or returned.

### Requirement 10: Synthetic Drop and Field Containment

**User Story:** As a Mist developer, I want types containing droppable fields to automatically participate in drop semantics without manual boilerplate, so that nested resources are cleaned up correctly by default.

#### Acceptance Criteria

1. WHEN a type has one or more fields with `Drop()` methods and does NOT explicitly define its own `Drop()` method, THE Compiler SHALL synthesize a `Drop()` method for that type.
2. THE synthesized `Drop()` SHALL call `Drop()` on each droppable field in declaration order (top field to bottom field, as they appear in the type body).
3. THE synthesized `Drop()` SHALL have return type `void`.
4. A type with a synthesized `Drop()` SHALL be treated as having drop semantics for all purposes — including triggering auto-drop at scope exit if the type is linear or relevant.
5. THIS inference SHALL cascade transitively: if C has `Drop()`, and B has a field of type C (making B implicitly droppable), and A has a field of type B, THEN A also implicitly has `Drop()`.
6. IF a type explicitly defines its own `Drop()` method, THE Compiler SHALL NOT generate a synthetic implementation — the manual implementation takes precedence.
7. WHEN a programmer provides a manual `Drop()` on a type with droppable fields, THE Compiler SHALL track which droppable fields have been explicitly dropped within the `Drop()` body. At the end of the `Drop()` body, THE Compiler SHALL insert `Drop()` calls for any droppable fields that were not explicitly dropped by the programmer.
8. THE drop order for auto-inserted field drops in a manual `Drop()` SHALL be: declaration order for remaining un-dropped fields, inserted at the end of the method body.

### Requirement 11: Owned Fields Require Owned Containers

**User Story:** As a language designer, I want to prevent non-owned types from containing owned fields, so that the single-holder invariant is never violated through aliasing.

#### Acceptance Criteria

1. IF a non-owned type declares a field of an affine or linear type, THEN THE Compiler SHALL report a compile-time error: "Non-owned type '{TypeName}' cannot contain {affine|linear} field '{FieldName}'. Aliasing the container would violate the single-holder invariant of the field."
2. THE Compiler SHALL permit owned types (including affine and linear types) to contain affine or linear fields.
3. THE Compiler SHALL permit any type to contain relevant fields (relevant types are aliasable and idempotent — no invariant violation).
4. THE Compiler SHALL permit any type to contain unrestricted fields.

### Requirement 12: Fields Can Only Be Dropped Inside Container's Drop

**User Story:** As a Mist developer, I want the compiler to prevent me from dropping fields outside of the container's `Drop()` method, so that I can't create partially-dead objects.

#### Acceptance Criteria

1. IF `Drop()` is called on a field access expression (e.g., `self.Bar.Drop()` or `foo.Bar.Drop()`) and the call site is NOT inside the containing type's `Drop()` method, THEN THE Compiler SHALL report a compile-time error: "Cannot drop field '{FieldName}' outside of the containing type's Drop() method."
2. INSIDE the containing type's `Drop()` method, calls to `Drop()` on fields SHALL be permitted.
3. THIS restriction applies to affine and linear fields. Relevant fields MAY have `Drop()` called from anywhere (since relevant types are idempotent by contract).

### Requirement 13: Drop Ordering and Nested Scopes

**User Story:** As a Mist developer, I want predictable drop ordering so that I can reason about which resources are cleaned up first when multiple droppables exist.

#### Acceptance Criteria

1. WHEN multiple local variables with drop obligations exist in the same scope, THE Compiler SHALL drop them in reverse declaration order (LIFO — last declared is dropped first).
2. WHEN nested scopes each contain droppable variables, THE Compiler SHALL drop inner scope variables before outer scope variables.
3. WHEN a `with(expr) { ... }` block contains droppable variables declared inside the block, THE Compiler SHALL drop those variables before dropping the `with` expression value.
4. WHEN a loop body contains linear/relevant variables, THE Compiler SHALL insert `Drop()` at the end of each iteration for variables declared within the loop body.
5. WHEN a linear variable is declared in a loop body and moved on some iterations (conditional move), THE Compiler SHALL insert `Drop()` only on iterations where the variable was not moved.

### Requirement 14: C# Emission for Drop Semantics

**User Story:** As a language developer, I want the C# emitter to generate correct deterministic cleanup code for all substructural disciplines.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL emit `Drop()` calls as regular method calls at each scope exit point where auto-drop was inserted.
2. THE CSharp_Emitter SHALL emit `Drop()` calls in reverse declaration order when multiple droppable variables exist in the same scope.
3. THE CSharp_Emitter SHALL emit `try/finally` blocks around scopes with linear/relevant variables to ensure `Drop()` is called even when exceptions occur.
4. THE CSharp_Emitter SHALL NOT emit runtime type checks for drop eligibility — all drop insertion is statically determined.
5. WHEN emitting `Drop()` for `Task<TResult>` or `ValueTask<TResult>`, THE CSharp_Emitter SHALL emit `await` expressions instead of a method call named `Drop`.
6. WHEN emitting `await expr` in Mist source, THE CSharp_Emitter SHALL emit the C# `await` expression (since `await` desugars to `Drop()` which maps to `await` in C#).
7. THE substructural modifiers (`affine`, `linear`, `relevant`) SHALL be erased in emitted C# — they have no runtime representation.

### Requirement 15: Diagnostics for Drop Semantics

**User Story:** As a Mist developer, I want clear compile-time error messages for drop-related violations.

#### Acceptance Criteria

1. IF a linear value is used after it has been dropped or moved, THEN THE Compiler SHALL report "Use of moved/dropped value '{Name}'."
2. IF an affine value is used after `Drop()` has been called, THEN THE Compiler SHALL report "Use of dropped value '{Name}'."
3. IF a non-owned type contains an affine or linear field, THEN THE Compiler SHALL report "Non-owned type '{TypeName}' cannot contain {modifier} field '{FieldName}'."
4. IF `Drop()` is called on a field outside the container's `Drop()` method, THEN THE Compiler SHALL report "Cannot drop field '{FieldName}' outside of the containing type's Drop() method."
5. IF conflicting modifiers are combined (`affine linear`, `owned linear`, `owned relevant`), THEN THE Compiler SHALL report "Conflicting type modifiers."
6. WHEN the compiler inserts auto-drop calls, THE Compiler SHALL NOT produce warnings or informational diagnostics (auto-drop is silent).

### Requirement 16: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want substructural type modifiers to be syntax-highlighted correctly.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `affine`, `linear`, and `relevant` as storage modifiers (scope `storage.modifier.mist`).
2. THE TextMate grammar SHALL highlight them in the same position as `owned` (before `class`/`struct`).
3. THE TextMate grammar SHALL recognize `Drop` as a special method name (scope `entity.name.function.destructor.mist`) when it appears as a method declaration.
4. THE TextMate grammar SHALL continue to recognize `owned class` and `owned struct` as before (no regression).

### Requirement 17: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating drop semantics across all substructural disciplines.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DropSemantics/Program.mist` with a `mist.toml` project file.
2. The sample SHALL demonstrate: linear type with void Drop, linear type with `T Drop()` (return value), affine type with `with` block, relevant type with multiple aliases, auto-drop at scope exit, drop elision on move, drop elision on return, reverse drop ordering, nested scope ordering, synthetic drop for containing types, field drop restriction, lending interaction, and cleanup that follows the path taken (a move into another local, a drop or return on one branch, and a consumed binding given a fresh value).
3. Each demonstrated pattern SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `DropSemantics.Program.RunDemo()`.
5. The sample SHALL compile and run without errors.

#### Proof Code

```mist
// DropSemantics — Demonstrates drop semantics across all substructural disciplines.

using Standard;

namespace DropSemantics;

// ─── Linear: exactly-once drop, returns void ─────────────────────────────────

// PROOF: linear class implies owned + compiler-enforced exactly-once Drop
public linear class FileHandle
{
    int Fd;

    public FileHandle(int fd)
    {
        this.Fd = fd;
    }

    // PROOF: Drop() with void return — cleanup only
    public void Drop()
    {
        Console.WriteLine($"  FileHandle({Fd}).Drop() called");
    }

    public int GetFd()
    {
        return Fd;
    }
}

// ─── Linear: exactly-once drop, returns T ────────────────────────────────────

// PROOF: linear type whose Drop returns a value — container dies, value escapes
public linear class LinearBox<T> where T : linear
{
    T Value;

    public LinearBox(T value)
    {
        this.Value = value;
    }

    // PROOF: Drop() returns the contained value — caller can capture it
    public T Drop()
    {
        Console.WriteLine($"  LinearBox.Drop() called, yielding value");
        return Value;
    }
}

// ─── Relevant: 1+ drops, idempotent ─────────────────────────────────────────

// PROOF: relevant class — aliasable, drop fires for every reference
public relevant class SharedResource
{
    string Id;
    bool Released;

    public SharedResource(string id)
    {
        this.Id = id;
        this.Released = false;
    }

    // PROOF: Drop() must be idempotent for relevant types
    public void Drop()
    {
        if (Released)
        {
            Console.WriteLine($"  SharedResource({Id}).Drop() no-op");
            return;
        }
        this.Released = true;
        Console.WriteLine($"  SharedResource({Id}).Drop() called");
    }

    public string GetId()
    {
        return Id;
    }
}

// ─── Affine: at most once, only in with ──────────────────────────────────────

// PROOF: affine class — owned, Drop only fires in with blocks
public affine class AuthToken
{
    string Token;

    public AuthToken(string token)
    {
        this.Token = token;
    }

    // PROOF: Drop() consumes the value when called
    public void Drop()
    {
        Console.WriteLine($"  AuthToken({Token}).Drop() called - consumed");
    }

    public string GetToken()
    {
        return Token;
    }
}

// ─── Containing type with synthetic drop ─────────────────────────────────────

// PROOF: type containing linear field infers drop semantics
// PROOF: synthetic Drop() calls field Drop() in declaration order
public linear class Session
{
    FileHandle Handle;
    FileHandle Log;

    public Session(int handleFd, int logFd)
    {
        this.Handle = new FileHandle(handleFd);
        this.Log = new FileHandle(logFd);
    }
    // No explicit Drop() — compiler synthesizes one that calls Handle.Drop() then Log.Drop()
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

public class Sink
{
    public static void ConsumeHandle(FileHandle h)
    {
        Console.WriteLine($"  Sink consumed FileHandle({h.GetFd()})");
        // PROOF: Drop() fires here at function exit
    }

    public static FileHandle PassThrough(FileHandle h)
    {
        Console.WriteLine($"  PassThrough FileHandle({h.GetFd()})");
        // PROOF: Drop() NOT called — h is returned (ownership transferred)
        return h;
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
        Console.WriteLine("=== Drop Semantics Sample ===");

        // ─── 1. Linear: auto-drop at scope exit ─────────────────────────
        Console.WriteLine("--- 1. Linear: auto-drop at scope exit ---");
        {
            var f = new FileHandle(1);
            Console.WriteLine($"  Created FileHandle({f.GetFd()})");
            // PROOF: compiler inserts f.Drop() here — exactly once
        }

        // ─── 2. Linear: drop elision on move ────────────────────────────
        Console.WriteLine("--- 2. Linear: drop elision on move ---");
        {
            var f = new FileHandle(2);
            Console.WriteLine($"  Created FileHandle({f.GetFd()})");
            Sink.ConsumeHandle(f);
            // PROOF: no Drop() here — moved to ConsumeHandle
        }

        // ─── 3. Linear: drop elision on return ──────────────────────────
        Console.WriteLine("--- 3. Linear: drop elision on return ---");
        {
            var f = new FileHandle(3);
            var f2 = Sink.PassThrough(f);
            Console.WriteLine($"  Got back FileHandle({f2.GetFd()})");
            // PROOF: f2.Drop() fires here
        }

        // ─── 4. Linear: Drop() with return value ────────────────────────
        Console.WriteLine("--- 4. Linear: Drop with return value ---");
        {
            var box = new LinearBox<FileHandle>(new FileHandle(42));
            var value = box.Drop();
            // PROOF: box is consumed, value captured
            Console.WriteLine($"  Got value: {value.GetFd()}");
        }

        // ─── 5. Linear: reverse drop ordering ───────────────────────────
        Console.WriteLine("--- 5. Linear: reverse drop ordering ---");
        {
            var first = new FileHandle(10);
            var second = new FileHandle(11);
            var third = new FileHandle(12);
            Console.WriteLine($"  Created: {first.GetFd()}, {second.GetFd()}, {third.GetFd()}");
            // PROOF: drops in reverse: third, second, first
        }

        // ─── 6. Relevant: drop fires for every reference ────────────────
        Console.WriteLine("--- 6. Relevant: drop fires for every reference ---");
        {
            var res = new SharedResource("conn-1");
            var alias = res;
            Console.WriteLine($"  res={res.GetId()}, alias={alias.GetId()}");
            // PROOF: both alias.Drop() and res.Drop() fire (one is no-op)
        }

        // ─── 7. Affine: only drops in with block ────────────────────────
        Console.WriteLine("--- 7. Affine: only drops in with block ---");
        with(new AuthToken("abc-123"))
        {
            Console.WriteLine("  Inside with-block");
            // PROOF: Drop() fires at block exit
        }

        // ─── 8. Affine: no auto-drop outside with ───────────────────────
        Console.WriteLine("--- 8. Affine: no auto-drop outside with ---");
        {
            var token = new AuthToken("xyz-789");
            Console.WriteLine($"  AuthToken({token.GetToken()}) falls out of scope");
            // PROOF: NO Drop() call — affine allows forgetting outside with
        }

        // ─── 9. Synthetic drop on containing type ────────────────────────
        Console.WriteLine("--- 9. Synthetic drop on containing type ---");
        {
            var session = new Session(50, 51);
            Console.WriteLine("  Session created with handles 50 and 51");
            // PROOF: synthetic Drop() calls Handle.Drop() then Log.Drop()
        }

        // ─── 10. Lending retains drop responsibility ─────────────────────
        Console.WriteLine("--- 10. Lending retains drop responsibility ---");
        {
            var f = new FileHandle(30);
            ReadFd(f);
            Console.WriteLine($"  Owner still has FileHandle({f.GetFd()})");
            // PROOF: Drop() fires on f here — lending did not move it
        }

        // ─── 11. Linear: cleanup follows the path actually taken ─────────
        Console.WriteLine("--- 11. Linear: cleanup follows the path taken ---");
        {
            var f = new FileHandle(60);
            var moved = f;
            Console.WriteLine($"  Moved FileHandle({moved.GetFd()}) to a new local");
        }
        DropWhen(61, false);
        DropWhen(62, true);
        var kept = KeepOrReplace(63, false);
        Console.WriteLine($"  KeepOrReplace(false) returned FileHandle({kept.GetFd()})");
        var given = KeepOrReplace(66, true);
        Console.WriteLine($"  KeepOrReplace(true) returned FileHandle({given.GetFd()})");
        given.Drop();
        kept.Drop();
        {
            var f = new FileHandle(64);
            f.Drop();
            f = new FileHandle(65);
            Console.WriteLine($"  Reassigned to FileHandle({f.GetFd()})");
        }

        // ─── 12. Affine: with block drops on early return ────────────────
        Console.WriteLine("--- 12. Affine: with block drops on early return ---");
        LeaveEarly();
        Console.WriteLine("  LeaveEarly returned");

        Console.WriteLine("=== Drop Semantics Done ===");
    }

    public static void LeaveEarly()
    {
        with(new AuthToken("early"))
        {
            Console.WriteLine("  Inside with-block, returning early");
            return;
        }
    }

    public static void DropWhen(int fd, bool early)
    {
        var f = new FileHandle(fd);
        if (early)
        {
            f.Drop();
        }
        Console.WriteLine($"  DropWhen({early}) leaving");
    }

    public static FileHandle KeepOrReplace(int fd, bool keep)
    {
        var f = new FileHandle(fd);
        if (keep)
        {
            return f;
        }
        return new FileHandle(fd + 4);
    }

    public static void ReadFd(readonly ref FileHandle f)
    {
        Console.WriteLine($"  ReadFd sees FileHandle({f.GetFd()})");
    }
}
```

**Expected output:**
```
=== Drop Semantics Sample ===
--- 1. Linear: auto-drop at scope exit ---
  Created FileHandle(1)
  FileHandle(1).Drop() called
--- 2. Linear: drop elision on move ---
  Created FileHandle(2)
  Sink consumed FileHandle(2)
  FileHandle(2).Drop() called
--- 3. Linear: drop elision on return ---
  PassThrough FileHandle(3)
  Got back FileHandle(3)
  FileHandle(3).Drop() called
--- 4. Linear: Drop with return value ---
  LinearBox.Drop() called, yielding value
  Got value: 42
  FileHandle(42).Drop() called
--- 5. Linear: reverse drop ordering ---
  Created: 10, 11, 12
  FileHandle(12).Drop() called
  FileHandle(11).Drop() called
  FileHandle(10).Drop() called
--- 6. Relevant: drop fires for every reference ---
  res=conn-1, alias=conn-1
  SharedResource(conn-1).Drop() called
  SharedResource(conn-1).Drop() no-op
--- 7. Affine: only drops in with block ---
  Inside with-block
  AuthToken(abc-123).Drop() called - consumed
--- 8. Affine: no auto-drop outside with ---
  AuthToken(xyz-789) falls out of scope
--- 9. Synthetic drop on containing type ---
  Session created with handles 50 and 51
  FileHandle(50).Drop() called
  FileHandle(51).Drop() called
--- 10. Lending retains drop responsibility ---
  ReadFd sees FileHandle(30)
  Owner still has FileHandle(30)
  FileHandle(30).Drop() called
--- 11. Linear: cleanup follows the path taken ---
  Moved FileHandle(60) to a new local
  FileHandle(60).Drop() called
  DropWhen(False) leaving
  FileHandle(61).Drop() called
  FileHandle(62).Drop() called
  DropWhen(True) leaving
  FileHandle(63).Drop() called
  KeepOrReplace(false) returned FileHandle(67)
  KeepOrReplace(true) returned FileHandle(66)
  FileHandle(66).Drop() called
  FileHandle(67).Drop() called
  FileHandle(64).Drop() called
  Reassigned to FileHandle(65)
  FileHandle(65).Drop() called
--- 12. Affine: with block drops on early return ---
  Inside with-block, returning early
  AuthToken(early).Drop() called - consumed
  LeaveEarly returned
=== Drop Semantics Done ===
```
