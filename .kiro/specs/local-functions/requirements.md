# Requirements Document

## Introduction

Local functions let a Mist program declare a named function as a statement: at file top beside
top-level statements, or inside any block. The declaration reads like a method declaration -- a
return type, a name, a parameter list and a block body -- and the function is callable from the
statement list it is declared in, before or after its declaration. A local function captures the
locals of the enclosing body exactly as a lambda does, and `static` refuses capture exactly as it
does on a lambda.

The feature exists because the language's owner wants the following shape, which ten book snippets
were originally written in, to be valid Mist:

```mist
using Standard;

public static void PrintMessage(string msg)
{
    Console.WriteLine(msg);
}

PrintMessage("fire-and-forget call");
```

Mist previously had no function declaration that could appear in a statement list anywhere. The
grammar's `Statement` rule had no such alternative, and the top-level-statements spec (Req 1.2) lists
the allowed top-level constructs without functions. This spec adds one construct that serves both
positions, since a top-level statement file is itself a statement list.

## Glossary

- **Local_Function**: A function declared as a statement, whose name is visible in the statement
  list it is declared in and in every block nested within it.
- **Declaring_List**: The statement list -- a block body, a `with` declaration's tail, or the
  top-level statements of a file -- whose scope holds the Local_Function's name.
- **Capture**: A reference from inside a Local_Function's body to a local, parameter, `this`, or
  instance member of the enclosing body, following the lambda's rules (first-class-functions
  spec, Req 12 and Req 16).
- **Hoisting**: Declaring every Local_Function of a Declaring_List in that list's scope before any
  statement of the list is bound, so that a call may precede the declaration textually.
- **Sync_Context / Async_Context**: As the structured-concurrency spec defines them: a body is a
  Sync_Context unless declared `async`.
- **Compiler**: The Mist compiler on the experimental (v2) pipeline. Classic shares the parser and
  never binds a Local_Function.
- **Refusal**: A compile error with a stated diagnostic id, recorded under `refusals/`.

## Requirements

### Requirement 1: Declaration Syntax

**User Story:** As a Mist developer, I want to declare a function inside a block or at file top with
the syntax I already use for methods, so that a helper does not need a class around it.

#### Acceptance Criteria

1. THE Compiler SHALL parse `Modifier* TypeReference IDENTIFIER TypeParameterList? ( ParameterList? )
   WhereClause* Block` as a Local_Function statement wherever a statement is accepted.
2. WHEN a Local_Function appears among a file's top-level items, THE Compiler SHALL treat it as a
   top-level statement of that file, so a file holding only local functions is a top-level
   statement file (top-level-statements spec, Req 1.2, amended by this spec).
3. THE Compiler SHALL parse the parameter list with the shared `Parameter` rule, so `ref`,
   `readonly`, `params`, ownership specifiers and attributes on a parameter parse as they do on a
   method.
4. THE Compiler SHALL NOT accept a return ownership specifier, a `with` clause, or an attribute
   list on a Local_Function. These are not part of the rule and fail to parse.
5. THE Compiler SHALL reject an existing valid statement being read as a Local_Function: every
   statement the grammar accepted before this spec parses as the same kind after it.

### Requirement 2: Modifiers

**User Story:** As a Mist developer, I want `public static` on a top-level function to be accepted
as written in the book, and `static` to mean what it means on a lambda.

#### Acceptance Criteria

1. THE Compiler SHALL accept an accessibility modifier (`public`, `private`, `protected`,
   `internal`) on a Local_Function and give it no meaning: a Local_Function is visible exactly in
   its Declaring_List and the blocks nested in it, and the modifier is not emitted.
2. THE Compiler SHALL accept `static` and SHALL refuse a `static` Local_Function whose body
   references an enclosing local, parameter, `this`, an instance field or an instance property,
   with diagnostic `MIST_PA_STATIC_LOCAL_FUNCTION_CAPTURE` at the reference.
3. THE Compiler SHALL accept `async`, with the meaning Requirement 6 gives it.
4. THE Compiler SHALL refuse `sealed`, `intrinsic`, `readonly`, `owned`, `ref`, `affine`, `linear`
   and `relevant` on a Local_Function with diagnostic `MIST_PM_LOCAL_FUNCTION_MODIFIER`, naming the
   modifier, at the declaration.
5. WHEN two accessibility modifiers are written, THE Compiler SHALL report
   `MIST_PARSE_MULTIPLE_ACCESS` as it does on a member.

### Requirement 3: Name, Scope and Hoisting

