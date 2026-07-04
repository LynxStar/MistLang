---
inclusion: fileMatch
fileMatchPattern: '**/ProgramModel/**'
---

# Program Model Verification Policy

## Rule: Batch Verification, Not Per-Change

When making a series of small, related changes to `Source/Mist/ProgramModel/` (refactoring, renaming, moving code between files, adjusting signatures), do NOT build or run tests after every individual change.

Instead:

1. Make all the related changes in a batch.
2. Build once at the end of the batch.
3. Fix any compilation errors.
4. Run tests once after the build succeeds.

## When To Build Mid-Batch

Only build between changes if:

- You are unsure whether the approach compiles (novel pattern, unfamiliar API)
- The user explicitly asks you to verify
- You are switching to a fundamentally different phase of work

## What Counts as "Small Changes"

- Renaming a field, parameter, or method
- Changing a method signature and updating its callers
- Extracting or inlining a method
- Adding/removing a using directive
- Moving logic between files without changing behavior
- Updating XML doc comments

These do NOT need individual build+test cycles.

## Full Test Suite

Run the full test suite (`dotnet test Mist.slnx`) only at natural checkpoints:

- End of a complete feature or refactoring phase
- Before presenting final results to the user
- When the user asks

Do NOT run 4600+ tests to verify a one-line rename compiled correctly.
