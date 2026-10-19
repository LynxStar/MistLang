# MistLang

Mist is a programming language that transpiles to C#. This repo holds the compiler, the
standard library, the sample suite and the language book.

## The pipeline

The compiler has one pipeline, named **standard**, built for separated responsibilities and
classes a human can read. It lives in `Source/Mist/ProgramModel/` (frontend),
`Source/Mist/ProgramAnalysis/` (Stage 5) and `Source/Mist/Backends/CSharpTranspiler/Emitter/`
(backend), and `Source/Mist/Driver/StandardPipeline.cs` runs it. Keep files small. If a file
is heading past ~700 lines, that is the signal to split it, not to keep appending.

Source-to-AST: `Source/Mist/Grammar/*.storm` drives the Storm source generator, which emits
AST node classes into `Source/Mist/Generated/`.

`--pipeline` is the selector. Its one value, `standard`, is the default, so omit the flag; any
other value is the parser's unknown-pipeline error.

"v2" names this pipeline wherever it appears: in type names (`CSharpEmitterV2`), in
`samples/AllFeaturesV2`, and in historical documentation and the specs. "Classic" and "v1" there
name the compiler it replaced, which is no longer in the tree.

## The clean-room rule (non-negotiable)

**The observable output of the compiled program is the contract. Classic's code is not a reference.**

- Specify behavior from the grammar and from `.kiro/specs/` — the delivered feature specs,
  each with `requirements.md` (formal acceptance criteria), `design.md` and `tasks.md`.
- The contract is the **observable output of the compiled program**, held by the baseline
  below. The emitted C# is free to change.
- Classic's source survives only in git history. Do NOT read, port, or derive code from it:
  it is not a design reference.

## Where progress is recorded

`samples/AllFeaturesV2` **is** the record. A sample runs if and only if it is
referenced from `samples/AllFeaturesV2/mist.toml`, its `RunDemo()` is called from
`samples/AllFeaturesV2/Program.mist`, and its output is in
`samples/__baselines__/AllFeaturesV2.out`. There is deliberately no second place asserting
greenness. The one sample it cannot hold is `samples/Testing`, a library with no entry point,
which `Testing_RunsUnderTheTestCommand` holds under `mist test` instead.

The baseline's provenance: most samples' lines were captured from classic while it existed. The
exceptions are the samples classic could not run -- `samples/LocalFunctions`, which it could not
parse, and `samples/NewtypeClones`, written for a revision of the newtypes spec it did not
implement, among them. Their V2-only provenance is recorded in `samples/AGENTS.md`; their lines
were captured from v2 and checked against the programs by hand. Note the blind spot — a stdout diff cannot see things like a dropped `sealed` modifier.

## Backlog

Carbonek project MIST is the **only record of outstanding work**, its findings and every flaky-test
observation. The Carbonek contract below states how to find work, record a flake and close a task,
and which task kinds are the owner's.

Representation and emission belong to the frontend/backend. Rules that judge a finished model
belong to Stage 5, whichever stage a diagnostic is declared at. `samples/AllFeaturesV2` is a stdout
baseline over valid programs, so it cannot prove that an analyzer refuses invalid ones. Each rule's
findings and deliberate admissions need tests at the tiers the testing philosophy assigns: Tier 1
against the decision, Tier 2 for rules that walk bodies, and Tier 3 rarely. See
`Source/Mist/ProgramAnalysis/AGENTS.md`.

## Carbonek contract

### The record

Carbonek project `MIST` is the only record of obligations, findings, flake observations and
execution. Nothing in this repository is a work queue: not `backlog/`, not the spec `tasks.md`
documents, not the test-run ledgers under `TestResults/`.

Task keys `MIST-NNN` and `ARCH-NNN` are the original ledger IDs, so a spec or code comment that
cites one names that task. Every imported task's `sourceReference` names its entry in the frozen
ledger at the import revision. `backlog/README.md` says how to read it. It is history, not edited.

### Two words

**Evidence** is what a Session reports when it ends: the completion report's `summary`,
`evidence` and `artifacts`, and for a workflow step its `stepEvidence`. It is proof that work
occurred. Every agent returns it, it needs no permission, and Done comes from it.

**A finding** is a fact recorded on a task about the task's own claim: a reproduction, a probe that
found nothing, a flake sighting, a decision a spec has settled, the history imported from the
ledger. It is appended with `add_finding`, which needs the `tasks:findings` scope, and it is never a
report of work.

### Kinds

Every Backlog task has a kind:

- **Confirmed**: current source or an executed probe shows the contract absent or violated.
- **Unverified**: a lead that needs confirmation or attribution. It becomes Confirmed only by
  `update_task` with `kind: Confirmed` and a finding that carries the proof.