**User Story:** As a Mist developer, I want to call a local function from anywhere in the block that
declares it, including before its declaration and from itself, so that helpers can be ordered for
reading rather than for the compiler.

#### Acceptance Criteria

1. THE Compiler SHALL declare every Local_Function of a Declaring_List in that list's scope before
   binding any statement of the list (Hoisting), so a call textually before the declaration
   resolves.
2. THE Compiler SHALL resolve a call to a Local_Function through the enclosing scope chain as a
   method group of exactly one candidate, so arguments are bound and judged as a method call's are:
   overload resolution, `ref` arguments, `params`, `owned` parameters and parameter `where`
   clauses all apply.
3. THE Compiler SHALL admit a Local_Function calling itself, and two Local_Functions of one
   Declaring_List calling each other.
4. WHEN a Declaring_List declares two Local_Functions of one name, THE Compiler SHALL report
   `MIST_PM_DUPLICATE_LOCAL_FUNCTION` at the second.
5. WHEN a Declaring_List declares a local variable with a Local_Function's name, THE Compiler
   SHALL report `MIST_PM_DUPLICATE_LOCAL` at the variable, since the function's name is already
   declared there (Hoisting).
6. THE Compiler SHALL admit a Local_Function in a nested block whose name shadows a local or
   Local_Function of an enclosing block, as nested blocks may shadow locals today.
7. THE Compiler SHALL admit converting a Local_Function's name to a function type
   (`func<int, int> f = Twice;`) under the method-group conversion rules of the
   delegate-func-conversion spec.
8. THE Compiler SHALL refuse a Local_Function that declares type parameters with
   `MIST_PM_LOCAL_FUNCTION_GENERIC` at the declaration. Generic local functions are out of scope.

### Requirement 4: Body and Captures

**User Story:** As a Mist developer, I want a local function to read and write the locals around
it the way a lambda can, so that it is a closure with a name.

#### Acceptance Criteria

1. THE Compiler SHALL bind a Local_Function's body in a scope of its own beneath the Declaring_List's
   scope, holding its parameters as locals, so the body sees the enclosing body's locals declared
   before the Local_Function and its own parameters and locals.
2. THE Compiler SHALL treat a reference from the body to an enclosing local or parameter as a
   Capture by reference: a write inside the body is visible outside and a write outside is visible
   inside, as the first-class-functions spec has it for lambdas (Req 16.1).
3. THE Compiler SHALL refuse a Capture of a ref struct, an `owned`, `borrowed` or `frozen` binding,
   or a `readonly ref` binding with `MIST_PA_CAPTURED_BINDING`, as it refuses the same capture by a
   lambda, with the message naming a local function rather than a lambda.
4. THE Compiler SHALL admit `this` and instance members inside a non-static Local_Function of an
   instance member, and SHALL refuse them inside a Local_Function of a static member exactly as it
   refuses them in the member itself.
5. THE Compiler SHALL admit a `return` inside the body against the Local_Function's declared return
   type, and SHALL judge the returned value's assignability and refinements against it.
6. THE Compiler SHALL admit a Local_Function inside a lambda body, a lambda inside a Local_Function
   body, and a Local_Function inside a Local_Function body.

### Requirement 5: Definite Assignment

**User Story:** As a Mist developer, I want the compiler to stop a local function from reading a
local that nothing has assigned yet, without refusing the ordinary case where the assignment comes
before the call.

#### Acceptance Criteria

1. THE Compiler SHALL judge a Local_Function's body for reads of its own unassigned locals as it
   judges any body (`MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT`).
2. THE Compiler SHALL compute, for each Local_Function, the set of enclosing locals its body reads
   without declaring, together with the sets of every Local_Function it calls or names, and SHALL
   refuse a call to the Local_Function, or a conversion of its name to a function type, at which any
   local of that set is not definitely assigned, with `MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED`
   naming the function and the local.
3. THE Compiler SHALL NOT report a read of an enclosing local inside the body itself, since
   criterion 2 judges it at every entry into the body.
4. THE Compiler SHALL NOT count an assignment inside a Local_Function's body as assigning the
   enclosing local at the call, as it does not count one inside a lambda.
5. THE Compiler SHALL admit a call after the captured local is assigned, whether the declaration
   precedes or follows the assignment.

### Requirement 6: Async Local Functions

**User Story:** As a Mist developer, I want `async int Fetch()` as a local function, so that the
structured-concurrency chapter's examples work at file top.

