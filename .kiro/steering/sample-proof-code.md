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

## Applies To

- All `requirements.md` files inside `.kiro/specs/*/` that include a sample application requirement
