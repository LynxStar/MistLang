---
name: testing-philosophy
description: The 6-tier test pyramid for MistLang in full - what tier a test belongs at, what infrastructure each tier may use, what is forbidden at each, and why. The binding rules are summarized in the root CLAUDE.md under "Tests". Use when writing, moving, or reviewing any test, or when deciding whether a test should exist at all. Strict adherence is required for all test code.
---


# Testing Philosophy

The binding rules are summarized in the root `CLAUDE.md` under `## Tests`, which is always loaded. This is the full text: every tier's allowed and forbidden infrastructure, and the reasoning behind each rule.

## Core Principle

A test exists for exactly one reason: to encode an invariant the language itself cannot guarantee.

That invariant is almost always *intent*. The compiler proves code is well-formed and runs; it cannot prove the code does what we *meant*. The gap a test fills is the distance between "compiles and runs" and "does what we intended."

Before writing any test, answer: **what intended behavior does this pin down, and why can the language not express it?** If there is no clear answer, the test should not exist.

## Enforcement Scope

Strict adherence is required for all test code. The compiler has one pipeline (ProgramModel, ProgramAnalysis, EmitterV2, AmbientProviderV2, StandardPipeline), and every test must land at the correct tier with the correct constraints. No exceptions.

## The 6-Tier Pyramid

Tests form a pyramid. The higher the tier, the more of the system it exercises, the fewer tests should exist at that level, and the more expensive each test is.

```
         /\          Tier 6: Full System (1 test)
        /  \         Tier 5: System (~1 per sample)
       /    \        Tier 4: Integration (handful per boundary)
      /      \       Tier 3: Component (moderate count)
     /        \      Tier 2: Functional (many)
    /          \     Tier 1: Unit (most plentiful)
   ______________
```

---

## What a Tier May Cost

Cost places a test as much as scope does. The expected impact of one test rises with the tier,
and only Tiers 5 and 6 may be expensive:

| Tier | Expected impact of one test |
|---|---|
| 1 Unit | Microseconds and kilobytes: the function and the values handed to it |
| 2 Functional | Milliseconds, in memory: one module's own code |
| 3 Component | Tens of milliseconds, in process: one domain's own collaborators |
| 4 Integration | Under ten seconds: one seam, bounded |
| 5 System | As slow as it needs to be: deep, rich and heavily involved, one whole program through compilation, processes and large external resources |
| 6 Full system | As slow as it needs to be: deep, rich and heavily involved, the whole sample suite, once |

A test that costs more than its tier allows is at the wrong tier, whatever its tag says: either
the stage that owns the invariant takes a hand-built input, or the test moves up and counts
against the higher tier's budget.

---

### Tier 1: Unit

**Purpose:** Verify a single function or method in isolation.

**Scope:** One function, one behavior. The input is constructed directly. No collaborators are real unless they are pure value types.

**Allowed infrastructure:**
- Direct construction of the input (e.g., build a `LiteralExpression` by hand)
- Mocks or stubs for any dependency
- Property-based testing with high iteration counts (hundreds of combinations)

**Forbidden:**
- Lexer, parser, or any upstream pipeline stage
- File I/O, process spawning, Roslyn compilation
- Any collaborator that is not a pure value: a provider, a resolver, a file system

**Example:** Testing that `EmitIntegerLiteral(3000000000, UInt32Type)` returns `"3000000000U"`. You construct the literal and the type directly, call the method, assert on the string.

**Quantity:** Most plentiful. Hundreds to thousands across the codebase.

---

### Tier 2: Functional

**Purpose:** Verify a feature area's internal chain -- multiple methods within one module working together to produce a correct outcome.

**Scope:** A logical feature that spans several internal methods but stays within one module boundary. The scope resolver walking through 10 methods to find a symbol. The body binder converting an AST statement node into a bound statement through its full dispatch chain.

**Allowed infrastructure:**
- Real implementations within the module under test
- Hand-constructed inputs that represent the module's public contract
- Stubs for cross-module dependencies

