# MistLang

Mist is a programming language that transpiles to C#. This repo holds the compiler, the
standard library, the sample suite and the language book.

## The two pipelines

**v1 / "classic" is a working compiler.** It compiles and runs every sample correctly.
It is also the code we are escaping: three files carry most of it —
`Semantics/Model/SemanticModelBuilder.cs` (~30k lines),
`Analysis/Builtin/TypeCheckerAnalyzer.cs` (~24k), and
`Backends/CSharpTranspiler/CSharpEmitter.cs` (~18k).

**v2 is a rewrite in progress**, aiming at the opposite: separated responsibilities and
classes a human can read. It lives in `Source/Mist/ProgramModel/` (frontend) and
`Source/Mist/Backends/CSharpTranspiler/Emitter/` (backend). Keep files small. If a v2 file
is heading past ~700 lines, that is the signal to split it, not to keep appending.

Source-to-AST is **shared**: `Source/Mist/Grammar/*.storm` drives the Storm source
generator, which emits AST node classes into `Source/Mist/Generated/`. Both pipelines
parse the same tree.

Select a pipeline with `--pipeline experimental` (v2); omitting the flag runs classic.

## The clean-room rule (non-negotiable)

**Classic's runtime behavior is the contract. Classic's code is not a reference.**

- Do NOT read, port, or derive v2 code from `Mist.Semantics` or `Mist.Analysis`. The point
  of the rewrite is to not re-import that architecture.
- Code under `Source/Mist/ProgramModel/` must not reference the legacy system at all — see
  the isolation rule in `Source/Mist/ProgramModel/CLAUDE.md`.
- Specify behavior from the grammar and from `.kiro/specs/` — 119 delivered feature specs,
  each with `requirements.md` (formal acceptance criteria), `design.md` and `tasks.md`.
  These are the sanctioned input for a from-scratch implementation.
- What v2 must match is the **observable output of the compiled program**. The emitted C#
  is free to differ, and several times has been deliberately better than classic's.

## Where v2 progress is recorded

`samples/AllFeaturesV2` **is** the record. A sample runs on v2 if and only if it is
referenced from `samples/AllFeaturesV2/mist.toml`, its `RunDemo()` is called from
`samples/AllFeaturesV2/Program.mist`, and its output is in
`samples/__baselines__/AllFeaturesV2.out`. There is deliberately no second place asserting
greenness. The one sample it cannot hold is `samples/Testing`, a library with no entry point,
which `Testing_RunsUnderTheTestCommandOnTheExperimentalPipeline` holds under `mist test` instead.

The baseline was captured from classic and is pipeline-independent: both pipelines are held
to the same stdout. There are two exceptions: `samples/LocalFunctions`, which classic cannot
parse, and `samples/OperatorOverloads`, which classic parses but cannot compile (it never emits
an operator declaration). Each is referenced from `AllFeaturesV2` alone, and its lines were
captured from v2 and read against the program by hand. Note the blind spot — a stdout diff cannot
see things like a dropped `sealed` modifier.

## Backlog registers

| File | Role |
|---|---|
| `backlog/emitter-v2-capability-progression.yaml` | **Source of truth** for what v2 still needs and in what order. 186 ordered capabilities, 75 samples, stated invariants. Valid YAML. Large — grep it, don't read it whole. |
| `backlog/analysis-v2-capability-progression.yaml` | **Source of truth for Stage 5**, the analysis phase. The 91 `analysis_phase_tasks` from the emitter register, reordered into the order a checker can be built in, with the invariants that gate them. Each rule cites the emitter order it came from. |
| `backlog/program-model-body-tasks.yaml` | The other axis: what the binder does not yet understand, by language construct. **Not valid YAML** despite the extension. |
| `backlog/flaky-tests.yaml` | Flake register. Recording a flake is mandatory (see `Test/CLAUDE.md`). |
| `backlog/general.md` | Issues outside any active spec. |
| `backlog/inventory-<date>.md` | Dated snapshots of what v2 still needs, counted from the registers above. |

