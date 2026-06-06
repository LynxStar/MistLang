# Git Stash + Test Verification Policy

## Rule

When verifying whether a test failure is pre-existing by stashing changes, you MUST rebuild before running tests. Using `dotnet test --no-build` after `git stash` is **always wrong** because the compiled binaries still reflect the stashed (new) code, not the working tree.

## Why

`dotnet test --no-build` reuses whatever binaries are already in `bin/`. After `git stash`, the source files revert but the compiled output remains unchanged. The test run exercises the old binaries and proves nothing about the old source.

## Correct Pattern

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

## Forbidden Pattern

```bash
# BAD: stash changes then skip the build — tests still run NEW code
git stash
dotnet test --no-build   # ← proves nothing, binaries are stale
git stash pop
```

## Preferred Alternative

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