**Forbidden:**
- Running upstream pipeline stages to manufacture input
- Process spawning, Roslyn compilation
- Testing behavior that belongs to a different module

**Example:** Testing that `ScopeResolver.Lookup("Fibonacci", bodyScope)` walks up the scope chain and finds the method registered on the parent type scope. You construct the scopes and registered methods by hand.

**Quantity:** Many. Tens to low hundreds.

---

### Tier 3: Component

**Purpose:** Verify a full phase/domain with real collaborators. This exercises an entire area -- does the ProgramModel builder work? Does the emitter produce correct source? -- using real dependencies within that domain.

**Scope:** A complete pipeline phase, with real internal collaborators. The "light integration" tier: it stays within one domain and lets the domain's own dependencies participate within the tier's cost. A dependency whose cost belongs to Tier 5 -- one that loads a large external resource -- is stubbed here unless it is the subject of the test.

**Allowed infrastructure:**
- Real implementations of collaborators within the domain, within the tier's cost
- A stub for any dependency whose cost belongs above this tier, carrying the few facts the test names
- Hand-constructed inputs at the domain's entry point
- Asserting on the domain's output (the model, the emitted source string)

**Forbidden:**
- Running pipeline stages outside the domain under test
- Roslyn compilation or process execution
- Loading a large external resource, outside the area that owns the loader
- Asserting on stdout of an executed program
- Property-based tests with > 50 iterations (these belong at Tier 1)

**Example:** Feeding an AST (hand-constructed or parsed from a minimal source) into `ProgramModelBuilder.Build` with a stub ambient provider that registers the handful of CLR types the source names, then inspecting the resulting model's types, methods, and bound expressions. Or: building a `ProgramModel` by hand and feeding it to `CSharpEmitterV2`, then asserting on the emitted `.cs` source strings without compiling them.

**Quantity:** Moderate. Tens.

---

### Tier 4: Integration

**Purpose:** Verify that different major areas of the pipeline integrate correctly at their boundaries.

**Scope:** Bridges two or more pipeline phases. The invariant being tested is the *seam* between them -- that data flows correctly across the boundary. Each integration test should name which boundary it tests.

Boundaries in the pipeline:
- Source text -> Lexer/Parser -> AST
- AST -> ProgramModel Builder -> IProgramModel
- IProgramModel -> CSharpEmitterV2 -> Emitted C# source
- Emitted C# source -> Roslyn in-process compiler -> DLL artifact
- DLL artifact -> Process execution -> stdout

**Allowed infrastructure:**
- Real implementations of the stages being bridged
- Running exactly the stages under test (not more)
- Roslyn compilation (only if the boundary being tested is emitter -> compiler)
- Process execution (only if the boundary being tested is compiler -> runtime)
- A collaborator whose cost belongs to Tier 5 only when the seam cannot be proved with a stub; most seams can

**Forbidden:**
- Exercising boundaries not under test (if testing AST -> ProgramModel, do not also compile and run)
- Property-based tests with high iteration (push wide coverage to Tier 1/2)
- More than ~10-20 tests per boundary

**Example:** A test that parses Mist source, builds the ProgramModel, emits C#, and compiles it with Roslyn to verify the emitted source is valid C#. It does NOT execute the DLL -- that's a different boundary.

**Quantity:** A handful per boundary. Low tens total.

---

### Tier 5: System

**Purpose:** Exercise most or all of the pipeline through representative sample programs. This goes beyond proving integration -- it proves the system achieves outcomes on real-world inputs.

**Scope:** Runs a sample program through the full pipeline. May or may not execute the output. The sample programs themselves are the tests -- each one exercises a cross-section of language features working together.

**Allowed infrastructure:**
- Full pipeline execution
- Process execution of compiled output
- Assertion on stdout against expected baselines
- Reading sample files from the `samples/` directory

