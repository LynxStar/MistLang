---
inclusion: manual
---

# Testing Philosophy

## Core Principle

A test exists for exactly one reason: **to encode an invariant the language itself cannot guarantee.**

Overwhelmingly, that invariant is *intent*. The compiler already proves the code is well-formed and runs; what it cannot prove is that the code does what we *meant*. "A function declared to return `int` returns an `int`" is guaranteed by the language. "That function returns the slot count, and it is off-by-one at arity 0" is intent -- and nothing but a test pins it down. The gap a test fills is almost always the distance between *compiles and runs* and *does what we intended*.

A smaller class of invariants covers genuine expressiveness gaps: properties the type system could in principle enforce but does not (e.g., "exactly one params pack per type-parameter list"). These are real and worth testing -- but they are the exception. Intent is the rule.

Do not test what the language already guarantees; it proves those for you. A test earns its place only by capturing intended behavior -- or a true expressiveness gap -- that would otherwise be held up by nothing but hope.

Before writing any test, answer: **what intended behavior does this pin down, and why can the language not express it?** If there is no clear answer, the test should not exist.

## What Not To Test

- **Language-guaranteed behavior.** If the type system, exhaustiveness checker, or language spec already makes a property impossible to violate, a test adds nothing but cost. (Do not test that a method declared to return `int` returns an `int`.)
- **The framework or standard library.** Assume `List<T>.Add`, `string.Split`, xUnit, and Roslyn work. Test *our* invariants, not our dependencies'.
- **Behavior already proven end-to-end by AllFeatures** (see below) -- unless the test encodes a *negative* invariant that AllFeatures structurally cannot cover.

Litmus test: *"Would this fail for a reason the compiler actually cares about, or is it re-asserting something already guaranteed?"* If the latter, delete it.

## The Test Taxonomy

Tests form a pyramid ordered by **how much of the codebase each one exercises. The more of the toolchain a test touches, the fewer of that kind there should be.**

1. **Building-block invariant tests -- many.**
   Target a single function, class, analyzer, or narrow pocket of code. This is where high-iteration property tests and wide `[Theory]` matrices belong: throw many inputs at one unit to pin an invariant it must uphold (e.g., "the monomorphizer produces exactly N slots for arity N", "this rule rejects X with diagnostic Y"). Cheap, fast, precise. Have lots of these.

2. **Coupling tests -- a handful.**
   Verify that two sections integrate correctly, where the invariant *only* becomes expressible at the seam between them (e.g., parser output feeding the semantic model for a construct). Deliberate and few.

3. **End-to-end flow tests -- fewer still.**
   Exercise a path through most of the pipeline. Reserve for invariants that genuinely require the whole flow.

4. **The AllFeatures crown jewel -- one.**
   `SamplesCompileAndMatchBaselineTest.AllFeatures_CompilesRunsAndMatchesBaseline` compiles and *runs* the entire sample corpus through the real CLI and diffs stdout against a stored baseline. It is the single most valuable test in the suite: it proves the whole chain works on real programs producing correct output. Invest in the samples; they are the top of the pyramid.

## Positive vs Negative Invariants (the AllFeatures boundary)

AllFeatures can only prove **positive** invariants: *a valid program produces correct output.* By construction it can **never** prove a **negative** invariant -- "this invalid program is rejected with diagnostic X" -- because a program that fails to compile cannot live in a compiles-and-runs corpus.

Therefore:

- **Positive behavior** that AllFeatures already exercises end-to-end does **not** need a separate full-compile test. If a feature's happy path is not covered, add it to a sample, not to a bespoke integration test.
- **Negative / rejection invariants** must be encoded as targeted tests against the stage that owns the diagnostic (the analyzer, type checker, parser). These are *not* redundant with AllFeatures and must never be deleted as such -- they are pushed down to their owning unit, not routed through a full compile.

## Exercise Only the Stage Under Test

**This is the most common and most important violation in the codebase. Hunt it down first.**

Test a stage by constructing its input directly and invoking only that stage. Do not reach for an upstream stage just because it is a convenient way to manufacture that input. Every upstream stage a test runs is a stage that:

- can fail the test for a reason unrelated to what is being tested,
- couples the test to code it asserts nothing about, and
- re-verifies work that stage's own tests already cover.

A full compile is the extreme case, but the smell appears at every level. Common forms:

- **Testing an analyzer** by feeding a source string through lex -> parse -> semantic model just to obtain a model to analyze. Instead, construct the semantic model (or the specific symbols and types) the analyzer consumes and invoke the analyzer on it directly.
- **Testing the semantic model** by lexing and parsing a source string to get compilation units and AST nodes. Instead, construct the `CompilationUnit`s and AST nodes directly and hand them to the model builder.
- **Testing the emitter** by compiling from source. Instead, build the model the emitter consumes and emit from that.

The rule: **the input to the stage under test is a fixture you build, not a by-product you derive by running earlier stages.**

Yes, hand-constructing an AST or a semantic model is more work than passing a string -- that friction is exactly why the shortcut is so common. Pay it, and use (or add) test builders for the input types so it stays cheap. The payoff is a test that fails only when the stage it names is actually wrong, and keeps passing when unrelated upstream stages change.

Only the deliberately-integration tests -- a handful of coupling tests, fewer end-to-end, and AllFeatures -- should span multiple stages, and only because the *seam between stages* is the invariant they encode. A suite where hundreds of unit tests each drive the whole pipeline (or most of it) is inverted and must be reshaped.

### Pragmatics: shared structures and the classic pipeline

How cleanly a stage can be isolated depends on the architecture around it. When a stage's input is a self-contained value, build it directly. But when a single structure is *threaded through the entire pipeline* -- as the AST is in the classic pipeline -- running the front end is often the only ergonomic way to obtain a valid instance, and hand-constructing it fights the design for little gain.

Scoped guidance for where the codebase is today:

- **On the classic pipeline**, obtaining a stage's input by running the *upstream* stages (lex/parse to produce the AST, then building the model) is an acceptable compromise. What remains a smell even here is a *full compile* -- a `driver.Compile` that also runs the stages *downstream* of the one under test (analysis you do not need, emit, Roslyn) when a lighter stage boundary exists. Stop at the stage under test.
- **The isolation ideal pays off most where separation of concerns is strong** -- the experimental pipeline. Do not retrofit the classic pipeline's tests for isolation. When it is retired, its tests are not migrated wholesale -- coverage is *re-derived*: expect to discard large swaths of the suite and bootstrap from scratch what actually should be tested against the experimental pipeline's clean stage boundaries. This document is the rubric for that port -- every candidate test must re-earn its place by the Core Principle before it is written against the new pipeline.

## Approved Pattern: Split Invariant From Integration

When an invariant is currently checked end-to-end, separate the *wide invariant coverage* (cheap, unit-level) from the *thin integration proof* (a couple of cases).

```csharp
// GOOD: the wide, position-independent invariant runs against the pure rule
// with no compile -- high iteration costs almost nothing.
[Property(MaxTest = 150)]
public bool Rule_HoldsForEveryPattern(Pattern p)
{
    return CheckRuleDirectly(p) == Oracle(p);
}

// GOOD: a couple of [Theory] cases prove the pipeline wires the rule in.
[Theory]
[InlineData("...")]
[InlineData("...")]
public void Pipeline_EmitsDiagnostic_ForRepresentativeCases(string src) { }
```

```csharp
// BAD: a full compile per iteration to assert a single-stage invariant.
[Property(MaxTest = 150)]
public bool Rule_Holds(Pattern p)
{
    var result = FullCompile(BuildSource(p));   // 8 stages for a 1-stage invariant
    return result.Diagnostics.Any(IsExpected) == Oracle(p);
}
```

## Deciding Where a Test Belongs

For any test -- new or existing -- walk this procedure:

1. **What intended behavior does it pin down?** If the language already guarantees it, or the framework owns it -> delete.
2. **Is it positive behavior already proven by AllFeatures?** -> delete, or fold the missing case into a sample.
3. **Which single stage owns the invariant?** If exactly one does -> it is a building-block test; invoke that stage in isolation on a directly-constructed input -- do not route a source string through the upstream stages (on the classic pipeline, generating that input via the front end is the tolerated exception noted above). High iteration is welcome here.
4. **Does the invariant only exist at a seam, or require the whole flow?** -> coupling or e2e test; keep it, but keep these few.

## Applies To

- All test code under `Test/`.
- Test-strategy decisions in feature specs and design documents.
- Any change that adds, moves, or removes a test.
