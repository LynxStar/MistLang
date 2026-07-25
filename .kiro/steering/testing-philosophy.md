---
inclusion: manual
---

# Testing Philosophy

## Core Principle

A test exists for exactly one reason: to encode an invariant the language itself cannot guarantee.

That invariant is almost always *intent*. The compiler proves code is well-formed and runs; it cannot prove the code does what we *meant*. The gap a test fills is the distance between "compiles and runs" and "does what we intended."

Before writing any test, answer: **what intended behavior does this pin down, and why can the language not express it?** If there is no clear answer, the test should not exist.

## Enforcement Scope

- **Experimental pipeline** (ProgramModel, EmitterV2, AmbientProviderV2, ExperimentalPipeline): strict adherence required. Every test must land at the correct tier with the correct constraints. No exceptions.
- **Classic pipeline** (SemanticModel, old emitter, old analyzers): this is a guideline. The classic pipeline and all its tests will eventually be discarded. Do not invest in reshaping existing classic tests. Do not write new classic pipeline tests that violate these tiers.

The old world fixes itself when we delete it. The new world must not dig the same hole.

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
- Real ambient provider, real scope resolver

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

**Scope:** A complete pipeline phase, with real internal collaborators. The "light integration" tier: it stays within one domain but lets real dependencies participate. The ambient provider is real. The type interner is real. The scope chain is real.

**Allowed infrastructure:**
- Real implementations of collaborators within the domain
- Real ambient provider (when testing ProgramModel construction)
- Hand-constructed inputs at the domain's entry point
- Asserting on the domain's output (the model, the emitted source string)

**Forbidden:**
- Running pipeline stages outside the domain under test
- Roslyn compilation or process execution
- Asserting on stdout of an executed program
- Property-based tests with > 50 iterations (these belong at Tier 1)

**Example:** Feeding an AST (hand-constructed or parsed from a minimal source) into `ProgramModelBuilder.Build` with a real ambient provider, then inspecting the resulting model's types, methods, and bound expressions. Or: building a `ProgramModel` by hand and feeding it to `CSharpEmitterV2`, then asserting on the emitted `.cs` source strings without compiling them.

**Quantity:** Moderate. Tens.

---

### Tier 4: Integration

**Purpose:** Verify that different major areas of the pipeline integrate correctly at their boundaries.

**Scope:** Bridges two or more pipeline phases. The invariant being tested is the *seam* between them -- that data flows correctly across the boundary. Each integration test should name which boundary it tests.

Boundaries in the experimental pipeline:
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

**Quantity:** Sparse, and justified per test. For the experimental pipeline, the Tier 6 orchestrator (`AllFeaturesV2`) already runs every working sample in one process, so a standalone Tier 5 test is redundant by default. Add one only when a sample needs a claim the orchestrator cannot make -- for example that it builds as its own root project rather than as a referenced one. Every Tier 5 test costs a process spawn, so the burden of proof is on adding it, not on omitting it.

---

### Tier 6: Full System

**Purpose:** Prove an entire pipeline works end-to-end on the language surface area it claims to support.

**Scope:** An orchestrator sample that composes other samples by project reference, compiled and executed through the real CLI, with stdout diffed against a stored baseline. One per pipeline:

| Pipeline | Sample | Baseline | Scope |
|---|---|---|---|
| Classic | `samples/AllFeatures` | `samples/__baselines__/AllFeatures.out` | Every implemented language construct |
| Experimental | `samples/AllFeaturesV2` | `samples/__baselines__/AllFeaturesV2.out` | Every sample currently working on v2 |

The two differ in what they claim. The classic orchestrator is a regression guard over a finished surface: it references every sample and must never shrink. The experimental orchestrator is a *progress ledger* over an unfinished one: it references only what v2 can actually compile and run today, and it grows.

**Allowed infrastructure:**
- Everything

**Forbidden:**
- More than 2 tests at this tier. Ever. One per pipeline, no exceptions.
- A third orchestrator for any reason. If a new pipeline appears, it replaces one of these, or it does not get a Tier 6 test.

**Examples:** `AllFeatures_CompilesRunsAndMatchesBaseline`, `AllFeaturesV2_CompilesRunsAndMatchesBaseline`

**Quantity:** Exactly 2.

#### The experimental orchestrator IS the recorded progression

`samples/AllFeaturesV2` is the single source of truth for which samples work on the v2 pipeline. A sample is working on v2 if and only if:

1. It is listed under `[references]` in `samples/AllFeaturesV2/mist.toml`, and
2. Its `RunDemo()` is called from `samples/AllFeaturesV2/Program.mist`, and
3. `AllFeaturesV2_CompilesRunsAndMatchesBaseline` is green.

Recording progress means adding those two lines and extending the baseline. Nothing else records it. Do not track v2 progress in a status field, a checklist, or a hand-maintained list, because none of those are executable and all of them can drift from reality.

This design makes progress impossible to lose silently. Deleting a reference removes output lines, which fails the baseline diff. Breaking a previously-working sample fails the same test. A declarative `status: done` field can be wrong forever without anything noticing; a Tier 6 baseline cannot.

**Corollary:** "works standalone on v2" is not the same claim as "works in composition on v2," and only the second one counts. A sample can compile and run correctly on its own while still failing inside the orchestrator, because composition additionally exercises cross-project reference loading, namespace-qualified access into referenced projects, and entry-point selection across multiple `Main` methods. Only membership in `AllFeaturesV2` proves the composed claim.

---

## Rules of Engagement

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

Enabling a sample on the experimental pipeline is recorded by adding it to `samples/AllFeaturesV2` (reference + `RunDemo()` call + baseline lines), which the single Tier 6 experimental test then enforces. It is not recorded by adding a Tier 5 test per sample, and it is not recorded by a status field in a generated file.

`backlog/emitter-v2-sample-progression.yaml` is a *planning* artifact, not a progress artifact. Its measured fingerprints answer "which sample is cheapest to attempt next"; they do not assert that anything works. Treat any `status:` field there as advisory, and treat `AllFeaturesV2` membership as authoritative when the two disagree.

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
- **Positive behavior already proven by AllFeatures** -- unless the invariant is negative (rejection/diagnostic) which AllFeatures structurally cannot cover.
- **Upstream stages.** If you're testing stage N, do not re-verify stages 1 through N-1. Their own tests cover them.

## Deciding Where a Test Belongs

1. **What invariant does it encode?** If the language guarantees it -> do not write it.
2. **Which stage owns the invariant?** That determines the minimum tier.
3. **Does the invariant require observing a seam between stages?** If yes -> Tier 4 (Integration).
4. **Does it require a realistic program exercising many features together?** If yes -> Tier 5 (System).
5. **Otherwise** -> Tier 1-3 based on scope. One function = Unit. Feature chain = Functional. Full phase with real deps = Component.

## Applies To

- All test code under `Test/` targeting the experimental pipeline (strict enforcement)
- All test code under `Test/` targeting the classic pipeline (guideline)
- Test-strategy decisions in feature specs and design documents
- Any change that adds, moves, or removes a test