A capability in the emitter register is measured by what it makes **representable and
emittable** — what new part of a program can be held in memory, or emitted and run. Rules
that walk a finished model and judge it belong to Stage 5 and are recorded as
`analysis_phase_tasks`, however early classic reports the equivalent. Getting this wrong
once cost an implementation that was written and reverted.

**Stage 5 is now open** and has its own register. The two arcs measure opposite things: the
emitter arc's ledger is `samples/AllFeaturesV2`, a stdout baseline over 75 valid programs,
which is structurally blind to a refusal. An analyzer that reports nothing passes every test
in the repo, so Stage 5's first order is a ledger of its own.

## Commands

```bash
dotnet build Mist.slnx
```

```bash
dotnet test Mist.slnx
```

```bash
dotnet format Mist.slnx --verify-no-changes
```

Run one sample through v2:

```bash
dotnet run --project Source/Mist.CLI -- run samples/HelloMist --quiet --pipeline experimental
```

Emitted C# lands in `samples/<Name>/out/` and is gitignored — it is scratch, not an artifact.

## Zero warnings

The codebase MUST build with zero warnings — CS*, STORM*, MSBuild, and every `dotnet format`
diagnostic enforced by `.editorconfig`. Trim warnings from third-party packages are excluded.

If a change introduces a warning, stop and explain it before proceeding: what it means, why
the change triggers it, the options, and the cost of each. Wait for a decision. If
suppression is approved, add a comment saying why. Never suppress silently.

Run `dotnet format --verify-no-changes` after touching `Source/` or `Test/`. If it crashes on
a namespace mismatch (IDE0130), fix that by hand first.

## Code comments

**A change adds at most one comment per file it touches. Zero is the normal number.** To add
a second to the same file, name the first one and say why both are needed. There is no
exemption for public members, subtle code, or long methods. Only comments you add count.

1. **No restating.** If the comment is the identifier with spaces in it, it does not go in. A
   bare member is finished, not unfinished.
2. **No history.** Present tense only — not what used to happen or what a fix did. That is
   the commit message's job.
3. **One home.** Each piece of reasoning is written once; point at it with `<see cref="..."/>`.
4. **No XML escapes.** No `&lt;` or `&gt;`, and no code sample split across lines.

The one comment is earned only by reasoning the code cannot carry: why this approach over the
obvious alternative, a constraint imposed from outside the file, an invariant a reader could
break without the compiler noticing, or why something is deliberately absent.

Applies to `Source/`, `Test/`, and `.mist` files. Exempt: commit messages, and the
`evidence:` fields in `backlog/*.yaml`, which exist to record what was measured.

Run `/code-comment-rationale` for worked examples — read it when a rule seems wrong, not
before writing.

## Mist language facts

Easy to get wrong because the C# instinct is different.

**No `typeof`.** Write the type name in expression position; the emitter generates
`typeof(T)`. `var t = int;` emits `var t = typeof(int);` and `string.Name` emits
`typeof(string).Name`.

**No `new T[] { ... }`.** The grammar's array creation requires an explicit size, so
`new T[]` with a braced initializer is a parse error — and it surfaces as
`MIST_PARSE_INCOMPLETE` at the enclosing method or class, which makes it look like the
signature is at fault. Use an array literal instead:

| C# | Mist |
|---|---|
| `new T[] { a, b, c }` | `[a, b, c]` |
| `int[] x = { 1, 2, 3 };` | `int[] x = [1, 2, 3];` |
| `new T[3]` | `new T[3]` (explicit size is fine) |
| `new List<T> { a, b }` | not supported — construct, then `Add` |

## Directory-scoped rules

These load when you work in the directory:

- `Source/CLAUDE.md` — C# coding style
- `Source/Mist/ProgramModel/CLAUDE.md` — v2 isolation rule and verification policy
- `Source/Mist/ProgramAnalysis/CLAUDE.md` — Stage 5: the analyzer contract, and how a rule proves itself
- `samples/CLAUDE.md` — sample integrity, and how to bank a sample on v2
- `Test/CLAUDE.md` — test tiers, process spawning, encoding, failure triage
- `book/CLAUDE.md` — book snippet policy

Skills: `/testing-philosophy` for the 6-tier test pyramid, `/code-comment-rationale` for the
reasoning behind the comment budget.
