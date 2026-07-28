# Test Failure Triage Policy

Two rules for the same situation: a test just failed and you need to know whether it is yours.

## Rule 1: Rebuild before blaming the working tree

When verifying whether a test failure is pre-existing by stashing changes, you MUST rebuild before running tests. Using `dotnet test --no-build` after `git stash` is **always wrong** because the compiled binaries still reflect the stashed (new) code, not the working tree.

### Why

`dotnet test --no-build` reuses whatever binaries are already in `bin/`. After `git stash`, the source files revert but the compiled output remains unchanged. The test run exercises the old binaries and proves nothing about the old source.

### Correct Pattern

```bash
# 1. Stash your changes
git stash

# 2. Rebuild from the now-reverted source (REQUIRED)
dotnet build

# 3. Run the tests against the freshly-built old code
dotnet test --no-build

# 4. Restore your changes
git stash pop
```

The key: `dotnet build` (or `dotnet test` without `--no-build`) MUST appear between `git stash` and the test execution.

### Forbidden Pattern

```bash
# BAD: stash changes then skip the build — tests still run NEW code
git stash
dotnet test --no-build   # ← proves nothing, binaries are stale
git stash pop
```

### Preferred Alternative

If you only need to check whether a test existed and passed before your changes, prefer:

```bash
# Check if the test file existed in the previous commit
git show HEAD:<path-to-test-file>

# Or run tests from a clean baseline commit
git stash
dotnet test --filter "FullyQualifiedName~TestName"
git stash pop
```

Note: `dotnet test` without `--no-build` will rebuild automatically, which is correct after a stash.

## Rule 2: Record every flaky test you find

When a test fails and then passes without any code change between the runs, you MUST append an entry to `backlog/flaky-tests.yaml` before moving on. This is required even when you are not fixing it, and even when the flake is obviously unrelated to what you are working on.

Fixing it is optional. Recording it is not.

### Why

A flake found mid-change is a distraction from that change, so leaving it unfixed is usually the right call. But leaving it *unrecorded* is not: the next person to hit it re-diagnoses it from scratch, and every unexplained red run erodes the signal the suite exists to carry. The register turns a recurring mystery into a known, closed question.

### What counts

- A test that fails on one run and passes on a rerun with no intervening edit.
- A test that fails in the full suite but passes under `--filter` in isolation.
- A property-based test that fails only on some random draws.
- A test whose failure depends on run order, parallelism, wall-clock time, or machine state.

A test that fails consistently is not flaky. It is either a real defect or a stale baseline, and it gets fixed rather than recorded.

### What to write

Append to the `flakes:` list in `backlog/flaky-tests.yaml`, following the field documentation at the top of that file. Two of those fields carry most of the value:

- **cause** — `unknown` is an acceptable answer. A guess dressed up as a cause is not, because it stops the next person from looking.
- **evidence** — how you concluded it was flaky and not yours. Enough that the diagnosis can be re-checked rather than re-derived. Name the files your change touched, note that the test file was unmodified, and record that the rerun was green.

### Establishing it is not yours

Before recording, confirm the test file is unmodified in your working tree and that the failure does not reproduce:

```bash
git status --short <path-to-test-file>
dotnet test --filter "FullyQualifiedName~TestName"
```

If the test file IS modified by your change, it is not a flake you found -- it is a flake you introduced, and it gets fixed.

## Applies To

- Any full-suite or filtered test run during a change
- Any investigation of whether a failure is pre-existing
- Verification steps before presenting results or committing