**Forbidden:**
- More than one test per sample (the sample IS the test)
- Property-based generation (samples are curated, not random)
- Duplicating coverage already proven at lower tiers (if a sample fails, the fix should be proven by a unit test, not another system test)

**Example:** `EnumReadonlyRestriction_CompilesAndProducesExpectedOutput` -- compiles one sample standalone through the CLI and asserts on its output.

**Quantity:** Sparse, and justified per test. The Tier 6 orchestrator (`AllFeaturesV2`) already runs every working sample in one process, so a standalone Tier 5 test is redundant by default. Add one only when a sample needs a claim the orchestrator cannot make -- for example that it builds as its own root project rather than as a referenced one. Every Tier 5 test costs a process spawn, so the burden of proof is on adding it, not on omitting it.

---

### Tier 6: Full System

**Purpose:** Prove the entire pipeline works end-to-end on the language surface area it claims to support.

**Scope:** One orchestrator sample that composes other samples by project reference, compiled and executed through the real CLI on its default pipeline, with stdout diffed against a stored baseline:

| Sample | Baseline | Scope |
|---|---|---|
| `samples/AllFeaturesV2` | `samples/__baselines__/AllFeaturesV2.out` | Every working sample |

The orchestrator is a *progress ledger*: it references only what the compiler can actually compile and run today, and it grows. It is equally the regression guard: a sample it references must keep printing its baseline lines, so it must never shrink.

**Allowed infrastructure:**
- Everything

**Forbidden:**
- More than 1 test at this tier. Ever. No exceptions.
- A second orchestrator for any reason. If a new pipeline appears, it takes over this orchestrator, or it does not get a Tier 6 test.

**Example:** `AllFeaturesV2_CompilesRunsAndMatchesBaseline`

**Quantity:** Exactly 1.

#### The orchestrator IS the recorded progression

`samples/AllFeaturesV2` is the single source of truth for which samples work. A sample is working if and only if:

1. It is listed under `[references]` in `samples/AllFeaturesV2/mist.toml`, and
2. Its `RunDemo()` is called from `samples/AllFeaturesV2/Program.mist`, and
3. `AllFeaturesV2_CompilesRunsAndMatchesBaseline` is green.

Recording progress means adding those two lines and extending the baseline. Nothing else records it. Do not track progress in a status field, a checklist, or a hand-maintained list, because none of those are executable and all of them can drift from reality.

This design makes progress impossible to lose silently. Deleting a reference removes output lines, which fails the baseline diff. Breaking a previously-working sample fails the same test. A declarative `status: done` field can be wrong forever without anything noticing; a Tier 6 baseline cannot.

**Corollary:** "works standalone" is not the same claim as "works in composition," and only the second one counts. A sample can compile and run correctly on its own while still failing inside the orchestrator, because composition additionally exercises cross-project reference loading, namespace-qualified access into referenced projects, and entry-point selection across multiple `Main` methods. Only membership in `AllFeaturesV2` proves the composed claim.

---

## Rules of Engagement

### Declare the tier in test metadata

Every new or migrated test must carry its tier in the framework's discoverable metadata.
This repo uses xUnit: `[Trait("Tier", "1")]` through `[Trait("Tier", "6")]`.
Exactly one effective tier per test. Tag a concrete class only if all tests share its tier;
otherwise tag individual methods and leave the class untagged. Class and method traits combine,
so method traits cannot override a class tier. `[Fact]`, `[Theory]` and FsCheck `[Property]` all
follow this rule; split theory rows requiring different tiers into different methods. Do not
use an assembly or shared base fixture to assign a blanket tier. Keep feature traits and
scheduling collections independent. See `Test/CLAUDE.md`, "Test tier classification", for examples
and runner filters. If another framework is used, use its native category/tag mechanism with
the same six-tier meaning.

Existing tests may remain unmarked until audited or migrated. Missing metadata means
unclassified; do not infer Tier 1 from the folder, name, or a comment. Choose the tier from the
invariant, then audit the implementation, including its helpers and fixtures. A tag claiming
Tier 1 while exercising Tier 4 infrastructure is a defect, and relabelling does not excuse a
single-stage invariant tested at the wrong tier.