- **Decision** and **Optional**: the owner's. They are never dispatched. No agent changes their
  kind or implements an invented policy to close one.

Promotion to Ready (`update_task` with `promote: true`) needs a Confirmed task with acceptance
criteria and delivery instructions.

### Finding work

Call `list_tasks` with `project: MIST` and the `status` and `kind` filters, for example
`status: [Backlog]` with `kind: [Confirmed]`. Its `text` filter searches titles, descriptions and
source references. `get_task` returns the body, revision and findings. Read every finding
before acting: each says what was measured, at which revision, and its limits.

### Recording a flaky test

`Test/AGENTS.md` rule 2 says when a sighting is owed and which fields it carries. Append it to the
matching task with `add_finding`, category `Observation`. A sighting with no matching task goes in
your report, with the same fields, for the operator to file as Unverified.

### Closing work

Closure is the completion report. Its `summary` says what changed; its `evidence` lists the
acceptance probes with their output before and after, the tests and their tiers, the ledger run
path, and the build and format results, with the limits; its `artifacts` name the commit that holds
the change. Nothing is appended to the task for closure: a finding is not a report of work. Done
comes from the Session outcome, never from an agent: Completed finishes the task, and Blocked or
Failed leaves it Blocked.

### New work

`create_tasks` makes Ready work with acceptance criteria and delivery instructions. It never makes
a Backlog record. Before calling it, check for a duplicate: `list_tasks` with `sourceReference` for
an exact source and `text` for the ID, symptom or path. Extend a match with a finding instead.
Give every new task a `sourceReference` that names its origin, so the next check finds it. A lead
that is not yet Confirmed goes in your report for the operator to file.

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

Run one sample:

```bash
dotnet run --project Source/Mist.CLI -- run samples/HelloMist --quiet
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
`.claude/skills/testing-philosophy/SKILL.md`. They bind all test code.
The skill holds the full per-tier detail and the reasoning.

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
| 6 Full system | `AllFeaturesV2` | Exactly 1, ever | A second orchestrator |

Cost rises with the tier and only Tiers 5 and 6 may be expensive: a collaborator that loads a
large external resource, runs a compiler or starts a process is stubbed below Tier 5 unless the
test lives in the area that owns it. The skill's "What a Tier May Cost" table is the budget.

Every new or migrated test must declare its tier in runnable test metadata. With xUnit, use
`[Trait("Tier", "1")]` through `[Trait("Tier", "6")]`: exactly one effective tier per test.
Tag a class only when every test in it shares that tier; use method traits for mixed classes.
Existing unclassified tests may remain unmarked until audited or migrated. An absent trait means
unclassified; a folder, test name or `// Tier N` comment is not a classifier. The convention and
audit procedure live in `Test/AGENTS.md` under "Test tier classification".

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
  or Tier 2 when the rule walks a body. A refusal is worth testing because `AllFeaturesV2` cannot see
  one; that says nothing about compiling a program to watch it fail.
- **Push down, never up.** When a system test fails, prove the fix at a lower tier; do not add
  another system test. High-iteration property tests belong at Tier 1–2 only.
- **Don't test** what the language guarantees, what xUnit, Roslyn or the BCL do, or upstream stages:
  a test of stage N does not re-verify stages 1 to N-1.

**These rules win.** A directory's AGENTS.md, a register entry or a spec's `tasks.md` that asks for
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

Applies to `Source/`, `Test/`, and `.mist` files. Exempt: commit messages, and Carbonek
findings, which exist to record what was measured.

See `.claude/skills/code-comment-rationale/SKILL.md` for worked examples — read it when a rule
seems wrong, not before writing.

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

These load when you work in the directory. They bind every agent, so read the scoped `AGENTS.md`
of every directory you touch if your tool has not already loaded it:

- `Source/AGENTS.md` — C# coding style
- `Source/Mist/ProgramModel/AGENTS.md` — the program model's design rules and verification policy
- `Source/Mist/ProgramAnalysis/AGENTS.md` — Stage 5: the analyzer contract, and how a rule proves itself
- `samples/AGENTS.md` — sample integrity, and how to bank a sample
- `Test/AGENTS.md` — compile cost, process spawning, encoding, failure triage (tiers: `## Tests` above)
- `book/AGENTS.md` — book snippet policy

Skills: `.claude/skills/testing-philosophy/SKILL.md` for the full per-tier detail and the
reasoning behind `## Tests`, `.claude/skills/code-comment-rationale/SKILL.md` for the reasoning
behind the comment budget.