#### Acceptance Criteria

1. WHEN a Local_Function is declared `async`, THE Compiler SHALL give it the return type
   `Task<T>` for a declared `T`, and `Task<void>` for a declared `void`, as it does a method
   (structured-concurrency spec, Req 2).
2. THE Compiler SHALL bind an `async` Local_Function's body as an Async_Context and a non-async
   one's as a Sync_Context, whatever the context around the declaration.
3. WHEN a Sync_Context calls a task-returning Local_Function, THE Compiler SHALL await the call
   implicitly and yield the result; WHEN an Async_Context calls it, the call yields the task
   (structured-concurrency spec, Req 5 and Req 6).
4. THE Compiler SHALL apply the `async` functor and the `await` functor to a call of a
   Local_Function as to any call.
5. WHEN a Sync_Context Local_Function contains an implicit or explicit await, or holds a task it
   must complete at scope exit, THE Compiler SHALL emit it as a C# `async` function returning a
   task and await every call to it, exactly as it promotes a sync method (the backend's async
   promotion), and the enclosing body is promoted in turn.

### Requirement 7: C# Emission

**User Story:** As a Mist developer, I want the emitted C# to be a C# local function, so the output
reads as the source did.

#### Acceptance Criteria

1. THE Compiler SHALL emit a Local_Function as a C# local function at the position of its
   declaration, inside the enclosing body -- or among the top-level statements for a top-level
   file.
2. THE Compiler SHALL emit `static` when written and SHALL NOT emit an accessibility modifier.
3. THE Compiler SHALL emit `async` and a task return type for an `async` or promoted Local_Function,
   spelled as the method emitter spells them.
4. THE Compiler SHALL spell the parameters, the return type, `ref` returns and `params` as the
   method emitter does, and SHALL drop an `owned` droppable parameter at body exit as a method
   body does.
5. WHEN a Local_Function's parameter or local, or its name, shadows a name of an enclosing block or
   the enclosing parameter list, THE Compiler SHALL rename it apart in the emitted C# as it renames
   a shadowing local today (C# refuses the shadowing, CS0136), at every site that spells it.
6. THE Compiler SHALL emit a call to a Local_Function as a bare call by name, and a name converted
   to a function type as the bare name.

### Requirement 8: Classic Pipeline

**User Story:** As a maintainer, I want classic to keep refusing programs with local functions
without crashing, since the parser is shared.

#### Acceptance Criteria

1. WHEN a program containing a Local_Function is compiled on the classic pipeline, THE Compiler
   SHALL report a diagnostic and exit without an unhandled exception.

### Requirement 9: Proof and Ledger

**User Story:** As a maintainer, I want the feature recorded where v2 progress is recorded.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/LocalFunctions/Program.mist` with a `RunDemo()` that
   exercises: a call before the declaration, recursion, mutual recursion, a capture written by the
   function and read after, `static`, `public static`, an `async` local function called from a sync
   and an async context, a promoted sync local function, a nested local function, a local function
   in a lambda, a local function converted to a function type, and a parameter `where`. Each
   prints a labeled line.
2. The sample SHALL be referenced from `samples/AllFeaturesV2/mist.toml`, its `RunDemo()` called
   from `samples/AllFeaturesV2/Program.mist`, and its output appended to
   `samples/__baselines__/AllFeaturesV2.out`. It SHALL NOT be referenced from `samples/AllFeatures`,
   which classic compiles and which cannot parse the feature; its baseline lines are captured from
   v2 and checked by hand against the program, and the orchestrator's documentation says so.
3. The ten book snippets rewritten by commit `d98e3e56` SHALL be restored to their `3f24c741` shape
   and their `.cs` twins regenerated by `tools/SnippetEmitter` with zero failures.
4. Every diagnostic this spec introduces SHALL have a case under `refusals/<rule-id>/`, and every
   shape this spec deliberately admits SHALL be pinned by a Tier 3 test on `ProgramModelProbe`.
5. `samples/AllFeaturesV2` and `samples/AllFeatures` SHALL remain identical to their baselines.
6. The capability SHALL be recorded in `backlog/emitter-v2-capability-progression.yaml`, and the
   rules in `backlog/analysis-v2-capability-progression.yaml`.

### Requirement 10: Book

**User Story:** As a Mist developer learning the language, I want the book to show local functions.

#### Acceptance Criteria

1. THE Book's Methods chapter SHALL gain a section on local functions with a snippet and its
   emitted C#, stating the scope rule, hoisting, captures and `static`.
