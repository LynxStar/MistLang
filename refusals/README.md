# refusals

**This directory and `RefusalCorpusTest` completely ignored the testing philosophy**
(`.claude/skills/testing-philosophy/SKILL.md`, `/testing-philosophy`). Every case is a full compile
of a whole program to check what one stage reports, so this directory is 759 Tier 4–5 tests of
Tier 1–2 invariants, which the philosophy names as "the most common violation". `RefusalCorpusTest`
is about 12 of the suite's 13 minutes (measured 2026-09-26).

**Stop making it worse: add nothing here.** A rule's refusals are proven at Tier 1–2 beside the
rule; see `Source/Mist/ProgramAnalysis/CLAUDE.md`.

The programs Mist must **not** compile, and the diagnostic each one must produce.

Walked by one test, `RefusalCorpusTest`, which compiles each program in-process on the experimental
pipeline and holds it to two claims:

- **Every expectation is met.** A marked diagnostic that is not reported fails the case.
- **Nothing else is reported.** Any error *or warning* the markers did not predict fails the case,
  which is what holds the no-cascade invariant: one mistake, one complaint.

A marker names a diagnostic id, and the id carries its severity. Most rules here refuse; a few owe a
**warning** instead, where that is the severity the finding has — `MIST_PA_SWITCH_NOT_EXHAUSTIVE` is
one, because classic warns for it too. Those cases compile, so they pay for emission and a Roslyn
compile where a refused case stops at the gate; that is the only difference.

## The markers

The expectation lives in the program, directly above the line it is about, so it cannot drift out of
step with an edit the way a hand-written line number does.

```mist
// REFUSE: MIST_PM_UNRESOLVED_IDENTIFIER
Console.WriteLine(nothingDeclaredThis);
```

`REFUSE` means *on the next line*. Some rules report at an enclosing declaration instead — a parse
failure surfaces at the enclosing member, and a conformance failure at the conforming type — and those
use the file-scoped form, which asserts only that the diagnostic is somewhere in the file:

```mist
// REFUSE-FILE: MIST_PARSE_INCOMPLETE
```

A file with no markers fails: a case that does not say what it is for is not a case.

## What belongs here

Nothing new; see the top of this file. The existing cases are grouped one directory per rule in
`backlog/analysis-v2-capability-progression.yaml`, plus `binding/` for the binder's refusals. The
directory name is what a failure reports.
