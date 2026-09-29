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
- Specify behavior from the grammar and from `.kiro/specs/` — the delivered feature specs,
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
to the same stdout. The exceptions are the samples classic cannot run -- `samples/LocalFunctions`,
which it cannot parse, and `samples/NewtypeClones`, written for a revision of the newtypes spec it
does not implement, among them. Each is marked V2-ONLY, with the reason, in the register's
`untested` list, is referenced from `AllFeaturesV2` alone, and has its lines captured from v2 and
read against the program by hand. Note the blind spot — a stdout diff cannot
see things like a dropped `sealed` modifier.

## Backlog registers

| File | Role |
|---|---|
| `backlog/emitter-v2-capability-progression.yaml` | **Source of truth** for what v2 still needs and in what order. Ordered capabilities, the samples each unlocks, and stated invariants. Valid YAML. Large — grep it, don't read it whole. |
| `backlog/analysis-v2-capability-progression.yaml` | **Source of truth for Stage 5**, the analysis phase. The `analysis_phase_tasks` from the emitter register, reordered into the order a checker can be built in, with the invariants that gate them. Each rule cites the emitter order it came from. |
| `backlog/program-model-body-tasks.yaml` | The other axis: what the binder does not yet understand, by language construct. **Not valid YAML** despite the extension. |
| `backlog/flaky-tests.yaml` | Flake register. Recording a flake is mandatory (see `Test/CLAUDE.md`). |
| `backlog/refusal-corpus-migration.md` | Refusal test migration: measurements, tier choices and verification procedure. |
| `backlog/classic-retirement.md` | What still depends on classic, the checklist for deleting it, and how v2 accounts for Stages 4-6 progress. |
| `backlog/general.md` | Issues outside any active spec. |
| `backlog/inventory-<date>.md` | Dated snapshots of what v2 still needs, counted from the registers above. |

A capability in the emitter register is measured by what it makes **representable and
emittable** — what new part of a program can be held in memory, or emitted and run. Rules
that walk a finished model and judge it belong to Stage 5 and are recorded as
`analysis_phase_tasks`, however early classic reports the equivalent. Getting this wrong
once cost an implementation that was written and reverted.

**Stage 5 is now open** and has its own register. `samples/AllFeaturesV2` is a stdout baseline
over valid programs, so it is structurally blind to a refusal: an analyzer that reports nothing
passes it. That gap is closed beside each rule, at the tiers the testing philosophy
(`.claude/skills/testing-philosophy/SKILL.md`, `/testing-philosophy`) assigns: Tier 1 against the
rule's decision, Tier 2 when the rule walks a body (flow, ownership), Tier 3 rarely. See
`Source/Mist/ProgramAnalysis/CLAUDE.md`.

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

## Tests

These are the binding rules of the testing philosophy,
`.claude/skills/testing-philosophy/SKILL.md` (`/testing-philosophy`) — strict on v2, a guideline
for classic. The skill holds the full per-tier detail and the reasoning.

A test exists only to encode an invariant the language cannot guarantee. Decide its tier from
the invariant — which stage owns it — then use only what that tier allows. Never let the
infrastructure at hand pick the tier.

| Tier | Scope | How many | Forbidden |
|---|---|---|---|
| 1 Unit | One function, input built directly | Most: hundreds to thousands | Lexer, parser, any upstream stage; file I/O; processes; Roslyn; the real ambient provider or scope resolver |
| 2 Functional | One module's internal chain | Many: tens to low hundreds | Running upstream stages to make input; processes; Roslyn; another module's behavior |
| 3 Component | One whole phase, real collaborators | Moderate: tens | Stages outside the domain; processes; Roslyn; asserting on stdout; property tests over 50 iterations |
| 4 Integration | The seam between named stages | A handful per boundary, ~10–20 at most | Boundaries not under test; high-iteration property tests |
| 5 System | One sample, whole pipeline | Sparse, justified per test | More than one test per sample; generated programs; coverage a lower tier already has |
| 6 Full system | `AllFeatures`, `AllFeaturesV2` | Exactly 2, ever | A third orchestrator |

Every new or migrated test must declare its tier in runnable test metadata. With xUnit, use
`[Trait("Tier", "1")]` through `[Trait("Tier", "6")]`: exactly one effective tier per test.
Tag a class only when every test in it shares that tier; use method traits for mixed classes.
Existing unclassified tests may remain unmarked until audited or migrated. An absent trait means
unclassified; a folder, test name or `// Tier N` comment is not a classifier. The convention and
audit procedure live in `Test/CLAUDE.md` under "Test tier classification".

Review the implementation, including helpers and fixtures, against the claimed tier. A mismatch
is a defect to resolve, and a tag never permits infrastructure forbidden at that tier. Before
adding higher-tier coverage beyond the pyramid's limits, explain the invariant, case count,
expected cost and why lower tiers cannot prove it, and get explicit human approval for the
exception. Count theory cases and repeated pipeline executions, not just classes or methods.

- **The most common violation** is running the whole pipeline, or most of it, to check what one
  stage does: a test that parses source and asserts on one property of the result.
  `CompilerDriver.Compile` runs every stage, so a test that calls it is Tier 4 or above however
  small its program is.
- **A diagnostic is owned by the rule that reports it**, and is proven at that rule's tier: Tier 1,
  or Tier 2 when the rule walks a body. A refusal is worth testing because `AllFeatures` cannot see
  one; that says nothing about compiling a program to watch it fail.
- **Push down, never up.** When a system test fails, prove the fix at a lower tier; do not add
  another system test. High-iteration property tests belong at Tier 1–2 only.
- **Don't test** what the language guarantees, what xUnit, Roslyn or the BCL do, or upstream stages:
  a test of stage N does not re-verify stages 1 to N-1.

**These rules win.** A directory's CLAUDE.md, a register entry or a spec's `tasks.md` that asks for
a test at a tier these rules forbid is wrong. Do not follow it: flag the conflict to the user.

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
- `Test/CLAUDE.md` — compile cost, process spawning, encoding, failure triage (tiers: `## Tests` above)
- `book/CLAUDE.md` — book snippet policy

Skills: `/testing-philosophy` for the full per-tier detail and the reasoning behind `## Tests`,
`/code-comment-rationale` for the reasoning behind the comment budget.
