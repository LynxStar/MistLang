# Body Binder Session Context

Use this to bootstrap a new session. Copy-paste the relevant sections into the prompt.

## What We're Building

A clean-room body binder for the Mist compiler's ProgramModel (Frontend 2). It resolves raw AST method bodies into the model's Statement/Expression hierarchy — fully typed, AST-free (the node is kept as an internal convenience, never on a contract). This is Phase 3 of `ProgramModelBuilder.Build`, running after all type/member signatures are resolved in Phases 1-2.

**Clean-room constraint (non-negotiable):** Do NOT read, reference, port, or derive from `Mist.Semantics` or `Mist.Analysis`. Specify all behavior from the grammar (`Source/Mist/Grammar/Mist.storm`) and the feature specs (`.kiro/specs/`).

## Current State (3 commits on `emitter-v2`)

The binder can resolve:
- Typed local declarations (`int x = 42;`)
- Return statements (with or without value)
- Expression statements (`RunDemo();`, `Console.WriteLine("...");`)
- Integer and string literals
- Local/parameter references
- Sibling method calls (identifier -> method on enclosing type)
- Static type member access (`Console.WriteLine`)
- Method invocations with arguments
- Nested blocks

Everything else degrades gracefully to `UnsupportedStatement` / `UnresolvedExpression` sentinels (retain the AST node, never throw).

**Proven:** The `Simpliest` sample (`Console.WriteLine("General KENOBI!")` + sibling method call) binds with zero sentinels.

## Architecture

All binders are **static classes** with no instance state. Shared deps passed explicitly:

- **`BodyBinder`** (`Construction/BodyBinder.cs`) — entry point. `BindDelegate(method, enclosingType, context)` seeds params into scope, calls `BindBody`. `BindBody(block, scope, returnType, enclosingType, context)` iterates statements.
- **`StatementBinder`** (`Construction/StatementBinder.cs`) — `BindStatement` dispatches by AST node kind. Handles: variable declarations (typed only for now), return, expression statements, nested blocks.
- **`ExpressionBinder`** (`Construction/ExpressionBinder.cs`) — descends the precedence chain (`VoidCoalescing -> LogicalOr -> ... -> Unary -> Postfix -> Primary`). Walks postfix suffixes (invocation, member access, method call). 3-tier identifier resolution: locals, enclosing type methods, type names via scope imports.

**Key types:**
- `BuilderContext` (`Construction/BuilderContext.cs`) — compilation-wide: `GlobalScope`, `ProgramModel`, `Diagnostics`, `AmbientProvider`, `TypeResolver` (created in its ctor).
- `BindingContext` (`Construction/BindingContext.cs`) — mutable per-call state: `Scope`, `ReturnType`, `TargetType`, `EnclosingType`. Has `PushTargetType(type)` returning a struct `IDisposable` for scoped save/restore.
- `WellKnownTypes` (`ProgramModel/WellKnownTypes.cs`) — single source of truth for Standard-namespace type constants (Int32, String, Boolean, etc.). Eliminates magic strings.

**Model types:**
- `Model/Statements/` — `Statement` (abstract), `Block`, `LocalDeclarationStatement`, `ReturnStatement`, `ExpressionStatement`, `UnsupportedStatement`
- `Model/Expressions/` — `Expression` (abstract, carries `IMistType Type` + `SyntaxNode Node`), `LiteralExpression`, `LocalReferenceExpression`, `MethodReferenceExpression`, `TypeReferenceExpression`, `InvocationExpression`, `MemberAccessExpression`, `UnresolvedExpression`
- `Model/LocalSymbol.cs` — name + resolved type for a local variable or parameter alias
- `Model/Scope.cs` — has `RegisterLocal(local, diagnostics)` (reports `MIST_PM_DUPLICATE_LOCAL`) + `LookupLocal(name)` (parent-chain walk)

## Design Decisions Made

1. **No public contracts for bound nodes.** They're internal model types (like `DefaultBlock`/`DefaultEntry`), not `Contracts/Bound/` interfaces. Pattern-match on concrete types.
2. **Mutable `BindingContext`** — not an immutable record. Scope mutates as locals register; target type changes between sub-expressions. `PushTargetType` disposable handles save/restore.
3. **`Scope.RegisterLocal` reports the diagnostic itself** — takes `DiagnosticBag`, emits `MIST_PM_DUPLICATE_LOCAL` on conflict. One registration point, always enforces the invariant.
4. **`BuilderContext` owns `TypeReferenceResolver`** — created in its constructor, no separate passing.
5. **`BindDelegate` vs `BindBody`** — `BindDelegate` seeds parameter aliases into scope (all delegates: methods, constructors, operators, cast operators share this). `BindBody` is the lower-level entry for property/indexer accessors directly.
6. **Phase 3 iterates `sourceTypes` (MemberType instances)** — same list Phase 2a uses. Only methods with a `Body` are bound. Phase 3 is after all signatures resolve, so cross-references work.
7. **Grammar quirk:** `int x;` and `int x = 42;` are separate AST nodes (`UninitializedVariableDeclaration` vs `TypedVariableDeclaration`) due to a PEG ambiguity with cast expressions. The binder currently only handles `TypedVariableDeclaration`; uninitialized declarations fall to `UnsupportedStatement`.

## Task List

See `backlog/program-model-body-tasks.yaml` for the full scope. Key next items:

- **var type inference** — `InferredVariableDeclaration` (separate slice, needs its own design thought)
- **More literals** — bool, float, char (trivial, same pattern as int/string)
- **Binary operators** — additive/multiplicative; needs a `BinaryExpression` node + operator resolution (or placeholder typing for now)
- **If/while/for/foreach statements** — control flow
- **`this` expression** — needs enclosing type in the expression binder
- **Uninitialized variable declarations** (`int x;`)
- **HelloMist sample** — exercises operators, if/else, loops, array creation, foreach, var, method calls with returns

## Files to Read First

1. `Source/Mist/ProgramModel/Construction/ExpressionBinder.cs` — the expression descent + suffix handling + identifier resolution
2. `Source/Mist/ProgramModel/Construction/StatementBinder.cs` — statement dispatch
3. `Source/Mist/ProgramModel/Construction/BodyBinder.cs` — thin entry point
4. `Source/Mist/ProgramModel/Construction/BindingContext.cs` — mutable context + PushTargetType
5. `Source/Mist/ProgramModel/WellKnownTypes.cs` — type constants
6. `Test/Mist.Tests/Integration/BodyBindingIntegrationTests.cs` — the test harness + assertions
7. `backlog/program-model-body-tasks.yaml` — full task list

## Build & Test

```bash
# Build (must be 0 warnings, 0 errors)
dotnet build Source/Mist/Mist.csproj -v q

# Run body-binding tests (4 tests, ~60ms, no process spawning)
dotnet test Test/Mist.Tests/Mist.Tests.csproj --filter "FullyQualifiedName~BodyBindingIntegrationTests"

# Format verify (scoped to ProgramModel)
dotnet format Mist.slnx --verify-no-changes --include Source/Mist/ProgramModel/

# Broader regression (ProgramModel + experimental pipeline tests)
dotnet test Test/Mist.Tests/Mist.Tests.csproj --filter "FullyQualifiedName~Integration|FullyQualifiedName~ExperimentalRepresentation|FullyQualifiedName~AmbientTypeProvider"
```

Note: `AllFeatures_IncludesFullAccessibilityModelOutput` fails pre-existing on this worktree (environment issue, unrelated to body binder).
