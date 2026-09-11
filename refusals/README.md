# refusals

The programs Mist must **not** compile, and the diagnostic each one must produce.

`samples/` is the other half of the same ledger: those are the programs that must run, asserted on
stdout. Nothing there can measure a refusal, because every sample is a valid program — so an analyzer
that reports nothing passes every test in `samples/`. This directory is where a refusal is recorded.

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

Any program the compiler must refuse, whichever stage refuses it. The corpus is seeded with Stage 4
binder refusals because those were the ones that existed when it was built — the ledger does not care
which stage said no, only that the program was refused and for the stated reason.

Grouped one directory per rule in `backlog/analysis-v2-capability-progression.yaml`, plus `binding/`
for refusals the binder already makes. The directory name is what a failure reports, so it is worth
keeping it the register's own id.

Keep each program minimal and ASCII-only (`Test/CLAUDE.md`), and keep it to ONE refusal where the rule
allows — a case proving two things at once proves neither when it fails.
