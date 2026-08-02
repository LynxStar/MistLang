---
inclusion: fileMatch
fileMatchPattern: "**/.kiro/specs/**/requirements.md"
---

# Sample Proof Code Policy

## Rule

Every spec's sample application requirement MUST include pre-written Mist source code blocks that serve as **proof-of-correctness tests**. These code blocks are written during the requirements/design phase — before implementation begins — and the implementation's job is to make that exact code compile and produce the specified output.

## Why

Without concrete proof code locked in before implementation, the agent optimizes for "green build" rather than "correct proof." It writes samples that avoid broken compiler paths instead of exercising them. The result is a sample that compiles and runs but doesn't actually prove the feature works end-to-end.

By writing the proof code upfront, the designer (the user) defines what "done" looks like. The implementer cannot water it down to sidestep a compiler gap.

## What Proof Code Must Contain

Each proof code block MUST:

1. **Use the feature through its intended API** — not just declare it. If the feature introduces an abstract type with methods, the proof code must call those methods and observe their results.
2. **Include `// PROOF:` comments** explaining what specific compiler capability each section exercises. These comments name the exact thing that would break if the implementation were incomplete.
3. **Include expected output** — the exact stdout lines the code must produce when compiled and run.
4. **Be falsifiable** — if the feature implementation were removed or incomplete, the code MUST either fail to compile or produce wrong output. Code that would still pass without the feature is not proof code.

## Format in Requirements Document

In the sample application requirement, include one or more fenced code blocks with the proof code:

````markdown
#### Proof Code

```mist
// PROOF: foreach resolves TElement=int from IEnumerable<int>
// PROOF: GetEnumerator() returns the concrete Enumerator struct
// PROOF: MoveNext()/Current drive the iteration
var range = new Range(3);
foreach (int x in range) {
    Console.WriteLine($"x={x}");
}
```

**Expected output:**
```
x=0
x=1
x=2
```
````

## Rules for Implementation

1. **The proof code is immutable during implementation.** The implementer MUST NOT modify the proof code to make it easier to compile. If the proof code doesn't compile, the compiler must be fixed — not the proof code.
2. **The sample app MUST contain the proof code verbatim** (it may contain additional demonstrations beyond the proof code, but the proof code must appear as-is).
3. **The expected output MUST match exactly.** If the actual output differs from the expected output, the task is not complete.
4. **If a proof code block cannot be made to work**, the implementer MUST stop and report the gap to the user. Do not silently skip it, comment it out, or replace it with a weaker demonstration.

## When the Proof Code and Its Sample Disagree

Rule 1 above assumes the proof code is the fresher artifact. Often it is not. The specs were
written organically, feature by feature, and then left; the samples have been maintained. So a
disagreement is usually spec drift, not an implementation gap.

**The sample is authoritative. Fix the spec.**

Decide which case you are in by running the sample:

| The sample | The proof code | Then |
|---|---|---|
| compiles and runs | does not match it | The proof code is STALE. Update the spec to match the sample. |
| does not compile either | — | A real gap. Rule 1 holds: fix the compiler, report to the user, change nothing. |

The distinction matters because the two look identical in a diff. A stale block and an
unimplemented block both "fail to match", and only running the sample tells them apart.

This does NOT license watering proof code down, which is what rule 1 exists to prevent. Lifting a
working sample's content into the spec makes the proof STRONGER, because it becomes a statement of
what demonstrably runs. Weakening a block so it stops exercising the feature is still forbidden,
and so is editing one because the compiler cannot do what it asks.

Measured on 2026-08-02, so the scale is known rather than assumed: of 28 proof-code blocks across
specs that designate a sample, 19 had drifted from it and 12 expected-output blocks did not match
what their sample actually printed. Two specific failures show what drift looks like — the
abstract-type-unification proof code omitted `public` on six abstract declarations and therefore
could not compile at all, and Requirement 16 of the same spec specified a forwarding stub that
recurses infinitely. Neither was an implementation gap; both were the document going stale beside
working code. All were reconciled the same day by lifting the sample into the spec.

### Two shapes that legitimately do not satisfy the check

A blanket "every block is a substring of its designated sample, and every expected output equals
that sample's run" is right for most blocks and wrong for these two, so a checker needs to exempt
them by name rather than treat them as drift:

1. **An inline illustration.** A `#### Proof Code` block under a requirement other than the sample
   requirement demonstrates that requirement in isolation and belongs to no sample. Its namespace
   gives it away by differing from the sample's. `abstract-type-unification` Requirement 26's
   `HidingDemo` block is the example.
2. **An excerpt with sectional output.** Where the proof code is a substring of a large sample, its
   expected output covers that excerpt's own lines, not the whole sample's run. `ownership-v2` is
   the example: 71 lines of a 757-line sample, with 8 lines of expected output against the sample's
   full 98.

A third case is worth knowing rather than exempting: a spec may carry proof code for a DIFFERENT
sample than the one it designates. `attributes-and-test-command` designates `samples/Attributes`
and its second block is `namespace Testing`, which belongs to `samples/Testing/Tests.mist`. Match on
the namespace, not on the designated path.

## Applies To

- All `requirements.md` files inside `.kiro/specs/*/` that include a sample application requirement