Before adding higher-tier coverage beyond the pyramid's limits, get explicit human approval
for an exception: state the invariant, case count, expected cost and why lower tiers cannot
prove it. Count theory rows and repeated pipeline executions, not merely classes or methods.
Do not hide 200 system cases in one theory or loop. Tests within the existing limits need no
extra approval.

### Tier determines allowed infrastructure, not the other way around

Decide which tier a test belongs to FIRST based on what invariant it encodes. Then constrain the infrastructure to what that tier allows. Do not let the available infrastructure dictate the tier.

### The most common violation

Running the full pipeline (or most of it) to test a single-stage invariant. If you're testing that a literal emits with the right suffix, you don't need a lexer, parser, ProgramModel builder, Roslyn compiler, and process execution. You need the emitter function and a hand-built input.

**Symptom:** A test that parses source code but only asserts on one property of the output.

**Fix:** Identify which stage owns the invariant. Build that stage's input directly. Test at Tier 1 or 2.

### Property tests belong at the bottom

High-iteration property-based tests (FsCheck `[Property]` with 100+ iterations) belong exclusively at Tier 1 (unit) or Tier 2 (functional). They are designed to find edge cases in a single rule or function. Running them at higher tiers multiplies cost without proportional value.

### Integration tests are about boundaries, not features

An integration test should be named after the boundary it proves, not the feature it exercises. "EmitterToRoslyn_ValidCSharp" is a good integration test. "Primitives_WorkCorrectly" is a system test wearing integration clothing.

### Progress is recorded by composition, not by declaration

Enabling a sample is recorded by adding it to `samples/AllFeaturesV2` (reference + `RunDemo()` call + baseline lines), which the single Tier 6 test then enforces. It is not recorded by adding a Tier 5 test per sample, and it is not recorded by a status field in a generated file.

Carbonek project MIST tracks outstanding obligations and their evidence. It does not assert
sample greenness. `AllFeaturesV2` membership and baseline output remain the progress record.
A green sample proves only the paths its program actually exercises; model shape, modifiers,
refusals and other unobserved behavior need checks at their owning tiers.

Feature-level coverage of what a sample uses belongs at Tiers 1-3.

### When a system test fails, the fix is proven at a lower tier

If `PrimitivesSample` starts failing because literal suffixes are wrong, the fix is:
1. Write a Tier 1 unit test that proves the suffix logic works
2. Fix the code
3. The system test passes again as a side effect

Do not write another system test to cover the edge case. Push it down.

## What Not To Test

- **Language-guaranteed behavior.** The type system already proves it.
- **Framework/library behavior.** Assume xUnit, Roslyn, and the BCL work.
- **Positive behavior already proven by the orchestrator** (`AllFeaturesV2`) -- unless the invariant is negative (rejection/diagnostic) which the orchestrator structurally cannot cover. That exception says a refusal is worth testing, not where: a diagnostic is owned by the rule that reports it, so it is proven at that rule's tier -- Tier 1, or Tier 2 when the rule walks a body. Compiling a program to watch it fail is Tier 4+ for a single-stage invariant, the most common violation above.
- **Upstream stages.** If you're testing stage N, do not re-verify stages 1 through N-1. Their own tests cover them.

## Deciding Where a Test Belongs

1. **What invariant does it encode?** If the language guarantees it -> do not write it.
2. **Which stage owns the invariant?** That determines the minimum tier.
3. **Does the invariant require observing a seam between stages?** If yes -> Tier 4 (Integration).
4. **Does it require a realistic program exercising many features together?** If yes -> Tier 5 (System).
5. **Otherwise** -> Tier 1-3 based on scope. One function = Unit. Feature chain = Functional. Full phase with real deps = Component.

## Applies To

- All test code under `Test/` (strict enforcement)
- Test-strategy decisions in feature specs and design documents
- Any change that adds, moves, or removes a test
